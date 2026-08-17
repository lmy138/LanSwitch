using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Management;
using System.Reflection;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("LanSwitch")]
[assembly: AssemblyDescription("One-click network adapter switch without recurring UAC prompts")]
[assembly: AssemblyProduct("LanSwitch")]
[assembly: AssemblyCopyright("Copyright (c) 2026")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace LanSwitch
{
    internal static class Program
    {
        internal const string ServiceName = "LanSwitchService";
        private const string InstalledDirectoryName = "LanSwitch";
        private const string InstalledFileName = "LanSwitch.exe";

        [STAThread]
        private static void Main(string[] args)
        {
            if (!Environment.UserInteractive)
            {
                ServiceBase.Run(new AdapterToggleService());
                return;
            }

            if (args.Length >= 2 && String.Equals(args[0], "--install", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = InstallService(args[1]);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                if (!EnsureServiceInstalled())
                {
                    return;
                }

                bool forceConfiguration = HasArgument(args, "--configure") ||
                    (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
                AdapterInfo adapter = Configuration.LoadAdapter();

                if (forceConfiguration || adapter == null)
                {
                    adapter = ShowAdapterSelection();
                    if (adapter == null)
                    {
                        return;
                    }

                    Configuration.SaveAdapter(adapter);
                    ShowNotification("网卡切换", "已选择：" + adapter.ConnectionName, ToolTipIcon.Info);

                    if (forceConfiguration || HasArgument(args, "--configure"))
                    {
                        return;
                    }
                }

                ToggleAdapter(adapter);
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    exception.Message,
                    "网卡切换失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static bool HasArgument(string[] args, string expected)
        {
            foreach (string argument in args)
            {
                if (String.Equals(argument, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool EnsureServiceInstalled()
        {
            if (ServiceExists())
            {
                return true;
            }

            string userSid = WindowsIdentity.GetCurrent().User.Value;
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = Assembly.GetExecutingAssembly().Location;
            startInfo.Arguments = "--install " + userSid;
            startInfo.Verb = "runas";
            startInfo.UseShellExecute = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;

            try
            {
                using (Process installer = Process.Start(startInfo))
                {
                    installer.WaitForExit();
                    if (installer.ExitCode != 0)
                    {
                        throw new InvalidOperationException("服务安装失败，错误代码：" + installer.ExitCode + "。");
                    }
                }
            }
            catch (Win32Exception exception)
            {
                if (exception.NativeErrorCode == 1223)
                {
                    return false;
                }
                throw;
            }

            return ServiceExists();
        }

        private static int InstallService(string userSid)
        {
            try
            {
                SecurityIdentifier parsedSid = new SecurityIdentifier(userSid);
                string installDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    InstalledDirectoryName);
                string targetPath = Path.Combine(installDirectory, InstalledFileName);
                string sourcePath = Assembly.GetExecutingAssembly().Location;

                Directory.CreateDirectory(installDirectory);
                if (!String.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourcePath, targetPath, true);
                }

                if (!ServiceExists())
                {
                    ManagedInstallerClass.InstallHelper(new string[] { targetPath });
                }

                GrantServiceStartPermission(parsedSid.Value);
                RunSc("description " + ServiceName + " \"按需切换用户选择的网络适配器\"");
                return 0;
            }
            catch (Exception exception)
            {
                try
                {
                    string logPath = Path.Combine(Path.GetTempPath(), "LanSwitch-install-error.txt");
                    File.WriteAllText(logPath, exception.ToString(), Encoding.UTF8);
                }
                catch
                {
                }
                return 1;
            }
        }

        private static void GrantServiceStartPermission(string userSid)
        {
            string sddl = RunSc("sdshow " + ServiceName).Trim();
            string userAce = "(A;;RP;;;" + userSid + ")";
            if (sddl.IndexOf(userAce, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }

            int saclIndex = sddl.IndexOf("S:", StringComparison.Ordinal);
            string updated = saclIndex >= 0
                ? sddl.Insert(saclIndex, userAce)
                : sddl + userAce;
            RunSc("sdset " + ServiceName + " \"" + updated + "\"");
        }

        private static string RunSc(string arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = Path.Combine(Environment.SystemDirectory, "sc.exe");
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("sc.exe 失败（" + process.ExitCode + "）：" + error + output);
                }
                return output;
            }
        }

        private static bool ServiceExists()
        {
            foreach (ServiceController service in ServiceController.GetServices())
            {
                using (service)
                {
                    if (String.Equals(service.ServiceName, ServiceName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static AdapterInfo ShowAdapterSelection()
        {
            List<AdapterInfo> adapters = AdapterRepository.GetPhysicalAdapters();
            if (adapters.Count == 0)
            {
                MessageBox.Show(
                    "没有找到可切换的物理网络适配器。",
                    "网卡切换",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return null;
            }

            using (AdapterSelectionForm form = new AdapterSelectionForm(adapters))
            {
                return form.ShowDialog() == DialogResult.OK ? form.SelectedAdapter : null;
            }
        }

        private static void ToggleAdapter(AdapterInfo adapter)
        {
            AdapterInfo current = AdapterRepository.GetAdapter(adapter.Id);
            if (current == null)
            {
                Configuration.Delete();
                throw new InvalidOperationException("之前选择的网卡已经不存在。请按住 Shift 再双击程序，重新选择网卡。");
            }

            Guid requestId = Guid.NewGuid();
            using (ServiceController controller = new ServiceController(ServiceName))
            {
                controller.Start(new string[] { current.Id.ToString("D"), requestId.ToString("D") });
            }

            ServiceResult result = WaitForResult(requestId, TimeSpan.FromSeconds(15));
            if (result == null)
            {
                throw new System.TimeoutException("等待网卡切换结果超时。请查看日志：" + Paths.LogPath);
            }
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Message + Environment.NewLine + "日志：" + Paths.LogPath);
            }

            string stateText = result.Enabled ? "已启用" : "已禁用";
            ShowNotification("网卡切换完成", current.ConnectionName + " " + stateText, ToolTipIcon.Info);
        }

        private static ServiceResult WaitForResult(Guid requestId, TimeSpan timeout)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                ServiceResult result = ServiceResult.TryRead(Paths.ResultPath);
                if (result != null && result.RequestId == requestId)
                {
                    return result;
                }
                Thread.Sleep(100);
            }
            return null;
        }

        private static void ShowNotification(string title, string text, ToolTipIcon icon)
        {
            using (NotifyIcon notification = new NotifyIcon())
            {
                notification.Icon = SystemIcons.Information;
                notification.Visible = true;
                notification.BalloonTipTitle = title;
                notification.BalloonTipText = text;
                notification.BalloonTipIcon = icon;
                notification.ShowBalloonTip(3500);

                Stopwatch stopwatch = Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < 3800)
                {
                    Application.DoEvents();
                    Thread.Sleep(50);
                }
                notification.Visible = false;
            }
        }
    }

    internal sealed class AdapterToggleService : ServiceBase
    {
        public AdapterToggleService()
        {
            ServiceName = Program.ServiceName;
            CanStop = true;
            AutoLog = false;
        }

        protected override void OnStart(string[] args)
        {
            Thread worker = new Thread(delegate() { ToggleAndStop(args); });
            worker.IsBackground = false;
            worker.Start();
        }

        private void ToggleAndStop(string[] args)
        {
            Guid requestId = Guid.Empty;
            bool success = false;
            bool enabled = false;
            string message = "未知错误";

            try
            {
                Guid adapterId;
                if (args == null || args.Length < 2 ||
                    !Guid.TryParse(args[0], out adapterId) ||
                    !Guid.TryParse(args[1], out requestId))
                {
                    throw new ArgumentException("服务收到的网卡或请求标识无效。");
                }

                enabled = AdapterRepository.ToggleAdapter(adapterId);
                success = true;
                message = enabled ? "网卡已启用。" : "网卡已禁用。";
                Logger.Write("Successfully toggled adapter " + adapterId + "; enabled=" + enabled + ".");
            }
            catch (Exception exception)
            {
                message = exception.Message.Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
                Logger.Write("ERROR: " + exception);
            }
            finally
            {
                if (requestId != Guid.Empty)
                {
                    ServiceResult.Write(Paths.ResultPath, requestId, success, enabled, message);
                }
                ExitCode = success ? 0 : 1;
                Stop();
            }
        }
    }

    [RunInstaller(true)]
    public sealed class ProjectInstaller : Installer
    {
        public ProjectInstaller()
        {
            ServiceProcessInstaller processInstaller = new ServiceProcessInstaller();
            processInstaller.Account = ServiceAccount.LocalSystem;

            ServiceInstaller serviceInstaller = new ServiceInstaller();
            serviceInstaller.ServiceName = Program.ServiceName;
            serviceInstaller.DisplayName = "通用网卡切换服务";
            serviceInstaller.StartType = ServiceStartMode.Manual;

            Installers.Add(processInstaller);
            Installers.Add(serviceInstaller);
        }
    }

    internal sealed class AdapterInfo
    {
        public Guid Id;
        public string ConnectionName;
        public string Description;
        public string AdapterType;
        public bool Enabled;
    }

    internal static class AdapterRepository
    {
        public static List<AdapterInfo> GetPhysicalAdapters()
        {
            List<AdapterInfo> result = new List<AdapterInfo>();
            string query = "SELECT GUID, NetConnectionID, Description, AdapterType, NetEnabled " +
                "FROM Win32_NetworkAdapter WHERE PhysicalAdapter=True AND NetConnectionID IS NOT NULL";

            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
            using (ManagementObjectCollection items = searcher.Get())
            {
                foreach (ManagementObject item in items)
                {
                    using (item)
                    {
                        AdapterInfo adapter = ToAdapterInfo(item);
                        if (adapter != null)
                        {
                            result.Add(adapter);
                        }
                    }
                }
            }

            result.Sort(delegate(AdapterInfo left, AdapterInfo right)
            {
                return StringComparer.CurrentCultureIgnoreCase.Compare(left.ConnectionName, right.ConnectionName);
            });
            return result;
        }

        public static AdapterInfo GetAdapter(Guid id)
        {
            string guidText = id.ToString("B").ToUpperInvariant();
            string query = "SELECT GUID, NetConnectionID, Description, AdapterType, NetEnabled " +
                "FROM Win32_NetworkAdapter WHERE GUID='" + guidText + "'";

            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
            using (ManagementObjectCollection items = searcher.Get())
            {
                foreach (ManagementObject item in items)
                {
                    using (item)
                    {
                        return ToAdapterInfo(item);
                    }
                }
            }
            return null;
        }

        public static bool ToggleAdapter(Guid id)
        {
            string guidText = id.ToString("B").ToUpperInvariant();
            string query = "SELECT * FROM Win32_NetworkAdapter WHERE GUID='" + guidText + "'";

            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
            using (ManagementObjectCollection items = searcher.Get())
            {
                foreach (ManagementObject adapter in items)
                {
                    using (adapter)
                    {
                        bool isEnabled = adapter["NetEnabled"] != null && Convert.ToBoolean(adapter["NetEnabled"]);
                        string methodName = isEnabled ? "Disable" : "Enable";
                        using (ManagementBaseObject invokeResult = adapter.InvokeMethod(methodName, null, null))
                        {
                            uint returnValue = Convert.ToUInt32(invokeResult["ReturnValue"]);
                            if (returnValue != 0)
                            {
                                throw new InvalidOperationException(methodName + " 返回错误代码 " + returnValue + "。");
                            }
                        }
                        return !isEnabled;
                    }
                }
            }

            throw new InvalidOperationException("找不到指定的网络适配器。");
        }

        private static AdapterInfo ToAdapterInfo(ManagementObject item)
        {
            string guidText = Convert.ToString(item["GUID"]);
            Guid id;
            if (!Guid.TryParse(guidText, out id))
            {
                return null;
            }

            AdapterInfo info = new AdapterInfo();
            info.Id = id;
            info.ConnectionName = Convert.ToString(item["NetConnectionID"]);
            info.Description = Convert.ToString(item["Description"]);
            info.AdapterType = Convert.ToString(item["AdapterType"]);
            info.Enabled = item["NetEnabled"] != null && Convert.ToBoolean(item["NetEnabled"]);
            return info;
        }
    }

    internal sealed class AdapterSelectionForm : Form
    {
        private readonly ListView adapterList;
        public AdapterInfo SelectedAdapter { get; private set; }

        public AdapterSelectionForm(List<AdapterInfo> adapters)
        {
            Text = "选择要切换的网卡";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 390);
            MinimumSize = new Size(640, 360);
            Font = new Font("Microsoft YaHei UI", 9F);
            Icon = SystemIcons.Information;

            Label heading = new Label();
            heading.Text = adapters.Count > 1
                ? "检测到多个物理网卡，请选择双击程序时要启用/禁用的网卡。"
                : "请确认双击程序时要启用/禁用的网卡。";
            heading.AutoSize = false;
            heading.Height = 50;
            heading.Dock = DockStyle.Top;
            heading.Padding = new Padding(12, 14, 12, 4);

            adapterList = new ListView();
            adapterList.Dock = DockStyle.Fill;
            adapterList.View = View.Details;
            adapterList.FullRowSelect = true;
            adapterList.HideSelection = false;
            adapterList.MultiSelect = false;
            adapterList.Columns.Add("连接名称", 135);
            adapterList.Columns.Add("硬件描述", 365);
            adapterList.Columns.Add("状态", 90);
            adapterList.Columns.Add("类型", 130);

            foreach (AdapterInfo adapter in adapters)
            {
                ListViewItem item = new ListViewItem(adapter.ConnectionName);
                item.SubItems.Add(adapter.Description);
                item.SubItems.Add(adapter.Enabled ? "已启用" : "已禁用");
                item.SubItems.Add(String.IsNullOrWhiteSpace(adapter.AdapterType) ? "未知" : adapter.AdapterType);
                item.Tag = adapter;
                adapterList.Items.Add(item);
            }

            Label help = new Label();
            help.Text = "通常应选择有线以太网网卡。以后可按住 Shift 再双击程序来重新选择。";
            help.AutoSize = false;
            help.Height = 42;
            help.Dock = DockStyle.Bottom;
            help.Padding = new Padding(12, 10, 12, 4);

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Bottom;
            buttons.Height = 52;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.Padding = new Padding(8);

            Button confirm = new Button();
            confirm.Text = "确认选择";
            confirm.AutoSize = true;
            confirm.Click += delegate
            {
                if (adapterList.SelectedItems.Count == 0)
                {
                    MessageBox.Show("请先选择一个网卡。", "网卡切换", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                SelectedAdapter = (AdapterInfo)adapterList.SelectedItems[0].Tag;
                DialogResult = DialogResult.OK;
                Close();
            };

            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.AutoSize = true;
            cancel.DialogResult = DialogResult.Cancel;

            buttons.Controls.Add(confirm);
            buttons.Controls.Add(cancel);
            Controls.Add(adapterList);
            Controls.Add(help);
            Controls.Add(buttons);
            Controls.Add(heading);
            AcceptButton = confirm;
            CancelButton = cancel;

            Shown += delegate
            {
                if (adapterList.Items.Count > 0)
                {
                    adapterList.Items[0].Selected = true;
                    adapterList.Items[0].Focused = true;
                }
            };
            adapterList.DoubleClick += delegate { confirm.PerformClick(); };
        }
    }

    internal static class Configuration
    {
        public static AdapterInfo LoadAdapter()
        {
            try
            {
                if (!File.Exists(Paths.ConfigPath))
                {
                    return null;
                }
                string text = File.ReadAllText(Paths.ConfigPath, Encoding.UTF8).Trim();
                Guid id;
                if (!Guid.TryParse(text, out id))
                {
                    return null;
                }
                return AdapterRepository.GetAdapter(id);
            }
            catch
            {
                return null;
            }
        }

        public static void SaveAdapter(AdapterInfo adapter)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.ConfigPath));
            File.WriteAllText(Paths.ConfigPath, adapter.Id.ToString("D"), Encoding.UTF8);
        }

        public static void Delete()
        {
            if (File.Exists(Paths.ConfigPath))
            {
                File.Delete(Paths.ConfigPath);
            }
        }
    }

    internal sealed class ServiceResult
    {
        public Guid RequestId;
        public bool Success;
        public bool Enabled;
        public string Message;

        public static void Write(string path, Guid requestId, bool success, bool enabled, string message)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string content = requestId.ToString("D") + "|" + success + "|" + enabled + "|" + message;
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        public static ServiceResult TryRead(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }
                string[] parts = File.ReadAllText(path, Encoding.UTF8).Split(new char[] { '|' }, 4);
                Guid requestId;
                if (parts.Length != 4 || !Guid.TryParse(parts[0], out requestId))
                {
                    return null;
                }
                ServiceResult result = new ServiceResult();
                result.RequestId = requestId;
                result.Success = Boolean.Parse(parts[1]);
                result.Enabled = Boolean.Parse(parts[2]);
                result.Message = parts[3];
                return result;
            }
            catch
            {
                return null;
            }
        }
    }

    internal static class Paths
    {
        private static readonly string CommonDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "LanSwitch");

        public static readonly string ResultPath = Path.Combine(CommonDirectory, "last-result.txt");
        public static readonly string LogPath = Path.Combine(CommonDirectory, "LanSwitch.log");
        public static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LanSwitch",
            "adapter.txt");
    }

    internal static class Logger
    {
        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Paths.LogPath));
                File.AppendAllText(
                    Paths.LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
            }
        }
    }
}

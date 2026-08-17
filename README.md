# LanSwitch

首次安装授权一次，之后无需 UAC 弹窗，一键启用或禁用指定网卡，让 Windows 自动在有线与无线网络之间切换。

## 功能

- 单文件程序，无需额外的 BAT、VBS 或安装包
- 首次运行自动列出本机物理网卡，引导用户选择并确认
- 支持不同品牌、不同名称的有线、无线及 USB 网卡
- 日常双击切换不再弹出 UAC 或命令行窗口
- 切换成功后显示“已启用 / 已禁用”桌面通知
- 按住 `Shift` 双击程序即可重新选择网卡
- 后台服务仅按需启动，切换完成后立即停止

## 下载

从 [Releases](https://github.com/lmy138/LanSwitch/releases/latest) 下载 `LanSwitch.exe`。带到其他电脑时只需复制这一个文件。

## 使用方法

1. 双击 `LanSwitch.exe`。
2. 首次运行时确认一次 UAC，用于安装权限受限的按需服务。
3. 在网卡列表中选择需要切换的网卡。一般选择有线以太网网卡；禁用后 Windows 会转用可用的无线网络。
4. 以后直接双击程序即可切换，完成后会显示桌面通知。

如需更改所选网卡，按住 `Shift` 再双击 `LanSwitch.exe`，或运行：

```powershell
LanSwitch.exe --configure
```

## 为什么首次仍需 UAC？

Windows 要求管理员权限才能启用或禁用网络适配器。LanSwitch 首次运行时安装一个手动启动的本机服务，并只授予当前用户“启动该服务”的权限。之后的切换由该服务执行，因此不需要反复确认 UAC。服务不会常驻，程序也不会关闭全局 UAC。

服务只接受格式正确的网卡 GUID 和请求 GUID，程序文件安装在受保护的 `Program Files` 目录中，普通用户不能替换服务程序或修改服务配置。

## 系统要求

- Windows 10 或 Windows 11
- .NET Framework 4.x

程序目前未使用商业代码签名证书，从浏览器下载后 Windows SmartScreen 可能在首次运行时显示提示。

## 从源码构建

在 Windows PowerShell 中运行：

```powershell
.\build.ps1
```

生成文件位于 `dist\LanSwitch.exe`。构建脚本使用 Windows 自带的 .NET Framework C# 编译器，不需要 Visual Studio。

## 卸载

在管理员 PowerShell 中执行：

```powershell
Stop-Service LanSwitchService -ErrorAction SilentlyContinue
sc.exe delete LanSwitchService
Remove-Item "$env:ProgramFiles\LanSwitch" -Recurse -Force
Remove-Item "$env:LOCALAPPDATA\LanSwitch" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$env:ProgramData\LanSwitch" -Recurse -Force -ErrorAction SilentlyContinue
```

## License

[MIT](LICENSE)

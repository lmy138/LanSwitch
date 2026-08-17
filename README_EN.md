# LanSwitch

[简体中文](README.md) | [English](README_EN.md)

Authorize once during installation, then enable or disable a selected network adapter with one click—without recurring UAC prompts—so Windows can switch automatically between wired and wireless networking.

## Features

- A single executable with no BAT, VBS, or separate installer
- Lists physical network adapters on first launch and asks the user to select and confirm one
- Works with wired, wireless, and USB adapters from different vendors and with different connection names
- No UAC prompt or command-line window during everyday switching
- Shows a desktop notification after an adapter is enabled or disabled
- Hold `Shift` while double-clicking the program to choose a different adapter
- Uses an on-demand service that stops immediately after completing the switch

## Download

Download `LanSwitch.exe` from [Releases](https://github.com/lmy138/LanSwitch/releases/latest). Only this one file needs to be copied to another computer.

## Usage

1. Double-click `LanSwitch.exe`.
2. Approve UAC once on the first launch. This installs the restricted on-demand service.
3. Select the network adapter you want to toggle. Usually this is the wired Ethernet adapter; once disabled, Windows can fall back to an available wireless connection.
4. Double-click the program whenever you want to switch. A desktop notification confirms the result.

To select a different adapter, hold `Shift` while double-clicking `LanSwitch.exe`, or run:

```powershell
LanSwitch.exe --configure
```

## Why is UAC still required on the first launch?

Windows requires administrator privileges to enable or disable a network adapter. LanSwitch installs a manual local service on first launch and grants the current user permission only to start that service. Later switches run through the service, so repeated UAC approval is unnecessary. The service does not remain running, and LanSwitch does not disable UAC globally.

The service accepts only valid adapter and request GUIDs. Its executable is installed under the protected `Program Files` directory, so a standard user cannot replace the service executable or modify its configuration.

## Requirements

- Windows 10 or Windows 11
- .NET Framework 4.x

The executable is not commercially code-signed. Windows SmartScreen may therefore display a warning the first time a downloaded copy is launched.

## Build from source

Run the following command in Windows PowerShell:

```powershell
.\build.ps1
```

The resulting executable is written to `dist\LanSwitch.exe`. The build script uses the .NET Framework C# compiler included with Windows, so Visual Studio is not required.

## Uninstall

Run the following commands in an elevated PowerShell window:

```powershell
Stop-Service LanSwitchService -ErrorAction SilentlyContinue
sc.exe delete LanSwitchService
Remove-Item "$env:ProgramFiles\LanSwitch" -Recurse -Force
Remove-Item "$env:LOCALAPPDATA\LanSwitch" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$env:ProgramData\LanSwitch" -Recurse -Force -ErrorAction SilentlyContinue
```

## License

[MIT](LICENSE)

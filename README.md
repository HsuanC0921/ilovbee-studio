# iLovbee Studio

A Windows keyboard settings app for the BY Tech B87, built with Rust. No .NET runtime is required. The interface is in Traditional Chinese and offers system, light, and dark themes.

[繁體中文](README.zh-TW.md) · [Download the latest release](https://github.com/HsuanC0921/ilovbee-studio/releases/latest) · [Release notes](docs/releases/0.9.0.md)

## Get started

Requires Windows 10/11 x64 and a USB connection for configuration. Supported device: `258A:0199`, release `2010`, usage `FF00:0001`, with a 520-byte Feature Report. Other keyboards sold as B87 may be incompatible.

1. Download and extract the Windows x64 release. Exit the manufacturer's DeviceDriver and any older iLovbee Studio process.
2. Connect the keyboard over USB and set its switch to wired mode.
3. Run `iLovbeeStudio.exe` and create a backup before editing settings.

The app window requires WebView2 Runtime. Closing the window leaves the app running in the system tray; use the tray menu to exit. To try the interface without accessing your keyboard or audio, run the included `離線示範.cmd` (offline demo).

## Features

- **Key mapping and macros:** Four layers, drafts and undo, macro recording and editing, and scheme import/export.
- **Lighting and audio effects:** Built-in backlighting, side-light colors, per-key RGB, custom effects, and system-audio visualization.
- **System mode and Win Lock:** Read and change Windows/Mac mode and Win Lock, with a reference for physical shortcuts.
- **Backups:** Export and restore profiles; roll back mapping and lighting writes on failure.

Onboard backlight recording still requires physical keys. Backups exclude onboard macros, custom backlight patterns, and firmware that cannot be read back. The app does not update firmware. See [usage and limitations](docs/usage.en-US.md) and [manual feature coverage](docs/manual-functions.md).

## Build

Requires Rust stable, a native Windows linker, and the Windows SDK. The Tauri window requires WebView2 Runtime.

```powershell
./build.ps1 -WithUi
```

Output goes to `dist/`. [Development setup](docs/usage.en-US.md#build) · [C# feature parity](docs/csharp-parity.md) · [Release gates and test scope](docs/release-validation.md)

MIT License. Independent project, not affiliated with iLovbee or BY Tech.

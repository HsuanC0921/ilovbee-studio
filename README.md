<div align="center">

# iLovbee Studio

**A lightweight, open replacement for the discontinued iLovbee Keyboard app, for the BY Tech B87.**

English · [繁體中文](README.zh-TW.md)

![Platform](https://img.shields.io/badge/platform-Windows%20x64-0078D4)
![.NET](https://img.shields.io/badge/.NET-10%20Native%20AOT-512BD4)
![Tauri](https://img.shields.io/badge/shell-Tauri%202-24C8DB)
![Status](https://img.shields.io/badge/status-personal%20project-lightgrey)

</div>

---

iLovbee Studio configures the BY Tech B87 mechanical keyboard on Windows: key remapping, macros, lighting, per-key RGB and audio-reactive effects. It is one ~3 MB Native AOT executable that runs in the system tray. The settings UI is a local web page, shown in an optional Tauri window with Mica and a native-looking title bar.

The interface is in Traditional Chinese. The vendor app is no longer updated. This project rebuilds its features from the device protocol, decoded by watching real hardware, and adds several features the vendor app does not have.

## Table of contents

- [Features](#features)
- [Supported hardware](#supported-hardware)
- [Getting started](#getting-started)
- [Usage](#usage)
- [How it works](#how-it-works)
- [Safety](#safety)
- [Development](#development)
- [Limitations](#limitations)
- [Disclaimer](#disclaimer)
- [License](#license)

## Features

### Keys and macros

- **Remapping** on all four layers: Base, FN1, FN2 and Tap.
  - Supports regular keys, F13–F24, media keys, Win+D and custom key combinations.
  - Edits are drafts, with undo/redo and batch apply.
- **Macros**: record them or edit by hand, including delays and modifiers. Up to 8 macros are written to the keyboard, and each can be bound to any key. The macro library can be imported and exported.
- **Profiles**: save all four layers locally and apply them in one click. Profiles can be renamed, imported and exported.

### Lighting

- **Built-in modes 1–17** with animated previews, plus brightness, speed, palette colour and colour mixing.
- **Custom RGB per mode**: set any colour for the first palette slot. This is the same setting as the vendor app's colour wheel.
- **Side indicator lights**: switch them on or off with a checkbox, with no Fn+Backspace needed.
- **Keyboard settings**: debounce (office or gaming), Tap on/off and Tap timing, and the wireless sleep timeout.

### Custom lighting (streamed live)

- **Per-key RGB**: paint any key any colour.
- **Per-key effects**: each key is steady, blinking, breathing or rainbow-cycling, with an adjustable speed.
- Rainbow and row-gradient fills, plus a library of up to 20 saved layouts.
- **Music visualizer**: 12 audio-reactive styles driven by whatever Windows is playing. Colours are a rainbow mix or a custom gradient of 1–6 colours, which can run by height, frequency, time or intensity.

### Everything else

- **Automatic backups**: every write is backed up first (the newest 60 are kept), read back to verify, and rolled back on failure. Full profile export and restore.
- Key tester, USB connection status, Bluetooth battery level and low-battery alerts.
- Tray menu with quick toggles and an option to start with Windows.

## Supported hardware

| Device | USB ID | Firmware release |
|---|---|---|
| BY Tech B87 | `258A:0199` | `2010` |

Other keyboards sold as "B87" may use different firmware and are **not** supported. The app refuses to write to any device that does not match exactly.

## Getting started

### Requirements

- Windows 10/11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Visual Studio Build Tools with the C++ workload, used to link Native AOT
- *Optional:* a Rust toolchain, to build the Tauri window

### Build and install

```powershell
git clone https://github.com/HsuanC0921/ilovbee-studio.git
cd ilovbee-studio
./build.ps1               # runs the tests, then publishes to dist/
./scripts/install.ps1     # installs for the current Windows user
```

To include the optional Tauri window, build it first. `build.ps1` picks it up automatically:

```powershell
cd ui
cargo build --release
```

Without it, the settings UI opens as an Edge app window instead, or in the default browser if Edge is missing.

The installer copies the app to `%LOCALAPPDATA%\Programs\iLovbee Studio`, adds a Start menu shortcut, and registers it under Windows **Installed apps**. It needs no administrator rights and does not need the vendor software.

### Uninstall

Uninstall from Windows Settings → Apps, or run `uninstall.ps1` in the install folder. Settings and backups in `%LOCALAPPDATA%\iLovbeeStudio` are kept.

## Usage

1. **Quit the vendor iLovbee app first.** Two programs writing to the keyboard overwrite each other, so iLovbee Studio refuses to write while the vendor driver is running.
2. Connect the keyboard by USB and switch it to wired mode.
3. Launch **iLovbee Studio** and click **重新讀取** (Reload) to read the keyboard.

Closing the window keeps the app in the system tray (about 20 MB). Use **結束小工具** (Exit) in the tray menu to quit. The tray menu can also toggle the music and custom lighting, and turn **開機自動啟動** (Start with Windows) on or off.

Custom lighting and the music visualizer are streamed frame by frame from the tray app. They only run while the app is running. When you stop them, the keyboard returns to its previous lighting mode.

## How it works

```
┌──────────────────────┐   HID feature report 6 (520 bytes)   ┌──────────┐
│ iLovbeeStudio.exe    │ ───────────────────────────────────▶ │ B87      │
│  · tray + live loop  │   keymaps · lighting · colour table   │ firmware │
│  · localhost server  │   macros · live LED frames            └──────────┘
└─────────┬────────────┘
          │ http://127.0.0.1:<random port>
┌─────────▼────────────┐
│ iLovbeeUI.exe (Tauri)│  or an Edge app window
└──────────────────────┘
```

The full protocol notes are in [`docs/firmware.md`](docs/firmware.md): commands, payload offsets, the 0x0a colour table and live frames. Feature parity with the vendor app is tracked in [`docs/parity.md`](docs/parity.md).

### Project layout

| Path | Contents |
|---|---|
| `src/` | Device protocol, HID access, codecs and lighting engine (shared with tests) |
| `app/` | Tray app, local HTTP API and web UI (`app/web/`) |
| `ui/` | Optional Tauri 2 window shell |
| `tests/` | Self-checking test runner |
| `scripts/` | Per-user install and uninstall scripts |
| `docs/` | Protocol research and parity notes |

## Safety

Hardware writes are deliberately conservative:

- **Backup → write → read back → roll back on mismatch.** Writes the keyboard cannot read back are backed up beforehand. These are macros and the colour table; the colour table is also saved locally.
- Only known commands with exact lengths are allowed. The app never sends firmware-update, ISP or DFU commands.
- The FN key cannot be remapped away.

The local web UI listens only on `127.0.0.1` on a random port. It requires a one-time token issued at startup (an HttpOnly, SameSite=Strict cookie) and checks the `Host` and `Origin` headers, so other websites cannot call it.

## Development

```powershell
dotnet run --project tests/Checks.csproj -c Release
```

The tests cover:

- packet headers and length limits
- profile validation
- key and macro encoding, checked against packets captured from the vendor app
- the colour table
- per-key effects and the music visualizer

`build.ps1` runs the tests before every publish.

## Limitations

- **The side-light colour cannot be changed.** No command for it has been found, and the vendor app has no such setting either.
- The lighting previews are software simulations, and a few modes differ slightly from the hardware.
- Battery level is the last value Windows reported over Bluetooth, so the keyboard must be paired at least once.
- Bluetooth firmware is untouched, so this project makes no claim of improving wireless stability.

## Disclaimer

This is an independent personal project. It is not affiliated with or endorsed by iLovbee or BY Tech. No vendor executables, firmware or user data are distributed. You use it at your own risk, although every write is backed up and verified.

## License

No license has been chosen yet. Until one is added, all rights are reserved by the author.

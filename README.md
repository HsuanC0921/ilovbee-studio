# iLovbee Studio

A Windows settings tool for the BY Tech B87 keyboard. Version 0.8.0 replaces the C# host with native Rust: HID access, verified writes and rollback, persistence, Win32 tray, Bluetooth battery reporting, WASAPI loopback, lighting and the local HTTP API. There is no C#/.NET build or runtime dependency.

[繁體中文與完整操作說明](README.zh-TW.md) · [Manual feature coverage](docs/manual-functions.md) · [Migration and validation](docs/rust-migration.md)

Version 0.9.0 restores external configuration synchronization and replaces the transparent shell with readable, opaque light/dark themes. Theme selection follows Windows or a saved manual preference. Text navigation remains visible in narrow windows. Demo mode isolates both USB and audio.

[C# feature parity](docs/csharp-parity.md) · [Release gates and test scope](docs/release-validation.md). Release staging requires unit/integration, browser, security, stress and dependency-advisory checks to pass.

The existing Traditional Chinese HTML/CSS/JavaScript UI is served by Rust, displayed in an optional Rust/Tauri shell or an Edge app window. UI rendering and interaction still use web technologies.

## Build

Windows 10/11 x64, Rust stable, and a native linker are required. MSVC builds need Visual Studio C++ Build Tools and Windows SDK. GNU builds require compatible MinGW tools. The optional Tauri window needs WebView2 at runtime.

```powershell
cargo test --locked --all-targets
cargo clippy --locked --all-targets -- -D warnings
./build.ps1
./build.ps1 -WithUi
./scripts/install.ps1
```

For an isolated offline demo with no USB access:

```powershell
cargo run -- --demo
```

Use `--headless --data-dir <path>` for local API testing. Headless mode accepts `quit` on stdin for graceful shutdown. Its connection file is in the selected data directory and must be kept private.

## Features

Four keymap layers with drafts and undo/redo; up to eight macros; twenty local keymap schemes; built-in lighting and RGB palettes; per-key RGB and effects; twelve audio-reactive styles; layout libraries; backups and profile restore; key testing; Windows Bluetooth battery readings; tray controls and per-user autostart.

The searchable manual page covers fifteen groups of manual features, including Bluetooth slots, receiver pairing, Win lock, Windows/Mac switching, knob controls, onboard lighting recording and factory reset. Features without a confirmed USB command explicitly remain physical keyboard operations.

## Compatibility and limits

Only USB `258A:0199`, release `2010`, usage `FF00:0001`, with a 520-byte Feature Report is accepted. Other keyboards named B87 may be incompatible.

Existing files in `%LOCALAPPDATA%\\iLovbeeStudio` retain their formats. Demo data defaults to a separate `iLovbeeStudio-Demo` directory. Malformed files produce errors and remain intact.

Mappings and lighting are backed up, written, read back and rolled back on failure. FN is protected during apply and restore. Macro buffers and color tables cannot be read back; no complete hardware backup or verified rollback is claimed for them. Profiles contain mappings and lighting settings, not firmware or unknown onboard macro/custom-light contents.

The Rust rewrite has offline, simulated-device and real-B87 coverage. Real tests include four-layer write/readback/restore, protected settings, per-key RGB, audio-reactive streaming and crash recovery. Physical Fn input and webcam observations identified and fixed a missing lighting-configuration selector when applying built-in modes. Some key-triggered effects still need physical-input verification; see `docs/hardware-validation.md`. Earlier observations in `docs/firmware.md` remain separate from the rewrite's evidence.

See the Traditional Chinese README for module layout, installation, data formats and operational details.

MIT License. Independent project, not affiliated with iLovbee or BY Tech.

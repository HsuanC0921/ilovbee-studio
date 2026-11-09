# iLovbee Studio

本專案為個人自用工具，僅依手上的 BY Tech B87 驗證。其他鍵盤或不同版本的 B87 可能不適用。

BY Tech B87 的 Windows 鍵盤設定工具，以 Rust 開發，不需要 .NET。介面使用繁體中文，提供跟隨 Windows、淺色與深色主題。

[English (US)](README.md) · [下載最新版](https://github.com/HsuanC0921/ilovbee-studio/releases/latest) · [發行說明](docs/releases/0.9.0.md)

## 使用

支援 Windows 10／11 x64。設定功能需要 USB 有線模式，限定裝置 `258A:0199`、release `2010`、usage `FF00:0001`、520-byte Feature Report。

1. 下載並解壓縮 Windows x64 版本，結束原廠 DeviceDriver 與舊版 iLovbee Studio。
2. 接上 USB，將鍵盤切至有線模式。
3. 執行 `iLovbeeStudio.exe`，先建立備份，再修改設定。

視窗使用 WebView2 Runtime。關閉視窗後，程式仍在系統匣執行。請從系統匣選單結束程式。想先試用介面，可執行包內的「離線示範.cmd」，不會存取真實鍵盤或音訊。

## 功能

- **改鍵與巨集**：四層映射、草稿與復原、巨集錄製與編輯、方案匯入／匯出。
- **燈光與音樂律動**：內建背光、側燈切色、逐鍵 RGB、自訂效果與系統音訊律動。
- **系統模式與 Win Lock**：讀取及切換 Windows／Mac 模式與 Win Lock，查閱實體快捷鍵。
- **備份**：支援設定檔匯出／還原，映射與燈效寫入失敗時會還原。

機內背光仍需用實體鍵錄製。備份不含無法讀回的機內巨集、自訂背光或韌體。程式不提供韌體更新。完整操作與限制見[使用說明](docs/usage.zh-TW.md)及[手冊功能對照](docs/manual-functions.md)。

## 建置

需要 Rust stable、Windows 原生連結器及 Windows SDK。Tauri 視窗需要 WebView2 Runtime。

```powershell
./build.ps1 -WithUi
```

產物位於 `dist/`。[開發環境與安裝](docs/usage.zh-TW.md#建置) · [C# 功能對照](docs/csharp-parity.md) · [發佈測試與範圍](docs/release-validation.md)

MIT License。獨立專案，與 iLovbee／BY Tech 無隸屬關係。

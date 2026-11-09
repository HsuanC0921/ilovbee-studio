# iLovbee Studio

BY Tech B87 的 Windows 設定工具。0.8.0 將原本的 C# 常駐程式完整改寫為 Rust；建置、執行不再依賴 .NET。介面保留繁體中文 HTML／CSS／JavaScript，由 Rust 提供本機 API，可使用 Rust／Tauri 視窗或 Edge 應用程式視窗。

[English](README.md) · [手冊功能對照](docs/manual-functions.md) · [移轉與驗證](docs/rust-migration.md)

## 使用

支援 Windows 10／11 x64，限定 USB `258A:0199`、release `2010`、usage `FF00:0001`、520-byte Feature Report。其他同名 B87 不代表相容。設定功能需要 USB 有線模式，無線配對仍由鍵盤處理。

1. 結束原廠 DeviceDriver 與舊版 iLovbee Studio。
2. 接上 USB，將鍵盤切至有線模式。
3. 執行 `iLovbeeStudio.exe`，先建立備份，再進行編輯。

關閉設定視窗後，Rust 主程式留在系統匣。從系統匣選單選擇「結束 iLovbee Studio」才會停止背景燈效並還原原模式。

### 離線示範

```powershell
./dist/iLovbeeStudio.exe --demo
```

此模式使用模擬裝置，畫面會明確標示。預設資料夾為 `%LOCALAPPDATA%\iLovbeeStudio-Demo`，與真實設定分開。模擬裝置不會存取 USB，重開程式會重設模擬鍵盤；方案、巨集與燈光配置仍會保存在示範資料夾。

## 功能

- 四層改鍵：預設、FN1、FN2、Tap；草稿、復原、重做、批次套用及草稿匯出／恢復。
- 一般按鍵、F13–F24、自訂組合鍵與媒體動作，含音量提高、降低及停止播放。
- 最多 8 組巨集，支援錄製、事件編輯、20-bit 延遲與巨集庫匯出／匯入。
- 最多 20 組四層方案，支援命名、套用、刪除、匯出／匯入。
- 內建背光 1–17、亮度、速度、色票、自訂 RGB、混色、指示燈開關。
- 防抖、Tap 啟用／時間、無線休眠設定；欄位解碼的實機證據見協定文件。
- 逐鍵 RGB、恆亮／閃爍／呼吸／彩虹，以及最多 20 組燈光配置。
- WASAPI 系統輸出擷取、18 頻段分析、12 種音樂律動、自訂 1–6 色漸層。
- Windows 最近回報的藍牙電量與時間、近期低電量系統匣通知。
- 按鍵測試、USB 狀態、備份保留份數、設定檔匯出及還原。
- 可搜尋、可依支援範圍篩選的手冊功能頁，共 15 項；標示哪些可在軟體設定、哪些仍需實體快捷鍵。

0.8.1 新增側燈切色、Win Lock 與 Windows／Mac 模式的狀態讀取及直接控制；使用實體快捷鍵後請重新讀取。也可將這些功能、機內背光 1／2／3，以及機內背光錄製／儲存指派到實體按鍵。

機內背光仍須按實體鍵錄製，原圖樣無法透過本程式備份或直接寫入。配對及恢復出廠仍依手冊在鍵盤操作。程式不持續輪詢設定；實測持續讀取會造成鍵盤延遲、重複輸入與閃燈。

## 建置

需求：Rust stable。建議使用 Windows MSVC 工具鏈，安裝 Visual Studio C++ Build Tools 與 Windows SDK。GNU 工具鏈也可建置，須備妥相容的 MinGW binutils。Tauri 視窗使用 Windows WebView2 Runtime。

```powershell
cargo test --locked --all-targets
cargo clippy --locked --all-targets -- -D warnings
./build.ps1
./build.ps1 -WithUi   # 同時建置 Tauri 視窗
./scripts/install.ps1
```

安裝腳本會修改目前使用者的程式目錄、開始功能表及解除安裝登錄。設定與備份不會隨解除安裝刪除。不需要安裝原廠軟體。

## 資料與安全

真實資料沿用 `%LOCALAPPDATA%\iLovbeeStudio`。舊版 `macros.json`、`schemes.json`、`customlights.json`、`live.ini`、`backup.ini`、`colortable.hex` 和 schema 1 設定檔保持相容。損壞檔案會回報錯誤並保留，不會靜默清空。

映射與燈效寫入採「檢查快照 → 備份 → 寫入 → 讀回 → 失敗還原」，關閉自動備份不影響記憶體還原。FN 鍵在一般套用、方案及設定檔還原時均受保護。舊三層設定檔還原時保留目前 Tap 層。

巨集緩衝區與色票表沒有已確認的讀回指令，無法宣稱已讀回驗證或完整回復。設定檔備份包含四層映射與 128-byte 燈效設定，不包含鍵盤內未知的巨集、機內自訂背光或韌體。色票最後送出的內容存在本機，寫入前另保留上一份本機色票。

本機 API 只監聽 `127.0.0.1` 隨機埠，驗證 Host、Origin 與工作階段 Cookie，限制請求大小。程式拒絕未允許的 HID 指令，不提供刷寫、ISP 或 DFU。

## 驗證狀態

Rust 離線測試與模擬裝置整合測試不等於實機驗證。改寫版現已對真實 B87 完成四層映射寫入／讀回／還原、設定欄位、逐鍵 RGB、音樂律動及當機恢復測試；也透過 Fn 操作與 webcam 找到並修正內建燈效的配置選擇問題。部分需按鍵觸發的效果仍未完成輸出確認。詳見 [本次實機測試](docs/hardware-validation.md)。舊版實機紀錄仍保留在 [協定筆記](docs/firmware.md)，與新版證據分開。

## 專案結構

| 路徑 | 內容 |
|---|---|
| `rust/` | Rust 協定、服務、資料、燈效、音訊、Win32 桌面與 API |
| `app/web/` | 繁中設定介面及手冊功能 |
| `src/keyboard.json` | 共用實體鍵盤座標 |
| `ui/` | Rust／Tauri 視窗 |
| `tests/` | Rust 核心及真實 HTTP 整合測試 |
| `scripts/` | 使用者層級安裝／解除安裝 |
| `docs/` | 協定證據、功能對照、移轉及驗證說明 |

獨立專案，與 iLovbee／BY Tech 無隸屬關係。不散布原廠軟體或韌體。MIT License。

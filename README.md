# iLovbee Studio

個人使用的 B87 繁體中文 Windows 桌面工具。C#、.NET 10、**WinUI 3**，不是 WPF 外觀套件或 WebView。

支援硬體：BY Tech B87，USB `258A:0199`，release `2010`。其他同名 B87 尚不適用。

## 功能

- 按鍵重設：搜尋一般按鍵、F13–F24、媒體鍵、Win+D，或自訂 Ctrl／Shift／Alt／Win 組合鍵。
- 跨層編輯草稿、復原／重做、單鍵取消、變更清單與批次套用；草稿自動保存在本機。
- 內建燈效模式；側燈使用實體 Fn＋Backspace。
- 三層映射與燈效設定檔匯出／還原。
- 修改前完整備份、寫入後讀回核對；失敗時嘗試還原並確認結果。
- 視窗內按鍵測試、USB 連線狀態、藍牙電量與本機診斷匯出。
- 獨立 NativeAOT 系統匣小工具：電量圖示、提示、低電量通知、更新與開啟主程式。

原小算盤鍵目前設為 Win+D：按一次顯示桌面，再按一次回復視窗。旋鈕位於 Pause 位置，圖中的旋鈕可開啟測試。

電量讀取 Windows 最近回報的藍牙百分比與時間，已在實機取得 100%；首次需要配對並連接藍牙。切回 USB 後可能顯示最近一次回報，並非持續即時測量。研究細節留在 [開發文件](docs/firmware.md)。

## 建置與執行

Windows x64、.NET 10 SDK。以 PowerShell 執行：

```powershell
./build.ps1
./dist/iLovbeeStudio.WinUI.exe
# 只需要輕量系統匣小工具時
./dist/iLovbeeWidget.exe
```

發佈結果包含 .NET 與 Windows App SDK 執行階段，請保留整個 dist 資料夾。無須安裝原廠軟體或以管理員執行。

小工具可單獨執行；本機測量 exe 約 1.94 MB，工作集約 16.8 MB、私有記憶體約 4.8 MB，實際用量隨系統而異。主介面不必常駐。沒有自動設定開機啟動。

開啟前請結束其他 iLovbee 設定程式。連接 USB 並切至有線模式，再按「重新讀取」。設定、備份與診斷存在 `%LOCALAPPDATA%\iLovbeeStudio`，不會自動上傳。

## 驗證

```powershell
dotnet run --project tests/Checks.csproj -c Release
# 選用：讀取實際裝置，結果只存本機，包含裝置路徑與設定內容
dotnet run --project tests/Checks.csproj -c Release -- --probe C:\path\probe.json
```

測試涵蓋封包長度與標頭、設定檔硬體／層數／長度限制、按鍵編碼。實際裝置 probe 只使用設定查詢，沒有韌體 dump 或刷寫。

`--hardware-smoke` 是選用的實機測試，會暫時將兩層的小算盤位置改為 F13，再於 finally 還原完整映射；執行前必須連接相容硬體並結束其他設定程式。不要把它當成唯讀 probe。

`iLovbeeStudio.WinUI.exe --ui-check C:\path\captures` 使用程式內版面檢查與 RenderTargetBitmap，檢查五頁與兩種桌面寬度後退出，不操作使用者的滑鼠或鍵盤。

## 範圍

這是獨立編寫的個人工具，不隸屬 iLovbee。未散布原廠執行檔、韌體、使用者設定或裝置診斷。尚未修改藍牙韌體；目前不能宣稱已改善藍牙穩定性。

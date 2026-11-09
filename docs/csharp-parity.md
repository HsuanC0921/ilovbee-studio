# C# 功能移轉對照

比對基準為最後一版 C# 主線 `c75e3a6`，不是原廠控制器。原始碼保留於 Git 歷史。正式程式沒有 .NET 執行相依。

| C# 功能／來源 | Rust 對應 | 驗證 |
|---|---|---|
| `app/Api.cs` 的 24 個 HTTP 入口 | `rust/api.rs` 與靜態資源入口 | 從舊程式擷取的路由契約，由真實 HTTP 整合測試逐項確認。另外測試有效與無效請求 |
| `KeyActions.cs`、`MappingEditor.cs`：一般鍵、組合鍵、媒體鍵、巨集指派，草稿與復原重做 | `actions.rs`、`service.rs`、`app/web/remap.js` | 按鍵／巨集編碼測試。瀏覽器測試草稿續接、復原重做及套用 |
| `Macros.cs`：事件編輯、錄製、複製、刪除、匯入匯出及上傳 | `model.rs`、`service.rs`、`app/web/macros.js` | 原廠巨集封包固定樣本、容量及延遲界線。瀏覽器事件錄製與操作。原生瀏覽器檔案選擇器另測 |
| `Schemes.cs`：四層方案建立、改名、套用、刪除及匯入匯出 | `api.rs`、`model.rs`、`app/web/misc.js` | 方案 API、完整 HTTP 流程、瀏覽器方案操作 |
| `Hardware.cs`：設定讀寫、FN 保護、衝突檢查、備份與還原 | `protocol.rs`、`windows.rs`、`service.rs` | 識別與指令白名單、過期快照、FN、失敗讀回及反向還原。保留之前實機證據 |
| `Hardware.cs`、備份設定：手動／自動備份及獨立保留份數 | `store.rs`、`api.rs` | 檔案原子替換、保留份數、路徑穿越拒絕、HTTP 與原生確認對話框 |
| 內建背光、亮度、速度、色票、防抖、Tap、休眠與指示燈 | `service.rs`、`app/web/lighting.js` | 設定交易測試、瀏覽器讀回核對流程。硬體行為範圍另見實機報告 |
| `LightLayouts.cs`、`LedFrame.cs`：126 槽 RGB、逐鍵行為與配置庫 | `lighting.rs`、`live.rs`、`app/web/custom.js` | 動畫／漸層測試、配置 API、瀏覽器繪色及配置存取 |
| `AudioLoopback.cs`、`LedFrame.cs`：12 種音樂律動、頻譜、配色與靈敏度 | `audio.rs`、`lighting.rs`、`live.rs` | 頻譜固定輸入、12 種渲染器、瀏覽器樣式切換。本版示範使用模擬頻譜，不開啟真實音訊 |
| `LiveConfig.cs`、`Program.cs`：外部 `live.ini` 變更同步 | `Config::stamp`、`App::reload_config` | 本次補回。測試執行中載入、停止、無效內容保留最後有效設定。只檢查檔案，不輪詢 HID |
| 常駐系統匣、音樂與配置快速切換、停止還原、單一執行個體 | `desktop.rs`、`live.rs`、`windows.rs` | 對照原選單與行為。即時燈效標記、正常退出及恢復測試。未藉測試修改使用者的開機設定 |
| Windows 藍牙電量、近期低電量提醒與 DPI 圖示 | `windows.rs`、`desktop.rs` | 對照原 Windows 裝置屬性。本次補回依 DPI 產生圖示，近期門檻維持 10 分鐘。無藍牙回報時不捏造百分比 |

保留 PascalCase JSON、UTF-8 BOM、舊三層備份與既有設定檔路徑。格式損壞會明確失敗並保留檔案。重複名稱、超量資料及錯誤值的拒絕屬於驗證強化，不移植舊版的靜默失敗行為。

這份對照確認軟體功能與資料契約，沒有將「存在入口」當成每個硬體效果都已驗證。0.9.0 的壓力、資安與瀏覽器測試均使用隔離示範裝置。未使用鏡頭、實體鍵盤壓測或真實音訊擷取。機內背光圖樣仍不可由軟體直接備份／寫入。這也不是 C# 版已完成的功能。

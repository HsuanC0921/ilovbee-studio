# Rust 移轉與驗證

版本：0.9.0。日期：2026-11-09。

0.9.0 的完整 C# 功能對照見 [csharp-parity.md](csharp-parity.md)，新增的正式發佈門檻見 [release-validation.md](release-validation.md)。本次重做側欄與深淺主題、補回外部 `live.ini` 同步及 DPI 圖示，並隔離示範音訊。本次沒有使用鏡頭或對實體鍵盤壓測。以下 0.8.x 實機紀錄保留原範圍。

0.8.1 新增側燈色彩、Win Lock、Windows／Mac 模式的直接控制與八種鍵盤內建功能指派。機內錄製仍需實體按鍵操作，原圖樣無法讀回備份。實機合併寫入及完整還原通過。35 項 Rust 測試與 13 項瀏覽器流程通過。診斷改為手動觸發快照，避免持續 HID 輪詢造成延遲及重複輸入。

後續已執行真實 B87 與 webcam 自動測試。目前證據、通過範圍與未結案項目見 [hardware-validation.md](hardware-validation.md)。

## 正式執行路徑

`iLovbeeStudio.exe` 是 Rust 執行檔，包含 Win32 系統匣、藍牙電量、WASAPI loopback、HID 通訊、設定儲存、背景燈效與 HTTP API。`iLovbeeUI.exe` 是選用的 Rust／Tauri 視窗，連至同一個本機 API。沒有外殼時使用 Edge 或預設瀏覽器。

舊 C# 原始碼與 `.csproj` 已從工作樹移除，原始實作可從 Git 歷史取得。HTML／CSS／JavaScript 仍負責畫面、按鍵草稿與使用者互動。這次不是將網頁介面改寫成純 Rust UI。

| 模組 | 責任 |
|---|---|
| `protocol.rs` | 520-byte 封包、讀寫指令及長度白名單、回應標頭核對、Transport 邊界 |
| `windows.rs` | SetupAPI／HID RAII、精確裝置識別、原廠程式衝突、按鍵閒置、藍牙屬性 |
| `service.rs` | 快照衝突檢查、FN 保護、寫入核對、反向還原、巨集與色票 |
| `model.rs` | 舊 JSON 模型、十六進位資料與巨集編碼驗證 |
| `store.rs` | 原格式儲存、同目錄暫存檔與原子替換、備份及獨立保留份數 |
| `lighting.rs` | 逐鍵行為、漸層、18 頻段 Goertzel 與 12 種律動 |
| `audio.rs` | 預設播放裝置 WASAPI loopback，浮點格式轉換、COM 生命週期 |
| `live.rs` | 即時串流、持久還原標記、停止及異常結束後恢復 |
| `api.rs` | 相容 API、工作階段 Cookie、Host／Origin 與大小限制 |
| `desktop.rs` | 系統匣、近期低電量通知、方案快速切換、開機啟動 |

## 資料相容

正式版使用原本的 `%LOCALAPPDATA%\iLovbeeStudio`。示範版使用不同目錄。`--data-dir` 可指定隔離資料夾。沒有自動掃描或改寫使用者的舊資料夾。

- 保留 PascalCase JSON 欄位、schema 1、四層方案及巨集事件格式。
- 接受舊三層 Profile。還原時不碰目前 Tap 層。四層方案仍要求完整四層。
- 接受 UTF-8 BOM。損壞資料會阻止啟動並回報檔名，保留原檔。
- 保留 live.ini、backup.ini、colortable.hex。缺省值與舊版一致。非法值會明確失敗，不自動覆寫原檔。
- 巨集延遲支援 0–0xFFFFF ms。超出範圍會拒絕，避免靜默截斷。
- 讀寫共用單一服務鎖，避免背景畫面和設定封包交錯。

## 修正的行為

- Profile 還原也必須保留目前 FN 鍵。
- 全部資料、巨集引用及鍵盤快照先通過檢查，才會寫入巨集與映射。
- 寫入失敗時回報原始錯誤。回復失敗不會顯示成功。
- 若巨集上傳後映射失敗，訊息會明確指出巨集可能已更新，無法承諾完整回復。
- 套用系統匣的燈光配置同時載入逐鍵行為。舊配置缺少 Fx 時使用恆亮。
- 音訊端點沒有新封包時補入靜音，避免最後一段頻譜永久停留。
- 示範模式不會退回真實 HID，也不是 USB 發生錯誤時的自動替代。
- 連線權杖交接 Cookie 後從畫面網址移除。回應不傳遞 Referer。

## 驗證方式

`cargo test --locked --all-targets` 包含核心測試與啟動真實執行檔的 HTTP 整合測試。模擬 Transport 可注入第二次寫入失敗、讀回不符及回復失敗，覆蓋使用者可以觀察到的結果。

`cargo clippy --locked --all-targets -- -D warnings` 和 `cargo fmt --all --check` 是建置檢查。`build.ps1` 先檢查再發佈，`-WithUi` 另外建置 Tauri 外殼。

本機主程式及 Tauri 外殼採 Windows GNU 工具鏈建置。工作目錄內準備工具鏈，未修改系統開發環境。0.8.0 的 Windows MSVC 建置已在 [GitHub CI](https://github.com/HsuanC0921/ilovbee-studio/actions/runs/42558997627) 通過。

初次建置後已補上真實 B87 的四層映射寫入／讀回／還原、設定欄位、RGB 串流、音樂律動與當機恢復測試。另外找到並修正內建燈效配置選擇欄位，完成相應瀏覽器回歸。無線配對、旋鈕輸出與部分按鍵觸發效果仍未核實。舊 `firmware.md` 中的紀錄仍與新版實測分開。

### 0.8.0 原始驗證紀錄

- `build.ps1 -WithUi` 成功：32 項測試通過、0 失敗。Rustfmt 與 Clippy（警告視為錯誤）通過。主程式與 Tauri release 建置成功。
- 修正後主程式 1,034,752 bytes，Tauri 視窗 4,468,224 bytes。GNU 發佈包另外包含 160,880-byte WebView2Loader.dll。
- 使用封裝目錄中的執行檔啟動隔離示範服務。Playwright 檢查 8 個頁面、1320／1030×860 兩個寬度，16 次頁面檢查均無 JavaScript 錯誤或內容區橫向溢位。手冊搜尋、無結果及支援範圍篩選通過。
- 封裝的 Tauri 程式實際建立原生視窗並正常關閉。封裝的 Rust 系統匣程式實際建立訊息視窗、維持執行並正常退出，退出後連線檔案已移除。此檢查不等同完整的原生視窗控制互動測試。
- 介面完成審查結果為 `ship`，無必要修正。既有 DESIGN.md 與樣式的字體、顏色及圓角紀錄有歷史差異，本次延用原介面，未重整整套設計文件。
- WASAPI 路徑已透過真實 Realtek 播放端點驗證約 983 Hz 的音訊及停止後靜音恢復。Windows 未回報 B87 藍牙電量。開機啟動登錄與低電量通知未修改使用者設定來測試。
- 已對真實鍵盤執行可還原測試並核對還原。沒有覆寫巨集、色票或韌體。沒有執行安裝／解除安裝。後續已推送原 GitHub repository，Windows CI 通過。

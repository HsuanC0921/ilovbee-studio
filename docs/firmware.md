# B87 硬體與韌體筆記

此文件供開發參考，與日常操作介面分開。研究日期：2026-10-18。

目前裝置回報 BY Tech B87，USB 258A:0199、release 2010。report 6 的 Feature Report 長度 520，為已驗證的設定介面；report 5 長度 6，其 ISP 用途尚未確認。MCU 型號與 Flash 容量未知。

## 官方檔案

[官方下載頁](https://en.ilovbee.com/download/14/) 的韌體 API 以 B87 查詢沒有資料。可下載的 20241220 安裝檔是控制軟體，靜態解包未找到獨立韌體。Repo 不附原廠檔案。

## 同名不同硬體

[EBADBEEF/ilovbee_b87](https://github.com/EBADBEEF/ilovbee_b87) 是 wired-only QMK／Vial、USB 342D:E4B7。

[arpagon 的 b87_3m 原始碼](https://github.com/arpagon/qmk_westberry_3m/tree/b87_3m/keyboards/rk/b87_3m) 是 WB32 系列，USB 342D:E469。取得的兩份 Intel HEX 均通過行校驗與 EOF 驗證，解碼大小 76,256 與 75,700 bytes，內嵌 USB 342D:E469，起始位址 0x08000000。識別碼與目前裝置不同，沒有將它們刷入本機。

## Dump 候選方案

[sinowisp](https://github.com/carlossless/sinowisp) 是待評估候選工具。其 read 流程可能寫入啟動跳轉標記，並非完全不修改裝置。必須先確認晶片與容量、相容的 ISP 指令及復原方法。未發送 report 5 ISP 指令，未執行韌體讀回、擦除或刷寫。

## 已驗證的設定協定

520 bytes 封包：report 6、command、layer、reserved、packet count、packet index、payload length (LE16)、payload。

| 指令 | 用途 | 資料長度 |
|---|---|---|
| 0x83 | 讀取按鍵層 0–2 | 416 bytes，104 個四位元組項目 |
| 0x03 | 寫入單層映射 | 416 bytes |
| 0x84 | 讀取燈效 | 128 bytes |
| 0x04 | 寫入燈效 | 128 bytes |
| 0x87 | 讀取原始電量／狀態 | 2 bytes，比例尚未校正 |

小算盤實體鍵對應項目 78：原值 02000192；F13 為 00000068；Win+D 為 00080007。已對實際裝置寫入 Win+D 並讀回確認，保留其他映射。

燈效模式索引位於 payload[10]。實機驗證：由模式值 1 寫為 0 後主背光立即熄滅；還原為 1 後彩色背光重新亮起。單獨修改 payload[26] 不會改變主背光。新版套用前重新讀取目前 payload，避免實體快捷鍵改變模式後使用舊快照；寫入後由 SetLights 讀回核對。其他燈效索引仍未逐一目視驗證。

## REA

[REA](https://github.com/morluto/rea) 用於原廠 PE 的靜態辨識與取證。這不代表已反編譯出完整韌體；本機沒有可確認相容的韌體原始碼。

## 燈效 payload 欄位（B87 258A:0199，0x84 讀取／0x04 寫入，128 bytes）

欄位來自原廠 DeviceDriver.exe 的靜態分析（燈效序列化函式 0x4117e0）與實機前後快照比對，不是韌體原始碼。

| 位置 | 內容 | 驗證 |
|---|---|---|
| [10] | 模式值：0 關閉、1 恆亮、2 呼吸 … 17 花開富貴、18 自訂（＝原廠清單順序＋1） | 實機 |
| [24] | 無線休眠時間，單位 30 秒：0x0A=5、0x14=10、0x28=20、0x3C=30 分鐘；0=永不 | 僅 10 分鐘對照原廠畫面確認，其餘為原廠換算表 |
| [56+2×模式] | 該模式亮度 1..4（Fn+↓×3 由 4 變 1） | 實機 |
| [57+2×模式] | 高 4 位元＝速度 1..4（原廠改 4→1），低 4 位元＝色票 0..6 固定色、7 混色 | 實機 |
| [126][127] | 結尾魔術值 0x5A 0xA5 | 讀取 |

色票（開啟常亮逐一目視）：0 紅、1 綠、2 深藍、3 黃、4 紫、5 淺藍、6 白，與原廠色表順序一致。payload 內沒有任意 RGB；原廠調色盤的色輪不會寫進這 128 bytes。

原廠各模式可用的控制項來自 KB.ini LedOpt1..17：亮度為模式 1–17、速度為 2–17、顏色／混色為 1、2、4–14。模式 18（自訂）的逐鍵顏色與音樂模式使用即時串流（原廠類別 CDevRKKB::SendMusicData，指令 0x0A），尚未實作。

原廠沒有逐模式的示意圖檔；燈效動畫引擎只有 8 種類型（花開、流水、風車、跑馬等），其按鍵分群檔已轉成 src/LightGroups.cs，用於部分示意動畫。

## 原廠程式巨集／音樂模式靜態分析（DeviceDriver.exe，未確認項目另註）

- 巨集管理類別為 `CMacroDlg`，逐鍵自訂為 `CCustomDlg`；兩者最終都由 0x40e5f0 的大型「寫入設定」函式送出。
- 寫入順序（0x411cba 起）：按鍵矩陣（逐層 `SetMatrix`）→ 巨集資料（`Macro data: (total size=%d)`，裝置類別 `CDev3632::SetMacro` 或 G5KB 的 `AccessData`，操作碼 5、寫入）→ LED 設定（`SetLED`，payload 同 0x4117e0 的欄位表）。
- `AccessData` 使用與本程式相同的 520 位元組 Feature report（欄位：[0]=report id、[1]=指令、[2]=層／區、[4]、[6..7]=長度、每包最多 512 位元組資料），每包送出後以 `HidD_GetFeature` 讀回並比對指令碼。
- 巨集檔（匯入／匯出）以魔術數 `0xFAFB88A0` 驗證；錯誤訊息：檔案無效、含不支援按鍵、超過裝置容量、巨集數量上限。
- 按鍵映射到巨集時，會寫入「Macro_Buffer_ID」，巨集資料另存於獨立緩衝區（`nMacroBufferSize`／`nMacroBufferNum`）。
- 音樂模式（`CDevRKKB::SendMusicData`）為電腦端錄製音訊後逐鍵串流 RGB，與 0x0A 指令相關；不是韌體內建效果。
- **尚未確認**：巨集緩衝區位元組格式、`AccessData` 操作碼 5 對應的實際指令值、Macro_Buffer_ID 在按鍵碼中的編碼。需要抓封包或繼續分析 0x40dcd0／0x40f620 周邊後才能實作，在此之前不寫入鍵盤。

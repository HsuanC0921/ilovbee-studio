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

燈效模式採原廠定義的索引，修改 payload[10] 並保留其餘資料；模式未逐一目視驗證。不要將封包讀回成功等同於所有燈效均已實測。

## REA

[REA](https://github.com/morluto/rea) 用於原廠 PE 的靜態辨識與取證。這不代表已反編譯出完整韌體；本機沒有可確認相容的韌體原始碼。

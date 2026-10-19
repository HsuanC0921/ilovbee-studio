# 0.3.0 驗證紀錄

2026-10-18，Windows x64。

- Native AOT 主程式（本機網頁介面）：Release publish，0 warnings、0 errors。
- NativeAOT 小工具：Release publish，0 warnings、0 errors。
- 21 項自動檢查通過：封包限制、設定檔驗證、編碼、跨層草稿、Undo/Redo、取消、Resume、FN 保護與組合鍵名稱。
- 實機：兩層按鍵批次套用與讀回核對通過；finally 還原後三層映射與燈效均與測試前相同。
- 實機：Windows 的 B87 5.0 藍牙電量屬性讀到 100%，含系統回報時間。
- 程式內 UI 檢查：1320／1030 邏輯像素，五個頁面的建立、鍵盤數量及套用按鈕狀態檢查通過。
- Native RenderTargetBitmap 畫面審查後，修正重映射控制區可見性及導覽選取一致性；複審列出的兩項修正均 resolved，disposition 為 ship。未使用 Computer Use 進行這一輪驗證。
- 小工具本機實測：exe 約 1.94 MB，工作集約 16.8 MB，Private memory 約 4.8 MB。這不是跨機器用量保證。

未涵蓋：所有燈效的逐一目視測試、所有組合鍵的應用程式行為、BLE GATT fallback 的實機讀取（目前優先使用已驗證的 Windows 屬性）、低電量實際放電過程、韌體 dump／刷寫與藍牙射頻穩定性。

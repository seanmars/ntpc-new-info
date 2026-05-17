## ADDED Requirements

### Requirement: 設定頁導覽入口
救援地圖頁面 (`/`) SHALL 在頂部狀態列或標題列加入「設定」連結 (或圖示按鈕), 點擊後 SHALL 透過 vue-router 導覽至 `/settings`, SHALL NOT 觸發整頁重新載入.

#### Scenario: 點擊設定連結
- **WHEN** 使用者於救援地圖頁面點擊「設定」連結
- **THEN** 系統 SHALL 透過 `router.push('/settings')` 導覽至設定頁, 瀏覽器網址列 SHALL 變為 `/settings`, SHALL NOT 觸發整頁 reload

#### Scenario: 鍵盤可達
- **WHEN** 使用者以 Tab 鍵聚焦於設定連結並按 Enter
- **THEN** 系統 SHALL 與滑鼠點擊相同地導覽至 `/settings`

### Requirement: 監測點覆蓋圖層
救援地圖 SHALL 在既有救援事件圖層之外, 額外渲染由 `GET /api/monitor-points` 取得的監測點作為獨立圖層; 監測點圖示 SHALL 視覺上與救援事件 marker 可區分 (例如不同顏色或形狀), 並 SHALL 在地圖縮放或重新渲染救援資料時保持不被覆蓋.

#### Scenario: 同時顯示兩種資料
- **WHEN** 救援地圖頁面同時取得救援事件與監測點清單
- **THEN** 地圖 SHALL 同時渲染兩種 marker, 兩種 marker SHALL 視覺上可區分

#### Scenario: 救援資料重整不影響監測點
- **WHEN** 救援事件圖層因 polling 重新整理 (clearLayers + 重新加入)
- **THEN** 監測點圖層 SHALL 維持顯示, SHALL NOT 因救援資料更新被誤刪

#### Scenario: 無監測點時不渲染
- **WHEN** `/api/monitor-points` 回傳空陣列
- **THEN** 地圖 SHALL 僅顯示救援事件 (若有), SHALL NOT 因空監測點清單拋出例外

## MODIFIED Requirements

### Requirement: 救援事件清單側邊欄
前端 SHALL 在地圖頁面顯示一個側邊欄 (在寬度 > 720px 時位於地圖左側, 寬度約 300px; 在 ≤720px 視窗寬度時改為位於地圖上方), 側邊欄 SHALL 由兩個獨立可摺疊群組組成: 「災情狀況」(顯示 `data.features` 的救援事件清單) 與「自設追蹤」(顯示來自 `GET /api/monitor-points` 的自設追蹤點清單). 兩個群組 SHALL 各自獨立 expand/collapse, 預設皆為 expanded; 摺疊狀態 SHALL 透過 `localStorage` (key `rescue-map.sidebar.groups`) 持久化, 重新整理頁面後 SHALL 保留前一次狀態. 「災情狀況」群組內每一項點擊時 SHALL 將地圖視角移動到該事件位置並開啟其 popup, 同時將該項目標記為選取狀態.

#### Scenario: 兩個群組同時渲染
- **WHEN** 頁面 mount 完成且 `data.features` 與 `/api/monitor-points` 皆回傳非空清單
- **THEN** 側邊欄 SHALL 在頂部顯示「災情狀況 (N)」群組標頭與「自設追蹤 (M)」群組標頭, N/M 為各自項目數; 預設兩群組 SHALL 皆為展開狀態 (除非 `localStorage` 另有記錄)

#### Scenario: 摺疊群組
- **WHEN** 使用者點擊任一群組標頭 (或標頭內的箭頭/按鈕)
- **THEN** 該群組 SHALL 切換 expand/collapse 狀態; collapsed 狀態 SHALL 隱藏其項目清單但 SHALL 保留標頭與計數; 另一群組 SHALL 不受影響

#### Scenario: 摺疊狀態跨重整保留
- **WHEN** 使用者摺疊任一群組後重新載入頁面
- **THEN** 側邊欄 SHALL 從 `localStorage` 讀取 `rescue-map.sidebar.groups` 並還原該摺疊狀態; 讀取失敗或 key 不存在時 SHALL 套用預設 (皆展開), 不 SHALL 拋例外

#### Scenario: 災情狀況項目渲染
- **WHEN** `data.features` 非空且災情群組為展開
- **THEN** 該群組 SHALL 為每個 feature 顯示一個列項, 內容 SHALL 至少包含: 與地圖標記同色的色點、`properties.fireType` (或 `properties.title` 作為 fallback)、`properties.endPointInfo`、`properties.featureId` 與該 feature 之 `caseList` 數量

#### Scenario: 自設追蹤項目渲染
- **WHEN** `/api/monitor-points` 回傳 M 筆且自設群組為展開
- **THEN** 該群組 SHALL 為每個自設點顯示一個列項, 內容 SHALL 至少包含: 名稱、座標 (小數至 6 位) 與 `radius` (公尺整數, 例如「1000 m」); 每項 SHALL 以與地圖 marker 一致的識別色標示

#### Scenario: 點擊災情項目定位地圖
- **WHEN** 使用者點擊災情狀況群組中任一項目
- **THEN** 地圖 SHALL `setView` 至該事件座標 (zoom level 至少為 15), 該事件主標記的 popup SHALL 自動開啟, 且該清單項目在側邊欄中 SHALL 顯示為選取狀態 (與其他項目視覺上有所區別)

#### Scenario: 空群組顯示提示
- **WHEN** 任一群組對應資料為空 (例如無事件或無自設點)
- **THEN** 該群組於展開狀態下 SHALL 顯示對應空狀態文字 (災情: 「目前無事件」; 自設追蹤: 「尚未新增自設追蹤點」), 並 SHALL 不渲染任何列項; 群組標頭計數 SHALL 為 0

#### Scenario: 標頭顯示各群組總數
- **WHEN** 側邊欄渲染
- **THEN** 災情狀況標頭 SHALL 顯示當前 `data.features` 的總數 (例如「災情狀況 (3)」), 自設追蹤標頭 SHALL 顯示當前自設點數量 (例如「自設追蹤 (2)」)

## ADDED Requirements

### Requirement: 自設追蹤點選取與 hover 顯示半徑圓圈
側邊欄「自設追蹤」群組與地圖上對應 marker SHALL 共用一個「focused monitor point」狀態 (由父元件持有), 當使用者 hover 或點擊 (pin) 任一自設點 (從清單或從地圖 marker 任一來源) 時, 主地圖 SHALL 於該自設點座標繪製一個半徑等於該點 `radius` (公尺) 的半透明圓圈 (`L.circle`, fillOpacity 約 0.15, stroke opacity 約 0.6, `interactive: false`); 取消 hover 且未 pin 時圓圈 SHALL 消失.

#### Scenario: hover 清單項目顯示圓圈
- **WHEN** 使用者將滑鼠移入自設追蹤群組中任一項目
- **THEN** 主地圖 SHALL 立即於該自設點座標繪製半徑圓圈 (公尺單位等於該點 radius), 並 SHALL 將地圖視角 panTo 至該點 (zoom 維持不變)

#### Scenario: 離開 hover 收回圓圈
- **WHEN** 使用者將滑鼠移出該項目且該項目未被 pin
- **THEN** 圓圈 SHALL 自地圖移除, 地圖視角 SHALL 不再 panTo

#### Scenario: 點擊 pin 該自設點
- **WHEN** 使用者點擊自設追蹤群組中任一項目
- **THEN** 該項目 SHALL 成為 pinned 狀態 (視覺上與其他項目區別, 類似災情項目 active 樣式), 圓圈 SHALL 保持顯示直到 (a) 使用者再次點擊該項目, (b) 點擊另一自設點, 或 (c) 點擊一個災情項目, 任一情況皆 SHALL 取消 pin

#### Scenario: 地圖 marker hover 顯示圓圈
- **WHEN** 使用者將滑鼠移入主地圖上某自設點 marker
- **THEN** 系統 SHALL 與清單 hover 相同地顯示該點半徑圓圈; 對應清單項目 SHALL 同步以視覺方式提示 (例如 hover 樣式)

#### Scenario: 切換另一自設點
- **WHEN** 已有某自設點 pinned 而使用者 hover 另一自設點
- **THEN** 圓圈 SHALL 顯示「pinned 點」的圓圈 (pin 優先於 hover), 但 hover 的清單項目 SHALL 顯示 hover 樣式; 若使用者點擊新項目則 pin SHALL 切換至新點並重畫圓圈

#### Scenario: 大量自設點時繪製單一圓圈
- **WHEN** 自設點有 N 筆而 `focusedMonitorId` 變更
- **THEN** 系統 SHALL 僅維護一個 `L.circle` layer (重複使用, 透過 `setLatLng` 與 `setRadius` 更新), SHALL NOT 為每次切換建立新 layer 物件

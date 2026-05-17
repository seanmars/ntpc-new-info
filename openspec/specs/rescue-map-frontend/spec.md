# rescue-map-frontend Specification

## Purpose
提供 Vue 3 前端介面, 透過 Leaflet 在地圖上視覺化 `/api/rescue/latest` 回傳的救援事件與車輛路徑, 並以自動輪詢、手動重新整理與狀態列呈現資料新鮮度及錯誤狀態, 讓值勤人員能即時掌握現場狀況.
## Requirements
### Requirement: Leaflet 地圖容器與底圖
前端應用 SHALL 在主頁面 (`/`) 中渲染一個佔滿視窗的 Leaflet 地圖, 預設中心為新北市政府座標 (約 `25.0124, 121.4651`), 預設 zoom level 為 11, 並使用 OpenStreetMap 作為底圖 tile 來源, 同時在地圖上顯示 OpenStreetMap 的 attribution.

#### Scenario: 首次進入頁面
- **WHEN** 使用者在後端 API 尚未回傳資料前進入 `/`
- **THEN** 畫面 SHALL 顯示已初始化的地圖 (含 OSM 底圖與 attribution), 以新北市為中心, 並 SHALL 不出現 JavaScript 例外

#### Scenario: 地圖容器尺寸
- **WHEN** 地圖容器渲染完成
- **THEN** 地圖 SHALL 至少佔滿視窗可視範圍的主要區域 (扣除可選的上方/側邊狀態列), 且視窗縮放時地圖 SHALL 重新計算尺寸 (`map.invalidateSize`) 以避免灰白區塊

### Requirement: 救援事件點渲染
前端 SHALL 將後端 `GET /api/rescue/latest` 回傳的 `data.features` (GeoJSON `FeatureCollection`) 中的每個 `Feature.geometry`(Point) 渲染為地圖上可互動的標記; 標記點擊後 SHALL 顯示 popup, popup 至少包含 `properties.fireType`, `properties.endPointInfo`, `properties.featureId`.

#### Scenario: 資料成功載入後渲染標記
- **WHEN** `/api/rescue/latest` 回應 200 且 `data.features` 包含至少一個 feature
- **THEN** 地圖上 SHALL 為每個 feature 顯示一個標記, 標記位置 SHALL 對應 `geometry.coordinates`(`[lng, lat]`, 渲染時翻轉為 Leaflet `[lat, lng]`)

#### Scenario: 點擊標記顯示 popup
- **WHEN** 使用者點擊任一事件標記
- **THEN** Leaflet popup SHALL 開啟, 內容 SHALL 至少包含該 feature 的 `fireType`, `endPointInfo`, `featureId` 以及該事件下 `caseList` 中各車輛的 `startPointInfo`

#### Scenario: 空的 FeatureCollection
- **WHEN** `/api/rescue/latest` 回應 200 但 `data.features` 為空陣列
- **THEN** 地圖 SHALL 維持已初始化狀態, 不顯示任何救援事件標記, 且 SHALL 在 UI 中提示「目前無進行中的救援事件」

### Requirement: 救援車輛路徑與起點渲染
前端 SHALL 為每個 feature 內 `properties.caseList` 中的每筆紀錄渲染對應的視覺元素: 當 `path` 為非空陣列時 SHALL 渲染為 polyline; `startPoint` SHALL 渲染為次要標記; 屬於同一 feature 的所有車輛 SHALL 使用相同顏色, 不同 feature SHALL 使用可區分的顏色 (例如固定色盤循環).

#### Scenario: 車輛具有路徑
- **WHEN** 某 `caseList[i].path` 為非空陣列
- **THEN** 地圖 SHALL 渲染一條 polyline, 座標依序為 `path` 中各個 `[lat, lng]` 點, 顏色 SHALL 與所屬 feature 的事件主標記一致

#### Scenario: 車輛無路徑資料
- **WHEN** 某 `caseList[i].path` 為 `null`
- **THEN** 系統 SHALL NOT 為該筆建立 polyline, 但 SHALL 仍依 `startPoint` 渲染次要標記 (若 `startPoint` 存在)

#### Scenario: 多事件顏色區分
- **WHEN** `data.features` 包含 2 個以上 feature
- **THEN** 不同 feature 對應的路徑/標記 SHALL 使用不同顏色, 使用者 SHALL 能透過顏色辨識同一事件下的車輛

### Requirement: 自動輪詢與手動重新整理
前端 SHALL 每隔固定間隔 (預設 60 秒, 可透過 `VITE_RESCUE_POLL_INTERVAL_MS` 環境變數覆寫) 自動向 `/api/rescue/latest` 發出 GET 請求, 並 SHALL 提供使用者可點擊的「重新整理」按鈕以立即觸發一次 fetch.

#### Scenario: 進入頁面立即抓取
- **WHEN** 地圖頁面 mount 完成
- **THEN** 系統 SHALL 立即 (不等待第一次間隔) 發出一次 fetch

#### Scenario: 自動輪詢
- **WHEN** 距離上一次 fetch 完成 (不論成功或失敗) 已超過設定的間隔
- **THEN** 系統 SHALL 自動再發出一次 fetch, 且 SHALL 在元件 unmount 時停止排程

#### Scenario: 手動重新整理
- **WHEN** 使用者點擊「重新整理」按鈕
- **THEN** 系統 SHALL 立即 fetch 一次, 並 SHALL 重設下一次自動輪詢的計時起點

#### Scenario: 輪詢期間舊資料保留
- **WHEN** 一次 fetch 正在進行中
- **THEN** 地圖上既有的標記與路徑 SHALL 保持不變直到新資料成功解析後才整批替換, 失敗時 SHALL 維持目前已渲染的資料

### Requirement: 資料新鮮度與錯誤狀態顯示
前端 SHALL 在頁面上以 status bar 或同等可見區域顯示 `meta.fetchedAt` (轉成本地時間的可讀字串), 並在 `meta.lastError` 不為空時顯示錯誤訊息與 `meta.lastErrorAt`; 當後端回傳 503 時 SHALL 顯示「資料尚未就緒」提示而非通用錯誤.

#### Scenario: 顯示資料抓取時間
- **WHEN** `/api/rescue/latest` 回應 200
- **THEN** status bar SHALL 顯示「資料時間: {本地時間字串}」, 字串 SHALL 基於 `meta.fetchedAt` 並使用使用者瀏覽器時區

#### Scenario: 後端記錄到上游錯誤
- **WHEN** `/api/rescue/latest` 回應 200 且 `meta.lastError` 為非空字串
- **THEN** status bar SHALL 額外顯示警告區塊, 內含 `meta.lastError` 與 `meta.lastErrorAt`, 視覺上 SHALL 與正常資訊有所區別 (例如顏色)

#### Scenario: 後端 503 (尚無資料)
- **WHEN** `/api/rescue/latest` 回應 503
- **THEN** 系統 SHALL 顯示「資料尚未就緒, 將自動重試」提示, SHALL NOT 顯示為通用錯誤, 並 SHALL 繼續維持輪詢

#### Scenario: 網路或其他錯誤
- **WHEN** fetch 失敗 (網路錯誤, 非 503 的 5xx, 4xx, JSON 解析失敗)
- **THEN** 系統 SHALL 在 status bar 顯示錯誤訊息與時間, 並 SHALL 繼續維持下一次輪詢排程

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

### Requirement: 開發環境 API proxy
專案 SHALL 在 Vite 設定中將 `/api` 開頭的請求 proxy 到後端開發伺服器 (`http://localhost:5129`), 使前端在開發階段能以相對路徑呼叫 API 而無需設定 CORS.

#### Scenario: 開發環境呼叫 API
- **WHEN** 開發者執行 `pnpm dev` 並從瀏覽器存取 `http://localhost:<vite-port>/api/rescue/latest`
- **THEN** 請求 SHALL 被 Vite dev server 轉發至 `http://localhost:5129/api/rescue/latest`, 並 SHALL 不出現瀏覽器 CORS 錯誤

#### Scenario: 後端未啟動
- **WHEN** 開發者執行 `pnpm dev` 但後端未啟動
- **THEN** 前端 fetch SHALL 失敗 (例如 ECONNREFUSED), 並 SHALL 依「網路或其他錯誤」情境在 UI 顯示錯誤, 不 SHALL 導致頁面崩潰

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

### Requirement: 自設追蹤點選取與 hover 顯示半徑圓圈
側邊欄「自設追蹤」群組與地圖上對應 marker SHALL 共用一個「focused monitor point」狀態 (由父元件持有), 當使用者 hover 或點擊 (pin) 任一自設點 (從清單或從地圖 marker 任一來源) 時且使用者偏好 `showRangeCircle` 為 true, 主地圖 SHALL 於該自設點座標繪製一個半徑等於該點 `radius` (公尺) 的半透明圓圈 (`L.circle`, fillOpacity 約 0.08, stroke opacity 約 0.4, `interactive: false`); 取消 hover 且未 pin 時圓圈 SHALL 消失. 自設追蹤群組 SHALL 於群組標題下方提供一個「顯示範圍圓圈」checkbox, 預設勾選, 狀態 SHALL 透過 `localStorage` (key 例如 `rescue-map.sidebar.monitor-options`) 持久化; 當 checkbox 取消勾選時, 即使使用者 hover/pin 自設點, 主地圖 SHALL NOT 繪製半徑圓圈, 已存在的圓圈 SHALL 立即移除.

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

#### Scenario: 取消勾選「顯示範圍圓圈」
- **WHEN** 使用者於自設追蹤群組取消「顯示範圍圓圈」checkbox
- **THEN** 主地圖目前若有半徑圓圈 SHALL 立即移除, 後續任何 hover/pin 自設點操作 SHALL NOT 繪製圓圈; checkbox 狀態 SHALL 寫入 `localStorage`, 重新整理頁面後 SHALL 保留為未勾選

#### Scenario: 重新勾選「顯示範圍圓圈」
- **WHEN** 使用者再次勾選「顯示範圍圓圈」checkbox, 且目前已有某自設點處於 hover 或 pin 狀態
- **THEN** 主地圖 SHALL 立即於該點座標繪製半徑圓圈; 若目前無任何 focused 自設點則 SHALL 不繪製, 但後續 hover/pin 操作 SHALL 恢復繪製


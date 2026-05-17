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
前端 SHALL 在地圖頁面顯示一個事件清單側邊欄 (在寬度 > 720px 時位於地圖左側, 寬度約 300px; 在 ≤720px 視窗寬度時改為位於地圖上方), 依序列出 `data.features` 中的每個事件, 點擊任一項目時 SHALL 將地圖視角移動到該事件位置並開啟其 popup, 同時將該項目標記為選取狀態.

#### Scenario: 事件清單渲染
- **WHEN** `data.features` 非空
- **THEN** 側邊欄 SHALL 為每個 feature 顯示一個列項, 內容 SHALL 至少包含: 與地圖標記同色的色點、`properties.fireType` (或 `properties.title` 作為 fallback)、`properties.endPointInfo`、`properties.featureId` 與該 feature 之 `caseList` 數量

#### Scenario: 點擊清單項目定位地圖
- **WHEN** 使用者點擊清單中任一項目
- **THEN** 地圖 SHALL `setView` 至該事件座標 (zoom level 至少為 15), 該事件主標記的 popup SHALL 自動開啟, 且該清單項目在側邊欄中 SHALL 顯示為選取狀態 (與其他項目視覺上有所區別)

#### Scenario: 空清單顯示提示
- **WHEN** `data.features` 為空
- **THEN** 側邊欄 SHALL 顯示「目前無事件」文字, 且 SHALL 不渲染任何列項

#### Scenario: 標題顯示事件總數
- **WHEN** 側邊欄渲染
- **THEN** 側邊欄標頭 SHALL 顯示當前 `data.features` 的總數 (例如「事件清單 (3)」)

### Requirement: 開發環境 API proxy
專案 SHALL 在 Vite 設定中將 `/api` 開頭的請求 proxy 到後端開發伺服器 (`http://localhost:5129`), 使前端在開發階段能以相對路徑呼叫 API 而無需設定 CORS.

#### Scenario: 開發環境呼叫 API
- **WHEN** 開發者執行 `pnpm dev` 並從瀏覽器存取 `http://localhost:<vite-port>/api/rescue/latest`
- **THEN** 請求 SHALL 被 Vite dev server 轉發至 `http://localhost:5129/api/rescue/latest`, 並 SHALL 不出現瀏覽器 CORS 錯誤

#### Scenario: 後端未啟動
- **WHEN** 開發者執行 `pnpm dev` 但後端未啟動
- **THEN** 前端 fetch SHALL 失敗 (例如 ECONNREFUSED), 並 SHALL 依「網路或其他錯誤」情境在 UI 顯示錯誤, 不 SHALL 導致頁面崩潰

## MODIFIED Requirements

### Requirement: 監測點清單顯示與重新整理
設定頁 SHALL 在 mount 時呼叫 `GET /api/monitor-points` 取得清單, 並依 `createdAt` 由舊到新排序顯示; 每一項 SHALL 顯示 `name`, `latitude` (小數至 6 位), `longitude` (小數至 6 位), `radius` (公尺, 整數) 與「刪除」按鈕.

#### Scenario: 載入並顯示清單
- **WHEN** 設定頁 mount 完成且 API 回傳 N 個監測點
- **THEN** 清單 SHALL 顯示 N 個項目, 每項皆有名稱、座標、半徑 (例如「半徑 1000 m」) 與刪除按鈕

#### Scenario: 空清單提示
- **WHEN** API 回傳空陣列
- **THEN** 設定頁 SHALL 顯示「尚未新增任何監測點」提示, SHALL NOT 顯示空列項

#### Scenario: 載入失敗
- **WHEN** API 呼叫失敗 (網路錯誤或非 2xx)
- **THEN** 設定頁 SHALL 顯示錯誤訊息與「重試」按鈕, SHALL NOT 導致頁面崩潰

### Requirement: 新增監測點對話框與三種輸入方式
點擊「新增監測點」按鈕 SHALL 開啟對話框 (modal), 對話框 SHALL 提供三種輸入分頁 (tabs): `GPS`, `搜尋地址`, `手動輸入`; 三者皆 SHALL 包含一個必填的 `name` 欄位 (1-50 字元) 與一個必填的 `radius` 欄位 (整數公尺, 50 ~ 50000, 預設 1000), 並在按下「儲存」時將 `{ name, latitude, longitude, radius }` 透過 `POST /api/monitor-points` 送出.

#### Scenario: 開啟與關閉對話框
- **WHEN** 使用者點擊「新增監測點」
- **THEN** 對話框 SHALL 開啟並預設聚焦於 `name` 欄位, `radius` 欄位 SHALL 預填 1000; 按下「取消」或關閉鍵 SHALL 關閉對話框且 SHALL NOT 送出 API 請求

#### Scenario: name 驗證
- **WHEN** 使用者點擊「儲存」但 `name` 為空或超過 50 字元
- **THEN** 系統 SHALL 顯示欄位錯誤提示, SHALL NOT 呼叫 API

#### Scenario: radius 驗證
- **WHEN** 使用者於 `radius` 欄位輸入 < 50, > 50000, 非整數或非數值
- **THEN** 系統 SHALL 即時顯示欄位錯誤 (例如「半徑必須介於 50 ~ 50000 公尺」), 「儲存」按鈕 SHALL 為 disabled

#### Scenario: 儲存成功後刷新清單
- **WHEN** `POST /api/monitor-points` 回傳 HTTP 201
- **THEN** 對話框 SHALL 關閉, 清單 SHALL 立即新增該筆 (本地 append 或重抓清單均可), SHALL 顯示成功提示 (例如 toast 或文字)

#### Scenario: 儲存失敗
- **WHEN** API 回傳 4xx/5xx 或網路失敗
- **THEN** 對話框 SHALL 保持開啟, 顯示錯誤訊息, SHALL NOT 關閉對話框, 使用者輸入 (含 `radius`) SHALL 保留以便修改

### Requirement: 對話框內預覽地圖與可拖曳標記
新增監測點對話框 SHALL 在三個輸入分頁下方顯示一個小型 Leaflet 預覽地圖, 當使用者透過任一輸入方式選定座標後, 地圖 SHALL 在該座標上以可拖曳的標記呈現; 使用者拖曳該標記 SHALL 更新目前選定座標, 並 SHALL 同步反映至手動分頁的 `lat`/`lng` 欄位 (若目前正位於手動分頁). 預覽地圖 SHALL 額外顯示一個以選定座標為中心、半徑為當前 `radius` (公尺) 的半透明圓圈 (`L.circle`, fillOpacity 約 0.15), 當 `radius` 或座標改變時 SHALL 即時同步圓圈尺寸與位置.

#### Scenario: 選定座標後顯示標記與半徑圓圈
- **WHEN** 使用者透過 GPS / 搜尋 / 手動輸入任一方式選定座標 (`selectedCoords` 由 null 變為非 null)
- **THEN** 預覽地圖 SHALL 在該座標上建立可拖曳的標記 (`L.marker(..., { draggable: true })`) 與一個 `L.circle` (半徑 = 當前 radius 公尺, 半透明), 並 SHALL 將地圖視角 panTo 至該座標

#### Scenario: 拖曳標記調整座標
- **WHEN** 使用者拖曳標記後放開
- **THEN** 系統 SHALL 將標記新位置 (取小數 6 位) 寫回 `selectedCoords`, 半徑圓圈 SHALL 同步移動到新中心, 「儲存」按鈕 SHALL 維持可用 (前提是 `name` 與 `radius` 合法), 且若使用者位於手動分頁, `lat`/`lng` 欄位 SHALL 同步更新為新值

#### Scenario: 半徑欄位變動同步圓圈
- **WHEN** 使用者於 `radius` 欄位輸入新合法值 (例如由 1000 改為 2500)
- **THEN** 預覽地圖上的 `L.circle` SHALL 立即以新半徑重繪 (`setRadius`), 中心 SHALL 不變, marker SHALL 不變

#### Scenario: 點擊地圖建立或移動標記
- **WHEN** 使用者點擊地圖任意位置
- **THEN** 系統 SHALL 將點擊座標 (取小數 6 位) 設為 `selectedCoords`, 若已有標記 SHALL 移動該標記到點擊位置, 若尚無標記 SHALL 新建可拖曳標記; 半徑圓圈 SHALL 隨之同步中心

#### Scenario: 對話框關閉清理地圖
- **WHEN** 使用者關閉對話框 (`props.open` 由 true 變 false)
- **THEN** 系統 SHALL 呼叫 `map.remove()` 釋放 Leaflet 資源 (含 marker 與 circle), 避免重複開關造成記憶體洩漏

#### Scenario: 預設地圖中心
- **WHEN** 對話框開啟且尚未選定任何座標
- **THEN** 預覽地圖 SHALL 以新北市政府座標 (`25.0124, 121.4651`) 為中心, zoom level 11 顯示, SHALL 不顯示任何標記與半徑圓圈

### Requirement: 編輯既有監測點
設定頁清單每一項 SHALL 提供「編輯」按鈕, 點擊後 SHALL 開啟與新增相同的對話框 (`MonitorPointForm`), 但以「編輯模式」運作: 對話框標題 SHALL 顯示「編輯監測點」, `name` SHALL 預填為目前值, `radius` SHALL 預填為目前值, `selectedCoords` SHALL 預填為目前 lat/lng, 預設分頁 SHALL 為「手動輸入」並將 `lat`/`lng` 欄位填入既有座標; 「儲存」按鈕點擊後 SHALL 呼叫 `PUT /api/monitor-points/{id}`, 成功時 SHALL 以新物件替換清單中對應項目並關閉對話框.

#### Scenario: 開啟編輯對話框預填值
- **WHEN** 使用者點擊清單中某項目的「編輯」按鈕
- **THEN** 對話框 SHALL 開啟, 標題 SHALL 為「編輯監測點」, `name` 欄位 SHALL 為目前值, `radius` 欄位 SHALL 為目前值, 預覽地圖上 SHALL 立即顯示可拖曳標記與半徑圓圈於目前座標與半徑, 分頁 SHALL 預設為「手動輸入」且 `lat`/`lng` 欄位 SHALL 預填為目前值

#### Scenario: 編輯成功
- **WHEN** 使用者於編輯模式調整任一欄位 (含 radius) 後點擊「儲存」, API 回傳 HTTP 200
- **THEN** 對話框 SHALL 關閉, 清單中對應項目 SHALL 被更新後的物件取代 (排序不變), SHALL 顯示成功提示

#### Scenario: 編輯失敗保留輸入
- **WHEN** PUT 失敗 (4xx/5xx 或網路錯誤)
- **THEN** 對話框 SHALL 保持開啟, 顯示錯誤訊息, 使用者輸入 (含 `radius`) SHALL 保留, 清單 SHALL 維持原狀

#### Scenario: 同一對話框可切換輸入方式
- **WHEN** 使用者於編輯模式切換到 GPS 或搜尋分頁並選定新座標
- **THEN** 對話框 SHALL 與新增模式相同地更新 `selectedCoords` 與地圖標記/圓圈, 「儲存」按鈕 SHALL 提交新值至 PUT 端點

#### Scenario: 取消編輯
- **WHEN** 使用者於編輯模式按下「取消」或關閉鈕
- **THEN** 對話框 SHALL 關閉, SHALL NOT 呼叫 API, 清單 SHALL 維持原狀

### Requirement: 監測點 composable 與型別
前端 SHALL 提供 `useMonitorPoints` composable 統一管理監測點清單的 reactive 狀態 (`points`, `error`, `isLoading`) 與 `create`, `update`, `remove`, `refresh` 方法; 並 SHALL 在 `src/types/monitorPoint.ts` 定義 `MonitorPoint` interface (`id`, `name`, `latitude`, `longitude`, `radius`, `createdAt`) 與 `MonitorPointCreateInput` interface (`name`, `latitude`, `longitude`, `radius`).

#### Scenario: 設定頁與地圖頁共用清單
- **WHEN** 設定頁透過 composable 新增監測點且導覽到地圖頁
- **THEN** 地圖頁 SHALL 能於 mount 時取得包含新監測點 (含 `radius`) 的清單 (透過再次呼叫 API 或共享 store, 兩者擇一)

#### Scenario: API 失敗時保留前次清單
- **WHEN** composable 的 refresh fetch 失敗 (4xx/5xx/網路錯誤)
- **THEN** composable SHALL 不清空既有 `points`, SHALL 將 error 暴露給呼叫端

#### Scenario: 舊回應缺 radius 欄位
- **WHEN** API 回應中某項目缺 `radius` 欄位 (舊後端過渡期)
- **THEN** composable SHALL 將該項目 `radius` 視為 1000, 不 SHALL 拋例外

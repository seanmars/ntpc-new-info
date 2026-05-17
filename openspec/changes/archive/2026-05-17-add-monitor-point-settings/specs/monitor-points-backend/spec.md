## ADDED Requirements

### Requirement: 監測點資料模型
後端 SHALL 以下列欄位定義監測點: `id` (string, server 端產生的 UUID), `name` (string, 1-50 字元, 必填), `latitude` (number, -90 ~ 90, 必填), `longitude` (number, -180 ~ 180, 必填), `createdAt` (ISO 8601 UTC 時間字串, server 端產生). 所有 API 回傳 JSON 皆使用 camelCase.

#### Scenario: 建立時 server 端產生 id 與 createdAt
- **WHEN** client 透過 `POST /api/monitor-points` 送出 `{ name, latitude, longitude }` (無 `id`, 無 `createdAt`)
- **THEN** server SHALL 產生新的 UUID 為 `id`, 以當前 UTC 時間為 `createdAt`, 並在 response 中回傳完整 monitor point 物件

#### Scenario: 拒絕含 id 的請求
- **WHEN** client 在 `POST /api/monitor-points` body 中提供 `id` 或 `createdAt`
- **THEN** server SHALL 忽略該欄位值, 依然由 server 端重新產生 id 與 createdAt

### Requirement: 取得所有監測點
後端 SHALL 提供 `GET /api/monitor-points` 端點, 回傳目前儲存的所有監測點清單 (JSON array), 排序依 `createdAt` 由舊到新.

#### Scenario: 有監測點時回傳清單
- **WHEN** client 呼叫 `GET /api/monitor-points` 且儲存中有 N 個監測點
- **THEN** response SHALL 為 HTTP 200, body 為包含 N 個物件的 JSON array, 每個物件含 `id`, `name`, `latitude`, `longitude`, `createdAt`

#### Scenario: 無監測點時回傳空陣列
- **WHEN** client 呼叫 `GET /api/monitor-points` 且儲存中尚無監測點
- **THEN** response SHALL 為 HTTP 200, body SHALL 為 `[]`, 而非 404 或 null

### Requirement: 新增監測點
後端 SHALL 提供 `POST /api/monitor-points` 端點, 接收 `{ name, latitude, longitude }` JSON body 並建立新監測點, 成功時回傳 201 Created 與完整 monitor point 物件.

#### Scenario: 成功建立
- **WHEN** client 送出合法的 `{ name: "辦公室", latitude: 25.034, longitude: 121.564 }`
- **THEN** server SHALL 回傳 HTTP 201, body 為完整 monitor point 物件 (含 server 端產生的 `id` 與 `createdAt`), 並 SHALL 將該物件附加到儲存清單

#### Scenario: 缺欄位
- **WHEN** body 缺少 `name`, `latitude` 或 `longitude` 任一欄位
- **THEN** server SHALL 回傳 HTTP 400 與 ProblemDetails 格式的錯誤訊息, SHALL NOT 寫入儲存

#### Scenario: name 長度違規
- **WHEN** `name` 為空字串或長度超過 50
- **THEN** server SHALL 回傳 HTTP 400, 錯誤訊息 SHALL 指明 name 限制

#### Scenario: 座標超出範圍
- **WHEN** `latitude` 不在 -90 ~ 90 之內, 或 `longitude` 不在 -180 ~ 180 之內
- **THEN** server SHALL 回傳 HTTP 400, 錯誤訊息 SHALL 指明哪個欄位超出範圍

### Requirement: 更新監測點
後端 SHALL 提供 `PUT /api/monitor-points/{id}` 端點, 接收與建立相同的 `{ name, latitude, longitude }` JSON body, 對指定 `id` 的監測點進行完整更新; 成功時 SHALL 回傳 HTTP 200 + 更新後的完整物件, `id` 與 `createdAt` SHALL 保持不變.

#### Scenario: 成功更新
- **WHEN** client 對既有 `id` 送出合法 `{ name, latitude, longitude }` 至 `PUT /api/monitor-points/{id}`
- **THEN** server SHALL 回傳 HTTP 200, body 為更新後的監測點; `id` 與 `createdAt` SHALL 維持原值, 僅 `name`/`latitude`/`longitude` 更新

#### Scenario: id 不存在
- **WHEN** client `PUT /api/monitor-points/{unknown-id}` 即使 body 合法
- **THEN** server SHALL 回傳 HTTP 404 NotFound, SHALL NOT 建立新監測點

#### Scenario: body 驗證失敗
- **WHEN** body 缺欄位、name 長度違規或座標超出範圍
- **THEN** server SHALL 回傳 HTTP 400 + ProblemDetails, 既有資料 SHALL 維持不變

#### Scenario: 更新持久化
- **WHEN** 更新成功
- **THEN** server SHALL 將整份清單寫回 `data/monitor-points.json` (採與新增/刪除相同的 atomic temp+rename 流程), 重啟後 GET 回傳的清單 SHALL 反映新值

### Requirement: 刪除監測點
後端 SHALL 提供 `DELETE /api/monitor-points/{id}` 端點移除指定監測點, 成功時回傳 HTTP 204 No Content.

#### Scenario: 成功刪除
- **WHEN** client 呼叫 `DELETE /api/monitor-points/{existing-id}`
- **THEN** server SHALL 自儲存中移除該監測點, 並回傳 HTTP 204 (無 body)

#### Scenario: 刪除不存在的 id
- **WHEN** client 呼叫 `DELETE /api/monitor-points/{unknown-id}`
- **THEN** server SHALL 回傳 HTTP 404 NotFound, SHALL NOT 修改儲存清單

### Requirement: JSON 檔案持久化
後端 SHALL 將監測點清單持久化於檔案 `<ContentRoot>/data/monitor-points.json` (路徑可由 `MonitorPointStore:FilePath` configuration 覆寫), 應用啟動時從檔案載入, 任何 CRUD 操作後 SHALL 同步寫回檔案.

#### Scenario: 啟動時載入既有檔案
- **WHEN** 應用啟動且 `data/monitor-points.json` 存在且為合法 JSON array
- **THEN** server SHALL 將檔案內容載入記憶體 store, 使後續 `GET /api/monitor-points` 回傳該清單

#### Scenario: 啟動時檔案不存在
- **WHEN** 應用啟動且 `data/monitor-points.json` 不存在
- **THEN** server SHALL 初始化 store 為空清單, SHALL NOT 拋例外, 第一次 `POST` 時 SHALL 自動建立資料夾與檔案

#### Scenario: 啟動時檔案毀損
- **WHEN** 應用啟動但 `data/monitor-points.json` 內容不是合法 JSON array
- **THEN** server SHALL 記錄 error log, 將 store 視為空, 並 SHALL 將毀損檔案改名為 `monitor-points.json.corrupt-<timestamp>` 以保留原資料

#### Scenario: 並發寫入互斥
- **WHEN** 兩個 `POST` 請求幾乎同時抵達
- **THEN** server SHALL 序列化檔案寫入 (例如以 `SemaphoreSlim` 或 lock 保護), 兩個監測點 SHALL 都被持久化, 檔案 SHALL NOT 出現損毀或部分寫入

### Requirement: Nominatim 地址搜尋代理
後端 SHALL 提供 `GET /api/geocode/search?q=<query>&limit=<n>` 端點, 將查詢代理至 OpenStreetMap Nominatim, 回傳結構化候選清單, 並 SHALL 強制設定 `User-Agent` 與套用最小間隔以遵守 Nominatim Usage Policy.

#### Scenario: 成功搜尋
- **WHEN** client 呼叫 `GET /api/geocode/search?q=台北101&limit=5`
- **THEN** server SHALL 呼叫上游 `https://nominatim.openstreetmap.org/search?q=...&format=json&limit=5&accept-language=zh-TW`, 並回傳 HTTP 200 與精簡後的 JSON array, 每個物件 SHALL 至少含 `displayName`, `latitude`, `longitude` (lat/lng 為 number 而非 string)

#### Scenario: 空查詢
- **WHEN** `q` 參數為空或缺少
- **THEN** server SHALL 回傳 HTTP 400, SHALL NOT 呼叫上游

#### Scenario: 上游失敗
- **WHEN** 上游 Nominatim 回應非 2xx 或逾時 (預設 10 秒)
- **THEN** server SHALL 回傳 HTTP 502 BadGateway 與簡要錯誤訊息, SHALL NOT 拋出未處理例外

#### Scenario: 標識可辨識的 User-Agent
- **WHEN** server 呼叫 Nominatim
- **THEN** request SHALL 含 `User-Agent` header 形如 `ntpc-new-info-backend/<version> (contact)`, 用以遵守 Nominatim Usage Policy

#### Scenario: 預設 limit
- **WHEN** client 未提供 `limit` 參數
- **THEN** server SHALL 預設 `limit=5`, 並 SHALL 將 `limit` clamp 至 1-10 區間

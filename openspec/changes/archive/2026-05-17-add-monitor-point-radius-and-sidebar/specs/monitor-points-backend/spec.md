## MODIFIED Requirements

### Requirement: 監測點資料模型
後端 SHALL 以下列欄位定義監測點: `id` (string, server 端產生的 UUID), `name` (string, 1-50 字元, 必填), `latitude` (number, -90 ~ 90, 必填), `longitude` (number, -180 ~ 180, 必填), `radius` (integer, 公尺, 50 ~ 50000, 預設 1000), `createdAt` (ISO 8601 UTC 時間字串, server 端產生). 所有 API 回傳 JSON 皆使用 camelCase.

#### Scenario: 建立時 server 端產生 id 與 createdAt
- **WHEN** client 透過 `POST /api/monitor-points` 送出 `{ name, latitude, longitude, radius }` (無 `id`, 無 `createdAt`)
- **THEN** server SHALL 產生新的 UUID 為 `id`, 以當前 UTC 時間為 `createdAt`, 並在 response 中回傳完整 monitor point 物件 (含 `radius`)

#### Scenario: 拒絕含 id 的請求
- **WHEN** client 在 `POST /api/monitor-points` body 中提供 `id` 或 `createdAt`
- **THEN** server SHALL 忽略該欄位值, 依然由 server 端重新產生 id 與 createdAt; `radius` 若有提供 SHALL 沿用 client 值

#### Scenario: 缺 radius 欄位
- **WHEN** client 送出 body 但未包含 `radius`
- **THEN** server SHALL 套用預設值 1000, 不 SHALL 回傳 400

### Requirement: 新增監測點
後端 SHALL 提供 `POST /api/monitor-points` 端點, 接收 `{ name, latitude, longitude, radius? }` JSON body 並建立新監測點, 成功時回傳 201 Created 與完整 monitor point 物件.

#### Scenario: 成功建立
- **WHEN** client 送出合法的 `{ name: "辦公室", latitude: 25.034, longitude: 121.564, radius: 2500 }`
- **THEN** server SHALL 回傳 HTTP 201, body 為完整 monitor point 物件 (含 server 端產生的 `id`, `createdAt`, 與 client 提供的 `radius: 2500`), 並 SHALL 將該物件附加到儲存清單

#### Scenario: 缺欄位
- **WHEN** body 缺少 `name`, `latitude` 或 `longitude` 任一欄位
- **THEN** server SHALL 回傳 HTTP 400 與 ProblemDetails 格式的錯誤訊息, SHALL NOT 寫入儲存

#### Scenario: name 長度違規
- **WHEN** `name` 為空字串或長度超過 50
- **THEN** server SHALL 回傳 HTTP 400, 錯誤訊息 SHALL 指明 name 限制

#### Scenario: 座標超出範圍
- **WHEN** `latitude` 不在 -90 ~ 90 之內, 或 `longitude` 不在 -180 ~ 180 之內
- **THEN** server SHALL 回傳 HTTP 400, 錯誤訊息 SHALL 指明哪個欄位超出範圍

#### Scenario: radius 超出範圍
- **WHEN** `radius` 小於 50 或大於 50000, 或非整數
- **THEN** server SHALL 回傳 HTTP 400 與 ProblemDetails, 錯誤訊息 SHALL 指明 radius 必須介於 50 ~ 50000 公尺

### Requirement: 更新監測點
後端 SHALL 提供 `PUT /api/monitor-points/{id}` 端點, 接收與建立相同的 `{ name, latitude, longitude, radius? }` JSON body, 對指定 `id` 的監測點進行完整更新; 成功時 SHALL 回傳 HTTP 200 + 更新後的完整物件, `id` 與 `createdAt` SHALL 保持不變.

#### Scenario: 成功更新
- **WHEN** client 對既有 `id` 送出合法 `{ name, latitude, longitude, radius }` 至 `PUT /api/monitor-points/{id}`
- **THEN** server SHALL 回傳 HTTP 200, body 為更新後的監測點; `id` 與 `createdAt` SHALL 維持原值, 僅 `name`/`latitude`/`longitude`/`radius` 更新

#### Scenario: id 不存在
- **WHEN** client `PUT /api/monitor-points/{unknown-id}` 即使 body 合法
- **THEN** server SHALL 回傳 HTTP 404 NotFound, SHALL NOT 建立新監測點

#### Scenario: body 驗證失敗
- **WHEN** body 缺欄位、name 長度違規、座標超出範圍或 radius 超出範圍
- **THEN** server SHALL 回傳 HTTP 400 + ProblemDetails, 既有資料 SHALL 維持不變

#### Scenario: 更新持久化
- **WHEN** 更新成功
- **THEN** server SHALL 將整份清單寫回 `data/monitor-points.json` (採與新增/刪除相同的 atomic temp+rename 流程), 重啟後 GET 回傳的清單 SHALL 反映新值 (含 `radius`)

### Requirement: JSON 檔案持久化
後端 SHALL 將監測點清單持久化於檔案 `<ContentRoot>/data/monitor-points.json` (路徑可由 `MonitorPointStore:FilePath` configuration 覆寫), 應用啟動時從檔案載入, 任何 CRUD 操作後 SHALL 同步寫回檔案; 載入舊版檔案缺 `radius` 欄位時 SHALL 套用預設值 1000 而 SHALL NOT 拋例外.

#### Scenario: 啟動時載入既有檔案
- **WHEN** 應用啟動且 `data/monitor-points.json` 存在且為合法 JSON array
- **THEN** server SHALL 將其載入記憶體, GET 端點 SHALL 立即回傳該清單

#### Scenario: 載入缺 radius 的舊資料
- **WHEN** 應用啟動載入的 JSON 中某項目缺少 `radius` 欄位
- **THEN** server SHALL 將該項目 `radius` 視為 1000 並繼續啟動, SHALL NOT 拋例外或拒絕該筆資料; 該項目在下一次 mutation 後 SHALL 以完整欄位寫回檔案

#### Scenario: 載入損毀檔案
- **WHEN** 應用啟動且 `data/monitor-points.json` 內容無法解析為 JSON
- **THEN** server SHALL 將該檔案改名為 `*.corrupt-<timestamp>` 隔離, 並以空清單啟動, 不 SHALL 拋例外

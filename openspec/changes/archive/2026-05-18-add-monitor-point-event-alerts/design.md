## Context

`RescuePollingService` (BackgroundService) 每 5 分鐘從上游拉取一份 GeoJSON 災訊資料, 寫入 `IRescueSnapshotStore` 提供 `GET /api/rescue/latest` 取用. `IMonitorPointStore` 則保存使用者自設追蹤點 (`lat`, `lng`, `radius` 公尺). 兩者目前各自獨立, 沒有任何 "命中通知" 機制.

需求: 每次 poll 成功後, 計算 features 是否落在任一 monitor point 的追蹤半徑內, 若有就把命中結果發到 message queue. 使用 Rebus + in-memory transport, 一律走 in-process `IBus.Publish`; 預設 handler 僅 logging, 不執行其他副作用.

## Goals / Non-Goals

**Goals:**
- 在不破壞既有 polling/snapshot 行為的前提下, 插入 "event detection + bus publish" 這個步驟.
- 採用 Rebus 抽象, 之後 transport 可以無痛換成 RabbitMQ/Azure Service Bus 等 broker.
- 訊息 schema 清楚: 包含 monitor point 識別 (id, name), 命中的 features 摘要, 以及 snapshot fetch 時間.
- Handler 採 plug-and-play: 加新通知管道時不需動發布端, 只要新增實作 `IHandleMessages<MonitorPointEventDetected>` 的類別.

**Non-Goals:**
- 不實作實際的對外推播 (Web Push / LINE / Webhook): 預設只 log.
- 不做去重/節流 (de-duplication / throttling): 同一 feature 連續兩次 poll 都命中會發兩則訊息; 後續若有需求再加.
- 不變更 `IRescueSnapshotStore` 或 `IMonitorPointStore` 介面.
- 不換掉 polling 本身的排程機制 (`PeriodicTimer`).
- 不做 cross-process 訊息持久化 — in-memory transport 重啟即失.

## Decisions

### Decision 1: 使用 Rebus + InMemoryNetwork transport
**選擇:** `Rebus` 主套件 + `Rebus.ServiceProvider` 整合, transport 使用 `UseInMemoryTransport(new InMemNetwork(), "monitor-point-alerts")`.

**理由:**
- 題目明確指定 Rebus.
- 不需要外部 broker, 開發/測試體驗最簡單.
- Rebus 的 transport 是 pluggable, 之後只需改 `Configure.With(...).Transport(...)` 一行就能切換.

**替代方案:**
- 自寫 `IEventPublisher` + `Channel<T>` consumer: 更輕量但失去 Rebus 的 retry/saga/timeout 能力, 也不符合需求指定.
- MediatR (in-process only): 同樣是 in-process, 但缺少 transport 抽象, 換 broker 需重寫.

### Decision 2: 命中檢測放在獨立服務 `MonitorPointEventDetector`
**選擇:** 新增 `MonitorPointEventDetector` 類別, 注入 `IMonitorPointStore` + `IBus`; `RescuePollingService` 在 success 後呼叫 `detector.DetectAndPublishAsync(snapshotData, ct)`.

**理由:**
- `RescuePollingService` 已經負責 HTTP fetch + snapshot 更新, 不適合再塞 GeoJSON 解析與距離計算 (single responsibility).
- 把 detector 抽出來方便單元測試 (傳入 fake `JsonNode` 與 in-memory monitor points).

**替代方案:**
- 直接在 `RescuePollingService.PollOnceAsync` 內 inline 邏輯: 程式碼會膨脹, 測試困難.

### Decision 3: 命中規則 = haversine 距離 ≤ radius
**選擇:** 用 haversine 公式計算 monitor point 與 feature 點之間的 great-circle distance (公尺), 距離 ≤ `radius` 即視為命中.

**理由:**
- monitor point 的 `radius` 單位為公尺 (見 `monitor-points-backend` spec), 用 haversine 跨經緯度算實際距離才正確; 直接比較經緯度差會在高緯度失準.
- 公式簡單, 不需引入 NetTopologySuite 等地理函式庫.

**替代方案:**
- 引入 NetTopologySuite + GeoJSON.NET: 功能更強但對單純的點-圓判斷過重.

### Decision 4: GeoJSON feature 取座標的策略
**選擇:** 對每個 `features[*]`:
1. 若 `geometry.type == "Point"`, 直接取 `geometry.coordinates`.
2. 若是 `LineString`/`Polygon`/`MultiPolygon` 等, 取所有 vertex 的座標, 任一 vertex 命中即視為整個 feature 命中.
3. 若 geometry 為 null 或無法解析, 跳過該 feature.

GeoJSON coordinates 為 `[longitude, latitude]` 順序 (這是 spec 規定, 不是 lat/lng).

**理由:**
- 上游資料以 Point 為大宗 (救援/受困點), 但仍可能有 LineString/Polygon (例如封路、災區範圍).
- vertex-命中近似法簡單且偏保守 (寧可多發不要漏發).

**替代方案:**
- 對 Polygon 算 polygon-circle 交集: 精準但實作複雜, 後續真有需要再升級.

### Decision 5: 失敗 poll 不觸發 detection
**選擇:** 僅在 `store.SetSuccess(data)` 之後呼叫 detector; 失敗分支 (`SetFailure`) 不做任何 detection 與 publish.

**理由:** snapshot 沒更新, 重複跑 detection 沒有意義, 且舊資料若已經被通知過, 重複觸發會放大假警報.

### Decision 6: Detector 例外不影響 poll loop
**選擇:** `RescuePollingService` 在呼叫 `detector.DetectAndPublishAsync(...)` 時包一層 try/catch, 例外只 log 不向上拋, 不影響 snapshot 已寫入的事實與下次 polling 排程.

**理由:** detection 是 "增值" 功能, 不應該因為一個 bug 連帶讓使用者拿不到災訊資料.

### Decision 7: 訊息 schema 預先涵蓋必要欄位
**選擇:** `MonitorPointEventDetected` 包含:
- `MonitorPointId` (string)
- `MonitorPointName` (string)
- `MonitorPointLatitude` / `MonitorPointLongitude` (double)
- `MonitorPointRadius` (int, 公尺)
- `MatchedFeatures` (IReadOnlyList<MatchedFeature>), 每筆含 `FeatureId` (string?, GeoJSON `id` 或 properties.id), `Distance` (double, 公尺), `Latitude` / `Longitude` (double, 命中那一點的座標), `Properties` (`JsonNode?`, 原始 properties)
- `DetectedAt` (DateTimeOffset)
- `SnapshotFetchedAt` (DateTimeOffset)

**理由:** handler 可能想 dedupe (用 `FeatureId`)、想顯示距離 (`Distance`)、想連結原始資料 (`Properties`), 一次給足才不需要回查 snapshot.

### Decision 8: Handler 註冊用 `AutoRegisterHandlersFromAssemblyOf<T>`
**選擇:** 在 `Program.cs` 用 `services.AutoRegisterHandlersFromAssemblyOf<LoggingMonitorPointAlertHandler>()` 一次掃描整個 WebApi assembly.

**理由:** 後續新增 handler 只需建立類別實作 `IHandleMessages<...>`, 不必再改 `Program.cs`.

## Risks / Trade-offs

- **Risk:** 大量 monitor points × 大量 features 導致 O(n×m) 計算可能拖慢 poll. → **Mitigation:** poll 間隔 5 分鐘級別、預期 monitor points < 100、features < 1000, O(n×m) ≈ 10^5 次距離計算 (μs 級), 影響可忽略. 若日後規模成長再導入空間索引 (e.g., R-tree, geohash bucket).
- **Risk:** 同一事件每次 poll 都重發訊息, log 會被洗版且未來真接推播會打擾使用者. → **Mitigation:** 本次明確標記為 non-goal, 後續 change 再加 de-dup (例如在 store 記錄已通知過的 `featureId`).
- **Risk:** In-memory transport 一旦進程死掉, queue 內容全失. → **Mitigation:** 接受此風險 (目前只 logging, 不需持久化); 後續若加實際推播會評估換成 persistent transport.
- **Risk:** GeoJSON 結構若上游變動, detector 解析會壞. → **Mitigation:** detector 對非預期結構走 "略過該 feature + warning log" 而非 throw, 確保 poll loop 不被連坐.
- **Trade-off:** 用 vertex-命中近似 Polygon 命中, 對 "monitor point 在 polygon 內部但離所有 vertex > radius" 的場景會漏判. 接受此 trade-off, 因目前上游資料以 Point 為主.

## Migration Plan

1. 加入 `Rebus` 與 `Rebus.ServiceProvider` NuGet 套件 (`dotnet add package`).
2. 新增 message contract / detector / handler 三個檔案.
3. `Program.cs` 加 Rebus 註冊 + handler auto-register.
4. `RescuePollingService` 加 `MonitorPointEventDetector` 注入與成功分支呼叫.
5. 本地驗證: 用 monitor points 圍住已知 rescue feature, 觀察 log 是否出現 `MonitorPointEventDetected` 結構化訊息.
6. 無資料庫變更, 無 API 破壞性變更, 不需 rollback 計畫; 真要 rollback revert commit 即可.

## Open Questions

- 是否需要在訊息中包含整份 GeoJSON `Feature` 物件 (geometry + properties) 而不只是 properties? → 現階段先放 `Properties` + 命中點座標已足夠, log 不會過長; 後續 handler 若需要再擴.
- 命中距離計算是否需以 monitor point 邊界 (公尺) 內為閾值, 或要含等於? → 採 `<=`, 包邊界, 邏輯與 frontend 顯示一致.

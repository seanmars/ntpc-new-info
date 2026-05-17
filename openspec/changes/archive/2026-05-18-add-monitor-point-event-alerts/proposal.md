## Why

目前 `RescuePollingService` 會週期性拉取災訊資料 (GeoJSON) 並更新 in-memory snapshot, 但使用者自設的 monitor points 完全是被動的展示元素 — 即使有救援/災害事件落在追蹤半徑內, 系統也不會主動發出任何通知. 我們需要一個事件驅動的機制, 在每次成功拉取災訊後判斷是否有事件命中任一 monitor point 的追蹤範圍, 並把命中結果以訊息形式發布出去, 為後續推播 (e.g., Web Push, LINE Notify, Webhook) 留下擴充點.

## What Changes

- 在 WebApi 引入 **Rebus** message bus 框架, 使用 **in-memory transport** (single-process), 透過 `IBus` 發布訊息, 並以 `IHandleMessages<T>` 註冊 handler.
- 新增 `MonitorPointEventDetected` message contract: 描述 "某 monitor point 在某次 poll 命中了哪些 rescue features".
- 在 `RescuePollingService` 成功完成一次 poll 後 (亦即 `store.SetSuccess(...)` 之後), 觸發事件命中檢查 (event detector); 把命中結果經 `IBus.Publish(...)` 發布.
- 命中檢查邏輯: 從 snapshot data 解析 GeoJSON `features`, 取得每筆 feature 的座標 (`geometry.coordinates` 或可推導出的 lat/lng), 對每個 monitor point 計算 haversine 距離, 距離 ≤ `radius` (公尺) 即視為命中.
- 新增 `LoggingMonitorPointAlertHandler` (預設 handler): 收到 `MonitorPointEventDetected` 後僅輸出結構化 log (`ILogger.LogInformation`), 不執行其他副作用 — 後續可再追加其他 handler 不需修改發布端.
- 訊息與 handler 在 `Program.cs` 透過 `services.AddRebus(...)` 與 `services.AutoRegisterHandlersFromAssemblyOf<...>()` 註冊.

## Capabilities

### New Capabilities
- `monitor-point-event-alerts`: 在每次災訊 poll 成功後檢查 monitor points 的追蹤半徑命中情形, 透過 in-process message bus (Rebus + in-memory transport) 發布 `MonitorPointEventDetected` 訊息, 並至少提供一個記錄用的預設 handler.

### Modified Capabilities
- `rescue-data-polling`: 既有的 polling 行為新增 "在成功 poll 後觸發 event detection + bus publish" 這條要求; snapshot 行為不變.

## Impact

- **新增套件依賴**: `Rebus` (核心) 與 `Rebus.ServiceProvider` (DI 整合); 兩者皆為 OSS 且支援 .NET 10.
- **新增程式碼**: `src/WebApi/Messaging/` 目錄下放 message contract (`MonitorPointEventDetected`)、event detector (`MonitorPointEventDetector`) 與 handler (`LoggingMonitorPointAlertHandler`).
- **修改 `RescuePollingService`**: 注入 detector + `IBus`, 在 success 分支觸發檢測與發布; 失敗分支保持不變.
- **修改 `Program.cs`**: 註冊 Rebus, in-memory transport, handler assembly auto-register.
- **不變動**: 既有 `IMonitorPointStore`/`IRescueSnapshotStore` 介面、所有 HTTP endpoints、frontend 行為皆無影響.
- **後續擴充**: handler 採 plugin 化, 後續加入推播只需新增實作 `IHandleMessages<MonitorPointEventDetected>` 的類別.

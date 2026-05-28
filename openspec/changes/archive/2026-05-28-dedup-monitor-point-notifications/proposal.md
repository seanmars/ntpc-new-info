## Why

`MonitorPointEventDetector` 目前在每次成功輪詢後, 對「當下命中追蹤點的所有 feature」全部重發 `MonitorPointEventDetected`。只要災情事件還在範圍內, 每個輪詢週期就重複通知一次 (dev 10 秒、production 300 秒一輪), 對接收者造成重複洗版。全災訊偵測器 (`RescueAllAlertsDetector`) 早已用「已見過的 featureId」做 new-only 去重, 追蹤點偵測器卻沒有, 行為不一致。

## What Changes

- 為 `MonitorPointEventDetector` 加入 per-monitor-point 的「已通知 featureId」去重: 每次偵測只針對「自上次成功偵測以來新進入範圍」的 feature 發布 `MonitorPointEventDetected`; 持續命中的同一 feature 不再重複通知。
- 去重狀態 keyed by `(monitorPointId, featureId)`: 同一 feature 命中不同追蹤點時, 各追蹤點獨立判定 new。
- 沿用 `RescueAllAlertsDetector` 的語義: 每輪以「當前命中集合」取代「已見集合」, 因此 feature 離開範圍後再回來會被視為新事件重新通知。
- 狀態重置: 無追蹤點時清空; 追蹤點被刪除時自動移除其狀態 (每輪以現存追蹤點重建)。
- 去重發生在 detector 層 (非 Discord handler 層), 因此 logging handler 與 Discord handler 一致地只收到 new 事件。

## Capabilities

### New Capabilities
<!-- 無新增 capability -->

### Modified Capabilities
- `monitor-point-event-alerts`: 新增「追蹤點通知去重」需求; 並調整既有 "One message per hit monitor point" scenario, 使每則訊息僅含「本輪新進入範圍」的 feature, 且只對有新 feature 的追蹤點發布。

## Impact

- 程式碼: `src/WebApi/Messaging/MonitorPointEventDetector.cs` (加入 per-mp seen-set 狀態與 new-only 過濾)。
- 行為: 對所有 `MonitorPointEventDetected` 訂閱者生效 (logging + Discord)。
- 規格: `openspec/specs/monitor-point-event-alerts/spec.md`。
- 不影響: message contract、命中判定規則 (haversine/radius)、Discord handler 格式、`RescueAllAlertsDetector`、前端。
- 已知限制 (非目標): 去重狀態僅存記憶體, 重啟後首次輪詢會把當前命中視為 new 重新通知一次; 無 `featureId` 的 feature 不參與去重, 維持每輪通知 (與 `RescueAllAlertsDetector` 略過無 id feature 的取捨一致, 真實上游資料 featureId 幾乎必有)。

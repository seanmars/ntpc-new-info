## Why

`RescuePollingService` 預設每 5 分鐘才會向上游 NTPC API 抓一次資料, 而前端目前的「重新整理」按鈕只是重新讀取 backend 的 in-memory 快照 (`GET /api/rescue/latest`), 不會觸發上游重抓. 當值勤人員需要在事件爆發當下取得最新災情, 沒有任何方式可以縮短這段最長達 5 分鐘的資料延遲, 只能被動等待下一次排程. 我們需要一個明確的「強制更新」入口, 讓使用者主動繞過輪詢節奏取得當下最新資料.

## What Changes

- 新增後端 endpoint `POST /api/rescue/refresh`, 同步觸發一次上游抓取, 成功後更新 snapshot 並回傳與 `GET /api/rescue/latest` 相同結構的 payload.
- 後端對該 endpoint 套用最小冷卻間隔 (預設 15 秒) 與單一進行中要求的互斥鎖, 避免短時間連續點擊或同時與排程輪詢造成多次上游打擊.
- 強制更新成功時觸發既有的 `IMonitorPointEventDetector` 與 `IRescueAllAlertsDetector` 流程, 行為與排程輪詢成功時一致.
- 前端 `RescueMapView` 新增一個與既有「重新整理」按鈕並排但視覺上可區別的「強制更新」按鈕 (例如圖示加文字標籤), 點擊後呼叫 `POST /api/rescue/refresh` 並等待回應, 期間按鈕顯示 loading / disabled 狀態.
- 後端因冷卻仍在生效而拒絕強制更新時 (HTTP 429), 前端 SHALL 在 status bar 顯示剩餘可重試秒數, 不視為錯誤.
- 強制更新成功後, 前端 SHALL 將新回傳的 `data` + `meta` 套用至既有 `useRescueData` 狀態, 並重設自動輪詢的計時起點 (與既有手動重新整理一致).

## Capabilities

### New Capabilities

- (none — extends existing capabilities only)

### Modified Capabilities

- `rescue-data-polling`: 新增「依需求觸發的同步上游抓取」需求, 涵蓋冷卻間隔、互斥鎖、與 detector 觸發行為.
- `rescue-map-frontend`: 新增「強制更新按鈕」需求 (按鈕位置、loading 狀態、429 冷卻提示), 既有「手動重新整理」需求保持不變.

## Impact

- 後端: 新增 `RescueController.ForceRefresh` action; `RescuePollingService` 或 `RescueDataFetcher` 需重構, 抽出可被 controller 重用的「執行一次 poll 並更新 snapshot」邏輯; 新增 cooldown / mutex 元件 (例如 `IRescueRefreshGate`); `RescuePollingOptions` 新增 `ForceRefreshCooldownSeconds`.
- 前端: 新增 `forceRefreshRescue()` API function; `useRescueData` 暴露 `forceRefresh()` 與冷卻狀態; `RescueMapView` 加入新按鈕與 429 提示 UI.
- 上游 NTPC API: 在最壞情況 (每 15 秒一次) 流量略增, 但仍遠低於常規瀏覽; cooldown 即為保護上游的閘門.
- Spec 文件: 更新 `openspec/specs/rescue-data-polling/spec.md` 與 `openspec/specs/rescue-map-frontend/spec.md` (透過此 change 的 delta).
- 無資料庫 schema 變更, 無破壞性 API 變更 (現有 `GET /api/rescue/latest` 行為不變).

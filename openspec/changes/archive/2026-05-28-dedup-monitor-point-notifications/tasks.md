## 1. Dedup state

- [x] 1.1 在 `MonitorPointEventDetector` 加入欄位 `Dictionary<string, HashSet<string>> _seenByMonitorPoint` 與 `lock` gate (比照 `RescueAllAlertsDetector`)
- [x] 1.2 加入 helper: 從 feature node 取 featureId (沿用既有 `ExtractFeatureId`) — 直接複用 `MatchedFeature.FeatureId` (已由 `ExtractFeatureId` 填好), 無需新 helper

## 2. New-only filtering

- [x] 2.1 偵測時為每個追蹤點蒐集 currentIds (本輪命中且有 featureId 的集合) 與命中的 `MatchedFeature` 清單
- [x] 2.2 計算 newFeatures: featureId 不在 `_seenByMonitorPoint[mpId]` 者; 無 featureId 的 feature 一律視為 new
- [x] 2.3 僅當某追蹤點 newFeatures 非空才發布 `MonitorPointEventDetected`, 且 `MatchedFeatures` 只含 new
- [x] 2.4 每輪以「現存追蹤點 + 當前 currentIds」重建 `_seenByMonitorPoint` (刪除的追蹤點狀態自然移除; 無追蹤點時清空)
- [x] 2.5 以 `lock` 保護所有讀寫

## 3. Verify

- [x] 3.1 `dotnet build src/WebApi` 通過
- [x] 3.2 啟動服務 + 臨時追蹤點實測: 首輪通知一次, 後續持續命中不再重發 (對照 log 的 `MonitorPointEventDetected` 次數); 測畢刪除追蹤點並停服務 — 9 個輪詢週期只發 1 次
- [x] 3.3 確認 `RescueAllAlertsDetector` 與 Discord handler 未被更動 — git status 僅 `MonitorPointEventDetector.cs` 變更

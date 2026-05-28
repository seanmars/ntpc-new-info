## Context

`MonitorPointEventDetector.DetectAndPublishAsync` 每次成功輪詢後, 對每個追蹤點蒐集所有命中 feature, 只要 `hitsByMonitorPoint[mp.Id]` 非空就發布一則 `MonitorPointEventDetected`。它沒有任何「上次通知過什麼」的狀態, 所以持續中的災情會每輪重發。

`RescueAllAlertsDetector` 已示範可借用的去重模式 (`src/WebApi/Messaging/RescueAllAlertsDetector.cs`): 維護 `_seenFeatureIds` HashSet, 每輪算出 `currentIds`, new = current - seen, 然後 `_seenFeatureIds = currentIds`。

偵測一律經 `RescueRefreshCoordinator` 的單一 `SemaphoreSlim _gate` 序列化 (scheduled 與 manual refresh 共用, 見 `RescuePollingService` 第 17/24 行 → `coordinator.RefreshAsync`), 因此偵測不會並行。

## Goals / Non-Goals

**Goals:**
- 持續命中的同一 feature 不再每輪重複通知; 只通知「本輪新進入範圍」者。
- 去重以 `(monitorPointId, featureId)` 為單位, 追蹤點之間互不影響。
- 與 `RescueAllAlertsDetector` 行為對齊 (feature 離開後再回來算新事件)。

**Non-Goals:**
- 不做跨重啟的持久化去重 (記憶體狀態, 重啟後首輪重新通知一次)。
- 不改 message contract、命中判定規則、Discord handler 格式、`RescueAllAlertsDetector`。
- 不為無 `featureId` 的 feature 設計穩定去重鍵。

## Decisions

**1. 去重狀態: `Dictionary<string, HashSet<string>>` keyed by monitorPointId, value 為該追蹤點已通知的 featureId 集合。**
每輪以「現存追蹤點 + 當前命中」重建這個 dict, 因此被刪除的追蹤點其狀態自然消失, 無追蹤點時 dict 為空。替代方案 (扁平 `HashSet<(mpId,featureId)>`) 較難「整批替換某 mp 的當前集合」, 故採巢狀結構。

**2. new-only 過濾: 對每個追蹤點, currentIds = 本輪命中的 featureId 集合; newFeatures = currentIds 不在 seen[mpId] 者; 僅當 newFeatures 非空才發布, 且訊息只含 new 的 `MatchedFeature`; 之後 seen[mpId] = currentIds。**
直接對齊 all-alerts 語義: 持續者被吸收, 離開再回來 (currentIds 不含 → 下輪再含) 視為新事件。

**3. 去重放在 detector 層, 不放 Discord handler 層。**
單一去重點涵蓋所有訂閱者 (logging + Discord), 避免每個 handler 各自維護狀態而行為分歧。明確記錄此決策, 以免日後有人誤把去重搬進 Discord handler。

**4. 無 `featureId` 的 feature 一律視為新 (不去重), 維持每輪通知。**
與 all-alerts「略過無 id feature」取捨一致; 真實上游資料 featureId 幾乎必有。曾考慮用 closest-vertex 座標當鍵, 但多頂點幾何的最近頂點會在輪詢間跳動, 反而誤判, 故捨棄。

**5. 以 `lock` 保護狀態, 比照 all-alerts。**
`_gate` 已序列化偵測, 嚴格說不需鎖; 但加上 cheap 的 `lock` 與既有 detector 一致, 並對未來若有非預期並行呼叫提供保險。

## Risks / Trade-offs

- [同一 featureId 但內容更新 (例如出勤車輛增加)] → 去重以 featureId 為準, 內容變動不會觸發再通知。屬刻意取捨 (避免吵); 若日後要「重大更新再通知」需另設計, 不在本次範圍。
- [重啟後重新通知一次] → 記憶體狀態的已知限制, 與 all-alerts 相同, 可接受。
- [closest-vertex 座標在多頂點幾何間跳動] → 已透過「無 id 不去重 + 不採座標鍵」迴避。
- [去重也減少 logging handler 的重複 log] → 視為正面效果 (log 同樣不該每 10 秒洗版)。

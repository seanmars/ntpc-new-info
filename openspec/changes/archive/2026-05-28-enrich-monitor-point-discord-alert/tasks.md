## 1. Property extraction helpers

- [x] 1.1 在 `DiscordMonitorPointAlertHandler` 加入 null-safe private static helper: 依 key 從 `JsonNode? properties` 取出 trimmed 字串 (取不到回 null)
- [x] 1.2 加入 helper 取災情類型 (`fireType` fallback `title`)
- [x] 1.3 加入 helper 從 `caseList` (JsonArray) 收集 `startPointInfo`, 回傳 (總車輛數, 前 N 筆名稱)

## 2. Message formatting

- [x] 2.1 重寫 `FormatMessage`: header 含監測點名稱、半徑、命中筆數、`DetectedAt` (ISO `o`)
- [x] 2.2 每筆 feature 輸出: 災情類型、地點 (`endPointInfo`)、事件編號 (`featureId`, 缺漏 fallback `lat,lng`)、距離 (公尺, 四捨五入)、類型 (`type`)、資料來源 (`dataSource`)
- [x] 2.3 每筆 feature 輸出出勤車輛摘要: 前 5 筆 `startPointInfo`, 其餘以 `...其他 N 車` 表示
- [x] 2.4 缺值/空字串/無法讀取的欄位略過該行, 不丟例外

## 3. Caps & safeguards

- [x] 3.1 維持 `FeatureLineCap = 5`; 超過以 `...and N more` 收尾
- [x] 3.2 加入 `MessageSoftCap` (~1800) 長度防線: 接近上限時提前停止輸出剩餘 feature, 以 `...and N more` 收尾
- [x] 3.3 確認最終訊息不超過 Discord 2000 字上限

## 4. Verify

- [x] 4.1 `dotnet build src/WebApi` 通過
- [x] 4.2 以 `sample/raw.data_1.json` 的 feature properties (含 18 車 caseList) 手動驗證格式與上限保護
- [x] 4.3 確認 `DiscordAllAlertsHandler` 未被更動

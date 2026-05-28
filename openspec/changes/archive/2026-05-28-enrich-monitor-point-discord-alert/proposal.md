## Why

當追蹤點 (monitor point) 命中救援事件時, 目前的 Discord 通知只列出 featureId 與距離, 對接收者而言過於簡陋, 無法直接判斷災情狀況。前端地圖 popup 早已能呈現完整災情資訊, 通知訊息卻沒有, 使用者必須再回到地圖才能得知細節。

## What Changes

- 豐富化 `DiscordMonitorPointAlertHandler` 產生的通知內容, 為每筆命中的 feature 加上完整災情欄位: 災情類型 (`fireType`/`title`)、地點 (`endPointInfo`)、事件編號 (`featureId`)、距離、類型 (`type`)、資料來源 (`dataSource`)、出勤車輛 (`caseList` 的 `startPointInfo`)。
- 對 `caseList` 出勤車輛數量做上限保護 (僅列出前幾筆, 其餘以 `...其他 N 車` 表示), 因單一事件可能多達十餘輛車。
- 對整則訊息長度做保護, 接近 Discord 2000 字上限時提前停止輸出剩餘 feature, 並以 `...and N more` 表示。
- 保留既有的 fallback: `featureId` 缺漏時改用 `lat,lng`。
- 僅調整 monitor-point handler; `DiscordAllAlertsHandler` (全災訊 firehose) 維持原本精簡格式不變。

## Capabilities

### New Capabilities
<!-- 無新增 capability -->

### Modified Capabilities
- `discord-notifications`: 「Monitor point event Discord handler」需求的 "Message content" scenario 由「每筆 feature 僅 feature id 或 lat,lng」改為「每筆 feature 含完整災情欄位」, 並新增車輛數與訊息長度上限保護的行為。

## Impact

- 程式碼: `src/WebApi/Discord/DiscordMonitorPointAlertHandler.cs` (訊息格式化邏輯)。
- 資料來源: 沿用 `MonitorPointEventDetected.MatchedFeatures[].Properties` (raw JSON node), 不需改動 message contract 或 detector。
- 規格: `openspec/specs/discord-notifications/spec.md` 的 Message content scenario。
- 不影響: detector、bus、Discord 設定/lifecycle、前端、`DiscordAllAlertsHandler`。

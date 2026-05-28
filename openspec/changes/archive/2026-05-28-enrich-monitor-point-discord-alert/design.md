## Context

`DiscordMonitorPointAlertHandler` 是 `IHandleMessages<MonitorPointEventDetected>` 的實作, 收到 bus 訊息後格式化一則 Discord 文字訊息並透過 `IDiscordNotifier.SendAsync` 送出。`MonitorPointEventDetected.MatchedFeatures[]` 的每筆 `MatchedFeature` 已攜帶上游原始 `Properties` (raw `JsonNode`), 但目前 handler 完全沒讀它, 只輸出 `featureId` 與距離。

前端地圖 popup (`vue-app/src/components/RescueMap.vue` 的 `buildPopupHtml`) 已示範了完整災情呈現: 災情類型 (`fireType`/`title`)、地點 (`endPointInfo`)、事件編號 (`featureId`)、出勤車輛 (`caseList[].startPointInfo`)。上游真實資料 (見 `sample/raw.data_1.json`) 證實單一事件的 `caseList` 可達 18 輛車, 因此直接全部展開會撞到 Discord 2000 字上限。

## Goals / Non-Goals

**Goals:**
- 追蹤點命中通知含完整災情欄位, 接收者不必回地圖即可判讀。
- 對車輛數與整則訊息長度做硬性上限保護, 不超過 Discord 限制。
- 沿用既有 message contract 與 detector, 僅改 handler 的格式化函式。

**Non-Goals:**
- 不改 `DiscordAllAlertsHandler` (全災訊 firehose 維持精簡)。
- 不改 message contract、detector、bus、Discord 設定/lifecycle、前端。
- 不引入時區轉換; 偵測時間維持 ISO `o` 格式 (與既有 handler 一致)。
- 不使用 Discord embed; 維持純文字 + markdown (與既有實作一致, 風險最低)。

## Decisions

**1. 從 `MatchedFeature.Properties` 抽取欄位, 而非擴充 message contract。**
`Properties` 已是 raw JSON node, 含全部上游欄位; 直接讀取最低侵入, 不需動 detector 或 `MonitorPointEventDetected`。替代方案 (在 contract 上加 strongly-typed 欄位) 會把上游 schema 綁進訊息型別, 彈性差且改動面大, 故不採用。

**2. 抽取邏輯以 private static helper 放在 handler 內, 比照 `DiscordAllAlertsHandler` 的 `PickFirst`/key-list 模式。**
不另建 helper class, 避免過早抽象。`fireType`→`title` 採 fallback, `featureId` 缺漏時 fallback 為 `lat,lng` (spec 既有要求, 須保留)。

**3. 雙層上限保護。**
- 車輛: `caseList` 僅列前 `VehicleNameCap` (5) 筆 `startPointInfo`, 其餘以 `...其他 N 車` 收尾。
- Feature: 維持 `FeatureLineCap` (5) 筆; 另加總長度防線, 當 `StringBuilder` 長度接近上限 (`MessageSoftCap` ≈ 1800) 時提前停止, 剩餘以 `...and N more` 表示。
兩道防線同時存在, 因為「5 筆 feature」與「2000 字」彼此獨立, 任一條件先觸發都要安全收尾。

**4. 半形標點 + 正體中文標籤。**
比照前端 popup 的中文欄位標籤 (地點/事件編號/出勤車輛), 但全程使用半形標點。

## Risks / Trade-offs

- [上游欄位缺漏或型別非預期] → 所有抽取走 null-safe helper, 缺值欄位直接略過該行, 不丟例外; handler 既有的 try/catch 仍包覆送出流程。
- [`caseList` 為非陣列或元素非物件] → helper 以 `as JsonArray` / null 檢查防護, 取不到名稱就略過。
- [總長度估算與實際送出略有出入] → soft cap 設在 1800 (距 2000 留 ~200 緩衝), 足以吸收收尾字串。
- [可讀性 vs 資訊量] → 每筆 feature 多行會變長, 但已由雙層上限控制; 維持精簡 firehose 的需求由另一個 handler 滿足。

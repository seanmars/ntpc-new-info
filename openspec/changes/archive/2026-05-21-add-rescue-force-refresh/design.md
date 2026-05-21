## Context

目前救援資料流為 pull-on-schedule:

- `RescuePollingService` (BackgroundService) 啟動時跑一次 `PollOnceAsync`, 之後每 `RescuePollingOptions.IntervalSeconds` (預設 300 秒) 透過 `PeriodicTimer` 觸發一次.
- `PollOnceAsync` 呼叫 `RescueDataFetcher.FetchAsync(url, ct)`, 成功時 `IRescueSnapshotStore.SetSuccess(data)`, 並 fire 兩個 detector (`IMonitorPointEventDetector`, `IRescueAllAlertsDetector`).
- `GET /api/rescue/latest` 是純讀 in-memory snapshot 的 endpoint, 從不主動觸發上游抓取.
- 前端 `useRescueData.refresh()` 雖名為「重新整理」, 實際只是再次 `GET /api/rescue/latest`, 取回的仍是同一個快照.

因此「快取陳舊」的最大延遲就是 `IntervalSeconds`. 在災情爆發、值勤人員需要當下最新資料的時刻, 沒有任何方式可以縮短這段延遲 — 這就是本次設計要解的核心矛盾.

主要 stakeholders: 值勤人員 (希望立刻看到新資料)、上游 NTPC API (不希望被同步觸發大量請求)、現有排程輪詢 (不希望被同時 in-flight 的強制更新打亂).

## Goals / Non-Goals

**Goals:**

- 在不改動既有 `GET /api/rescue/latest` 行為的前提下, 新增同步觸發上游抓取的入口 `POST /api/rescue/refresh`.
- 把「執行一次 poll」的核心邏輯從 `RescuePollingService.PollOnceAsync` 抽出, 成為 controller 與背景服務共用的元件, 避免兩條路徑分裂.
- 透過 cooldown + 互斥鎖防止上游被短時間打爆, 且 cooldown 對所有來源 (此 endpoint, 排程輪詢) 一致, 使用者無法靠重整繞過.
- 強制更新成功時, downstream 副作用 (snapshot 更新、detector 觸發) 與排程輪詢成功時完全相同, 不出現「強制更新後 Discord 沒通知」這種落差.
- 前端按鈕的 UX 明確: loading 期間 disabled; 429 時顯示剩餘秒數而非通用錯誤; 成功後重設下一次自動輪詢計時.

**Non-Goals:**

- 不引入 server-sent events / WebSocket — 維持 polling 模型, 強制更新只是「立即觸發一次 poll」.
- 不為強制更新加上 rate limiting 以外的 auth/quota 管制 (目前系統未做使用者驗證).
- 不改 `GET /api/rescue/latest` 的回傳結構或 HTTP 狀態語意.
- 不調整 `IntervalSeconds` 預設值 — cooldown 是另一個獨立的 knob.
- 不為強制更新提供「在 cooldown 中排隊等下次可執行時自動觸發」的行為 — 直接回 429, 由前端決定要不要重試.

## Decisions

### Decision 1: 抽出 `IRescueRefreshCoordinator` 同時供 controller 與背景服務使用

新增一個 service:

```csharp
public interface IRescueRefreshCoordinator
{
    Task<RescueRefreshOutcome> RefreshAsync(RescueRefreshTrigger trigger, CancellationToken ct);
}

public enum RescueRefreshTrigger { Scheduled, Manual }

public sealed record RescueRefreshOutcome(
    RescueRefreshStatus Status,
    JsonNode? Data,
    DateTimeOffset? FetchedAt,
    string? ErrorMessage,
    TimeSpan? RetryAfter);

public enum RescueRefreshStatus { Success, Failure, Throttled }
```

- `RescuePollingService.PollOnceAsync` 改為呼叫 `coordinator.RefreshAsync(Scheduled, ct)`.
- `RescueController.ForceRefresh` 呼叫 `coordinator.RefreshAsync(Manual, ct)`.
- Coordinator 內含 fetcher 呼叫、snapshot 更新、detector 觸發 — 即 `PollOnceAsync` 的搬家.

**為什麼**: 避免 controller 直接呼叫 `RescueDataFetcher` 後與 `RescuePollingService` 各自 SetSuccess / SafeDetectAsync, 重複 + 易漂移. 集中一份保證兩條路徑行為等價.

**替代方案考慮**:

- *Controller 直接重用 `RescuePollingService` 的 public method*: 違反 `BackgroundService` 不該被 inject 進 controller 的習慣, 且測試難以 isolate.
- *Controller 直接觸發 `_pollingCts.Cancel()` 讓背景服務的 PeriodicTimer 提前喚醒*: 不可靠 (PeriodicTimer 無此原生支援), 而且仍然繞不開 fetcher / detector 路徑共用問題.

### Decision 2: Cooldown + 互斥鎖採用 `SemaphoreSlim(1,1)` + 時間戳記

```csharp
private readonly SemaphoreSlim _gate = new(1, 1);
private DateTimeOffset _lastSuccessAt = DateTimeOffset.MinValue;
private static readonly TimeSpan ManualCooldown = ...; // from options
```

進入 `RefreshAsync(Manual, ct)`:

1. `await _gate.WaitAsync(ct)` — 若已有人在執行, 等其完成 (但見 Decision 3 對 Manual 的特殊處理).
2. 進入後檢查 `Manual && now - _lastSuccessAt < ManualCooldown` → 立刻 release 並回 `Throttled` 帶 `RetryAfter`.
3. 否則執行 fetch → SetSuccess/SetFailure → detectors.
4. 成功才更新 `_lastSuccessAt` (失敗不重設 cooldown, 讓使用者可以馬上重試 — 失敗本身就是訊號).
5. `finally _gate.Release()`.

`Scheduled` trigger 不檢查 cooldown (排程節奏自己控管).

**為什麼**: `SemaphoreSlim` 保證單一進行中要求; 用時間戳記做 cooldown 比起額外引入 `IDistributedCache` 或 `System.Threading.RateLimiting` 簡單且夠用 (單一 process, 不需跨節點同步).

**替代方案考慮**:

- *`System.Threading.RateLimiting.FixedWindowRateLimiter`*: 更精緻但對 N=1 的場景過度.
- *Manual 與 Scheduled 共用 cooldown 計時*: 拒絕 — Scheduled 由 `IntervalSeconds` 控管, 沒理由再被 manual cooldown 限制.

### Decision 3: Manual 在 gate 被 Scheduled 佔用時的策略

- 若 `Manual` 嘗試取 gate 但被佔用, 短暫等候 (例如 `TryWaitAsync` 帶 timeout 約 2 秒). 若超時 → 回 `Throttled` 帶 `RetryAfter` = 預估剩餘 fetch 時間 (約 30 秒, 即 RequestTimeout).
- 不無限等候, 避免 client 卡住.

**為什麼**: 排程輪詢已在進行時, 強制更新如果 reuse 該次結果其實是合理的; 但實作上 await 同一個 Task 結果比較複雜, 短超時 + 429 已能給使用者明確回饋 (「系統正在抓取, 請稍候再試」).

**替代方案考慮**: 將進行中的 Task 透過 `TaskCompletionSource` cache, 讓 Manual share 同一個結果 — 行為較理想, 但複雜度顯著增加, 暫不採用, 列為未來可選優化.

### Decision 4: HTTP 介面設計

- Method: `POST /api/rescue/refresh` (POST 表達 side-effect, 即使無 body).
- 成功 200, body 結構 = `GET /api/rescue/latest` 200 的 body (`{ data, meta }`), 前端可以直接套用同一個 reducer.
- Cooldown / busy: HTTP 429, 帶 `Retry-After` header (秒數) + JSON body `{ error, retryAfterSeconds }`.
- Fetch 失敗: HTTP 502 Bad Gateway, body `{ error: "upstream fetch failed", lastError, lastErrorAt }`. 不回 500 (這不是 server 錯誤, 是上游錯誤), 不回 503 (該 code 已被「資料尚未就緒」佔用).

**為什麼**: 429 + `Retry-After` 是標準, 前端可以直接 parse; 502 區分上游失敗與 server 內部錯誤.

### Decision 5: 前端 UX

- `RescueMapView` 既有的「重新整理」按鈕保留, 新增另一個並排的按鈕 (建議文案: 「強制更新」, icon 可選 ⟳ 加上小 lightning, 與一般 refresh 視覺區別).
- 點擊後按鈕進入 `disabled + loading spinner` 狀態, 直到 fetch resolve (成功 / 失敗 / 429).
- 429 時, status bar 顯示「上游冷卻中, NN 秒後可再次強制更新」, 該訊息倒數歸零即消失, 按鈕同時 disabled 直到倒數結束.
- 成功時走既有 `useRescueData` 的 state 更新路徑, 重設下次自動輪詢的計時 (與既有 `refresh()` 一致).
- 在 429 之外的失敗 (例如 502) 採用既有 `error` ref 的 `failure` 種類, 與一般 fetch 錯誤 UI 一致.

**為什麼**: 兩個按鈕並排雖然多一個 UI 元素, 但語意完全不同 (「重新讀快取」vs「重抓上游」), 合併會讓使用者混淆按一下到底發生什麼. 顯式區分.

### Decision 6: Options / 設定

`RescuePollingOptions` 新增:

```json
{
  "RescuePolling": {
    "ForceRefreshCooldownSeconds": 15
  }
}
```

- 預設 15 秒. 不設 0 (代表不限) — 哲學上不希望 endpoint 完全沒有保護.
- 與 `IntervalSeconds` 一樣支援 `IOptionsMonitor` 熱重載.

## Risks / Trade-offs

- **Risk**: 即使有 cooldown, 多個瀏覽器分頁同步點擊仍可能在 cooldown 過後立刻打上游.
  → **Mitigation**: 15 秒 cooldown 已遠低於上游可承受頻率 (人工觸發, 量級非常小); 若未來成為問題, 可改成 sliding window + per-IP, 但目前 over-engineering.

- **Risk**: `SemaphoreSlim` cooldown 是 process-local, 若未來多實例部署 (load balancer 後面), cooldown 不會跨節點同步.
  → **Mitigation**: 目前單實例部署, 不解; 若未來部署多實例, 改用 distributed cache (Redis) 存 `_lastSuccessAt`. 列為 open question.

- **Trade-off**: Manual trigger 觸發 detectors 表示一次點擊可能立刻送出 Discord 訊息. 若使用者反覆按、上游回的資料一致, 既有的「同事件去重」邏輯應已防止重複通知 (這是 `IMonitorPointEventDetector` / `IRescueAllAlertsDetector` 的責任).
  → **Mitigation**: 此 change 不修改 detector 行為; 任務清單會包含「驗證 detector 對相同 snapshot 的去重」測試項.

- **Risk**: 抽出 `IRescueRefreshCoordinator` 是中等規模重構, 可能影響既有單元測試 (若有 mock `RescueDataFetcher` 直接呼叫的測試).
  → **Mitigation**: 漸進改, 先確認既有測試覆蓋, 重構後逐一綠燈.

- **Trade-off**: HTTP 429 + 502 區分讓前端 mapping 多一層. 若全部塞進 200 + body status field, 前端較單純但破壞 HTTP 語意.
  → **Decision**: 維持 HTTP 語意, 前端的 fetch helper 已會處理 4xx/5xx.

## Open Questions

- Cooldown 預設值 15 秒是否合適? 可在 review / 實作後依 UX 試用結果調整, 暫定 15 秒.
- 未來是否需要把進行中 Task 共享給 manual trigger (Decision 3 替代方案)? 看實際使用頻率再決定.
- 多實例部署 cooldown 是否需跨節點同步? 取決於部署規劃, 目前單實例不解.

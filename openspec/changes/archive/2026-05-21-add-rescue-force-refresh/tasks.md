## 1. Backend - 抽出 refresh coordinator

- [x] 1.1 在 `src/WebApi/Services/` 新增 `IRescueRefreshCoordinator.cs` (介面) 與 `RescueRefreshCoordinator.cs` (實作), 含 `RefreshAsync(RescueRefreshTrigger trigger, CancellationToken ct)` 方法、`RescueRefreshTrigger` enum (`Scheduled`, `Manual`)、`RescueRefreshStatus` enum (`Success`, `Failure`, `Throttled`)、與 `RescueRefreshOutcome` record (含 `Status`, `Data`, `FetchedAt`, `ErrorMessage`, `RetryAfter`).
- [x] 1.2 將原 `RescuePollingService.PollOnceAsync` 的核心邏輯 (呼叫 fetcher → SetSuccess/SetFailure → 觸發兩個 detector) 搬入 `RescueRefreshCoordinator.RefreshAsync`. 保留 logging 結構 (poll succeeded / poll failed) 並標註 trigger 來源.
- [x] 1.3 修改 `RescuePollingService.PollOnceAsync` 改為呼叫 `coordinator.RefreshAsync(RescueRefreshTrigger.Scheduled, ct)`. 移除其內部直接對 fetcher / store / detectors 的依賴 (改由 coordinator 持有).
- [x] 1.4 在 `Program.cs` (或對應的 DI 註冊處) 將 `IRescueRefreshCoordinator` 註冊為 singleton (與 `IRescueSnapshotStore` 同 lifetime).

## 2. Backend - Cooldown 與 mutex gate

- [x] 2.1 在 `RescueRefreshCoordinator` 內加入 `SemaphoreSlim _gate = new(1, 1)` 與 `DateTimeOffset _lastSuccessAt` 欄位.
- [x] 2.2 實作 `RefreshAsync` 入口: `Manual` trigger 嘗試 `_gate.WaitAsync` 帶 timeout (= upstream RequestTimeout); 取不到 gate → 回 `Throttled` 帶 `RetryAfter`. `Scheduled` trigger 用無 timeout 的 `WaitAsync(ct)`.
- [x] 2.3 進入 gate 後, `Manual` 額外檢查 `now - _lastSuccessAt < ManualCooldown`; 在 cooldown 內 → release gate 並回 `Throttled` 帶 `RetryAfter = ManualCooldown - elapsed`. `Scheduled` 跳過 cooldown 檢查.
- [x] 2.4 fetch 流程結束後, 僅在 status 為 `Success` 時更新 `_lastSuccessAt` (對 Manual 與 Scheduled 兩種 trigger 皆然). failure 不更新. `finally` 內 release gate.
- [x] 2.5 為 `RescuePollingOptions` 增加 `ForceRefreshCooldownSeconds` 屬性 (預設 15). 在 coordinator 內透過 `IOptionsMonitor<RescuePollingOptions>` 讀取 `CurrentValue.ForceRefreshCooldownSeconds`. 驗證: 不在 [1, 3600] 範圍內時 fall back 到 15 並 log warning (與既有 IntervalSeconds 的驗證模式一致).
- [x] 2.6 更新 `appsettings.json` 與 `appsettings.Development.json` 的 `RescuePolling` 區塊, 加入 `"ForceRefreshCooldownSeconds": 15` (或加註解保留預設).

## 3. Backend - Controller endpoint

- [x] 3.1 在 `RescueController` 加入 `[HttpPost("refresh")] public async Task<IActionResult> ForceRefresh(CancellationToken ct)`. 注入 `IRescueRefreshCoordinator`.
- [x] 3.2 呼叫 `coordinator.RefreshAsync(RescueRefreshTrigger.Manual, ct)`, 依 `outcome.Status` 對應 HTTP:
  - `Success` → 200 + body `{ data, meta }` (與 `GetLatest` 200 body 結構一致, 直接取 `outcome.Data` / `outcome.FetchedAt`, 不讀 store.Current 以避免與並行 Scheduled poll race)
  - `Throttled` → 429 + `Retry-After` header (`Math.Ceiling(outcome.RetryAfter.Value.TotalSeconds)` 字串) + body `{ error, retryAfterSeconds }`
  - `Failure` → 502 + body `{ error, lastError, lastErrorAt }` (從 `store.Current` 取 last error)
- [x] 3.3 加入適當的 OpenAPI / Scalar attribute (`[ProducesResponseType]`) 以便文件呈現完整.

## 4. Backend - 測試 (deferred)

> Repo 目前沒有任何 .NET test project, 也找不到既有 `*Test*.cs` 或 frontend `*.test.ts`. 為避免在此 change 中混入 test infra setup, 以下測試任務 deferred 至獨立 change 處理. 此 change 改以「手動 UI 驗證」(7.2) + `dotnet build` / `pnpm type-check` 作為品質閘.

- [ ] 4.1 (deferred — needs test project) 為 `RescueRefreshCoordinator` 加 unit test (透過 fake fetcher + in-memory store + stub detectors): 涵蓋 (a) Manual 成功流程、(b) Manual 在 cooldown 內被 throttle、(c) Manual cooldown 過後可再次執行、(d) Manual 失敗不重設 cooldown 時間戳、(e) Scheduled 不受 cooldown 限制、(f) 同時觸發兩個 Manual 時只有一次上游呼叫.
- [ ] 4.2 (deferred — needs test project) 為 `RescueController.ForceRefresh` 加 integration test 或 unit test: 涵蓋成功 200、429 帶 `Retry-After`、502 帶 last error 三種回應.
- [ ] 4.3 (deferred — no existing tests) 確認既有 `RescuePollingService` 與 detector 相關測試在重構後仍綠燈; 若有原本直接 mock fetcher 的測試, 改為 mock coordinator.

## 5. Frontend - API client 與 composable

- [x] 5.1 在 `vue-app/src/api/rescue.ts` 新增 `forceRefreshRescue(signal?: AbortSignal): Promise<ForceRefreshResult>` 函式. `ForceRefreshResult` 為 discriminated union: `{ status: 'ok'; body: RescueResponse }` | `{ status: 'throttled'; retryAfterSeconds: number }` | `{ status: 'error'; error: string }`. 從 `Retry-After` header 與 body `retryAfterSeconds` 取兩者較大者作為倒數來源.
- [x] 5.2 在 `vue-app/src/composables/useRescueData.ts` 新增 `forceRefresh()` 函式與相關 ref: `isForcing` (boolean, 進行中), `cooldownRemainingSeconds` (number, 0 = 無冷卻), 並暴露給 view.
- [x] 5.3 `forceRefresh()` 行為: (a) 若 `isForcing || cooldownRemainingSeconds > 0` → return; (b) 設 `isForcing = true`, 呼叫 `forceRefreshRescue`; (c) `ok` → 套用 data/meta 至既有 ref, 並呼叫 `scheduleNext()` 重設自動輪詢計時; (d) `throttled` → 設 `cooldownRemainingSeconds`, 啟動 1 秒 interval 每秒遞減直到 0, 不寫入 `error` ref; (e) `error` → 寫入 `error.value` (kind: 'failure'). finally 設 `isForcing = false`.
- [x] 5.4 在 `useRescueData` unmount 時清除冷卻倒數 interval 與任何 `AbortController`.

## 6. Frontend - UI

- [x] 6.1 在 `RescueMapView.vue` (或 status bar 元件) 既有「重新整理」按鈕旁加入「強制更新」按鈕, 文案明確區分 (例如「重新整理」/ 「強制更新」), 並加上 tooltip 說明差異. 視覺風格 (icon/顏色) 與既有按鈕一致但可區別.
- [x] 6.2 強制更新按鈕 binding: `disabled = isForcing || cooldownRemainingSeconds > 0`; 顯示 loading spinner 當 `isForcing = true`; 顯示倒數秒數 inline 當 `cooldownRemainingSeconds > 0` (例如「強制更新 (15s)」).
- [x] 6.3 在 status bar 加入冷卻訊息區塊: `cooldownRemainingSeconds > 0` 時顯示「上游冷卻中, 剩餘 NN 秒」, 視覺上與一般錯誤訊息有所區別 (建議用提示色而非錯誤色).
- [x] 6.4 強制更新失敗 (502 或網路錯誤) 沿用既有 `error` ref 的 `failure` 顯示路徑, 確認 status bar 能正確呈現訊息與時間, 既有地圖標記不被清空.

## 7. Frontend - 測試 / 驗證

- [ ] 7.1 (deferred — needs test infra) 加入 (或擴充) `useRescueData` 的單元測試, 涵蓋 forceRefresh 成功、throttled、failure 三條路徑與 disabled state 流轉.
- [ ] 7.2 手動驗證 (UI, **由 user 執行**): 啟動 Aspire AppHost → 開地圖頁 → 點「強制更新」確認立即看到 `meta.fetchedAt` 更新; 連點兩次確認第二次顯示倒數提示; 倒數結束後可再次點擊; 後端關閉時點擊確認 502 / network 錯誤訊息正確顯示.

## 8. 文件與部署

- [x] 8.1 更新 `README.md` 的「後端 API」段落, 加入 `POST /api/rescue/refresh` endpoint 說明 (含 200 / 429 / 502 回應範例) 與「強制更新」UI 行為.
- [x] 8.2 確認 `RescuePolling:ForceRefreshCooldownSeconds` 在 `.env` / Aspire AppHost 設定 (若有 override) 一致, 並於 README「設定」段落補上. 註: `.env` 與 `AppHost/appsettings*.json` 目前並未 override `RescuePolling` 任何欄位, 因此無需同步; README 設定範例已更新.
- [x] 8.3 跑 `dotnet build` (success, 0 warning) 與 `pnpm type-check` + `pnpm lint` (both clean) 確認無錯誤. archive 由 user 於確認 7.2 手動驗證後執行 (`/opsx:archive`).

## Context

專案目前由 `WebApi` (ASP.NET Core 10) + `vue-app` (Vue 3 + Vite + TypeScript + Leaflet) 組成, 並由 .NET Aspire `AppHost` 編排. 後端僅有一份背景輪詢服務 (`RescuePollingService`) 與 in-memory `RescueSnapshotStore`; 沒有任何資料庫、ORM 或檔案 IO 程式碼. 前端使用 vanilla Leaflet, 透過 composable (`useRescueData`) 管理狀態, 透過 Vite dev proxy 將 `/api` 轉發到後端 `http://localhost:5129`.

新功能需要:
1. 新增前端 `/settings` 頁面 + 三種輸入方式的對話框 + 監測點清單.
2. 新增後端 CRUD endpoint 與 Nominatim 代理.
3. 監測點需持久化, 但不引入資料庫.
4. 救援地圖頁需在原有圖層上再疊一層監測點圖層, 且兩層需獨立輪詢避免重繪閃爍.

**Stakeholders**: 值勤操作員 (需要快速記錄關心位置), 系統維護者 (需關注 Nominatim 配額與檔案備份).

## Goals / Non-Goals

**Goals:**
- 提供最小可用的監測點 CRUD 體驗, 涵蓋 GPS / 搜尋 / 手動三種輸入.
- 後端以 JSON 檔案持久化, 重啟後仍保留資料, 不引入資料庫依賴.
- 透過後端代理 Nominatim, 統一掌控 `User-Agent`、rate limit 與錯誤處理.
- 監測點圖層與救援事件圖層獨立, 互不影響.

**Non-Goals:**
- 不導入 user/auth 系統 (本期為全站共用).
- 不做監測點分組、標籤、顏色客製.
- 不做附近事件偵測、推播通知.
- 不做監測點匯入/匯出 (CSV/GeoJSON).
- 不做 i18n; UI 文字皆使用繁體中文.
- 不做行動裝置最佳化 (對話框 RWD 僅基本可用).
- 不更動既有救援事件圖層的渲染邏輯 (僅新增 layer, 不重構).

## Decisions

### Decision 1: 持久化採 JSON 檔案 + `SemaphoreSlim` 互斥
**選擇**: 後端啟動時讀取 `<ContentRoot>/data/monitor-points.json` 到 in-memory `List<MonitorPoint>`; 任何寫操作後重新序列化整份檔案, 寫入時以 `SemaphoreSlim(1,1)` 序列化, 並先寫到 `*.tmp` 再 `File.Move(atomic: true)` 取代以避免半寫毀損.

**替代方案**:
- SQLite + EF Core: 完整 ACID 但需新增 NuGet、migration、context 設定. 對 <100 筆資料是 overkill.
- 純 in-memory: 重啟即失, 違反使用者期待.

**理由**: 監測點數量極小 (預計 <100), 全量重寫成本可忽略; 不引入新依賴, KISS.

### Decision 2: store 介面 `IMonitorPointStore` 抽出檔案 IO
公開介面為 `Task<IReadOnlyList<MonitorPoint>> GetAllAsync(CancellationToken)`, `Task<MonitorPoint> AddAsync(NewMonitorPoint input, CancellationToken)`, `Task<bool> RemoveAsync(string id, CancellationToken)`. 實作類別 `MonitorPointStore` 注入 `IOptions<MonitorPointStoreOptions>`, `TimeProvider`, `ILogger<MonitorPointStore>`. 啟動時於 `IHostedService` 或建構子內非同步 `EnsureLoadedAsync()`; 採 lazy-load 避免阻塞啟動 (除非首次 API 呼叫). 為簡單起見, 第一版採 **建構子同步載入** + lock 保護, 等同 ASP.NET Core 啟動 DI singleton 的單次行為.

**理由**: 與既有 `RescueSnapshotStore` 風格一致 (singleton + Volatile/Interlocked), 但因檔案 IO 需要 async 與互斥, 改採 `SemaphoreSlim` + async 方法.

### Decision 3: 模型驗證採 ASP.NET Core Data Annotations + ProblemDetails
DTO `MonitorPointCreateRequest { string Name, double Latitude, double Longitude }` 套用 `[Required]`, `[StringLength(50, MinimumLength=1)]`, `[Range(-90, 90)]`, `[Range(-180, 180)]`. controller `[ApiController]` 自動將驗證失敗轉成 RFC 7807 ProblemDetails.

**替代方案**: FluentValidation. 但目前專案沒有此依賴, 而 Data Annotations 已足夠.

### Decision 4: Nominatim 代理用具名 `HttpClient` + 全域節流
在 `Program.cs` 註冊 `builder.Services.AddHttpClient<NominatimGeocoder>(...)`, 設定 `BaseAddress`, `Timeout=10s`, `User-Agent=ntpc-new-info-backend/<version>`. `NominatimGeocoder` 內部以 `SemaphoreSlim(1,1)` + `await Task.Delay` 確保兩次外送請求最少間隔 1100ms, 符合 Nominatim Usage Policy 「最多 1 req/sec」.

**替代方案**:
- Polly rate-limiter policy: 較完整但要新增 `Polly.RateLimiting` NuGet (專案 deps 中已有 `Polly.RateLimiting.dll`, 但設定額外複雜).
- 不做節流: 易被 Nominatim 封鎖.

**理由**: 第一版 traffic 極低, 簡單 mutex+delay 即可; 若日後擴大再導入 Polly.

### Decision 5: 前端對話框三 tabs 採內嵌條件渲染, 不引入 UI library
`MonitorPointForm.vue` 內部維護 `activeTab: 'gps' | 'search' | 'manual'`, 三段內容透過 `v-if` 顯示. 共享狀態: `name`, `selectedCoords: { lat, lng } | null`. 「儲存」按鈕在 `name` 合法且 `selectedCoords` 非 null 時啟用.

**理由**: 不引入 Element Plus / Vuetify, 與既有 `RescueMapView.vue` 原生 CSS 風格一致.

### Decision 6: 前端用獨立 composable `useMonitorPoints`, 不放入 Pinia
新增 `useMonitorPoints` 與既有 `useRescueData` 風格一致, 暴露 `points`, `error`, `isLoading`, `create`, `remove`, `refresh`. 設定頁與地圖頁各自呼叫此 composable, 雖然不會跨元件共享 reactive state, 但因兩頁面是路由切換 (不同步存在), 每次進入頁面重新 fetch 是可接受的代價, 換取簡單.

**替代方案**:
- Pinia store: 跨頁面共享 reactive state, 切頁不重 fetch. 但專案目前完全沒有 Pinia, 引入有過度設計疑慮.
- Provide/inject 到 App 層: 比 Pinia 輕量但需處理 lifecycle, 沒有顯著優勢.

**理由**: 與 `useRescueData` 對稱, KISS. 切頁觸發一次 GET 對 <100 筆資料無感.

### Decision 7: Leaflet 上監測點圖層用 `divIcon` 或藍色 `circleMarker` 區分
事件主標記目前是紅/橙色 `circleMarker` (radius 9). 監測點改用:
- **方案 A**: `L.circleMarker(latlng, { radius: 7, color: '#1976d2', fillColor: '#1976d2', fillOpacity: 0.9 })` (藍色實心) — 與救援事件顏色明顯不同.
- **方案 B**: `L.marker(latlng, { icon: L.divIcon({ html: '<span class="mp-pin">★</span>', className: 'monitor-pin' }) })` (星形 icon).

第一版採 **方案 A** (藍色 circleMarker), 因簡單且不涉及 CSS/HTML 注入. 若日後需要更明顯區分再升級到 divIcon.

### Decision 8: 監測點圖層生命週期與救援事件解耦
在 `RescueMap.vue` 新增第二個 `L.FeatureGroup` (`monitorLayer`) 加到 map; 新增 prop `monitorPoints: MonitorPoint[]`; watch prop 變動時 `monitorLayer.clearLayers()` + 重建. 既有 `rescueLayer` 邏輯完全不動, 兩者互不干擾.

`RescueMapView.vue` 同時調用 `useRescueData()` 與 `useMonitorPoints()`, 將兩份 reactive 結果分別傳入 `<RescueMap>` 對應 props.

### Decision 9: Nominatim API 回傳精簡映射
Nominatim 原始回傳含 `place_id`, `licence`, `osm_type`, `lat` (string), `lon` (string), `class`, `type`, `display_name`, `boundingbox`, `importance`. backend 僅取出 `displayName`, `latitude` (`double.Parse(lat)`), `longitude` (`double.Parse(lon)`), 丟棄其他欄位避免暴露 Nominatim implementation detail 給前端.

### Decision 10: 設定頁與地圖頁的監測點輪詢策略
- **地圖頁**: `useMonitorPoints` 沿用 `setTimeout` 60s 自動輪詢 (與救援資料同節奏); 切頁離開時清理 timer.
- **設定頁**: 不需要自動輪詢 (因為使用者本身正在編輯), 但需要在 `create` / `remove` 後自動更新清單 (透過呼叫成功後 append/filter local state, 不重 fetch 整份).

`useMonitorPoints` 提供選項 `{ autoPoll: boolean }` 來區分兩種頁面行為.

## Risks / Trade-offs

- **[Risk] Nominatim 公共服務不穩或限流** → Mitigation: backend 設 10s timeout 且失敗時回 502, 前端顯示錯誤訊息但允許切換到其他輸入方式. 文件中註明 production 流量大時建議自架 Nominatim 或改用商業 geocoder.
- **[Risk] JSON 檔案損毀 (磁碟故障 / 寫入中斷)** → Mitigation: 寫入採 `*.tmp` + `File.Move`; 啟動時若解析失敗將檔案改名為 `*.corrupt-<ts>` 保留, 並以空清單啟動.
- **[Risk] HTTPS 環境外 geolocation API 不可用 (現代瀏覽器要求 secure context)** → Mitigation: GPS 失敗時顯示明確錯誤訊息, 提示改用搜尋或手動輸入; 開發環境 `http://localhost` 屬 secure context 例外, 不受影響.
- **[Risk] 多人同時編輯導致清單以 last-write-wins 覆寫他人變更** → Mitigation: 本期共用清單但操作量極低, last-write-wins 可接受; 文件註明此限制, 日後若需多人併編再加入 ETag / If-Match.
- **[Risk] Aspire 容器化部署時 `data/` 沒有 volume mount, 重新部署即流失** → Mitigation: 在 README / OpenSpec 中提醒部署者需將 `<ContentRoot>/data/` 掛載為持久化 volume.
- **[Trade-off] 不引入 Pinia, 每次切頁重 fetch** → 換來程式碼簡單; 對 <100 筆資料無感.
- **[Trade-off] 不做 import/export 與標籤** → 換來 spec 簡潔, 第一版聚焦核心 CRUD; 後續可作為獨立 change 擴充.
- **[Trade-off] 共用清單而非 per-user** → 換來零 auth 依賴, 適合小團隊 / 內部使用; 日後需多 user 隔離再做 breaking change.

## Migration Plan

1. **後端**:
   1. 新增 `Models/MonitorPoint.cs`, `Options/MonitorPointStoreOptions.cs`, `Options/NominatimOptions.cs`.
   2. 新增 `Services/IMonitorPointStore.cs`, `Services/MonitorPointStore.cs`, `Services/NominatimGeocoder.cs`.
   3. 新增 `Controllers/MonitorPointsController.cs`, `Controllers/GeocodeController.cs`.
   4. 在 `Program.cs` 註冊 `IMonitorPointStore` (Singleton), `AddHttpClient<NominatimGeocoder>`, 並 `Configure<MonitorPointStoreOptions>` / `Configure<NominatimOptions>`.
   5. 在 `appsettings.json` 加入預設區段 (檔案路徑、Nominatim base URL、UA、min interval).
   6. 在 `.gitignore` 加入 `src/WebApi/data/*.json`.
2. **前端**:
   1. 新增 `types/monitorPoint.ts`, `api/monitorPoints.ts`, `api/geocode.ts`.
   2. 新增 `composables/useMonitorPoints.ts`.
   3. 新增 `components/MonitorPointForm.vue`, `components/MonitorPointList.vue`.
   4. 新增 `views/SettingsView.vue`.
   5. 修改 `router/index.ts` 加入 `/settings` 路由.
   6. 修改 `RescueMapView.vue` 加入「設定」連結 + 注入 `useMonitorPoints`, 傳入 `<RescueMap>`.
   7. 修改 `RescueMap.vue` 加入 `monitorPoints` prop 與監測點圖層渲染.
3. **驗證**: type-check + lint + 啟動 AppHost, 在瀏覽器手動測試三種輸入方式、刪除、地圖圖層、503/4xx 錯誤路徑.

**Rollback**: 前後端皆為新增程式碼, 不修改既有 spec 行為; 直接 revert commit 即可, 資料層只需刪除 `data/monitor-points.json`.

## Open Questions

- 是否需要為監測點加入「顯示/隱藏於地圖」的開關 (per-point visibility)? — 暫不加入, 第一版全部都顯示.
- Nominatim `User-Agent` 內的 contact email 是否要列為公開? — 由部署者於 `appsettings.json` 設定, spec 不硬編碼.
- 監測點 `name` 是否允許 emoji / 跨多語言? — 預設允許 1-50 任意 Unicode 字元; 不做白名單.

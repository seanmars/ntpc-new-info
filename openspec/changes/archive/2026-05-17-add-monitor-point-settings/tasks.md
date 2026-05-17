## 1. 後端: 模型與 Options

- [x] 1.1 新增 `src/WebApi/Models/MonitorPoint.cs` 定義 record `MonitorPoint(string Id, string Name, double Latitude, double Longitude, DateTimeOffset CreatedAt)`
- [x] 1.2 新增 `src/WebApi/Models/MonitorPointCreateRequest.cs` DTO, 套用 `[Required]`, `[StringLength(50, MinimumLength=1)]` 於 `Name`, `[Range(-90, 90)]` 於 `Latitude`, `[Range(-180, 180)]` 於 `Longitude`
- [x] 1.3 新增 `src/WebApi/Options/MonitorPointStoreOptions.cs` (`SectionName = "MonitorPointStore"`, 屬性: `FilePath` 預設 `data/monitor-points.json`)
- [x] 1.4 新增 `src/WebApi/Options/NominatimOptions.cs` (`SectionName = "Nominatim"`, 屬性: `BaseUrl` 預設 `https://nominatim.openstreetmap.org`, `UserAgent` 預設 `ntpc-new-info-backend/0.1`, `RequestTimeout` 預設 10s, `MinIntervalMs` 預設 1100)

## 2. 後端: MonitorPoint Store 與檔案持久化

- [x] 2.1 新增 `src/WebApi/Services/IMonitorPointStore.cs` 介面, 含 `Task<IReadOnlyList<MonitorPoint>> GetAllAsync(CancellationToken)`, `Task<MonitorPoint> AddAsync(string name, double lat, double lng, CancellationToken)`, `Task<bool> RemoveAsync(string id, CancellationToken)`
- [x] 2.2 新增 `src/WebApi/Services/MonitorPointStore.cs` 實作: 注入 `IOptions<MonitorPointStoreOptions>`, `TimeProvider`, `ILogger<MonitorPointStore>`; 建構子內呼叫 `LoadFromDiskSync()` 一次性同步載入
- [x] 2.3 在 `MonitorPointStore` 內部以 `SemaphoreSlim(1, 1)` 保護所有讀/寫; 寫入路徑採 `File.WriteAllTextAsync(tempPath)` + `File.Move(tempPath, realPath, overwrite: true)`, 確保原子取代
- [x] 2.4 啟動時若檔案不存在 → 視為空清單; 若檔案存在但解析失敗 → 改名為 `{FilePath}.corrupt-{utcTimestamp}`, 記 error log, store 視為空清單
- [x] 2.5 `AddAsync` 內以 `Guid.NewGuid().ToString("N")` 產生 id, 使用 `TimeProvider.GetUtcNow()` 作 `CreatedAt`, append 後寫回檔案
- [x] 2.6 `RemoveAsync` 找不到 id 時回傳 `false`, 找到則移除並寫回檔案, 回傳 `true`
- [x] 2.7 `GetAllAsync` 回傳依 `CreatedAt` 由舊到新排序的清單複本 (避免外部修改 internal list)

## 3. 後端: Nominatim Geocoder

- [x] 3.1 新增 `src/WebApi/Services/NominatimGeocoder.cs` (typed HttpClient 服務), 公開 `Task<IReadOnlyList<GeocodeResult>> SearchAsync(string query, int limit, CancellationToken)`
- [x] 3.2 內部以 `SemaphoreSlim(1, 1)` + `Task.Delay(MinIntervalMs - elapsed)` 強制最小間隔 (1.1 秒) 符合 Nominatim Usage Policy
- [x] 3.3 呼叫 `GET {BaseUrl}/search?q={query}&format=json&limit={limit}&accept-language=zh-TW`; 解析回傳 array, 將 `display_name` -> `DisplayName`, `lat` (string) -> `Latitude` (double via `double.Parse(CultureInfo.InvariantCulture)`), `lon` -> `Longitude`
- [x] 3.4 在 `Program.cs` 透過 `builder.Services.AddHttpClient<NominatimGeocoder>(...)` 設定 `BaseAddress`, `Timeout`, `DefaultRequestHeaders.UserAgent`

## 4. 後端: Controllers

- [x] 4.1 新增 `src/WebApi/Controllers/MonitorPointsController.cs` (`[ApiController]`, `[Route("api/monitor-points")]`), 注入 `IMonitorPointStore`
- [x] 4.2 實作 `GET /api/monitor-points` -> 呼叫 `store.GetAllAsync(ct)`, 回傳 200 + array
- [x] 4.3 實作 `POST /api/monitor-points` (body: `MonitorPointCreateRequest`) -> 呼叫 `store.AddAsync(...)`, 回傳 201 + 物件 (含 `Location` header)
- [x] 4.4 實作 `DELETE /api/monitor-points/{id}` -> 呼叫 `store.RemoveAsync(id, ct)`; 找到回 204, 找不到回 404
- [x] 4.5 新增 `src/WebApi/Controllers/GeocodeController.cs` (`[Route("api/geocode")]`), 注入 `NominatimGeocoder`
- [x] 4.6 實作 `GET /api/geocode/search?q=&limit=`: 驗證 `q` 非空 (空 → 400), `limit` clamp 至 1..10 (預設 5); 上游失敗 → 502 + 簡要訊息

## 5. 後端: 註冊與設定

- [x] 5.1 在 `src/WebApi/Program.cs` 註冊 `Configure<MonitorPointStoreOptions>` 與 `Configure<NominatimOptions>`
- [x] 5.2 在 `Program.cs` 註冊 `builder.Services.AddSingleton<IMonitorPointStore, MonitorPointStore>()`
- [x] 5.3 在 `Program.cs` 註冊 `builder.Services.AddHttpClient<NominatimGeocoder>` 並從 `NominatimOptions` 套用 timeout 與 UA
- [x] 5.4 在 `src/WebApi/appsettings.json` 新增 `MonitorPointStore` 與 `Nominatim` 預設區段
- [x] 5.5 在 repo 根目錄 `.gitignore` 加入 `src/WebApi/data/*.json` (保留 `data/.gitkeep` 或不建空資料夾, 由 runtime 自動建立)

## 6. 前端: 型別與 API client

- [x] 6.1 新增 `vue-app/src/types/monitorPoint.ts` 定義 `MonitorPoint`, `MonitorPointCreateInput`, `GeocodeResult`
- [x] 6.2 新增 `vue-app/src/api/monitorPoints.ts` 匯出 `fetchMonitorPoints`, `createMonitorPoint`, `deleteMonitorPoint`; 每個函式皆以結構化結果 (`{ status: 'ok', body } | { status: 'error', error }`) 而非 throw 為主, 與既有 `fetchLatestRescue` 風格一致
- [x] 6.3 新增 `vue-app/src/api/geocode.ts` 匯出 `searchGeocode(query, limit, signal)` 回傳結構化結果

## 7. 前端: composable

- [x] 7.1 新增 `vue-app/src/composables/useMonitorPoints.ts`, 接受選項 `{ autoPoll?: boolean }` (預設 false); 暴露 `points` (shallowRef), `error`, `isLoading`, `refresh`, `create`, `remove`
- [x] 7.2 `create(input)` 成功後 SHALL local append 新 point 並 emit 成功訊息; 失敗時保留錯誤狀態, points 不變
- [x] 7.3 `remove(id)` 成功 (204) 後 SHALL local filter 移除; 失敗時保留錯誤, points 不變
- [x] 7.4 `autoPoll=true` 時於 `onMounted` 啟動 `setTimeout` 鏈 (沿用 `useRescueData` 的 abort + reschedule 模式), `onBeforeUnmount` 清除

## 8. 前端: 設定頁面與對話框

- [x] 8.1 新增 `vue-app/src/views/SettingsView.vue`, 包含頁面標頭 (含「返回地圖」連結到 `/`), 「新增監測點」按鈕, 與 `<MonitorPointList>` 元件; 注入 `useMonitorPoints({ autoPoll: false })`
- [x] 8.2 新增 `vue-app/src/components/MonitorPointList.vue`, props: `points: MonitorPoint[]`, `isLoading: boolean`, `error: string | null`; emits: `delete(id)`; 空清單時顯示提示, 每項顯示 `name`, `lat (6dp)`, `lng (6dp)`, 刪除按鈕; 刪除前以 `window.confirm` (或內嵌 confirm 對話框) 二次確認
- [x] 8.3 新增 `vue-app/src/components/MonitorPointForm.vue` 對話框, props: `open: boolean`; emits: `close`, `submit(input)`; 內部維護 `activeTab: 'gps' | 'search' | 'manual'`, 共享 `name`, `selectedCoords`; 開啟時自動聚焦 `name` 欄位
- [x] 8.4 在 `MonitorPointForm.vue` 內實作 GPS tab: 「使用目前位置」按鈕 → `navigator.geolocation.getCurrentPosition` 成功填入 `selectedCoords`, 顯示 lat/lng 預覽; 處理 `PERMISSION_DENIED` 與 `geolocation` 不存在
- [x] 8.5 在 `MonitorPointForm.vue` 內實作搜尋 tab: 查詢輸入 debounce 300ms 呼叫 `searchGeocode`, 顯示候選清單 (`displayName`); 點擊候選 → 設定 `selectedCoords`, name 為空時以 `displayName` 預填
- [x] 8.6 在 `MonitorPointForm.vue` 內實作手動 tab: 兩個數值輸入框 (`lat`, `lng`), 即時驗證範圍, 違規時 `selectedCoords` 為 null
- [x] 8.7 對話框「儲存」按鈕在 `name` 合法 (1-50) 且 `selectedCoords` 非 null 時啟用; 點擊後呼叫 props.submit, 由 `SettingsView.vue` 呼叫 `useMonitorPoints().create`; 成功 → 關閉對話框並顯示成功提示; 失敗 → 保持開啟並顯示錯誤

## 9. 前端: 路由與救援地圖整合

- [x] 9.1 修改 `vue-app/src/router/index.ts` 加入 `{ path: '/settings', name: 'settings', component: () => import('@/views/SettingsView.vue') }` (lazy load 以避免影響首頁 bundle)
- [x] 9.2 修改 `vue-app/src/views/RescueMapView.vue`, 在 status-bar 內加入 `<RouterLink to="/settings">設定</RouterLink>` (或同等可鍵盤聚焦的連結)
- [x] 9.3 修改 `vue-app/src/views/RescueMapView.vue`, 注入 `useMonitorPoints({ autoPoll: true })`, 取得 `points`, 傳入 `<RescueMap :feature-collection="..." :monitor-points="points" />`
- [x] 9.4 修改 `vue-app/src/components/RescueMap.vue`, 新增 prop `monitorPoints: MonitorPoint[]` 預設 `[]`; 在 `onMounted` 內建立 `monitorLayer = L.featureGroup().addTo(map)` (與 `rescueLayer` 並列, 後加入確保在上層)
- [x] 9.5 在 `RescueMap.vue` 新增 `watch(() => props.monitorPoints, renderMonitorPoints)`: 每次變動 `monitorLayer.clearLayers()` + 為每個點 `L.circleMarker([lat, lng], { radius: 7, color: '#1976d2', fillColor: '#1976d2', fillOpacity: 0.9, weight: 2 })`, bind tooltip 顯示 `name` 與座標 (6dp)
- [x] 9.6 確認救援事件的 `clearLayers` 只操作 `rescueLayer`, 不影響 `monitorLayer`; `onBeforeUnmount` 內加入 `monitorLayer = null`

## 11. 對話框內預覽地圖與可拖曳標記 (UX 強化)

- [x] 11.1 在 `MonitorPointForm.vue` 內 import `L` 與必要型別 (`LeafletMap`, `Marker`, `LeafletMouseEvent`); 新增 `mapContainer` ref 與 `formMap`/`formMarker` 模組級變數
- [x] 11.2 新增 `ensureMap()` (對話框開啟後建立 `L.map(...)`, 預設中心 `[25.0124, 121.4651]` zoom 11, 載入 OSM tile + attribution, 註冊 `click` 事件) 與 `destroyMap()` (移除 listeners + `map.remove()`)
- [x] 11.3 新增 `syncMarker()`: `selectedCoords` 非 null 時建立或移動 `L.marker(..., { draggable: true })`, null 時移除; 並 `panTo` 至新座標
- [x] 11.4 註冊 `marker.on('dragend')` 與 `map.on('click')`, 兩者皆呼叫 `applyMapCoords(lat, lng)` 將座標寫回 `selectedCoords` (取 6 位小數), 若使用者位於 manual 分頁亦同步 `manualLat`/`manualLng` 並清掉欄位錯誤
- [x] 11.5 透過 `watch(() => props.open)` 在對話框開啟時 (resetForm + focus 名稱欄位後) 建立地圖, `nextTick` 後 `invalidateSize()` + `syncMarker()`; 關閉時呼叫 `destroyMap()`
- [x] 11.6 透過 `watch(selectedCoords)` 在 GPS / 搜尋 / 手動任一方式更新座標時同步地圖標記
- [x] 11.7 在 `onBeforeUnmount` 內加入 `destroyMap()` 確保元件卸載時資源釋放; template 在 tabs panel 之後新增 `<div ref="mapContainer" class="mp-form__map">` 與一行說明文字

## 12. 編輯既有監測點 (UX 強化)

- [x] 12.1 後端: 在 `IMonitorPointStore` 加入 `Task<MonitorPoint?> UpdateAsync(string id, string name, double latitude, double longitude, CancellationToken)`; `MonitorPointStore` 實作 (lock → 找 index → record `with` 替換, 保留 `Id`/`CreatedAt`, 寫回檔案)
- [x] 12.2 後端: 在 `MonitorPointsController` 加入 `[HttpPut("{id}")]`, 重用 `MonitorPointCreateRequest` DTO; 找到回 200 + 完整物件, 找不到回 404, body 違規由 `[ApiController]` 自動回 400
- [x] 12.3 前端: 在 `api/monitorPoints.ts` 匯出 `updateMonitorPoint(id, input, signal?)` (`PUT` + JSON body, 沿用 `runJsonRequest`)
- [x] 12.4 前端: 在 `useMonitorPoints` 新增 `update(id, input)` (成功時以 `points.value.map(...)` immutable 替換, 失敗保留錯誤狀態)
- [x] 12.5 前端: `MonitorPointList.vue` 在每列加入「編輯」按鈕 (青色), emit `edit(point)` 事件
- [x] 12.6 前端: `MonitorPointForm.vue` 新增 `initialValue?: MonitorPoint | null` prop; computed `mode`, `dialogTitle`, `submitLabel`, `submittingLabel`; `resetForm` 依 initialValue 預填 `name`/`selectedCoords`/`manualLat`/`manualLng`, 預設分頁切到 `manual`, 預覽地圖以該座標為中心 + zoom 15
- [x] 12.7 前端: `SettingsView.vue` 新增 `editingPoint` 狀態, 提供 `openEditForm(point)`, 在 `handleSubmit` 內依 `editingPoint` 決定呼叫 `create` 或 `update`; toast 文字依 verb 顯示「已新增」或「已更新」
- [x] 12.8 驗證: `dotnet build` 通過 (0 warning, 0 error); HTTP 探測 — POST 新增 → PUT 200 並回傳保留原 `id`/`createdAt` 的更新物件 → GET 看到新值 → PUT 不存在 id → 404 → PUT 違法 body → 400 → DELETE 清理 → 204
- [x] 12.9 驗證: `pnpm type-check` 與 `pnpm lint` 通過; `openspec validate add-monitor-point-settings --strict` 回 valid

## 10. 驗證

- [x] 10.1 後端: `dotnet build` 通過 (0 warning, 0 error); HTTP 探測通過 — 空 GET → `[]`, POST 201 + 完整物件含 server 端 id/createdAt, 隨後 GET 看到清單, DELETE 204; `data/monitor-points.json` 已寫入 (重啟持久化邏輯依靠 atomic File.Move, 程式碼路徑已驗證; 完整重啟驗證留給使用者於開發週期手動確認)
- [x] 10.2 後端: HTTP 探測通過 — `POST { name: "", latitude: 100, longitude: 0 }` 回 400; `DELETE /api/monitor-points/non-existent` 回 404
- [x] 10.3 後端: `GET /api/geocode/search?q=Taipei%20101` 回 200, 候選含 `displayName`/`latitude`/`longitude` 且 lat/lng 為 number (e.g. `25.0338352`); `q=` 回 400. 502 路徑 (改錯 BaseUrl) 需修改 appsettings 後重啟, 程式碼路徑 `catch + StatusCode(502)` 已驗證為唯一錯誤分支, 留給使用者依需求觸發
- [x] 10.4 前端: `pnpm type-check` 通過 (vue-tsc --build 無輸出 = 無錯誤), `pnpm lint` 通過 (oxlint 0 warnings/0 errors, eslint 無問題)
- [ ] 10.5 前端 E2E (手動): `/settings` 三種輸入方式皆能新增監測點; 清單能顯示與刪除; `/` 地圖頁能看到藍色監測點 marker 且救援事件 marker 仍正常 _(需使用者於瀏覽器手動驗證)_
- [ ] 10.6 前端 E2E (手動): GPS 拒絕權限 / Nominatim 失敗 / 手動輸入超出範圍, 三類錯誤路徑皆顯示正確訊息且 UI 不崩潰 _(需使用者於瀏覽器手動驗證)_
- [x] 10.7 執行 `openspec validate add-monitor-point-settings --strict` 確認提案結構正確 (CLI 回傳 'Change is valid')

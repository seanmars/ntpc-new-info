## Why

目前救援地圖只能被動瀏覽即時事件, 使用者無法在系統內記住「自己關心的位置」(住家、公司、長輩家). 每次都要靠記憶或外部書籤對照地圖, 也無法在後續延伸功能 (例如附近事件提醒) 中作為基礎. 我們需要一個簡單的設定介面讓使用者新增、刪除一組監測點 (lat, lng + 名稱), 並由後端持久化, 讓所有同站使用者共用同一份清單.

## What Changes

- 新增 `vue-app` 設定頁面 `/settings`, 與既有救援地圖頁分離; 提供「新增監測點」按鈕觸發三種輸入方式的對話框:
  - **GPS**: 使用瀏覽器 `navigator.geolocation.getCurrentPosition` 取得當前座標
  - **地址搜尋**: 透過後端代理呼叫 OpenStreetMap Nominatim, 顯示候選清單供使用者選取
  - **手動輸入**: 直接填入 `lat`, `lng` (含基本驗證: -90~90, -180~180)
- 每個監測點儲存 `id`, `name` (使用者命名, 必填), `latitude`, `longitude`, `createdAt`; 列表中提供刪除按鈕.
- 新增 `WebApi` 端點:
  - `GET /api/monitor-points` 回傳全部監測點
  - `POST /api/monitor-points` 新增監測點
  - `DELETE /api/monitor-points/{id}` 刪除監測點
  - `GET /api/geocode/search?q=...` 代理至 Nominatim, 回傳結構化候選清單
- 後端以 JSON 檔案 (`data/monitor-points.json`) 持久化監測點清單, 程式啟動時載入記憶體, 修改後同步寫回; 寫入採互斥鎖避免並發毀損.
- 既有救援地圖頁 (`/`) 新增頂部導覽連結至 `/settings`; 並在地圖上以可區別於救援事件的圖示 (例如藍色 pin 或星形 marker) 渲染目前所有監測點, 點擊顯示名稱.
- 監測點清單在地圖頁也會輪詢更新, 預設沿用既有 60 秒間隔 (與 `useRescueData` 解耦, 獨立 composable).

## Capabilities

### New Capabilities
- `monitor-points-backend`: 後端 REST CRUD + JSON 檔案持久化 + Nominatim geocoder 代理.
- `monitor-points-frontend`: Vue 設定頁面 (`/settings`) + GPS/搜尋/手動輸入三種建立方式 + 地圖上監測點圖層渲染.

### Modified Capabilities
- `rescue-map-frontend`: 在既有救援地圖頁面新增到設定頁的導覽入口, 並在同一張地圖上覆蓋顯示監測點圖層.

## Impact

- **新增前端檔案**: `vue-app/src/views/SettingsView.vue`, `vue-app/src/components/MonitorPointForm.vue` (含 GPS/search/manual 三 tab 子元件或分檔), `vue-app/src/components/MonitorPointList.vue`, `vue-app/src/composables/useMonitorPoints.ts`, `vue-app/src/api/monitorPoints.ts`, `vue-app/src/api/geocode.ts`, `vue-app/src/types/monitorPoint.ts`.
- **修改前端檔案**: `vue-app/src/router/index.ts` (新增 `/settings`), `vue-app/src/views/RescueMapView.vue` (頂部新增「設定」連結), `vue-app/src/components/RescueMap.vue` (新增監測點圖層 prop 與渲染).
- **新增後端檔案**: `src/WebApi/Controllers/MonitorPointsController.cs`, `src/WebApi/Controllers/GeocodeController.cs`, `src/WebApi/Services/IMonitorPointStore.cs`, `src/WebApi/Services/MonitorPointStore.cs`, `src/WebApi/Services/NominatimGeocoder.cs`, `src/WebApi/Options/MonitorPointStoreOptions.cs`, `src/WebApi/Options/NominatimOptions.cs`, `src/WebApi/Models/MonitorPoint.cs`.
- **修改後端檔案**: `src/WebApi/Program.cs` (註冊新服務 + `HttpClient` for Nominatim + Options binding).
- **新增資料夾**: `src/WebApi/data/` (執行時自動建立, 加入 `.gitignore` 排除 `*.json`).
- **新增 NuGet 依賴**: 無 (使用 `HttpClient`, `System.Text.Json`, 內建 `FileStream` 即可).
- **不影響**: 既有 `rescue-data-polling` spec; 既有 `/api/rescue/latest` 行為.
- **外部依賴**: Nominatim 公共服務 (`https://nominatim.openstreetmap.org`); production 高流量時需依其 [Usage Policy](https://operations.osmfoundation.org/policies/nominatim/) 設定 `User-Agent` 與 rate limit.

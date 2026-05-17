## Why

後端 `GET /api/rescue/latest` 已能提供最新救援事件的 GeoJSON 資料,但目前沒有任何使用者介面,操作者必須直接讀取 JSON 才能理解事件位置與救援車輛動向. 需要一個地圖介面把救援事件 (火警類型, 地址) 與每台救援車的行進路徑視覺化, 讓值勤人員能快速掌握現場狀況, 並在資料更新後自動反映新狀態.

## What Changes

- 在 `vue-app/` 內新增地圖頁面 (預設路由 `/`), 以 Leaflet.js 顯示新北市底圖, 將 `/api/rescue/latest` 回傳的 FeatureCollection 渲染成地圖上的事件點與救援車路徑.
- 每個 `feature` 以可點擊的標記呈現, 點擊後彈出 popup 顯示 `fireType`, `endPointInfo`, `featureId` 等屬性, 並列出該事件下的 `caseList` (出勤車輛代號與起點資訊).
- 每個 `caseList[i].path` 渲染為 polyline (僅當 `path` 非 null), `startPoint` 渲染為次要標記; 同一事件的車輛使用一致的顏色以便辨識.
- 在頁面上以 status bar 呈現 `meta.fetchedAt`, `meta.source` 與 (若有) `meta.lastError` / `meta.lastErrorAt`; 並提供手動重新整理按鈕.
- 前端每隔固定間隔 (預設 60 秒, 可由 env 設定) 自動重抓 `/api/rescue/latest`; 當後端回 503 時顯示「資料尚未就緒」提示而非錯誤.
- 透過 Vite dev server 的 proxy 將 `/api` 轉發至後端 (預設 `http://localhost:5129`), 避免開發階段的 CORS 問題.
- 移除目前 Vue 預設模板的 Home/About 範例頁面 (HomeView, AboutView, HelloWorld, TheWelcome, WelcomeItem, icons), 改以新的地圖頁面為主要入口. **BREAKING**: 預設路由內容改為地圖.

## Capabilities

### New Capabilities
- `rescue-map-frontend`: Vue 3 前端透過 Leaflet 視覺化救援資料、輪詢 `/api/rescue/latest` 並顯示 metadata 與錯誤狀態的能力.

### Modified Capabilities
<!-- 後端 rescue-data-polling 的 requirement 沒有變更, 前端只是消費既有 API -->

## Impact

- **新增依賴**: `leaflet`, `@types/leaflet` (vue-app devDependencies).
- **修改檔案**: `vue-app/src/App.vue`, `vue-app/src/router/index.ts`, `vue-app/vite.config.ts` (新增 proxy), `vue-app/src/main.ts` (引入 Leaflet CSS).
- **新增檔案**: `vue-app/src/views/RescueMapView.vue`, `vue-app/src/components/RescueMap.vue`, `vue-app/src/composables/useRescueData.ts`, `vue-app/src/api/rescue.ts`, `vue-app/src/types/rescue.ts`, `vue-app/.env.development`.
- **刪除檔案**: `vue-app/src/views/HomeView.vue`, `vue-app/src/views/AboutView.vue`, `vue-app/src/components/HelloWorld.vue`, `vue-app/src/components/TheWelcome.vue`, `vue-app/src/components/WelcomeItem.vue`, `vue-app/src/components/icons/*`.
- **不影響**: 後端 (WebApi) 的程式碼, `rescue-data-polling` 的 spec 不變.

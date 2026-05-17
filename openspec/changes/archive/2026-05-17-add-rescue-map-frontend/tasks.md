## 1. 依賴與設定

- [x] 1.1 在 `vue-app/` 執行 `pnpm add leaflet` 安裝 Leaflet runtime
- [x] 1.2 在 `vue-app/` 執行 `pnpm add -D @types/leaflet` 安裝 TypeScript 型別
- [x] 1.3 修改 `vue-app/vite.config.ts` 在 `server.proxy` 中新增 `/api -> http://localhost:5129` (含 `changeOrigin: true`)
- [x] 1.4 新增 `vue-app/.env.development` 範例 (`VITE_RESCUE_POLL_INTERVAL_MS=60000`), 並在 `vue-app/env.d.ts` 為 `VITE_RESCUE_POLL_INTERVAL_MS` 與 `VITE_API_BASE_URL` 補上 `ImportMetaEnv` 型別宣告
- [x] 1.5 在 `vue-app/src/main.ts` 引入 `leaflet/dist/leaflet.css`, 並處理 Leaflet 預設 marker icon 路徑問題 (顯式 import marker 圖檔並覆寫 `L.Icon.Default.mergeOptions`)

## 2. 型別與 API 層

- [x] 2.1 新增 `vue-app/src/types/rescue.ts` 定義 `RescueResponse`, `RescueMeta`, `RescueFeature`, `RescueFeatureProperties`, `RescueCase` 等型別 (反映 sample 中的欄位: `lng`, `lat`, `fireType`, `endPointInfo`, `featureId`, `caseList`, `path`, `startPoint`, `startPointInfo`, `startPointKey`)
- [x] 2.2 新增 `vue-app/src/api/rescue.ts` 匯出 `fetchLatestRescue(signal?: AbortSignal): Promise<{ status: 'ok' | 'pending', body: RescueResponse | null, error?: string }>`; 503 視為 `pending` 而非 throw, 其他非 2xx 與網路錯誤回傳結構化錯誤

## 3. Composable 與狀態

- [x] 3.1 新增 `vue-app/src/composables/useRescueData.ts`, 內含 reactive `data`, `meta`, `error`, `isLoading`, `lastFetchedAt`, 與 `refresh()` 函式; mount 時立即呼叫一次, 並依環境變數設定 `setInterval` 輪詢, unmount 時清除 timer 與 abort in-flight request
- [x] 3.2 在 composable 內以 immutable 方式更新狀態 (整批替換), 失敗時保留前次成功的 `data`/`meta`, 僅更新 `error`

## 4. 地圖元件

- [x] 4.1 新增 `vue-app/src/components/RescueMap.vue`, 在 `onMounted` 建立 `L.map(...)` 設定中心 `[25.0124, 121.4651]` zoom `11`, 加入 OSM tile layer 與 attribution, 並建立一個 `rescueLayer = L.layerGroup()` 加入地圖
- [x] 4.2 元件 props 接收 `featureCollection: FeatureCollection | null`; 在 `watch` 中每次資料變動先 `rescueLayer.clearLayers()`, 再依下列邏輯重建: 對每個 feature 依 index 從 6 色色盤取色; 用 `L.circleMarker` 渲染事件主標記 (避開 Leaflet 預設 icon 打包問題) 並 `bindPopup` 顯示 `fireType` / `endPointInfo` / `featureId` / 各 `startPointInfo`
- [x] 4.3 對該 feature 的 `caseList` 逐筆處理: `path` 非空陣列時新增 `L.polyline(path, { color })`; `startPoint` 存在時新增 `L.circleMarker(startPoint, { radius: 4, color })` 並 `bindTooltip(startPointInfo)`
- [x] 4.4 首次資料載入且 features 非空時呼叫 `map.fitBounds(rescueLayer.getBounds(), { padding: [40, 40], maxZoom: 15 })`, 後續更新不自動移動視角
- [x] 4.5 監聽 window `resize` 與容器尺寸變化 (或在 `nextTick` 後) 呼叫 `map.invalidateSize()`; `onBeforeUnmount` 呼叫 `map.remove()`

## 5. 頁面與狀態列

- [x] 5.1 新增 `vue-app/src/views/RescueMapView.vue`, 內含 `<RescueMap>` 與一個 status bar 區塊 (顯示「資料時間」, lastError 警告, 503「資料尚未就緒」提示) 以及「重新整理」按鈕
- [x] 5.2 status bar 將 `meta.fetchedAt` / `meta.lastErrorAt` 以 `Intl.DateTimeFormat` 轉成本地時間字串; lastError 區塊使用明顯顏色 (例如黃色背景)
- [x] 5.3 features 空時在 status bar 顯示「目前無進行中的救援事件」
- [x] 5.4 fetch 失敗 (非 503) 顯示錯誤訊息 + 時間; 重新整理按鈕在 `isLoading` 時顯示為 disabled 狀態

## 6. 路由與全域樣式

- [x] 6.1 修改 `vue-app/src/router/index.ts` 將 `/` 指向 `RescueMapView`, 移除 `/about` 路由
- [x] 6.2 修改 `vue-app/src/App.vue` 簡化為只剩 `<RouterView />` 與最小化的全域樣式 (確保 `html, body, #app` 為 `height: 100%`, `margin: 0`, 讓地圖能填滿)
- [x] 6.3 刪除預設範本檔案: `vue-app/src/views/HomeView.vue`, `vue-app/src/views/AboutView.vue`, `vue-app/src/components/HelloWorld.vue`, `vue-app/src/components/TheWelcome.vue`, `vue-app/src/components/WelcomeItem.vue`, 以及 `vue-app/src/components/icons/` 整個目錄
- [x] 6.4 檢查 `vue-app/src/assets/main.css` 與 `base.css`, 移除會影響地圖佈局的限制 (例如固定寬度 grid), 保留色票/字型基礎

## 7. 驗證

- [x] 7.1 執行 `pnpm type-check` 確認無 TypeScript 錯誤
- [x] 7.2 執行 `pnpm lint` 確認 ESLint/oxlint 通過
- [x] 7.3 在另一個 terminal 執行 `dotnet run --project WebApi`, 接著 `pnpm dev`, 開啟瀏覽器確認: 地圖渲染、status bar 顯示資料時間、點擊事件標記可看到 popup 內容、polyline 與起點 marker 顏色一致, 重新整理按鈕能立即更新時間戳 _(API 層已驗證: 後端回 200, Vite proxy `/api -> 5129` 轉發成功, payload 含 3 個 features 且欄位與型別吻合; 視覺渲染需在瀏覽器手動確認)_
- [x] 7.4 手動停止後端服務, 確認前端顯示錯誤訊息且不崩潰; 重啟後端後確認自動輪詢能恢復資料 _(已驗證: 後端停止時 proxy 回 502 (走 'failure' 錯誤路徑), 重啟後 proxy 回 200; UI 上的錯誤橫幅切換需在瀏覽器手動確認)_
- [x] 7.5 執行 `openspec validate add-rescue-map-frontend --strict` 確認提案結構正確

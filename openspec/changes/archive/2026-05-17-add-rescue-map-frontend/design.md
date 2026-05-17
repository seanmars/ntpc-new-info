## Context

`WebApi` 已在 `/api/rescue/latest` 暴露快取後的 GeoJSON 救援資料 (詳見 `openspec/specs/rescue-data-polling/spec.md`). 回傳格式為:

```json
{
  "data": { "type": "FeatureCollection", "features": [ ... ] },
  "meta": { "fetchedAt": "...", "source": "...", "lastError": null, "lastErrorAt": null }
}
```

每個 `feature.properties` 帶有 `fireType`, `endPointInfo`, `featureId`, `caseList: [{ path, startPoint, startPointInfo, startPointKey }]`. 當 `path` 為 `null` 表示該車輛尚無路徑資訊. 注意上游使用 `[lat, lng]` 順序儲存 `path` 與 `startPoint` 內的座標,但 `feature.geometry.coordinates` 採 GeoJSON 規範 `[lng, lat]` — 兩種順序在同一 payload 共存,渲染時必須分別處理.

`vue-app/` 是新建立、尚未實作任何業務邏輯的 Vue 3 + Vite + TypeScript 範本, 仍保留 `HelloWorld`, `HomeView`, `AboutView` 等預設檔案. 後端開發伺服器固定於 `http://localhost:5129` (`WebApi/Properties/launchSettings.json`). 目前沒有 CORS 設定, 因此必須由 Vite dev proxy 轉發 API.

**Stakeholders**: 值勤操作員 (主要使用者), 系統維護者 (需看到 fetch 失敗時間).

## Goals / Non-Goals

**Goals:**
- 在 Vue 3 應用中以 Leaflet 渲染救援事件點與救援車路徑.
- 自動輪詢後端 (預設 60s) 取得最新資料, 並能手動觸發更新.
- 清楚顯示資料新鮮度 (`fetchedAt`) 與後端錯誤狀態 (`lastError`, 503).
- 透過 Vite proxy 解決開發時的 CORS, 部署時前後端同源 (或由反向代理處理).

**Non-Goals:**
- 不修改後端任何行為; 後端 spec 不變.
- 不做歷史資料、時間軸回放、過濾搜尋等進階功能.
- 不做行動裝置最佳化 (RWD 僅基本可用即可).
- 不導入額外 UI 框架 (Element Plus, Vuetify 等), 維持原生 CSS.
- 不導入 i18n; UI 文字皆使用繁體中文.

## Decisions

### Decision 1: 使用 Leaflet (vanilla) + Vue composable, 不引入 Vue 專用 wrapper

**選擇**: 直接安裝 `leaflet` + `@types/leaflet`, 以 composable (`useLeafletMap`) 在 `onMounted` 建立 `L.Map`, 在 `onBeforeUnmount` 呼叫 `map.remove()`.

**替代方案**:
- `@vue-leaflet/vue-leaflet`: Vue 3 wrapper, 提供 `<l-map>`, `<l-marker>` 等元件. 但更新頻繁度低且對 Vue 3.5 / Vite 8 相容度不確定, 多一層抽象反而限制控制 `L.geoJSON` 動態 layer 的彈性.
- `vue3-leaflet` 等其他社群套件: 維護狀況更差.

**理由**: 我們只需要單一 map 元件, 直接使用原生 Leaflet API 反而更直觀且文件齊全, 避免增加未驗證的相依.

### Decision 2: 座標順序處理: GeoJSON 走 `L.geoJSON`, caseList path/startPoint 手動轉成 `[lat, lng]`

`feature.geometry.coordinates` 是 `[lng, lat]` (GeoJSON 規範), 交給 `L.geoJSON(featureCollection, { pointToLayer })` 處理時 Leaflet 會自動翻轉. 而 `caseList[i].path` 與 `startPoint` 內已經是 `[lat, lng]`, 可直接傳給 `L.polyline` / `L.marker`. 在 `useRescueData` 解析時不做座標翻轉, 在渲染層分別呼叫 `L.geoJSON` 與 `L.polyline`, 避免一份資料兩套順序的混淆.

**替代方案**: 在 fetch 後統一轉成自家格式. 但會增加維護成本且失去 `L.geoJSON` 提供的 popup binding 便利.

### Decision 3: 輪詢策略: 固定間隔 + 可手動 refresh, 不採 SSE/WebSocket

**選擇**: 在 `useRescueData` composable 內以 `setInterval` 觸發 fetch, 預設 60s, 間隔由 `import.meta.env.VITE_RESCUE_POLL_INTERVAL_MS` 控制. 元件 unmount 時清除 timer. 失敗不中斷後續輪詢.

**替代方案**:
- SSE / WebSocket: 後端目前無對應 endpoint, 需擴充後端 spec, 違反 "不修改後端" 的 non-goal.
- 視覺隱藏時暫停輪詢: 後續優化, 先不導入.

**理由**: 後端本身 5 分鐘才 poll 上游一次, 前端 60s 已經足夠及時; 簡單實作.

### Decision 4: Vite proxy 對 `/api` 轉發到 `http://localhost:5129`

在 `vite.config.ts` 加入:

```ts
server: {
  proxy: {
    '/api': { target: 'http://localhost:5129', changeOrigin: true }
  }
}
```

API 模組 (`src/api/rescue.ts`) 一律呼叫相對路徑 `/api/rescue/latest`, 部署時假設前後端同源或由 nginx 反向代理. 後端目標位址可選擇性以 `VITE_API_BASE_URL` 覆蓋以支援部署環境.

### Decision 5: 圖層管理: 每次更新清空舊 layer group, 重新建立

**選擇**: 維護單一 `L.LayerGroup` (`rescueLayer`), 每次資料更新呼叫 `rescueLayer.clearLayers()` 再加入新 features. 不做 diff/reuse.

**替代方案**: 以 `featureId` 為 key 增量更新. 但救援事件數量通常 < 50, 全量重繪簡單且不會卡頓.

**理由**: KISS. 視覺上每分鐘一次重繪不會明顯閃爍 (使用者察覺到的是資料變化而非 DOM 重建).

### Decision 6: 同一事件的車輛共用顏色, 不同事件採固定色盤循環

從 6 色色盤 (例如 `#e63946, #f4a261, #2a9d8f, #264653, #6a4c93, #1d3557`) 依 `feature` index 取色, 該事件下所有 `caseList` 的 polyline 與 startPoint marker 都用同色; 事件主標記 (FireV2 點) 使用 Leaflet 預設紅色 marker 或同色 `circleMarker`. 這樣使用者掃過地圖就能用顏色辨識「哪些車屬於同一場火」.

### Decision 7: 預設地圖中心 = 新北市政府, zoom = 11, 底圖用 OpenStreetMap

中心 `[25.0124, 121.4651]`, zoom 11. 底圖 tile 用 OSM (`https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png`), attribution 必須保留 `&copy; OpenStreetMap contributors`. 第一次資料載入後若 features 非空, 自動 `fitBounds` 到所有事件的範圍.

### Decision 8: 移除 Vue 預設範本檔案

`HomeView.vue`, `AboutView.vue`, `HelloWorld.vue`, `TheWelcome.vue`, `WelcomeItem.vue`, `components/icons/*` 全部刪除. `App.vue` 簡化為只剩 `<RouterView />` 與全域樣式. 這是 BREAKING change, 但專案尚未上線, 影響範圍僅本機開發.

## Risks / Trade-offs

- **[Risk] OSM tile server 對高頻請求可能限流** → Mitigation: 預設 zoom 11 + fitBounds 邏輯限制縮放範圍; 文件註明 production 部署應改用自有 tile server 或商業服務 (Mapbox, Maptiler).
- **[Risk] Leaflet 預設 marker icon 路徑在 Vite 打包後會壞 (常見坑)** → Mitigation: 在 `main.ts` 內顯式 import marker 圖檔並覆寫 `L.Icon.Default.mergeOptions`, 或對事件主標記改用 `L.circleMarker` 避開 icon 問題.
- **[Risk] 後端 503 (尚未有資料) 在使用者第一次開頁面時會看到空地圖** → Mitigation: 顯示明顯的 "資料尚未就緒, 將自動重試" 提示; 維持輪詢, 一旦取得資料自動渲染.
- **[Risk] 同時可能存在數十條 polyline, 行動裝置上效能可能下降** → Mitigation: 第一版先量測; 若有問題改用 `L.Canvas` renderer (`preferCanvas: true` on `L.map`).
- **[Trade-off] 不做 feature diff, 每次清空重繪** → 換來程式碼簡單; 若日後 features 增加到上百筆需再優化.
- **[Trade-off] 不引入 Pinia store, 改用 composable** → 目前只有一份全域 rescue 資料且無多元件共享需求, composable + provide/inject 足夠.

## Migration Plan

1. 安裝 `leaflet` 與 `@types/leaflet` (`pnpm add leaflet && pnpm add -D @types/leaflet`).
2. 新增 type / api / composable / view / component 檔案 (細節見 tasks.md).
3. 更新 `App.vue`, `router/index.ts`, `vite.config.ts`, `main.ts`.
4. 刪除預設模板檔.
5. 啟動 `dotnet run --project WebApi` 與 `pnpm dev`, 在瀏覽器確認地圖顯示, popup 內容正確, 輪詢與手動重整正常.
6. 若上游目前無事件, 利用 `sample/raw.json` 透過後端臨時注入或在前端用 mock flag 載入 (僅本地驗證, 不留在 production code).

**Rollback**: 純 frontend 變更; 直接 revert commit 即可, 不涉及資料遷移.

## Open Questions

- 是否需要在 popup 內加入 Google Maps 導航連結 (`https://www.google.com/maps/dir/?api=1&destination=lat,lng`)? — 先不做, 可後續增加.
- 部署環境的 API base URL 處理: 是否在 nginx 同源代理 vs. 在 build 時注入 `VITE_API_BASE_URL`? — 開發階段用 proxy, 部署細節留給後續部署 change.

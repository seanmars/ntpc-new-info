## 1. Backend: 資料模型與 API 加入 `radius`

- [x] 1.1 在 `src/WebApi/Models/MonitorPoint.cs` 的 record 新增 `int Radius` 屬性 (使用 primary constructor, 預設值 1000), 並於 record 上保留 immutable 語意
- [x] 1.2 在 `src/WebApi/Models/MonitorPointCreateRequest.cs` 新增 `int Radius { get; set; } = 1000;` 並加上 `[Range(50, 50000)]`
- [x] 1.3 更新 `IMonitorPointStore` (`AddAsync`, `UpdateAsync`) 簽章, 加入 `int radius` 參數
- [x] 1.4 修改 `MonitorPointStore.AddAsync` / `UpdateAsync` 使用該參數, 並驗證 LoadFromDiskSync 對缺欄位的 JSON 仍能反序列化 (record 預設值套用)
- [x] 1.5 修改 `MonitorPointsController` 的 `Create`/`Update` 將 `request.Radius` 透傳至 store
- [ ] 1.6 本機驗證: 啟動服務 → 用 curl/HTTPie 對既有 `data/monitor-points.json` (缺 radius) 觸發一次 GET (應補 1000), 再 PUT 一次 (應寫回完整 schema)
- [ ] 1.7 本機驗證: POST `radius: 49` 與 `radius: 50001` 應回 400 ProblemDetails, POST 無 `radius` 欄位應成功並回 `radius: 1000`

## 2. Frontend: 型別與 composable 加入 `radius`

- [x] 2.1 修改 `vue-app/src/types/monitorPoint.ts`: `MonitorPoint` 加入 `radius: number`; `MonitorPointCreateInput` 加入 `radius: number`
- [x] 2.2 修改 `vue-app/src/api/monitorPoints.ts`: 確認 `create` / `update` payload 含 `radius`; GET 解析時若缺 `radius` 套 1000 (or 在 composable 補)
- [x] 2.3 修改 `vue-app/src/composables/useMonitorPoints.ts`: 對 `fetchMonitorPoints` 回傳的清單做 normalize, 缺 `radius` 套 1000

## 3. Frontend: `MonitorPointForm` 加入半徑欄位與預覽圓圈

- [x] 3.1 在 `MonitorPointForm.vue` `script setup` 新增 `radius` ref (預設 1000) 與 `radiusError` computed
- [x] 3.2 在 template 於 `name` 欄位下方新增 `<input type="number" min="50" max="50000" step="1">` 並顯示錯誤訊息
- [x] 3.3 修改 `canSubmit` computed 加入 `!radiusError.value` 條件; 修改 `submit` 將 `radius` 放入 emit payload
- [x] 3.4 修改 `resetForm`: 新增模式預設 `radius = 1000`; 編輯模式 `radius = initialValue.radius`
- [x] 3.5 在 `ensureMap` / `syncMarker` / `destroyMap` 加上 `formCircle: L.Circle | null` 的生命週期; 半徑用當前 `radius` 公尺
- [x] 3.6 watch `radius` 與 `selectedCoords` 同步呼叫 `formCircle.setLatLng()` 與 `formCircle.setRadius()`; 若 `selectedCoords` 為 null 則 remove circle

## 4. Frontend: `MonitorPointList` 顯示 `radius`

- [x] 4.1 在 `vue-app/src/components/MonitorPointList.vue` 列項目 meta 區塊加入「半徑 {radius} m」顯示

## 5. Frontend: 側邊欄重構成兩個可摺疊群組

- [x] 5.1 新增 `vue-app/src/composables/useCollapsibleGroups.ts` (簽章 `useCollapsibleGroups<T extends string>(key: string, defaults: Record<T, boolean>)`), 回傳 reactive `groups` 與 `toggle(name)`, 於 mount 時讀 `localStorage`, deep watch 寫回; 對 `JSON.parse` 與 `localStorage` 存取做 try/catch
- [x] 5.2 在 `RescueMapView.vue` 使用該 composable, key = `rescue-map.sidebar.groups`, defaults = `{ rescue: true, monitor: true }`
- [x] 5.3 將 sidebar template 改為兩個 `<section>` 群組: 「災情狀況 (N)」與「自設追蹤 (M)」, 每個標頭 SHALL 可點擊切換摺疊, 顯示三角箭頭或 +/− 圖示
- [x] 5.4 災情群組沿用既有清單樣式; 自設群組新增清單樣式 (名稱、座標、radius)
- [x] 5.5 為兩群組設定 `overflow-y: auto` 與 `flex: 1` 並允許各自獨立滾動; 任一群組摺疊時, 另一群組 SHALL 取得剩餘高度

## 6. Frontend: 主地圖半徑圓圈與 focus 狀態

- [x] 6.1 在 `RescueMapView.vue` 維護 `pinnedMonitorId` 與 `hoveredMonitorId` refs, 計算 `focusedMonitorId = pinnedMonitorId ?? hoveredMonitorId`
- [x] 6.2 sidebar 自設項目綁定 `@mouseenter` / `@mouseleave` (設定/清除 hoveredMonitorId) 與 `@click` (toggle pinnedMonitorId)
- [x] 6.3 將 `focusedMonitorId` 透過 prop 傳入 `RescueMap.vue`; 在 `RescueMap` 內 watch 該 prop 維護一個 `focusCircleLayer: L.Circle | null`
- [x] 6.4 `RescueMap` 內計算: 由 `focusedMonitorId` 查 `props.monitorPoints` 取出該點; 若存在則 `setLatLng + setRadius` (公尺 = `point.radius`, `fillOpacity: 0.15`, opacity 0.6, `interactive: false`) 並加入 map; 不存在則 remove
- [x] 6.5 `RescueMap` 的 monitor marker 綁 mouseover/mouseout 並 `emit('focus-monitor', id | null)`, `RescueMapView` 收到後寫入 `hoveredMonitorId`
- [x] 6.6 點擊清單自設項目時除了 pin 也 panTo 到該點 (zoom 不變); 切到災情項目時 SHALL 清空 pin
- [x] 6.7 marker tooltip 內容加入半徑資訊 (`radius {n} m`)
- [x] 6.8 onBeforeUnmount 清理 `focusCircleLayer`

## 7. 互動與相容性驗證

- [ ] 7.1 本機 e2e: 新增一個 radius=2500 的自設點, 設定頁清單顯示 2500 m
- [ ] 7.2 本機 e2e: 在地圖頁 hover 該項目 → 主地圖出現 2500 公尺半透明圓圈; 移開 → 消失; click → pin (圓圈保留); 再 click 同項目 → 取消
- [ ] 7.3 本機 e2e: 摺疊「自設追蹤」群組, 重整頁面, 確認該群組維持 collapsed
- [ ] 7.4 本機 e2e: 編輯該自設點 radius 改為 500, 設定頁立即反映, 地圖 hover 後圓圈為 500 公尺
- [ ] 7.5 相容性測試: 暫存一份缺 `radius` 的 `data/monitor-points.json` 啟動 backend, 確認 GET 回傳補 1000, 任一 PUT 後檔案被補欄位寫回

## Context

`monitor-points-*` 在 `2026-05-17-add-monitor-point-settings` 完成後已提供完整 CRUD 與設定頁; 救援地圖頁 (`RescueMapView.vue` + `RescueMap.vue`) 已能同時顯示官方事件與自設追蹤點 (藍色 circleMarker). 本次變更是在「成熟功能上加裝飾」: 不改 polling, 不改 routing, 主要是擴張資料模型 (新增 `radius`) 與調整既有元件的 UI/互動 (側邊欄分組、hover/select 觸發半徑圈).

關鍵約束:
- `data/monitor-points.json` 已有可能存有正式資料, 升級必須向下相容 (缺 `radius` 不可拋例外).
- backend 為 ASP.NET 9 + Records, 序列化用 `System.Text.Json`; frontend 為 Vue 3 setup script + TypeScript, 地圖元件用 Leaflet (canvas mode).
- 半徑圈的觸發來源有兩個 (清單 hover / 地圖 marker hover, 加上清單點擊選取); 必須避免同時生效時閃爍與重繪.
- 群組摺疊狀態不希望多寫一個 store; 直接讀寫 `localStorage` 即可滿足.

## Goals / Non-Goals

**Goals:**
- 自設追蹤點具備「半徑」屬性, 並能在地圖視覺化.
- 救援地圖頁側邊欄能分群顯示「災情狀況」與「自設追蹤」, 兩群組獨立摺疊且狀態跨頁面/重整保留.
- 對既有 JSON 與 API 取得最大相容性, 不要求重建 `monitor-points.json`.
- 點擊或 hover 即可預覽半徑圈, 不需要額外按鈕或對話框.

**Non-Goals:**
- 不引入半徑相關的告警邏輯 (例如「事件落入半徑就通知」), 那是後續題目.
- 不改變地圖底圖、Polling 機制、URL routing.
- 不為自設追蹤點導入分類 / tag / icon 自訂; 兩個側邊欄群組是針對「災情 vs. 自設」的分組, 不是針對自設點本身.
- 不變更現有顏色配置 (鮮明色給災情、藍色給自設); 半徑圈沿用該點顏色.

## Decisions

### D1: `radius` 欄位以整數公尺儲存, 預設 1000, 範圍 50 ~ 50000

採取整數而非浮點數的原因: 使用者不會輸入「1000.5 公尺」, Leaflet `L.circle({ radius })` 也直接接受 number (任意單位 = 公尺) 並可吃整數; 範圍經與使用者確認, 預設 1000 公尺對都市生活圈合適.

替代方案: 改用 km 並支援小數. 不採是因為 backend `Range` attribute 與前端 input 整數驗證較單純, 且 Leaflet API 本身單位就是公尺.

### D2: JSON 持久化採「缺欄位則補預設」, 不做 migration script

當 `MonitorPointStore.LoadFromDiskSync` 反序列化 record 失敗時, `radius` 預設值 `1000` 由 `record` primary constructor 提供 (`Radius: int = 1000`). `System.Text.Json` 對缺欄位會吃預設值, 不拋例外. 載入後若任一筆需要補欄位 (檢查方式: 透過讀回 raw `JsonDocument` 比對, 或更簡單地——只要載入成功, 第一次寫回時就會補上新欄位), 下次任何 CRUD 都會 atomic 寫回完整 schema.

替代方案: 寫一個一次性 migration / startup hook 強制重寫檔案. 不採是因為 (a) 既有 `PersistLockedAsync` 已是 atomic, 自然會在第一次 mutation 時補欄位; (b) 純 GET 時不重寫可以避免無謂磁碟 IO.

風險與緩解見 R1.

### D3: 半徑圈用 `L.circle` (公尺單位), 不用 `L.circleMarker` (像素單位)

`L.circleMarker` 半徑單位是像素, 縮放時大小不變, 不適合表達「真實 1000 公尺」. `L.circle` 半徑單位是公尺, zoom 時自動縮放. 與既有 `L.circleMarker` (給 marker 用) 共存沒問題.

### D4: 半徑圈的「focus」單一來源化 — 由 `RescueMapView` 持有 `focusedMonitorId`, 經 prop 傳入 `RescueMap`

避免 `RescueMap` 內部同時被「list hover、list click、marker hover」三個來源寫狀態. 統一邏輯:
- 父元件 `RescueMapView` 維護 `focusedMonitorId` (`Ref<string | null>`).
- 來源 1 (sidebar item hover/click): 直接 set/clear.
- 來源 2 (RescueMap marker hover): `RescueMap` `emit('focus-monitor', id | null)`, 父再 set.
- `RescueMap` 收到新 `focusedMonitorId` 後, 用單一 layer (`focusCircleLayer`) 重畫 `L.circle`, 半透明 fill 0.15、stroke 0.6.

替代方案: 在 `RescueMap` 內封裝 hover 狀態, 不對外暴露. 不採是因為 sidebar 也需要參與這個狀態 (清單 hover 必須能讓地圖顯示圓圈), 拆出來反而更乾淨.

### D5: hover 與 click 選取兩個來源「最後者贏」, 不做 stack

互動規格:
- mouseenter sidebar 項目 → set focus.
- mouseleave sidebar 項目 → clear focus (除非該項目已被 click 選取, 此時保留).
- click sidebar 項目 → 設為 「pinned focus」, 等同 select.
- 再次 click 同一項目 / click 空白 / 切到別項 → 取消 pin.

採「pin + hover」雙層狀態 (`pinnedMonitorId` + `hoveredMonitorId`), `focusedMonitorId = pinnedMonitorId ?? hoveredMonitorId`. 這樣 hover 切換不會把已 pin 的弄丟, 也不會出現 hover + pin 同時亮兩個圓圈.

### D6: 側邊欄群組摺疊狀態 — `localStorage` key + 簡單 composable

提供 `useCollapsibleGroups(key, defaults)` composable:
- 讀: mount 時嘗試 `JSON.parse(localStorage.getItem(key))`, 失敗就用 defaults.
- 寫: `watch` reactive map, deep 變動時 stringify 寫回.
- key 命名: `rescue-map.sidebar.groups`, 值為 `{ rescue: boolean; monitor: boolean }`, 預設 `{ rescue: true, monitor: true }`.

不另引入 Pinia / vuex; 只兩個布林狀態. 在 SSR / 無 `window` 環境 fallback 至 defaults (此專案是 SPA, 但仍寫安全 guard).

### D7: 編輯既有點時, 半徑欄位預填且自動驗證

`MonitorPointForm` 已支援 edit 模式 (`initialValue`); 此次只需在 `resetForm` 加入 `radius` 預填. 對話框內預覽地圖 (`formMap`) 也用同一個 `L.circle` (`formCircle`) 顯示半徑, 隨 marker 移動同步重設 latlng 與 radius. UX: 即使在 manual / search / gps 分頁切換, 半徑欄位都是獨立元素 (不在 tab 內), 始終可編輯.

## Risks / Trade-offs

- **R1: 既有 `data/monitor-points.json` 升級風險** → 採用 `record` primary constructor 為 `Radius` 設定預設值 1000, `System.Text.Json` 缺欄位時自動套用. 由於專案使用 `MonitorPoint` record 直接序列化, 必須確保新增 `Radius` 後預設值生效; 寫一個 unit-style 啟動測試 (或本機驗證) 確認 LoadFromDisk 對舊格式不爆.
- **R2: 半徑圈大半徑時可能佔滿畫面、難以點到其他 marker** → 圓圈僅在 focused 狀態繪製, 非永久; 預設透明度 fill 0.15, stroke 透明度 0.6, 不攔截滑鼠 (`interactive: false`).
- **R3: 大量自設追蹤點 (例如 50+ 個) 時清單滾動 + hover 觸發頻繁** → 已用「最後者贏」+ 單一 layer, 不會建多個 layer 物件; hover handler 不重建 marker, 只重設 `L.circle` 中心與半徑.
- **R4: localStorage 配額或被使用者清除** → `useCollapsibleGroups` 寫入時 `try/catch`, 失敗只 console.warn, 不影響功能; 讀取失敗則回 defaults.
- **R5: 後端先更新但前端尚未更新時, 前端可能 POST 缺 `radius`** → 後端 `MonitorPointCreateRequest.Radius` 設預設值 1000 (POCO property default) 並標記 `[Range]`; 即使前端漏送也接受並補 1000. 這是過渡期保護, 預期前後端會一起部署.
- **R6: trade-off — 側邊欄變兩組後, 災情數量大時操作高度有限** → 預設兩群組皆 expand 但各自有 `overflow-y: auto` 限高 (主清單可 50/50 分配高度), 確保滾動可達. 摺疊任一組時, 另一組 takes remaining height.

## Migration Plan

1. Backend 先合入並部署: 新增 `Radius` 欄位 (預設 1000, `[Range(50,50000)]`), 端點驗證, `MonitorPointStore` 容忍舊資料.
2. 重啟服務後 GET 既有清單會包含 `radius: 1000`; 後續任一 mutation 會把 1000 寫回 JSON.
3. Frontend 接著合入: type 加 `radius`, form 加欄位, 地圖加圓圈 layer, 側邊欄改群組.
4. 部署前透過實機操作驗證 (golden path: 新增/編輯/刪除自設點; hover/select 顯示圓圈; 摺疊群組重整後保留).
5. 回滾策略: 前端可獨立 revert, 後端可在 hot patch 中將 `Range` 放寬或將 `Radius` 設為 nullable 並再次部署; 既有 JSON 不會因新欄位被破壞 (`System.Text.Json` 在後端舊版會忽略多餘欄位).

## Open Questions

- 半徑圈是否需要顯示半徑數字標籤 (例如「1000 m」於圓圈中心)?  目前暫不加, 視 review 而定.
- 是否要在 marker `bindTooltip` 加上半徑資訊? 預設是, 沿用既有 `name / lat / lng`, 多加一行 `radius {n} m`.
- 將來若要做「事件落入半徑內告警」, 半徑單位、儲存形式是否已足夠? 評估後認為公尺 integer 已足夠.

## Why

目前救援地圖左側清單僅顯示官方救援事件, 使用者已能新增自設追蹤點但難以在地圖頁快速檢閱與選取; 同時自設追蹤點僅是一個 marker, 缺少表達「關心範圍」的能力, 使用者無法直觀判斷某個官方事件是否落在自己關注區域內. 透過 (1) 在側邊欄加入可摺疊的「自設追蹤」群組、(2) 為自設點加入半徑欄位、(3) 互動時於地圖上以半透明圓圈呈現該半徑, 可同時改善資訊組織與空間感知, 不需後續再開新功能.

## What Changes

- 自設追蹤點資料模型 (backend + frontend type) 新增 `radius` 欄位 (公尺, 整數, 50 ~ 50000, 預設 1000); 既有資料載入時若缺欄位 SHALL 補預設值, 不破壞既有 `data/monitor-points.json` 檔案.
- `POST /api/monitor-points` 與 `PUT /api/monitor-points/{id}` 接受並驗證 `radius`; `GET` 回應加入該欄位.
- `MonitorPointForm.vue` 在三個輸入分頁外, 新增「半徑」數值欄位 (公尺), 並在預覽地圖上同步顯示半徑圓圈; 拖曳/編輯時即時更新.
- 救援地圖頁側邊欄改為兩個可摺疊群組: 「災情狀況」(原事件清單) 與「自設追蹤」(自設追蹤點清單); 兩群組各自獨立 expand/collapse, 預設皆展開, 狀態 SHALL 儲存於 `localStorage`, 重整頁面後保留.
- 自設追蹤點清單項目顯示名稱、座標、半徑; 點擊或滑鼠 hover 該項目時, 主地圖 SHALL 對該自設點顯示半透明 (約 0.15) 半徑圓圈, 並 panTo 至該點; 取消選取或離開 hover 後圓圈 SHALL 消失.
- 在主地圖 marker 上 hover 也 SHALL 顯示對應半徑圓圈, 行為與清單 hover 一致 (任一來源觸發即可).

## Capabilities

### New Capabilities
<!-- 無新增 capability; 既有的 monitor-points-* 與 rescue-map-frontend 已涵蓋. -->

### Modified Capabilities
- `monitor-points-backend`: 監測點資料模型加上 `radius`; 建立 / 更新端點接受並驗證 `radius`; JSON 持久化新增該欄位且向下相容缺漏欄位.
- `monitor-points-frontend`: `MonitorPoint` interface 與 `useMonitorPoints` 處理 `radius`; 新增/編輯對話框加入半徑欄位並於預覽地圖同步圓圈.
- `rescue-map-frontend`: 側邊欄重構成「災情狀況」+「自設追蹤」兩個可摺疊群組 (狀態存 `localStorage`); 主地圖在自設點被選取或 hover 時顯示半透明半徑圓圈.

## Impact

- Backend (`src/WebApi`):
  - `Models/MonitorPoint.cs`, `Models/MonitorPointCreateRequest.cs` 加入 `Radius`.
  - `Services/MonitorPointStore.cs`: `AddAsync` / `UpdateAsync` 簽章加入 `radius`; JSON 反序列化容忍缺欄位 (套用預設值並回寫).
  - `Controllers/MonitorPointsController.cs`: 透傳 `radius`.
  - `data/monitor-points.json` schema 升級, 既有資料於下次寫回時自動補欄位.
- Frontend (`vue-app/src`):
  - `types/monitorPoint.ts`, `api/monitorPoints.ts`, `composables/useMonitorPoints.ts`: 加入 `radius` 流轉.
  - `components/MonitorPointForm.vue`: 新增半徑欄位 + 預覽圓圈.
  - `components/MonitorPointList.vue`: 顯示半徑.
  - `views/RescueMapView.vue`: 側邊欄結構改造、群組摺疊狀態 (`localStorage` key `rescue-map.sidebar.groups`)、選取/hover 狀態傳給地圖.
  - `components/RescueMap.vue`: 新增可控制的「focused monitor point」props, 對該點以 `L.circle` (半徑單位公尺, 0.15 透明度) 繪製覆蓋層.
- 不影響: rescue-data-polling、geocode 端點、Vue router 設定.
- 相容性: 既有 `monitor-points.json` 可直接載入; 缺 `radius` 視為 1000 並於下次寫入時補上; 前端對舊 API 回應 (無 `radius`) 亦套用相同預設, 但這只是過渡期 fallback, 後端會先於前端部署.

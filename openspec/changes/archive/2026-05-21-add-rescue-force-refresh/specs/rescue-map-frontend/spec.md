## ADDED Requirements

### Requirement: 強制更新按鈕

前端 SHALL 在救援地圖頁面 (`/`) 提供一個獨立於既有「重新整理」按鈕的「強制更新」按鈕, 視覺上與既有按鈕並排顯示且可明確區別 (例如不同 icon, 不同文案, 或附加標記). 點擊該按鈕 SHALL 透過 `POST /api/rescue/refresh` 同步觸發後端向上游重抓; 在等待回應期間, 該按鈕 SHALL 進入 disabled + loading 狀態, 直到請求 resolve (成功、失敗、或 429) 才恢復可點擊. 既有「重新整理」按鈕 (重新讀取 backend cache 的行為) SHALL 保留不變.

#### Scenario: 點擊強制更新按鈕

- **WHEN** 使用者於救援地圖頁面點擊「強制更新」按鈕
- **THEN** 系統 SHALL 立即發出 `POST /api/rescue/refresh` 請求, 按鈕 SHALL 進入 disabled + loading 視覺狀態, 既有「重新整理」按鈕的 disabled 狀態 SHALL NOT 被影響

#### Scenario: 強制更新成功後資料更新

- **WHEN** `POST /api/rescue/refresh` 回應 200
- **THEN** 系統 SHALL 將回應中 `data` 與 `meta` 套用至既有 `useRescueData` 狀態 (與既有 `GET /api/rescue/latest` 成功時相同的 reducer 路徑), 地圖標記與側邊欄 SHALL 立即重新渲染為新資料, status bar 的「資料時間」SHALL 更新為新的 `meta.fetchedAt`, 下一次自動輪詢的計時起點 SHALL 重設

#### Scenario: 強制更新按鈕與既有自動輪詢互動

- **WHEN** 強制更新成功完成
- **THEN** 既有自動輪詢的計時起點 SHALL 從強制更新完成時刻重新計算 (即下一次自動輪詢應發生於強制更新完成後一個完整輪詢間隔之後), 避免緊接著自動再打一次

#### Scenario: 強制更新請求進行中不可重複觸發

- **WHEN** 上一次「強制更新」請求尚未 resolve, 使用者再次點擊按鈕
- **THEN** 系統 SHALL 忽略此次點擊 (按鈕 disabled 即可達成), SHALL NOT 發出第二次 `POST /api/rescue/refresh`

### Requirement: 強制更新冷卻提示

當後端因 cooldown 拒絕強制更新時 (HTTP 429), 前端 SHALL 在 status bar 顯示「上游冷卻中, 剩餘 NN 秒」之類的明確訊息 (NN 來自回應 `Retry-After` header 或 body `retryAfterSeconds`), 且 SHALL NOT 將此情境顯示為一般錯誤. 在冷卻倒數期間, 「強制更新」按鈕 SHALL 保持 disabled, 倒數歸零後 SHALL 自動恢復可點擊; 倒數訊息 SHALL 隨之消失. 既有自動輪詢與「重新整理」按鈕的行為 SHALL NOT 受冷卻影響.

#### Scenario: 後端因冷卻回傳 429

- **WHEN** `POST /api/rescue/refresh` 回應 HTTP 429
- **THEN** 前端 SHALL 從 `Retry-After` header 或 JSON body `retryAfterSeconds` 取得秒數, 於 status bar 顯示「上游冷卻中, 剩餘 NN 秒」之類訊息, SHALL NOT 在 `error` 狀態顯示為一般錯誤, 並 SHALL 啟動每秒更新的倒數

#### Scenario: 冷卻期間按鈕鎖定

- **WHEN** 冷卻倒數尚未歸零
- **THEN** 「強制更新」按鈕 SHALL 維持 disabled 狀態, 點擊 SHALL 無效; 既有「重新整理」按鈕 SHALL 不受影響, 自動輪詢 SHALL 繼續排程

#### Scenario: 冷卻倒數結束

- **WHEN** 冷卻倒數歸零
- **THEN** 「強制更新」按鈕 SHALL 恢復可點擊狀態, status bar 的冷卻訊息 SHALL 消失

#### Scenario: 冷卻期間其他資料更新不誤清訊息

- **WHEN** 冷卻倒數進行中, 自動輪詢或既有「重新整理」按鈕成功取得新資料
- **THEN** 冷卻訊息 SHALL 維持顯示直到倒數歸零, SHALL NOT 因其他 fetch 成功而被清除; 「強制更新」按鈕仍 SHALL 維持 disabled

### Requirement: 強制更新失敗處理

當後端強制更新因上游錯誤 (HTTP 502) 或網路/JSON 解析失敗回傳非 429 的失敗時, 前端 SHALL 在 status bar 顯示錯誤訊息與發生時間 (與既有「網路或其他錯誤」情境一致), 按鈕 SHALL 立即恢復可點擊 (不啟動冷卻倒數), 既有的自動輪詢排程 SHALL NOT 因此中斷.

#### Scenario: 後端回傳 502 (上游錯誤)

- **WHEN** `POST /api/rescue/refresh` 回應 HTTP 502
- **THEN** 前端 SHALL 從 body 取得 `error` 訊息, 於 status bar 以與一般錯誤相同的視覺風格顯示「強制更新失敗: {error}」與時間; 「強制更新」按鈕 SHALL 立即恢復可點擊; 既有地圖標記與資料 SHALL NOT 因此被清空

#### Scenario: 網路或 JSON 解析失敗

- **WHEN** `POST /api/rescue/refresh` 在送出或解析回應時失敗 (網路斷線, fetch reject, JSON parse error)
- **THEN** 前端 SHALL 比照 HTTP 502 的處理方式顯示錯誤訊息, 按鈕 SHALL 立即恢復可點擊, 既有資料 SHALL NOT 被清空

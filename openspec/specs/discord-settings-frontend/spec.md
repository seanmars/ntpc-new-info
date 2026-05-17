# discord-settings-frontend Specification

## Purpose
TBD - created by archiving change add-discord-bot-notifications. Update Purpose after archive.
## Requirements
### Requirement: 設定頁新增 Discord 通知區塊
設定頁 (`/settings`, `SettingsView.vue`) SHALL 在既有「監測點」區塊之下新增一個獨立的「Discord 通知」區塊, 區塊 SHALL 顯示啟用狀態、目前 channel id, 以及目前 bot token 的遮罩預覽 (例如 `...ab12`), 並 SHALL 提供進入編輯模式的入口 (按鈕或可直接編輯的表單).

#### Scenario: 初次進入設定頁顯示停用狀態
- **WHEN** 後端 `GET /api/settings/discord` 回傳 `{ enabled: false, hasToken: false, tokenPreview: null, channelId: 0 }` 且使用者進入設定頁
- **THEN** 「Discord 通知」區塊 SHALL 顯示「已停用」狀態, channel id 顯示為 `(未設定)`, token 顯示為 `(未設定)`, SHALL 提供「設定」或同等按鈕讓使用者開始編輯, SHALL NOT 顯示啟用切換為可勾選

#### Scenario: 已設定但停用
- **WHEN** 後端回傳 `{ enabled: false, hasToken: true, tokenPreview: "...ab12", channelId: 123 }`
- **THEN** 區塊 SHALL 顯示「已停用」狀態, channel id 顯示為 `123`, token 顯示為 `...ab12`, SHALL 提供「啟用」與「編輯」入口

#### Scenario: 已啟用
- **WHEN** 後端回傳 `enabled: true, hasToken: true, channelId > 0`
- **THEN** 區塊 SHALL 以可辨識的視覺差異 (例如綠色徽章) 顯示「已啟用」狀態, 並 SHALL 顯示同樣的 channel id 與 token 預覽

#### Scenario: 載入失敗
- **WHEN** `GET /api/settings/discord` 回應非 2xx 或網路錯誤
- **THEN** 區塊 SHALL 顯示錯誤訊息與「重試」按鈕, SHALL NOT 阻擋頁面其他區塊 (監測點) 的渲染

### Requirement: Discord 設定表單 (DiscordSettingsForm)
編輯入口 SHALL 開啟一個 `DiscordSettingsForm` 元件 (對話框或內嵌表單皆可), 表單 SHALL 包含三個欄位: `enabled` (勾選框), `botToken` (密碼類型輸入框, placeholder 顯示既有 token 的遮罩預覽), `channelId` (數字輸入框); 提交時 SHALL 呼叫 `PUT /api/settings/discord`.

#### Scenario: 開啟編輯預填值
- **WHEN** 使用者點擊「編輯」按鈕
- **THEN** 表單 SHALL 開啟, `enabled` 勾選框 SHALL 反映目前 `enabled` 值, `channelId` 欄位 SHALL 預填目前 `channelId` (若為 0 則欄位為空), `botToken` 欄位 SHALL 留空 (NOT 預填), placeholder SHALL 顯示 `tokenPreview` (例如 `輸入新值以替換 (目前: ...ab12)`) 或在無 token 時顯示 `輸入 bot token`

#### Scenario: 不輸入 token 仍可儲存其他欄位
- **WHEN** 使用者僅修改 `channelId` 或 `enabled`, 將 `botToken` 欄位留空, 點擊儲存
- **THEN** 前端 SHALL 送出 `PUT` 且 body 中 `botToken` 欄位 SHALL 被省略 (或設為 null), 後端 SHALL 保留既有 token, 表單 SHALL 在成功後關閉, 區塊 SHALL 反映新的 enabled / channelId

#### Scenario: 輸入新 token 取代既有
- **WHEN** 使用者於 `botToken` 欄位輸入任何非空字串並儲存
- **THEN** 前端 SHALL 在 `PUT` body 的 `botToken` 欄位送出該字串, 後端 SHALL 以新值取代, 表單成功後關閉, 區塊 SHALL 顯示新的 `tokenPreview` (新 token 的末 4 字)

#### Scenario: 啟用但缺欄位
- **WHEN** 使用者勾選 `enabled` 但 `channelId` 為 0/空, 或 token 從未設定且本次也未輸入
- **THEN** 「儲存」按鈕 SHALL 為 disabled, 表單 SHALL 即時顯示欄位錯誤 (例如「啟用前需先設定 token 與 channel id」), SHALL NOT 呼叫 API

#### Scenario: channelId 必須為正整數
- **WHEN** 使用者於 `channelId` 欄位輸入 0、負數、小數或非數值
- **THEN** 表單 SHALL 即時顯示欄位錯誤, 「儲存」按鈕 SHALL 為 disabled, SHALL NOT 呼叫 API

#### Scenario: API 拒絕儲存
- **WHEN** `PUT /api/settings/discord` 回傳 HTTP 400 (例如後端驗證失敗) 或其他非 2xx
- **THEN** 表單 SHALL 保持開啟, 顯示錯誤訊息, 使用者輸入 (包含已輸入的新 token) SHALL 保留, 區塊 SHALL NOT 變更

#### Scenario: 取消編輯不送出
- **WHEN** 使用者點擊「取消」或關閉表單
- **THEN** 表單 SHALL 關閉, SHALL NOT 呼叫 API, 區塊 SHALL 保持先前狀態, 已輸入 (未送出) 的 token 字串 SHALL 不被保留於記憶體之外的任何地方

### Requirement: Token 永遠不顯示於 UI 與 console
前端 SHALL NEVER 將完整 bot token 渲染至 DOM, 寫入 `console`, 寫入 `localStorage` / `sessionStorage`, 或包含於分析事件; 任何顯示 token 的地方 SHALL 僅顯示後端提供的 `tokenPreview` (`...xxxx`).

#### Scenario: GET 回應不含 raw token
- **WHEN** 前端接收 `GET /api/settings/discord` 回應
- **THEN** 前端 SHALL 只讀取 `enabled`, `hasToken`, `tokenPreview`, `channelId` 四個欄位; 若回應意外包含其他欄位 (例如 `botToken`), 前端 SHALL 忽略, SHALL NOT 顯示於 UI 也 SHALL NOT 寫入任何持久化儲存

#### Scenario: 使用者輸入的新 token 僅存在於表單區域性 ref
- **WHEN** 使用者於 `botToken` 欄位輸入新值
- **THEN** 該字串 SHALL 僅存在於表單元件的 reactive ref/state, SHALL NOT 被寫入 `localStorage` / `sessionStorage` / Pinia 持久化, 且 SHALL 於表單關閉時被釋放 (例如表單元件 unmount)

#### Scenario: 任何錯誤訊息不回放 token
- **WHEN** 表單因驗證失敗或 API 錯誤顯示訊息
- **THEN** 訊息文字 SHALL NEVER 包含使用者輸入的 token (例如不寫「您輸入的 token <值> 不合法」), 任何 console.log / console.error SHALL 同樣 NOT 包含 token 字串

### Requirement: 刪除 Discord 設定
設定頁的「Discord 通知」區塊 SHALL 在已儲存任何設定 (`hasToken == true` OR `channelId > 0`) 時提供「刪除設定」按鈕, 點擊後 SHALL 先以 `window.confirm` (或同等模式對話框) 進行二次確認, 確認後 SHALL 呼叫 `DELETE /api/settings/discord`, 成功後 SHALL 將區塊重設為預設停用狀態並顯示成功提示.

#### Scenario: 無已儲存設定時不顯示刪除按鈕
- **WHEN** `discordSettings.hasToken === false` 且 `discordSettings.channelId === 0`
- **THEN** 設定區塊 SHALL NOT 顯示「刪除設定」按鈕 (沒有東西可刪)

#### Scenario: 確認後刪除成功
- **WHEN** 使用者點擊「刪除設定」按鈕並於確認對話框點擊「確認」, 且 `DELETE /api/settings/discord` 回應 HTTP 204
- **THEN** composable 內部 `settings` SHALL 被重設為預設值 (`enabled: false, hasToken: false, tokenPreview: null, channelId: 0`), 區塊 SHALL 即時反映 (狀態徽章顯示「已停用」, token 與 channel id 皆顯示「(未設定)」), 並 SHALL 透過 toast 顯示成功提示

#### Scenario: 使用者取消刪除
- **WHEN** 使用者於確認對話框點擊「取消」
- **THEN** SHALL NOT 呼叫 API, 區塊 SHALL 維持原狀, SHALL NOT 顯示任何提示

#### Scenario: 刪除失敗
- **WHEN** `DELETE` 回應非 2xx 或網路錯誤
- **THEN** 區塊 SHALL 維持原狀 (settings 不被重設), SHALL 透過 toast 顯示錯誤訊息

### Requirement: 設定 composable 與型別
前端 SHALL 提供 `useDiscordSettings` composable 管理 reactive 狀態 (`settings`, `error`, `isLoading`) 與 `refresh`, `update`, `remove` 方法; 並 SHALL 於 `src/types/discordSettings.ts` 定義 `DiscordSettingsView` interface (`enabled: boolean`, `hasToken: boolean`, `tokenPreview: string | null`, `channelId: number`) 與 `DiscordSettingsUpdateInput` interface (`enabled: boolean`, `botToken?: string | null`, `channelId: number`).

#### Scenario: refresh 取得目前設定
- **WHEN** composable 的 `refresh` 被呼叫
- **THEN** composable SHALL 呼叫 `GET /api/settings/discord`, 將回傳值塞入 `settings`, 並更新 `isLoading` / `error`; 失敗時 SHALL 將 error 暴露給呼叫端且 SHALL 保留前次成功的 `settings`

#### Scenario: update 提交變更
- **WHEN** composable 的 `update(input)` 被呼叫
- **THEN** composable SHALL 呼叫 `PUT /api/settings/discord` 並以 `input` 為 body, 回傳 `{ status: 'ok', settings }` 或 `{ status: 'error', error }`; 成功時 SHALL 更新內部 `settings` 為回應內容

#### Scenario: update 處理 botToken 三態
- **WHEN** 呼叫 `update({ enabled, botToken: undefined | null, channelId })`
- **THEN** composable SHALL 在 `PUT` body 中省略 `botToken` 欄位 (或送出 null) 以表達「保留既有」; 呼叫 `update({ enabled, botToken: '', channelId })` SHALL 送出空字串以表達「清除」; 呼叫 `update({ enabled, botToken: 'X', channelId })` SHALL 送出 `'X'` 以表達「替換」

#### Scenario: remove 重設為預設狀態
- **WHEN** composable 的 `remove()` 被呼叫且 `DELETE /api/settings/discord` 回傳 HTTP 204
- **THEN** composable SHALL 將內部 `settings` 設為預設值 (`enabled: false, hasToken: false, tokenPreview: null, channelId: 0`) 並回傳 `{ status: 'ok' }`; 失敗時 SHALL 保留前次 `settings` 並回傳 `{ status: 'error', error }`

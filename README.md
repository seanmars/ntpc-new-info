# ntpc-new-info

新北市政府開放資料的即時資訊整合平台. 目前以「救援事件動態圖層」為第一個範例, 後端週期性抓取 NTPC 開放 API, 前端以 Leaflet 地圖視覺化呈現.

## Technology Stack

- **Orchestrator**: .NET Aspire 13.1.1 (AppHost)
- **Backend**: ASP.NET Core (.NET 10) + Scalar OpenAPI
- **Frontend**: Vue 3 + TypeScript + Vite 8 + Leaflet + Pinia + Vue Router
- **Observability**: OpenTelemetry (透過 ServiceDefaults)

## Project Structure

```
ntpc-new-info/
├── AppHost/              # Aspire orchestrator, 統一啟動 WebApi 與 vue-app
├── ServiceDefaults/      # 共用的 telemetry / health check / service discovery
├── src/
│   └── WebApi/           # REST API, 內含 BackgroundService 輪詢上游
├── vue-app/              # Vue 3 + Leaflet 地圖前端
└── openspec/             # 規格文件 (specs)
```

## Requirements

- .NET 10 SDK
- Node.js `^20.19.0 || >=22.12.0`
- pnpm (vue-app 使用 pnpm 為套件管理工具)

## Quick Start

### 1. 安裝前端相依套件

```powershell
cd vue-app
pnpm install
cd ..
```

### 2. 透過 Aspire AppHost 啟動全部服務

```powershell
dotnet run --project AppHost
```

AppHost 會自動:
- 啟動 `WebApi` (預設 http://localhost:5129)
- 啟動 `vue-app` (Vite dev server)
- 開啟 Aspire Dashboard, 可查看 logs / traces / metrics

### 3. 個別啟動 (替代方式)

```powershell
# Terminal 1: 後端
dotnet run --project src/WebApi

# Terminal 2: 前端
cd vue-app
pnpm dev
```

## 後端 API

### `GET /api/rescue/latest`

回傳最近一次成功從上游抓取的救援事件 GeoJSON 快照.

- **200 OK** - 已有快照:

  ```json
  {
    "data": { "features": [...] },
    "meta": {
      "fetchedAt": "2026-05-17T12:34:56Z",
      "lastError": null,
      "lastErrorAt": null
    }
  }
  ```

- **503 Service Unavailable** - 首次輪詢尚未成功完成

### 設定 (`appsettings.json`)

```json
{
  "RescuePolling": {
    "UpstreamUrl": "https://e.ntpc.gov.tw/v3/api/map/dynamic/layer/rescue",
    "Interval": "00:05:00",
    "RequestTimeout": "00:00:30"
  }
}
```

設定可在執行期間透過 configuration reload 更新, 下次排程即套用新值.

### OpenAPI 文件

開發模式下提供 Scalar UI: `http://localhost:5129/scalar/v1`

### Discord 通知 (選用)

當監測點被救援事件命中時, 可透過 Discord bot 發送訊息到指定 channel.

啟用步驟:

1. 於 [Discord Developer Portal](https://discord.com/developers/applications) 建立 application, 在「Bot」分頁建立 bot 並複製 token.
2. 將 bot 邀請到目標 Discord server, 確認 bot 在目標 channel 具有 `Send Messages` 權限.
3. 啟動 WebApi, 於前端 `/settings` 頁開啟「Discord 通知」區塊, 填入 bot token + channel id 並勾選啟用.

設定會持久化至 `src/WebApi/data/discord-settings.json` (已被 `.gitignore` 排除).
Token 預設不會回傳於 `GET /api/settings/discord`, 僅顯示末 4 碼遮罩預覽; 後續更新時若 token 欄位留空則保留既有值.

## 前端

預設於 `/` 路由顯示全螢幕 Leaflet 地圖, 中心為新北市政府 (約 `25.0124, 121.4651`, zoom 11).

- 預設每 60 秒自動輪詢 `/api/rescue/latest`, 可透過 `VITE_RESCUE_POLL_INTERVAL_MS` 環境變數覆寫
- 事件清單側邊欄 (寬度 > 720px 顯示於左側, 否則改為上方)
- Vite dev server 將 `/api` 代理至 `http://localhost:5129`, 避免 CORS

### 常用指令

```powershell
cd vue-app
pnpm dev          # 開發模式
pnpm build        # 建置 production bundle
pnpm type-check   # vue-tsc 型別檢查
pnpm lint         # oxlint + eslint
pnpm format       # prettier
```

## Container

Copy and rename the `.env.example` to `.env`

```powershell
# Just build the image and run with compose
./build.ps1
docker compose up -d
# or
podman compose up -d
```


## Specs

詳細功能需求與場景描述位於 `openspec/specs/`:

- `rescue-data-polling` - 後端輪詢與快取行為
- `rescue-map-frontend` - 前端地圖與互動行為

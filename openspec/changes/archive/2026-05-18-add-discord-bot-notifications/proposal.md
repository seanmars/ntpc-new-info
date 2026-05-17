## Why

The system already detects when rescue events fall inside a monitor point's radius and publishes `MonitorPointEventDetected` to an in-process bus, but the only built-in handler writes a log line — no human is notified. We want Discord push notifications, AND we want the bot's token + target channel to be editable at runtime from the existing frontend settings page (the same UX surface where users already manage monitor points), so that operators can wire up or rotate the bot without redeploying the backend or shipping secrets into `appsettings.json`.

## What Changes

- Add a persistent `IDiscordSettingsStore` (file-backed, mirroring the existing `MonitorPointStore` pattern) that holds `Enabled`, `BotToken`, `ChannelId`, with a single record persisted as JSON.
- Add REST endpoints under `/api/settings/discord`:
  - `GET` returns `{ enabled, hasToken, tokenPreview, channelId }` — the **raw token is never returned**, only `hasToken` + last 4 chars as `tokenPreview` (e.g. `"...ab12"`).
  - `PUT` accepts `{ enabled, botToken, channelId }`; if `botToken` is omitted/null, the stored token is preserved; if `botToken` is an empty string, the stored token is cleared.
- Add an `IDiscordNotifier` abstraction (singleton) with a Discord.Net REST-based implementation, plus a `NoopDiscordNotifier` used while disabled.
- Add a `DiscordBotLifecycleService` (`BackgroundService`) that, on startup AND on every settings change, reconciles the live `DiscordRestClient` against current settings — log in, log out, or stay disconnected as appropriate.
- Add an `IHandleMessages<MonitorPointEventDetected>` handler that formats hits as Discord messages and sends them via `IDiscordNotifier`.
- Add a Discord settings section to the existing `SettingsView.vue` page (same panel as monitor points), with a Vue composable `useDiscordSettings` driving the REST endpoints.
- The bot token input on the UI shows the masked preview when a token already exists; submitting an empty input keeps the existing token; submitting any non-empty value replaces it.

## Capabilities

### New Capabilities
- `discord-notifications`: Backend capability covering the settings store, REST API contract (with token-masking rule), `IDiscordNotifier` abstraction, hot-reloading bot lifecycle, and the `MonitorPointEventDetected` Discord handler.
- `discord-settings-frontend`: Frontend capability covering the Discord settings section added to the existing settings page — form fields, masked-token UX, save/error feedback, and the API contract it consumes.

### Modified Capabilities
<!-- No requirement changes to existing specs.
     `monitor-point-event-alerts` already states the bus auto-registers IHandleMessages<MonitorPointEventDetected> from the WebApi assembly,
     so adding another handler is in-spec.
     `monitor-points-frontend` is untouched — the new section is additive to SettingsView.vue at the page level. -->

## Impact

- **Backend**: `src/WebApi/` gains a `Discord/` folder (settings store, options, notifier, no-op, lifecycle service, bot state, handler), a new controller, and a small DI block in `Program.cs`. Adds `Discord.Net` NuGet package. New persistence file `data/discord-settings.json` (alongside existing `data/monitor-points.json`).
- **Frontend**: `vue-app/src/views/SettingsView.vue` gains a new section. New files: `vue-app/src/composables/useDiscordSettings.ts`, `vue-app/src/components/DiscordSettingsForm.vue`, `vue-app/src/types/discordSettings.ts`.
- **Secrets**: The token never appears in the repo, never appears in GET responses, and is logged only as the same masked preview. The on-disk file (`data/discord-settings.json`) contains it in plaintext — treated like any other state file (must be on a trusted volume; should be in `.gitignore` alongside the monitor-points file).
- **Runtime**: One outbound WebSocket/REST connection to Discord while enabled. Settings changes trigger a re-login (debounced) without restarting the host. Polling loop and bus are unaffected by Discord state.
- **Ops**: Day-zero deploy is safe — settings default to disabled, the feature is dark until someone enables it via the UI.

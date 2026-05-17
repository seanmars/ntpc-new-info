## 1. Project setup

- [x] 1.1 Add `Discord.Net` NuGet package reference to `src/WebApi/WebApi.csproj` (latest stable compatible with .NET 10)
- [x] 1.2 Create folder `src/WebApi/Discord/` to hold all Discord-related backend types
- [x] 1.3 Confirm `.gitignore` already excludes `src/WebApi/data/*.json` (it does — covered by line 485); no change needed
- [x] 1.4 `dotnet build` succeeds with the new package

## 2. Settings store

- [x] 2.1 Create `src/WebApi/Options/DiscordSettingsStoreOptions.cs` with `SectionName = "DiscordSettingsStore"`, property `FilePath` defaulting to `"data/discord-settings.json"`
- [x] 2.2 Create `src/WebApi/Discord/DiscordSettings.cs` (record holding `Enabled bool`, `BotToken string`, `ChannelId ulong`) and `DiscordSettingsSnapshot` (immutable view for event consumers)
- [x] 2.3 Create `src/WebApi/Discord/IDiscordSettingsStore.cs` exposing `Task<DiscordSettingsSnapshot> GetAsync(CancellationToken ct)`, `Task<DiscordSettingsSnapshot> UpdateAsync(DiscordSettings settings, CancellationToken ct)`, and `event EventHandler<DiscordSettingsSnapshot>? SettingsChanged`
- [x] 2.4 Create `src/WebApi/Discord/DiscordSettingsStore.cs` modeled on `MonitorPointStore`: `SemaphoreSlim _gate`, sync load in constructor, async update with atomic temp-file write + move-overwrite, corrupt-file quarantine via `<file>.corrupt-<timestamp>` rename + Error log; raise `SettingsChanged` only when the new snapshot differs from the previous
- [x] 2.5 Register `IDiscordSettingsStore` as singleton in `Program.cs`; bind `DiscordSettingsStoreOptions` from configuration section

## 3. Bot state and notifier

- [x] 3.1 Create `src/WebApi/Discord/DiscordBotState.cs` — singleton with `volatile bool IsReady`, `DiscordRestClient? Client`, `string? CurrentLoggedInToken`, `SemaphoreSlim ReconfigGate = new(1,1)`
- [x] 3.2 Create `src/WebApi/Discord/IDiscordNotifier.cs` with `Task SendAsync(string content, ulong? channelId = null, CancellationToken cancellationToken = default)`
- [x] 3.3 Create `src/WebApi/Discord/DiscordRestNotifier.cs` — depends on `DiscordBotState`, `IDiscordSettingsStore`, `ILogger<>`; reads current snapshot to resolve effective channel id; if `!state.IsReady` log Warning ("dropped Discord message to channel {ChannelId}", with NO token) and return; otherwise `state.Client.GetChannelAsync(channelId)` then `SendMessageAsync(content)`
- [x] 3.4 Create `src/WebApi/Discord/NoopDiscordNotifier.cs` returning `Task.CompletedTask`
- [x] 3.5 In `Program.cs`, register `DiscordBotState` and `IDiscordNotifier` (always `DiscordRestNotifier` — the notifier itself handles the disabled case via state.IsReady and the store snapshot; the no-op is only used by tests / explicit override scenarios. Reconsider during implementation if a no-op is actually clearer for the always-disabled path)

## 4. Bot lifecycle (hot reload)

- [x] 4.1 Create `src/WebApi/Discord/DiscordBotLifecycleService.cs` as `BackgroundService`, depending on `IDiscordSettingsStore`, `DiscordBotState`, `ILogger<>`
- [x] 4.2 In `ExecuteAsync`: read initial snapshot, call internal `ReconcileAsync(snapshot, ct)`, then subscribe to `store.SettingsChanged` and await an internal `TaskCompletionSource` that is signaled on host shutdown so the service stays alive
- [x] 4.3 Implement `ReconcileAsync(snapshot, ct)`: acquire `state.ReconfigGate`; switch on target:
  - `Enabled=false` → if `state.Client != null` dispose+logout, set `IsReady=false`, clear `CurrentLoggedInToken`
  - `Enabled=true, token unchanged, channel changed` → no-op (notifier picks up new channel from store)
  - `Enabled=true, token changed or no client` → dispose existing if any, create new `DiscordRestClient`, `LoginAsync(TokenType.Bot, token)`, set `IsReady=true`, log Info with `client.CurrentUser.Username` (never token); on exception log Error with token masked, leave `IsReady=false`
- [x] 4.4 In `SettingsChanged` handler, call `ReconcileAsync(snapshot, CancellationToken.None)` inside a try/catch that logs at Error so a malformed snapshot never crashes the host
- [x] 4.5 In `StopAsync`, unsubscribe from `SettingsChanged`, dispose `state.Client` if present, swallow exceptions during shutdown
- [x] 4.6 Register `DiscordBotLifecycleService` as `IHostedService` in `Program.cs`

## 5. REST endpoint

- [x] 5.1 Create `src/WebApi/Controllers/DiscordSettingsController.cs` at route `/api/settings/discord`
- [x] 5.2 Define DTOs: `DiscordSettingsViewDto { bool Enabled, bool HasToken, string? TokenPreview, ulong ChannelId }` and `DiscordSettingsUpdateRequest { bool Enabled, string? BotToken, ulong ChannelId }` (`BotToken` nullable to support the three-state semantics)
- [x] 5.3 Implement `GET`: read snapshot, build `TokenPreview` as `null` if token empty else `"..." + token[^4..]` (mind tokens shorter than 4 chars; handle gracefully)
- [x] 5.4 Implement `PUT`: resolve effective `BotToken` (omitted/null → keep existing; "" → clear; non-empty → replace); validate effective state (`Enabled=true` requires non-empty token + channelId > 0); on validation failure return HTTP 400 with `{ field, message }` body and DO NOT call the store; on success call `store.UpdateAsync` and return the same shape as `GET`
- [x] 5.5 Ensure the controller NEVER returns the raw token in any field, NEVER logs the raw token in any path (validation failures included)

## 6. `MonitorPointEventDetected` Discord handler

- [x] 6.1 Create `src/WebApi/Discord/DiscordMonitorPointAlertHandler.cs` implementing `IHandleMessages<MonitorPointEventDetected>`, depending on `IDiscordNotifier` and `ILogger<>`
- [x] 6.2 Format message: title line with monitor point name, second line with `Matched: {count} @ {DetectedAt:o}`, then up to 5 lines `- {FeatureId ?? lat,lng} ({distance}m)`, plus `...and N more` line when truncated
- [x] 6.3 Wrap `SendAsync` in try/catch; on exception log at Warning with `{MonitorPointId, ChannelId}` fields (never token), return `Task.CompletedTask`
- [x] 6.4 No DI change needed — `AutoRegisterHandlersFromAssemblyOf<LoggingMonitorPointAlertHandler>` (already in Program.cs:53) scans the WebApi assembly

## 7. Frontend types and composable

- [x] 7.1 Create `vue-app/src/types/discordSettings.ts` exporting:
  - `interface DiscordSettingsView { enabled: boolean; hasToken: boolean; tokenPreview: string | null; channelId: number }`
  - `interface DiscordSettingsUpdateInput { enabled: boolean; botToken?: string | null; channelId: number }`
- [x] 7.2 Create `vue-app/src/composables/useDiscordSettings.ts` with reactive `settings`, `error`, `isLoading`, plus `refresh()` (GET) and `update(input)` (PUT) methods, modeled on `useMonitorPoints`
- [x] 7.3 In `update`, build the PUT body so that if `input.botToken === undefined` the field is omitted from the JSON body entirely; if `null` send `null`; if string send the string. Implemented via explicit conditional set on `body.botToken` for documentation; same wire result as relying on `JSON.stringify` omitting undefined.

## 8. Frontend settings section

- [x] 8.1 Create `vue-app/src/components/DiscordSettingsForm.vue` — modal or inline form with fields: `enabled` (checkbox), `botToken` (input type=password, placeholder shows current `tokenPreview` like `輸入新值以替換 (目前: ...ab12)`), `channelId` (input type=number, min=1)
- [x] 8.2 Form local state: keep the newly typed token only in a local `ref<string>` that lives inside the component; on submit, send `undefined` (omit) when the input is empty, otherwise send the string
- [x] 8.3 Client-side validation: disable submit when `enabled=true` AND (`!hasToken && !newTokenInput.length` OR `channelId < 1`); show field error messages in Traditional Chinese matching the existing form style
- [x] 8.4 Update `vue-app/src/views/SettingsView.vue`: add a new `<section class="settings-section">` block after the existing monitor-points section, wire `useDiscordSettings` for state, show enabled badge + masked token + channelId, show "編輯"/"設定" CTA that opens `DiscordSettingsForm`, reuse the existing toast pattern for success/error feedback
- [x] 8.5 No persistent UI storage: confirm in code that the newly-typed token never lands in `localStorage`, `sessionStorage`, Pinia, or any console.log (grep verification in verification section)

## 9. Verification

- [x] 9.1 `dotnet build` succeeds with no new warnings
- [x] 9.2 `pnpm type-check` and `pnpm lint` pass in `vue-app/`
- [ ] 9.3 (manual) Start the app via `dotnet run --project AppHost` with no `data/discord-settings.json` present; confirm: app starts, settings page loads, Discord section shows "已停用", no Discord login attempted
- [ ] 9.4 (manual) In the UI, set a valid bot token + a channel id the bot can post to, tick Enabled, click Save; confirm: PUT returns 200, settings section refreshes to show "已啟用" + masked token, server logs show one Info entry with the bot username (NOT the token), `data/discord-settings.json` is created with the values
- [ ] 9.5 (manual) Manually trigger or wait for a monitor-point hit; confirm a Discord message appears in the configured channel and the existing logging handler still logs as before
- [ ] 9.6 (manual) Change ONLY the channel id via the UI to a channel the bot CAN access; confirm: no relogin in logs, next hit goes to the new channel
- [ ] 9.7 (manual) Change the channel id to one the bot CANNOT access; confirm: handler logs a Warning, bus does not retry the message indefinitely, polling and other handlers continue normally
- [ ] 9.8 (manual) Untick Enabled and Save; confirm: bot logs out (one Info log), `IsReady=false`, subsequent monitor-point hits produce no Discord traffic
- [ ] 9.9 (manual) Submit the form with `botToken` empty (after a token was previously stored) and `enabled` unchecked + new channel id; confirm: server preserves the existing token (next GET still shows the same `tokenPreview`)
- [ ] 9.10 (manual) Submit the form with `botToken` empty + `enabled` checked; confirm the form's client-side validation disables submit (server-side 400 is the safety net, not the primary UX)
- [ ] 9.11 (manual) Tail server logs across all flows above; confirm the raw bot token never appears in any log line, error, or response body
- [x] 9.12 README update: add a short "Discord notifications" subsection covering: where to create a bot, how to enable via `/settings`, file location of the persisted settings, that the file is git-ignored

## 10. Delete settings (feature addition)

- [x] 10.1 Add `Task<DiscordSettingsSnapshot> ResetAsync(CancellationToken ct)` to `IDiscordSettingsStore`; implement in `DiscordSettingsStore` to set `_settings = DiscordSettings.Default`, delete the persisted file if it exists (best-effort), raise `SettingsChanged` only if previous state differed from default
- [x] 10.2 Add `[HttpDelete]` action to `DiscordSettingsController` that calls `store.ResetAsync(ct)` and returns HTTP 204
- [x] 10.3 Add `deleteDiscordSettings(signal?)` to `vue-app/src/api/discordSettings.ts` (handles 204 like `deleteMonitorPoint`)
- [x] 10.4 Add `remove()` method to `useDiscordSettings` composable that calls the API and, on 204, sets `settings` to the default view (`enabled: false, hasToken: false, tokenPreview: null, channelId: 0`)
- [x] 10.5 In `SettingsView.vue`, add a "刪除設定" button to the Discord card (only rendered when `hasToken || channelId > 0`); on click use `window.confirm` then call `remove()`; reuse the existing toast pattern
- [x] 10.6 `dotnet build` clean; `pnpm type-check` + `pnpm lint` clean
- [ ] 10.7 (manual) Verify: with active Discord settings, click delete + confirm → bot logs out (one Info log via lifecycle), card returns to defaults, `data/discord-settings.json` is removed from disk

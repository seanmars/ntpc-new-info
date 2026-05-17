## Context

The WebApi already runs an in-process Rebus bus with an auto-registered logging handler (`LoggingMonitorPointAlertHandler`). There is also an established settings UX: `SettingsView.vue` already hosts the monitor-points CRUD UI, and `MonitorPointStore` (file-backed JSON, `SemaphoreSlim`-guarded, atomic temp-file swap, corrupt-file quarantine) is the project's canonical "small runtime-mutable state" pattern.

This change rides those two rails:
- A second handler subscribes to `MonitorPointEventDetected` and pushes to Discord.
- A second settings section on the same Vue page configures the bot.

The original draft of this change put Discord config in `appsettings.json` with `ValidateOnStart`. We deliberately rejected that approach in favor of a runtime-mutable store so that (a) the bot token never enters source control, (b) operators can rotate credentials without redeploy, and (c) the activation/deactivation experience matches how monitor points are already managed.

## Goals / Non-Goals

**Goals:**
- A single shared `IDiscordNotifier` singleton any component can resolve and call (`SendAsync(string, ulong?, CancellationToken)`).
- A persistent settings store editable via REST + UI, with the **token never exposed in GET responses** (only `hasToken` + last 4-char preview).
- Hot reload: changes to settings reconcile the live bot client (login / logout / no-op) without restarting the host.
- Failure in Discord delivery MUST NOT break the polling loop, the bus, or the host.
- The feature is fully usable starting from the default state of an empty store (Enabled=false, no token, no channel).

**Non-Goals:**
- Per-monitor-point channel routing (deferred — proposal explicitly picked single global channel).
- Inbound Discord interactions (slash commands, buttons). Send-only.
- Multi-bot / multi-tenant: exactly one bot identity at a time.
- Authentication / authorization for the settings endpoint. Same posture as `MonitorPointsController` (open). If the project later adds auth, this endpoint should be the first protected one.
- Retry / dedup / exactly-once delivery. At-most-once is acceptable.
- Encrypted at-rest token storage. Out of scope — the file lives next to `monitor-points.json` on a trusted volume.

## Decisions

### Decision: File-backed `DiscordSettingsStore` mirroring `MonitorPointStore`
Same `SemaphoreSlim` gate, same atomic temp-file swap, same corrupt-file quarantine, same `IHostEnvironment.ContentRootPath` resolution. Configured path defaults to `data/discord-settings.json`. The serialized record holds `Enabled`, `BotToken`, `ChannelId`.

**Alternative considered**: `IOptionsMonitor<DiscordOptions>` with `reload-on-change` writing back to `appsettings.json` — rejected because the secret would land in a file commonly under source control, and `appsettings.json` rewriting is fragile (lossy of comments, JSON-with-comments mismatches, environment-overlay confusion).

**Alternative considered**: A new lightweight database (SQLite) — rejected as gratuitous; one record's worth of state does not justify a dependency.

### Decision: Token never returned in GET; `tokenPreview` shows last 4 chars
GET shape:
```json
{ "enabled": true, "hasToken": true, "tokenPreview": "...ab12", "channelId": 1234567890123456789 }
```
PUT semantics for `botToken`:
- Field **omitted** OR `null` → keep existing token.
- Field is `""` (empty string) → clear stored token (and force `enabled=false` server-side if it was true).
- Field is any non-empty string → replace stored token.

This lets the UI safely re-submit the form (with the masked preview displayed but the input left empty) without accidentally wiping the token, while still allowing explicit clearing.

**Alternative considered**: Always require the full token on every PUT — rejected as a UX wart; users who want to change only the channel id should not have to re-type the token.

### Decision: `DiscordBotState` singleton as shared mutable state between notifier and lifecycle
A tiny class holding `volatile bool IsReady`, `DiscordRestClient? Client`, a `ulong? CurrentChannelId`, and a `SemaphoreSlim ReconfigGate`. Both `DiscordRestNotifier` and `DiscordBotLifecycleService` are constructed with this state. The lifecycle service writes; the notifier reads.

**Why this shape**: avoids cross-injecting two services into each other (which would couple their lifetimes confusingly) and keeps the gate / readiness flag colocated with the resource they protect.

### Decision: `DiscordRestClient` (REST-only) over `DiscordSocketClient`
Send-only means no need for a gateway WebSocket. Lighter, no reconnection state machine, no heartbeat. If we ever add slash commands we can swap or stack a `DiscordSocketClient` later.

### Decision: Lifecycle reconciles on startup AND on every store change
The store exposes an event (`event EventHandler<DiscordSettingsSnapshot>? SettingsChanged`). The lifecycle service subscribes; on each notification, under the reconfig gate:
1. If new state is `Enabled=false` and a client exists → `LogoutAsync` + dispose, set `IsReady=false`.
2. If new state is `Enabled=true`:
   - If token changed (or no client yet) → dispose any existing client, create new `DiscordRestClient`, `LoginAsync`, swap into state, set `IsReady=true`. On failure, log and leave `IsReady=false`.
   - If only `ChannelId` changed → no relogin; just update `CurrentChannelId`.

The store debounces by serializing writes through its own gate — the lifecycle handler runs synchronously per change event.

**Alternative considered**: Restart the entire app on settings change — clearly worse for an app that does background polling.

### Decision: Settings-change validation lives in the controller, not in the store
The controller rejects `Enabled=true` with no token or `ChannelId=0` (HTTP 400). The store itself is permissive — it stores what it is told. This keeps the store dumb and lets future call sites (e.g., a CLI) reuse the validation rule from one place.

### Decision: PUT atomically replaces all three fields
There is no PATCH endpoint. The Vue form always sends the full record, with `botToken` honoring the omit/empty/value tri-state above. This avoids the field-by-field state machine that comes with PATCH and matches the existing `MonitorPointsController.Update` style (which also takes a full `MonitorPointCreateRequest`).

### Decision: Handler swallows notifier exceptions; bus does not retry
Same rationale as the original draft: re-firing a "rescue event near monitor point X" 20 minutes later is worse than dropping it; the in-memory transport has no DLQ configured.

## Risks / Trade-offs

- **[Anyone with API access can read partial token + channel id and rotate the bot token]** → Accepted in this iteration; consistent with the project's current open API surface (monitor points are also openly mutable). When auth is added, this endpoint should be among the first protected.
- **[Token stored in plaintext at rest]** → Accepted; trusted-volume assumption matches the monitor-points JSON file. If we ever target hostile environments, swap the store for DPAPI / a secret manager — the `IDiscordSettingsStore` abstraction shields callers.
- **[Race between PUT and a poll-triggered handler send]** → Handled by the reconfig gate. If a send is in flight when settings change, it completes against the old `CurrentChannelId`; the next send uses the new value. No worse than at-most-once semantics already allow.
- **[Re-login bursts from rapid edits]** → Mitigated by the reconfig gate serializing changes; user-driven edits are infrequent enough that we don't add a debounce timer.
- **[Lifecycle service event handler throws]** → Handler is wrapped in try/catch and logged at Error. Failure leaves `IsReady=false` and the user can retry via another PUT.
- **[Frontend shows stale state if backend changes externally]** → Acceptable; the settings page already refreshes on mount. We do not add live push.
- **[`tokenPreview` could leak partial entropy]** → 4 chars from a typical Discord token (~70 chars) reveals negligible information; sufficient for "is this the same token I typed?" UX without enabling reconstruction.

## Migration Plan

Purely additive. No data migration.

1. Ship code. On first run, `data/discord-settings.json` does not exist → store treats this as the default `Enabled=false` state, `Discord/` lifecycle service starts but immediately enters disabled mode, no login attempted, no errors.
2. Operator navigates to the settings page, opens the new Discord section, fills in token + channel id, ticks Enabled, clicks Save.
3. `PUT` succeeds → settings event fires → lifecycle service logs in → `IsReady=true`. Next monitor-point hit produces a Discord message.

Rollback = uncheck Enabled and Save, or `DELETE data/discord-settings.json` and restart. The data file SHOULD be in `.gitignore` from day one (same as `data/monitor-points.json` presumably already is).

# discord-notifications Specification

## Purpose
TBD - created by archiving change add-discord-bot-notifications. Update Purpose after archive.
## Requirements
### Requirement: Persistent Discord settings store
The system SHALL persist a single Discord settings record (`Enabled`, `BotToken`, `ChannelId`) to a JSON file on disk through an `IDiscordSettingsStore` service, SHALL load the record at startup, SHALL serialize all reads/writes through a single per-instance gate so concurrent callers cannot observe torn writes, and SHALL write updates atomically by writing to a temp file and moving it over the destination.

#### Scenario: First run with no file
- **WHEN** the application starts and the configured store file does NOT exist
- **THEN** the store SHALL initialize in memory with `Enabled = false`, empty `BotToken`, `ChannelId = 0`, SHALL NOT create the file until the first successful write, and SHALL NOT throw

#### Scenario: Existing file loaded
- **WHEN** the application starts and the configured store file exists and contains valid JSON for the record
- **THEN** the store SHALL load the persisted values and make them available to callers without any further I/O

#### Scenario: Corrupt file quarantined
- **WHEN** the store file exists but cannot be parsed as JSON
- **THEN** the store SHALL rename the file to `<file>.corrupt-<timestamp>`, SHALL log an Error including the original and quarantine paths, SHALL initialize with default empty state, and SHALL NOT throw out of the constructor

#### Scenario: Atomic write
- **WHEN** any write occurs (`UpdateAsync`)
- **THEN** the store SHALL write the new JSON payload to `<file>.tmp` first, SHALL then `File.Move(temp, file, overwrite: true)`, and on a failure between those two steps SHALL attempt to delete the temp file before propagating the original exception

#### Scenario: Notifies subscribers on change
- **WHEN** a write completes successfully and changes ANY field of the record
- **THEN** the store SHALL raise a `SettingsChanged` notification carrying the new snapshot, exactly once, after the in-memory state and the on-disk file have both been updated

### Requirement: Discord settings REST endpoint
The system SHALL expose `GET`, `PUT`, and `DELETE` endpoints under `/api/settings/discord`. The `GET` response SHALL NEVER contain the raw bot token; the `PUT` request SHALL support three modes for the `botToken` field — keep (omit/null), clear (empty string), or replace (non-empty string) — and SHALL reject requests that would leave the store in an invalid `Enabled = true` state. The `DELETE` request SHALL reset the stored settings to defaults (disabled, empty token, channel id 0) and SHALL remove the persisted store file from disk if present.

#### Scenario: GET returns masked view
- **WHEN** a client issues `GET /api/settings/discord`
- **THEN** the response SHALL be HTTP 200 with body `{ "enabled": <bool>, "hasToken": <bool>, "tokenPreview": <string|null>, "channelId": <number> }`, where `tokenPreview` SHALL be either `null` (when no token stored) or the last 4 characters of the stored token prefixed with `"..."` (e.g., `"...ab12"`), and the response SHALL NEVER contain the full token under any field name

#### Scenario: PUT keeps existing token when field omitted or null
- **WHEN** a client issues `PUT /api/settings/discord` with a body where `botToken` is missing OR `botToken` is `null`
- **THEN** the stored `BotToken` SHALL remain unchanged, and the other fields SHALL be replaced with the request values

#### Scenario: PUT clears token when field is empty string
- **WHEN** a client issues `PUT /api/settings/discord` with `botToken: ""`
- **THEN** the stored `BotToken` SHALL be cleared (empty), and if the request also sets `enabled: true` the request SHALL be rejected per the validation rules below

#### Scenario: PUT replaces token when field is non-empty
- **WHEN** a client issues `PUT /api/settings/discord` with `botToken: "<non-empty value>"`
- **THEN** the stored `BotToken` SHALL be replaced with that value

#### Scenario: PUT validation rejects enabled-without-credentials
- **WHEN** a client issues `PUT /api/settings/discord` whose resulting state would be `enabled = true` AND (`botToken` is empty after applying the above rules OR `channelId` is 0)
- **THEN** the response SHALL be HTTP 400 with a body identifying which field is invalid, and the store SHALL NOT be modified

#### Scenario: Successful PUT returns the same masked shape as GET
- **WHEN** a `PUT` succeeds
- **THEN** the response SHALL be HTTP 200 with the same shape as `GET` reflecting the new state, and SHALL NEVER include the raw token

#### Scenario: Token never logged in responses or telemetry
- **WHEN** any component logs or echoes the Discord settings (request validation errors, lifecycle events, handler send failures, etc.)
- **THEN** the log/output SHALL NEVER contain the raw `BotToken`; references to the token SHALL use either `tokenPreview` style ("...ab12") OR a fixed placeholder ("***")

#### Scenario: DELETE resets settings to defaults
- **WHEN** a client issues `DELETE /api/settings/discord`
- **THEN** the response SHALL be HTTP 204 (No Content), the stored settings SHALL be replaced with defaults (`Enabled = false`, empty `BotToken`, `ChannelId = 0`), and any persisted store file on disk SHALL be removed; a subsequent `GET` SHALL return the default masked view (`enabled: false, hasToken: false, tokenPreview: null, channelId: 0`)

#### Scenario: DELETE notifies subscribers
- **WHEN** a `DELETE` succeeds AND the previous state had any non-default field
- **THEN** the store SHALL raise a `SettingsChanged` event carrying the new default snapshot so the lifecycle service SHALL log the bot out (if it was logged in) within its normal reconcile path

#### Scenario: DELETE on already-default state
- **WHEN** a client issues `DELETE /api/settings/discord` and the stored state is already at defaults
- **THEN** the response SHALL still be HTTP 204, the store file (if present) SHALL still be removed, and no `SettingsChanged` event needs to fire (the snapshot is unchanged)

### Requirement: `IDiscordNotifier` abstraction
The system SHALL expose an `IDiscordNotifier` service registered in dependency injection as a singleton, providing `SendAsync(string content, ulong? channelId = null, CancellationToken cancellationToken = default)` that delivers a text message to the specified channel, defaulting to the current stored `ChannelId` when `channelId` is null.

#### Scenario: Default channel routing
- **WHEN** a caller invokes `SendAsync(content)` without `channelId`
- **THEN** the notifier SHALL deliver the message to the channel currently stored in `IDiscordSettingsStore`

#### Scenario: Explicit channel override
- **WHEN** a caller invokes `SendAsync(content, channelId: 123)`
- **THEN** the notifier SHALL deliver the message to channel `123`, NOT to the stored default

#### Scenario: No-op when disabled
- **WHEN** stored `Enabled = false` and a caller invokes `SendAsync(content)`
- **THEN** the call SHALL complete successfully, SHALL NOT make any network request to Discord, and SHALL NOT throw

#### Scenario: Singleton lifetime
- **WHEN** two consumers resolve `IDiscordNotifier`
- **THEN** they SHALL receive the same instance, ensuring a single shared underlying Discord client

#### Scenario: Send before bot ready
- **WHEN** stored `Enabled = true` BUT the bot lifecycle has not (yet, or has failed to) establish a logged-in client
- **THEN** the notifier SHALL skip the network call, SHALL log a Warning identifying the dropped message's target channel id (NEVER the token), and SHALL return `Task.CompletedTask` without throwing

### Requirement: Discord bot lifecycle with hot reload
The system SHALL manage a single shared `DiscordRestClient` inside a hosted background service that reconciles the live client against the current settings on application startup AND on every `SettingsChanged` notification, SHALL maintain a readiness flag the notifier checks before sending, and SHALL serialize all reconfiguration through an internal gate so that concurrent settings changes cannot produce overlapping login/logout operations.

#### Scenario: Startup with enabled and valid settings
- **WHEN** the application starts with stored `Enabled = true`, non-empty `BotToken`, and `ChannelId > 0`
- **THEN** the lifecycle service SHALL log in once, SHALL log a single Information entry containing the bot's username (NEVER the token), and SHALL set the readiness flag to `true`

#### Scenario: Startup with disabled settings
- **WHEN** the application starts with stored `Enabled = false`
- **THEN** the lifecycle service SHALL NOT attempt any Discord login, SHALL leave the readiness flag `false`, and SHALL still start successfully so it can react to later changes

#### Scenario: Toggle enabled at runtime
- **WHEN** a `SettingsChanged` notification arrives with `Enabled = true` while the current live client is null or logged out
- **THEN** the lifecycle service SHALL create a `DiscordRestClient`, SHALL log in using the new token, and on success SHALL set the readiness flag to `true`

#### Scenario: Toggle disabled at runtime
- **WHEN** a `SettingsChanged` notification arrives with `Enabled = false` while a live client exists
- **THEN** the lifecycle service SHALL log out and dispose the client, SHALL set the readiness flag to `false`, and SHALL leave the stored token unchanged

#### Scenario: Token change at runtime
- **WHEN** a `SettingsChanged` notification arrives with `Enabled = true` and a `BotToken` different from the one the live client logged in with
- **THEN** the lifecycle service SHALL dispose the existing client, create a new one, log in with the new token, and update the readiness flag accordingly

#### Scenario: Channel-only change at runtime
- **WHEN** a `SettingsChanged` notification arrives with `Enabled = true` and only `ChannelId` differs from the previous snapshot
- **THEN** the lifecycle service SHALL NOT log out or relogin; the notifier SHALL observe the new channel via the store on its next send

#### Scenario: Login failure does not crash the host
- **WHEN** Discord rejects the login (invalid token, network failure, etc.)
- **THEN** the lifecycle service SHALL catch the exception, SHALL log it at Error level (token masked), SHALL leave the readiness flag `false`, and SHALL NOT propagate the exception out of the change handler or the host

#### Scenario: Reconfigurations are serialized
- **WHEN** multiple `SettingsChanged` notifications arrive concurrently
- **THEN** the lifecycle service SHALL process them one at a time under an internal gate so login and logout operations cannot overlap

#### Scenario: Clean shutdown
- **WHEN** the host requests shutdown
- **THEN** the lifecycle service SHALL dispose the live client (if any) within a reasonable grace period, SHALL unsubscribe from `SettingsChanged`, and SHALL NOT throw if shutdown occurs before any login completed

### Requirement: Monitor point event Discord handler
The system SHALL register an `IHandleMessages<MonitorPointEventDetected>` implementation that, for each delivered bus message, formats a human-readable Discord message describing the monitor point hit and sends it via `IDiscordNotifier` exactly once, and SHALL isolate notifier failures so that the bus does NOT retry or dead-letter the message.

#### Scenario: One Discord message per delivered bus message
- **WHEN** the in-process bus delivers a single `MonitorPointEventDetected` to the handler
- **THEN** the handler SHALL invoke `IDiscordNotifier.SendAsync` exactly once

#### Scenario: Message content
- **WHEN** the handler formats a `MonitorPointEventDetected`
- **THEN** the resulting Discord message SHALL include the monitor point name, the count of matched features, the detection time, and one line per matched feature (feature id if present, else `lat,lng`), up to a cap of 5 entries; any features beyond the cap SHALL be represented by a single `...and N more` line

#### Scenario: Notifier failure does not poison the bus
- **WHEN** `IDiscordNotifier.SendAsync` throws
- **THEN** the handler SHALL catch the exception, SHALL log it at Warning level (with `MonitorPointId` and target `ChannelId` fields, but NEVER the bot token), and SHALL return `Task.CompletedTask` so Rebus does NOT retry or dead-letter the message

#### Scenario: Coexists with the logging handler
- **WHEN** the bus delivers a `MonitorPointEventDetected`
- **THEN** BOTH the existing logging handler AND the Discord handler SHALL receive the message; failure of one handler SHALL NOT prevent the other from processing it

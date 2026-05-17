## Context

The project is a brand-new ASP.NET Core Web API on .NET 10 (`WebApi/WebApi.csproj`, target framework `net10.0`, nullable + implicit usings enabled). `Program.cs` is the minimal scaffold — controllers, OpenAPI, and authorization middleware are wired but no domain logic exists yet, and the `Controllers/` folder is empty. There is no existing background service infrastructure to extend.

The upstream of interest is `https://e.ntpc.gov.tw/v3/api/map/dynamic/layer/rescue`, a public-facing NTPC open data endpoint that returns the "rescue" layer for the dynamic map. We do not control its rate limit, schema stability, or uptime. The frontend in `vue-app/` will consume our backend's cached copy rather than calling the upstream directly.

Stakeholders: backend operators (need to tune polling cadence without redeploys), frontend developers (need a stable, low-latency endpoint), and the upstream data provider (we should not hammer their service).

## Goals / Non-Goals

**Goals:**
- Fetch the upstream rescue dataset on a recurring schedule (default 5 minutes) inside the backend process.
- Allow operators to change the polling interval at runtime without restarting.
- Serve the cached snapshot from a dedicated read API with metadata about freshness.
- Keep the design minimal — no new NuGet packages, no external infrastructure (Redis, message queues, etc.).
- Fail safely: upstream failures must never crash the host or wipe the cached snapshot.

**Non-Goals:**
- Authentication / authorization on the read API (the upstream is public; auth can be layered on later if required).
- Persisting snapshots across restarts (in-memory only; the first poll after startup repopulates).
- Reverse-engineering or normalizing the upstream JSON shape — we treat the payload as opaque JSON and pass it through.
- Horizontal scale-out coordination (single-instance assumed; if multiple instances run, each polls independently — see Risks).
- Push/streaming the data to clients (REST poll-from-clients is sufficient for the v1 frontend).

## Decisions

### 1. Use `BackgroundService` (not a timer in `Program.cs`)

A class inheriting from `Microsoft.Extensions.Hosting.BackgroundService` registered via `AddHostedService<T>()` is the idiomatic ASP.NET Core pattern for long-running work tied to the host lifetime. It handles graceful shutdown via the `stoppingToken` parameter and integrates with the DI container so we can inject `IHttpClientFactory`, `IOptionsMonitor<RescuePollingOptions>`, and `ILogger<T>`.

**Alternative considered:** `System.Threading.Timer` started from `Program.cs`. Rejected because timers don't participate in graceful shutdown by default, don't compose with DI, and require manual disposal — three sources of subtle bugs for no gain.

### 2. `IOptionsMonitor<RescuePollingOptions>` for runtime-adjustable interval

`IOptionsMonitor<T>` re-reads bound options whenever the underlying `IConfiguration` source changes (and `appsettings.json` is registered with `reloadOnChange: true` by default in the web host builder). The poll loop reads `_options.CurrentValue.Interval` at the top of each iteration, so an edit to `appsettings.json` takes effect on the next iteration without a restart.

**Alternative considered:**
- `IOptions<T>` — rejected; it's a snapshot captured at startup, no live updates.
- `IOptionsSnapshot<T>` — rejected; it's scoped, not safe to consume from a singleton `BackgroundService`.
- Admin HTTP endpoint that mutates an in-memory value — possible follow-up but unnecessary for v1; configuration reload already meets the requirement and keeps the source of truth in one place.

### 3. Polling loop shape: `await PollOnce(); await Task.Delay(interval, ct);`

Sequential, single-flight per iteration. Pro: simple, no overlapping fetches, no risk of two in-flight polls stepping on each other. Con: a slow poll delays the next one. Acceptable because the request timeout (30s) is much smaller than the default interval (5 min).

The initial fetch runs immediately on startup before entering the wait loop, so the cache is populated as soon as possible (otherwise clients hitting the API in the first 5 minutes would all get HTTP 503).

### 4. Typed `HttpClient` via `IHttpClientFactory`

Register with `services.AddHttpClient<RescuePollingService>(client => { client.Timeout = ...; client.DefaultRequestHeaders.UserAgent.Add(...); })`. This avoids socket exhaustion (the classic `new HttpClient()` antipattern), centralizes timeout/header config, and is the supported pattern for .NET 10. Default timeout: 30 seconds, configurable via options.

### 5. Snapshot storage: a single `volatile`/`Interlocked.Exchange`-protected reference to an immutable record

A small `RescueSnapshot(string PayloadJson, DateTimeOffset FetchedAt, string? LastError, DateTimeOffset? LastErrorAt)` record stored behind a thread-safe accessor (a `RescueSnapshotStore` singleton). Writes use `Interlocked.Exchange` (or a `lock`); reads return the current reference. Because the record is immutable, readers can safely access its fields without further synchronization.

**Alternative considered:** `IMemoryCache` — overkill for a single key, brings expiration semantics we don't want (we explicitly want stale data to remain available on upstream failure).

### 6. Payload handling: passthrough opaque JSON string

Read the upstream response as a UTF-8 string and validate it parses as JSON (`JsonDocument.Parse`) to catch garbage. Store the raw string and re-emit it inside our response envelope under `data`. This avoids coupling our backend to the upstream schema, which we don't own.

**Alternative considered:** Deserialize to a strongly-typed model. Rejected for v1 because we don't yet know the full upstream schema or which fields the frontend uses; we'd be building a typed wall we'd immediately have to chip holes in. Can be added later behind the same endpoint.

### 7. API shape: `GET /api/rescue/latest`

A single controller action returning `{ "data": <upstream-payload>, "meta": { "fetchedAt": "...", "source": "..." } }`. Returns 503 when no snapshot exists, with `{ "error": "...", "lastError": "..." }`. The endpoint never calls upstream — purely a cache read.

### 8. Configuration layout

```json
"RescuePolling": {
  "UpstreamUrl": "https://e.ntpc.gov.tw/v3/api/map/dynamic/layer/rescue",
  "Interval": "00:05:00",
  "RequestTimeout": "00:00:30"
}
```

`TimeSpan` strings are natively bindable in .NET configuration, which keeps the format human-readable and easy to edit on a running host.

## Risks / Trade-offs

- **Upstream schema change breaks frontend silently** → Mitigation: passthrough JSON means the backend itself never fails on schema drift, but the frontend can. Add structured logging of payload size/hash so unexpected changes are at least visible in logs. Future: optional schema validation in spec v2.
- **Two backend instances both poll the upstream** → Mitigation: documented non-goal for v1. If/when we scale horizontally, options are (a) leader election via distributed lock, (b) move polling to a separate worker process, or (c) accept the duplicate calls if upstream allows them.
- **`IOptionsMonitor` interval changes only apply at the start of the next wait** → A change made 30 seconds into a 5-minute wait still takes up to 4.5 minutes to kick in. Acceptable; documented in the spec scenarios. If snappier reaction is needed later, the loop can use a cancellable wait that's reset on options change events.
- **Cache returns very stale data when upstream is down for a long time** → Mitigation: the meta object exposes `fetchedAt`, so clients can decide what to do. Optionally serve 503 instead of stale once data exceeds a threshold age — left out of v1 to keep behavior simple and predictable.
- **No backoff on repeated failures** → Mitigation: we poll on a fixed schedule (default 5 min) which is already gentle; aggressive backoff would just delay recovery. If upstream returns 429, log the `Retry-After` header for operator visibility but otherwise continue on the configured interval.
- **HttpClient typed registration ties HttpClient lifetime to the BackgroundService (singleton)** → Mitigation: `AddHttpClient<T>` correctly handles message-handler rotation under the hood even for singleton consumers in .NET 10, so this is the supported path.

## Migration Plan

Greenfield change — no migration. Rollout:
1. Merge and deploy. Polling starts automatically; first poll runs immediately on startup.
2. Verify in logs that the first poll succeeds.
3. Smoke-test `GET /api/rescue/latest`.
4. To change cadence, edit `RescuePolling:Interval` in `appsettings.json` on the running host (or via your configuration provider of choice) and wait for the next iteration.

Rollback: revert the merge; no data migration needed.

## Open Questions

- Do we want an admin endpoint (e.g., `POST /api/rescue/refresh`) that forces an out-of-band poll? Useful for ops, but adds an attack-surface concern. Defer to a follow-up unless ops asks for it.
- Should the read API support conditional requests (`ETag`/`If-None-Match`) so the frontend can avoid redownloading unchanged payloads? Likely yes in a follow-up; trivial to add once we expose `fetchedAt`.

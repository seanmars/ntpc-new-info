## Why

The frontend needs current "rescue" (救援) layer data from the NTPC open data endpoint, but the upstream API at `https://e.ntpc.gov.tw/v3/api/map/dynamic/layer/rescue` should not be called on every client request — that would increase latency, leak the backend's outbound traffic onto every page load, and risk being rate-limited or banned by the data provider. We need the backend to act as a caching proxy that polls the upstream on a configurable schedule and serves clients from its in-memory snapshot.

## What Changes

- Add a background polling service that fetches rescue layer data from the upstream NTPC endpoint at a configurable interval (default: 5 minutes).
- Make the polling interval **runtime-adjustable** — operators can change it via configuration (e.g., `appsettings.json` edits picked up by `IOptionsMonitor`, or an admin endpoint) without restarting the service.
- Add a read-only HTTP API endpoint (`GET /api/rescue/latest`) that returns the most recently fetched rescue data plus metadata about when it was retrieved.
- Register a typed `HttpClient` for the upstream call with a sensible timeout and `User-Agent`.
- Surface basic observability: log poll attempts, success/failure, and elapsed time; expose `lastFetchedAt` and `lastError` in the API response or a separate health-style endpoint.

## Capabilities

### New Capabilities
- `rescue-data-polling`: Periodically retrieves the NTPC rescue layer from the upstream API, caches the latest successful snapshot in memory, exposes a runtime-adjustable polling interval, and serves the cached data through a public read API.

### Modified Capabilities
<!-- none — this is a greenfield capability; no existing specs in openspec/specs/ -->

## Impact

- **Code**: New files in `WebApi/` — a `BackgroundService` for polling, an options class for interval/URL, a controller for `/api/rescue/latest`, a typed `HttpClient` registration, and a thread-safe in-memory snapshot store. Wire-up in `Program.cs`.
- **Config**: New `RescuePolling` section in `appsettings.json` (`UpstreamUrl`, `Interval`, request timeout). Must be reloadable at runtime (`IOptionsMonitor<T>`).
- **Dependencies**: No new NuGet packages required — uses `Microsoft.Extensions.Http`, `Microsoft.Extensions.Hosting`, and `Microsoft.Extensions.Options` which ship with ASP.NET Core / .NET 10.
- **Outbound traffic**: Adds a recurring outbound HTTPS call from the backend to `e.ntpc.gov.tw` on the configured interval.
- **No breaking changes** — this is purely additive.

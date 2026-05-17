## 1. Dependencies

- [x] 1.1 Add `Rebus` NuGet package to `src/WebApi/WebApi.csproj` (latest stable supporting .NET 10)
- [x] 1.2 Add `Rebus.ServiceProvider` NuGet package to `src/WebApi/WebApi.csproj`
- [x] 1.3 Run `dotnet restore` and confirm the solution still builds

## 2. Message contract

- [x] 2.1 Create folder `src/WebApi/Messaging/`
- [x] 2.2 Create `src/WebApi/Messaging/MonitorPointEventDetected.cs` as a `sealed record` with fields: `MonitorPointId`, `MonitorPointName`, `MonitorPointLatitude`, `MonitorPointLongitude`, `MonitorPointRadius`, `MatchedFeatures` (`IReadOnlyList<MatchedFeature>`), `DetectedAt`, `SnapshotFetchedAt`
- [x] 2.3 Create `MatchedFeature` `sealed record` with fields: `FeatureId` (string?), `Latitude` (double), `Longitude` (double), `Distance` (double, meters), `Properties` (`System.Text.Json.Nodes.JsonNode?`)

## 3. Geo helpers

- [x] 3.1 Add internal static class `GeoDistance` (under `WebApi.Messaging` or `WebApi.Services`) with method `static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)` using Earth radius 6_371_000 m
- [x] 3.2 Unit-cover the helper informally by verifying known distances (e.g., Taipei 101 to Taipei Main Station ~3.5 km) before wiring into detector

## 4. Event detector service

- [x] 4.1 Create `src/WebApi/Messaging/IMonitorPointEventDetector.cs` exposing `Task DetectAndPublishAsync(JsonNode snapshotData, DateTimeOffset snapshotFetchedAt, CancellationToken ct)`
- [x] 4.2 Create `src/WebApi/Messaging/MonitorPointEventDetector.cs` implementing the interface; inject `IMonitorPointStore`, `IBus`, `TimeProvider`, `ILogger<MonitorPointEventDetector>`
- [x] 4.3 In `DetectAndPublishAsync`: load monitor points; return early if empty
- [x] 4.4 Parse `snapshotData["features"]` as `JsonArray`; iterate features
- [x] 4.5 For each feature, extract geometry vertices via a private helper `EnumerateVertices(JsonNode geometry)` supporting Point / LineString / Polygon / MultiLineString / MultiPolygon; log a warning and skip on unknown/null/invalid geometry
- [x] 4.6 Remember that GeoJSON coordinates are `[lon, lat]`, NOT `[lat, lon]`
- [x] 4.7 For each (monitorPoint, feature) pair: among feature vertices, find min haversine distance; if `min <= radius`, record a `MatchedFeature` using that closest vertex
- [x] 4.8 Group matched features by monitor point; for each monitor point with at least one match, build a `MonitorPointEventDetected` and call `IBus.Publish(msg)`
- [x] 4.9 Wrap publish call so individual feature/parse failures only log; do not abort the whole detection

## 5. Logging handler

- [x] 5.1 Create `src/WebApi/Messaging/LoggingMonitorPointAlertHandler.cs` implementing `Rebus.Handlers.IHandleMessages<MonitorPointEventDetected>`
- [x] 5.2 In `Handle(MonitorPointEventDetected message)`, emit a single `LogInformation` call with structured fields: `MonitorPointId`, `MonitorPointName`, `MatchCount`, `DetectedAt`, plus the matched feature ids as a separate scope/property (NO HTTP/file side effects)

## 6. Rebus wiring in Program.cs

- [x] 6.1 In `src/WebApi/Program.cs`, add `using Rebus.Config;`, `using Rebus.ServiceProvider;`, `using Rebus.Transport.InMem;`
- [x] 6.2 Add `builder.Services.AddRebus(configure => configure.Transport(t => t.UseInMemoryTransport(new InMemNetwork(), "monitor-point-alerts")));`
- [x] 6.3 Add `builder.Services.AutoRegisterHandlersFromAssemblyOf<LoggingMonitorPointAlertHandler>();`
- [x] 6.4 Register the detector: `builder.Services.AddSingleton<IMonitorPointEventDetector, MonitorPointEventDetector>();`
- [ ] 6.5 Ensure the bus starts with the host (Rebus.ServiceProvider auto-starts via the built-in hosted service — confirm by checking startup logs show "Rebus has started")

## 7. Polling integration

- [x] 7.1 Modify `src/WebApi/Services/RescuePollingService.cs` constructor to inject `IMonitorPointEventDetector detector`
- [x] 7.2 In `PollOnceAsync`, after `store.SetSuccess(data)`, capture `var fetchedAt = store.Current.FetchedAt ?? DateTimeOffset.UtcNow;` and call `await SafeDetectAsync(data, fetchedAt, ct);`
- [x] 7.3 Implement private `SafeDetectAsync(JsonNode data, DateTimeOffset fetchedAt, CancellationToken ct)` that calls the detector inside try/catch; on exception, `logger.LogError(ex, "Monitor point event detection failed.");` and swallow
- [x] 7.4 Confirm failure branch (`SetFailure`) does NOT call the detector

## 8. Local verification

- [x] 8.1 `dotnet build` cleanly with no new warnings
- [ ] 8.2 Run the WebApi via AppHost; confirm startup logs include Rebus initialization
- [ ] 8.3 Add a monitor point near a known rescue feature via `POST /api/monitor-points`; wait one poll cycle (or trigger by lowering `RescuePolling:Interval` temporarily); confirm a structured `MonitorPointEventDetected`-style log line appears
- [ ] 8.4 Move the monitor point far away (or set tiny `radius`); confirm no log line appears in the next poll cycle
- [ ] 8.5 Confirm `GET /api/rescue/latest` still serves the snapshot and the polling service keeps running for at least 2 cycles after a deliberately injected detector error (e.g., temporarily throw inside the detector to verify isolation)

## 9. Documentation hygiene

- [x] 9.1 Update `README.md` if it mentions message infrastructure (otherwise skip) — skipped: README does not currently mention message infrastructure
- [ ] 9.2 Once verified, run `/opsx:archive` (or `openspec archive`) per project workflow

## 1. Configuration & options

- [x] 1.1 Create `WebApi/Options/RescuePollingOptions.cs` with properties `UpstreamUrl` (string), `Interval` (TimeSpan), `RequestTimeout` (TimeSpan); include `public const string SectionName = "RescuePolling";`.
- [x] 1.2 Add a `RescuePolling` section to `WebApi/appsettings.json` with defaults: `UpstreamUrl = "https://e.ntpc.gov.tw/v3/api/map/dynamic/layer/rescue"`, `Interval = "00:05:00"`, `RequestTimeout = "00:00:30"`.
- [x] 1.3 Bind options in `Program.cs` via `builder.Services.Configure<RescuePollingOptions>(builder.Configuration.GetSection(RescuePollingOptions.SectionName));`.

## 2. Snapshot store

- [x] 2.1 Create `WebApi/Services/RescueSnapshot.cs` as an immutable `record` with fields `string PayloadJson`, `DateTimeOffset FetchedAt`, `string? LastError`, `DateTimeOffset? LastErrorAt`.
- [x] 2.2 Create `WebApi/Services/IRescueSnapshotStore.cs` exposing `RescueSnapshot? Current { get; }`, `void SetSuccess(string payloadJson, DateTimeOffset fetchedAt)`, and `void SetFailure(string error, DateTimeOffset at)`.
- [x] 2.3 Implement `WebApi/Services/RescueSnapshotStore.cs` using `Interlocked.Exchange` (or a `lock`) so reads/writes are thread-safe; preserve the previous successful payload when `SetFailure` is called.
- [x] 2.4 Register the store as singleton: `builder.Services.AddSingleton<IRescueSnapshotStore, RescueSnapshotStore>();`.

## 3. Typed HttpClient

- [x] 3.1 Create `WebApi/Services/RescueDataFetcher.cs` (typed HttpClient consumer) that exposes `Task<string> FetchAsync(string url, CancellationToken ct)` returning the raw JSON body and throwing on non-2xx or invalid JSON.
- [x] 3.2 In `Program.cs`, register `builder.Services.AddHttpClient<RescueDataFetcher>((sp, client) => { var opts = sp.GetRequiredService<IOptions<RescuePollingOptions>>().Value; client.Timeout = opts.RequestTimeout; client.DefaultRequestHeaders.UserAgent.ParseAdd("ntpc-new-info-backend/0.1"); });`.
- [x] 3.3 Inside `FetchAsync`, validate the body parses with `JsonDocument.Parse` before returning; throw `InvalidDataException` if it does not.

## 4. Background polling service

- [x] 4.1 Create `WebApi/Services/RescuePollingService.cs` inheriting from `BackgroundService`; constructor-inject `IHttpClientFactory` (or the typed `RescueDataFetcher`), `IRescueSnapshotStore`, `IOptionsMonitor<RescuePollingOptions>`, and `ILogger<RescuePollingService>`.
- [x] 4.2 In `ExecuteAsync`, run `PollOnceAsync` immediately, then loop: read `_options.CurrentValue.Interval`, validate it (`> TimeSpan.Zero`, fall back to 5 minutes with a warning if invalid), `await Task.Delay(interval, stoppingToken)`, then `PollOnceAsync` again until `stoppingToken` cancels.
- [x] 4.3 Implement `PollOnceAsync` to call `RescueDataFetcher.FetchAsync(options.UpstreamUrl, ct)`, write success to the store on completion, and on any exception write a failure entry and log the error — never let an exception escape the loop.
- [x] 4.4 Register the service: `builder.Services.AddHostedService<RescuePollingService>();`.

## 5. Read API endpoint

- [x] 5.1 Create `WebApi/Controllers/RescueController.cs` with `[ApiController]` and `[Route("api/rescue")]`.
- [x] 5.2 Inject `IRescueSnapshotStore` and `IOptions<RescuePollingOptions>` into the controller.
- [x] 5.3 Implement `GET /api/rescue/latest`: if `store.Current` is null, return `StatusCode(503, new { error = "rescue data not yet available", lastError = ... })`; otherwise return `Ok(new { data = JsonDocument.Parse(snapshot.PayloadJson).RootElement, meta = new { fetchedAt = snapshot.FetchedAt, source = options.UpstreamUrl } })` so the cached JSON is re-emitted structurally (not as an escaped string).
- [x] 5.4 Verify the controller is picked up by `app.MapControllers()` (already wired in `Program.cs`).

## 6. Verification

- [x] 6.1 `dotnet build WebApi/WebApi.csproj` succeeds with no warnings. (Verified: 0 Warning(s), 0 Error(s) on .NET 10 SDK 10.0.201.)
- [x] 6.2 Run `dotnet run --project WebApi` and confirm the log line for the first successful poll appears within the request timeout. (Verified: `Rescue poll succeeded (13740 bytes) ... in 80.675 ms.` appeared right after startup.)
- [x] 6.3 `GET http://localhost:<port>/api/rescue/latest` returns HTTP 200 with non-empty `data` and a `meta.fetchedAt` close to "now". (Verified: HTTP 200, `data.data` contains live ambulance/rescue features, `meta.fetchedAt` matched startup time.)
- [ ] 6.4 **Manual verification** — Edit `WebApi/appsettings.json` while the app runs and change `RescuePolling:Interval` to `"00:00:30"`; observe the next "Rescue poll succeeded" log line arrives ~30s after the previous one instead of 5 min. Reverting to `"00:05:00"` takes effect on the next iteration. (Skipped automation: requires multi-minute wait windows and timing-sensitive log diffing.)
- [ ] 6.5 **Manual verification** — Edit `WebApi/appsettings.json` and set `UpstreamUrl` to `"https://invalid.example/"`; observe `Rescue poll failed` log entries, then `GET /api/rescue/latest` still returns HTTP 200 with the previously cached `meta.fetchedAt` and a non-null `meta.lastError`. Revert the URL afterwards. (Skipped automation: editing-and-reverting a real config file from inside a smoke test has unacceptable blast radius if a revert step is skipped.)
- [ ] 6.6 **Manual verification** — Cold-start the app and `GET /api/rescue/latest` before the first poll completes; expect HTTP 503 with `{ "error": "rescue data not yet available", "lastError": null, "lastErrorAt": null }`. In practice the upstream responded in ~80ms during the smoke test so this race is hard to hit without an artificial startup delay; the 503 code path is exercised by reading `RescueController.GetLatest` and inspecting the early-return branch. (Code-reviewed; runtime race not reproduced.)

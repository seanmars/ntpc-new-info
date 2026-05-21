using Microsoft.Extensions.Options;

using WebApi.Messaging;
using WebApi.Options;

namespace WebApi.Services;

public sealed class RescueRefreshCoordinator(
    RescueDataFetcher fetcher,
    IRescueSnapshotStore store,
    IMonitorPointEventDetector detector,
    IRescueAllAlertsDetector allAlertsDetector,
    IOptionsMonitor<RescuePollingOptions> options,
    IHostApplicationLifetime appLifetime,
    TimeProvider time,
    ILogger<RescueRefreshCoordinator> logger) : IRescueRefreshCoordinator
{
    private const int DefaultCooldownSeconds = 15;
    private const int MaxCooldownSeconds = 3600;
    // Bounded wait when a Manual refresh contends with an in-flight refresh.
    // Spec allows up to RequestTimeout; we pick something much shorter to keep UX snappy.
    private static readonly TimeSpan ManualGateWaitTimeout = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastSuccessAt = DateTimeOffset.MinValue;

    public async Task<RescueRefreshOutcome> RefreshAsync(RescueRefreshTrigger trigger, CancellationToken ct)
    {
        // Gate wait honors the caller's ct so a request that aborts releases its slot fast.
        if (trigger == RescueRefreshTrigger.Manual)
        {
            using var gateCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            gateCts.CancelAfter(ManualGateWaitTimeout);
            try
            {
                await _gate.WaitAsync(gateCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timed out waiting for another refresh to finish; advise client to retry shortly.
                return Throttled(TimeSpan.FromSeconds(5), "another refresh is in progress");
            }
        }
        else
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
        }

        try
        {
            if (trigger == RescueRefreshTrigger.Manual)
            {
                var cooldown = ResolveCooldown();
                var elapsed = time.GetUtcNow() - _lastSuccessAt;
                if (elapsed < cooldown)
                {
                    return Throttled(cooldown - elapsed, "manual refresh cooldown is active");
                }
            }

            // Fetch + snapshot + detectors run under ApplicationStopping so a user
            // navigating away mid-fetch does not abandon work other tabs need.
            return await ExecuteFetchAsync(trigger, appLifetime.ApplicationStopping).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RescueRefreshOutcome> ExecuteFetchAsync(RescueRefreshTrigger trigger, CancellationToken ct)
    {
        var url = options.CurrentValue.UpstreamUrl;
        var started = time.GetUtcNow();

        try
        {
            var data = await fetcher.FetchAsync(url, ct).ConfigureAwait(false);
            store.SetSuccess(data);
            var fetchedAt = store.Current.FetchedAt ?? time.GetUtcNow();
            _lastSuccessAt = fetchedAt;

            logger.LogInformation(
                "Rescue refresh ({Trigger}) succeeded from {Url} in {ElapsedMs} ms",
                trigger, url, (time.GetUtcNow() - started).TotalMilliseconds);

            await SafeDetectAsync(data, fetchedAt, ct).ConfigureAwait(false);

            return new RescueRefreshOutcome(
                Status: RescueRefreshStatus.Success,
                Data: data,
                FetchedAt: fetchedAt,
                ErrorMessage: null,
                ErrorAt: null,
                RetryAfter: null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            store.SetFailure(ex.Message);
            var current = store.Current;
            logger.LogError(ex, "Rescue refresh ({Trigger}) failed against {Url}", trigger, url);

            return new RescueRefreshOutcome(
                Status: RescueRefreshStatus.Failure,
                Data: null,
                FetchedAt: current.FetchedAt,
                ErrorMessage: current.LastError ?? ex.Message,
                ErrorAt: current.LastErrorAt,
                RetryAfter: null);
        }
    }

    private async Task SafeDetectAsync(System.Text.Json.Nodes.JsonNode data, DateTimeOffset fetchedAt, CancellationToken ct)
    {
        try
        {
            await detector.DetectAndPublishAsync(data, fetchedAt, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Monitor point event detection failed");
        }

        try
        {
            await allAlertsDetector.DetectAndPublishAsync(data, fetchedAt, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Rescue all-alerts detection failed");
        }
    }

    private TimeSpan ResolveCooldown()
    {
        var configured = options.CurrentValue.ForceRefreshCooldownSeconds;
        if (configured < 1 || configured > MaxCooldownSeconds)
        {
            logger.LogWarning(
                "RescuePolling:ForceRefreshCooldownSeconds value {Value} is out of range [1, {Max}]; falling back to {Default}",
                configured, MaxCooldownSeconds, DefaultCooldownSeconds);
            return TimeSpan.FromSeconds(DefaultCooldownSeconds);
        }
        return TimeSpan.FromSeconds(configured);
    }

    private static RescueRefreshOutcome Throttled(TimeSpan retryAfter, string reason)
    {
        return new RescueRefreshOutcome(
            Status: RescueRefreshStatus.Throttled,
            Data: null,
            FetchedAt: null,
            ErrorMessage: reason,
            ErrorAt: null,
            RetryAfter: retryAfter);
    }
}

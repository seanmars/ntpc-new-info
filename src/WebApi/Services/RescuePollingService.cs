using Microsoft.Extensions.Options;

using WebApi.Options;

namespace WebApi.Services;

public sealed class RescuePollingService(
    RescueDataFetcher fetcher,
    IRescueSnapshotStore store,
    IOptionsMonitor<RescuePollingOptions> options,
    ILogger<RescuePollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.CurrentValue.Interval;
        logger.LogInformation("RescuePollingService starting with interval {Interval}.", interval);

        await PollOnceAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await PollOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }

        logger.LogInformation("RescuePollingService stopped.");
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var url = options.CurrentValue.UpstreamUrl;
        var started = DateTimeOffset.UtcNow;

        try
        {
            var data = await fetcher.FetchAsync(url, ct);
            store.SetSuccess(data);

            logger.LogInformation(
                "Rescue poll succeeded from {Url} in {ElapsedMs} ms.",
                url, (DateTimeOffset.UtcNow - started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            store.SetFailure(ex.Message);
            logger.LogError(ex, "Rescue poll failed against {Url}.", url);
        }
    }
}
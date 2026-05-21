using Microsoft.Extensions.Options;

using WebApi.Options;

namespace WebApi.Services;

public sealed class RescuePollingService(
    IRescueRefreshCoordinator coordinator,
    IOptionsMonitor<RescuePollingOptions> options,
    ILogger<RescuePollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(options.CurrentValue.IntervalSeconds);
        logger.LogInformation("RescuePollingService starting with interval {Interval}", interval);

        await coordinator.RefreshAsync(RescueRefreshTrigger.Scheduled, stoppingToken);

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await coordinator.RefreshAsync(RescueRefreshTrigger.Scheduled, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }

        logger.LogInformation("RescuePollingService stopped");
    }
}

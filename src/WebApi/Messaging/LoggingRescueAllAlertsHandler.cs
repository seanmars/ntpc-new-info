using Rebus.Handlers;

namespace WebApi.Messaging;

public sealed class LoggingRescueAllAlertsHandler(
    ILogger<LoggingRescueAllAlertsHandler> logger) : IHandleMessages<RescueAllAlertsDetected>
{
    public Task Handle(RescueAllAlertsDetected message)
    {
        var newIds = message.NewAlerts.Select(a => a.FeatureId).ToArray();

        logger.LogInformation(
            "RescueAllAlertsDetected: NewCount={NewCount} TotalCount={TotalCount} DetectedAt={DetectedAt} SnapshotFetchedAt={SnapshotFetchedAt} NewFeatureIds={NewFeatureIds}",
            message.NewAlerts.Count,
            message.TotalAlertCount,
            message.DetectedAt,
            message.SnapshotFetchedAt,
            newIds);

        return Task.CompletedTask;
    }
}

using Rebus.Handlers;

namespace WebApi.Messaging;

public sealed class LoggingMonitorPointAlertHandler(
    ILogger<LoggingMonitorPointAlertHandler> logger) : IHandleMessages<MonitorPointEventDetected>
{
    public Task Handle(MonitorPointEventDetected message)
    {
        var featureIds = message.MatchedFeatures
            .Select(f => f.FeatureId ?? "(no-id)")
            .ToArray();

        logger.LogInformation(
            "MonitorPointEventDetected: MonitorPointId={MonitorPointId} MonitorPointName={MonitorPointName} MatchCount={MatchCount} DetectedAt={DetectedAt} SnapshotFetchedAt={SnapshotFetchedAt} FeatureIds={FeatureIds}",
            message.MonitorPointId,
            message.MonitorPointName,
            message.MatchedFeatures.Count,
            message.DetectedAt,
            message.SnapshotFetchedAt,
            featureIds);

        return Task.CompletedTask;
    }
}

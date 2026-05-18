using System.Text.Json.Nodes;

namespace WebApi.Messaging;

public sealed record RescueAllAlertsDetected(
    IReadOnlyList<NewAlert> NewAlerts,
    int TotalAlertCount,
    DateTimeOffset DetectedAt,
    DateTimeOffset SnapshotFetchedAt);

public sealed record NewAlert(string FeatureId, JsonNode? Properties);

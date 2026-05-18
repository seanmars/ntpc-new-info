using System.Text.Json.Nodes;

namespace WebApi.Messaging;

public interface IRescueAllAlertsDetector
{
    Task DetectAndPublishAsync(JsonNode snapshotData, DateTimeOffset snapshotFetchedAt, CancellationToken ct);
}

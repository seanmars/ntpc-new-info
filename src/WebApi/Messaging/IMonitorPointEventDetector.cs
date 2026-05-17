using System.Text.Json.Nodes;

namespace WebApi.Messaging;

public interface IMonitorPointEventDetector
{
    Task DetectAndPublishAsync(JsonNode snapshotData, DateTimeOffset snapshotFetchedAt, CancellationToken ct);
}

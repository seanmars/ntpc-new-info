using System.Text.Json.Nodes;

namespace WebApi.Messaging;

public sealed record MonitorPointEventDetected(
    string MonitorPointId,
    string MonitorPointName,
    double MonitorPointLatitude,
    double MonitorPointLongitude,
    int MonitorPointRadius,
    IReadOnlyList<MatchedFeature> MatchedFeatures,
    DateTimeOffset DetectedAt,
    DateTimeOffset SnapshotFetchedAt);

public sealed record MatchedFeature(
    string? FeatureId,
    double Latitude,
    double Longitude,
    double Distance,
    JsonNode? Properties);

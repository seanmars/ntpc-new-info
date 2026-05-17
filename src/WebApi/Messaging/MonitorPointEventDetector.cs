using System.Text.Json.Nodes;

using Rebus.Bus;

using WebApi.Models;
using WebApi.Services;

namespace WebApi.Messaging;

public sealed class MonitorPointEventDetector(
    IMonitorPointStore monitorPointStore,
    IBus bus,
    TimeProvider time,
    ILogger<MonitorPointEventDetector> logger) : IMonitorPointEventDetector
{
    public async Task DetectAndPublishAsync(JsonNode snapshotData, DateTimeOffset snapshotFetchedAt, CancellationToken ct)
    {
        var monitorPoints = await monitorPointStore.GetAllAsync(ct);
        if (monitorPoints.Count == 0)
        {
            return;
        }

        if (snapshotData["features"] is not JsonArray features)
        {
            logger.LogWarning(
                "Snapshot has no 'features' array; skipping detection. SnapshotFetchedAt={SnapshotFetchedAt}",
                snapshotFetchedAt);
            return;
        }

        var hitsByMonitorPoint = new Dictionary<string, List<MatchedFeature>>(monitorPoints.Count);
        var featureIndex = -1;

        foreach (var featureNode in features)
        {
            featureIndex++;
            if (featureNode is null)
            {
                continue;
            }

            var vertices = EnumerateVertices(featureNode["geometry"], featureIndex, snapshotFetchedAt);
            if (vertices.Count == 0)
            {
                continue;
            }

            var featureId = ExtractFeatureId(featureNode);
            var properties = featureNode["properties"];

            foreach (var mp in monitorPoints)
            {
                var (closestVertex, minDistance) = FindClosestVertex(mp, vertices);
                if (closestVertex is null || minDistance > mp.Radius)
                {
                    continue;
                }

                var matched = new MatchedFeature(
                    FeatureId: featureId,
                    Latitude: closestVertex.Value.Latitude,
                    Longitude: closestVertex.Value.Longitude,
                    Distance: minDistance,
                    Properties: properties);

                if (!hitsByMonitorPoint.TryGetValue(mp.Id, out var list))
                {
                    list = new List<MatchedFeature>();
                    hitsByMonitorPoint[mp.Id] = list;
                }
                list.Add(matched);
            }
        }

        if (hitsByMonitorPoint.Count == 0)
        {
            return;
        }

        var now = time.GetUtcNow();
        foreach (var mp in monitorPoints)
        {
            if (!hitsByMonitorPoint.TryGetValue(mp.Id, out var matched) || matched.Count == 0)
            {
                continue;
            }

            var message = new MonitorPointEventDetected(
                MonitorPointId: mp.Id,
                MonitorPointName: mp.Name,
                MonitorPointLatitude: mp.Latitude,
                MonitorPointLongitude: mp.Longitude,
                MonitorPointRadius: mp.Radius,
                MatchedFeatures: matched,
                DetectedAt: now,
                SnapshotFetchedAt: snapshotFetchedAt);

            try
            {
                await bus.Publish(message);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to publish MonitorPointEventDetected for MonitorPointId={MonitorPointId}.",
                    mp.Id);
            }
        }
    }

    private (Vertex? Vertex, double Distance) FindClosestVertex(MonitorPoint mp, IReadOnlyList<Vertex> vertices)
    {
        Vertex? closest = null;
        var min = double.MaxValue;
        foreach (var v in vertices)
        {
            var d = GeoDistance.HaversineMeters(mp.Latitude, mp.Longitude, v.Latitude, v.Longitude);
            if (d < min)
            {
                min = d;
                closest = v;
            }
        }
        return (closest, min);
    }

    private List<Vertex> EnumerateVertices(JsonNode? geometry, int featureIndex, DateTimeOffset snapshotFetchedAt)
    {
        var vertices = new List<Vertex>();
        if (geometry is null)
        {
            return vertices;
        }

        var type = geometry["type"]?.GetValue<string>();
        var coords = geometry["coordinates"];
        if (string.IsNullOrEmpty(type) || coords is null)
        {
            logger.LogWarning(
                "Feature at index {FeatureIndex} has unparseable geometry (type={Type}); skipping. SnapshotFetchedAt={SnapshotFetchedAt}",
                featureIndex, type, snapshotFetchedAt);
            return vertices;
        }

        try
        {
            switch (type)
            {
                case "Point":
                    if (TryReadPosition(coords, out var p))
                    {
                        vertices.Add(p);
                    }
                    break;

                case "MultiPoint":
                case "LineString":
                    AppendPositions(coords as JsonArray, vertices);
                    break;

                case "MultiLineString":
                case "Polygon":
                    foreach (var inner in (coords as JsonArray) ?? new JsonArray())
                    {
                        AppendPositions(inner as JsonArray, vertices);
                    }
                    break;

                case "MultiPolygon":
                    foreach (var polygon in (coords as JsonArray) ?? new JsonArray())
                    {
                        foreach (var ring in (polygon as JsonArray) ?? new JsonArray())
                        {
                            AppendPositions(ring as JsonArray, vertices);
                        }
                    }
                    break;

                default:
                    logger.LogWarning(
                        "Feature at index {FeatureIndex} has unsupported geometry type '{Type}'; skipping. SnapshotFetchedAt={SnapshotFetchedAt}",
                        featureIndex, type, snapshotFetchedAt);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Feature at index {FeatureIndex} threw while reading coordinates (type={Type}); skipping. SnapshotFetchedAt={SnapshotFetchedAt}",
                featureIndex, type, snapshotFetchedAt);
            return new List<Vertex>();
        }

        return vertices;
    }

    private static void AppendPositions(JsonArray? positions, List<Vertex> vertices)
    {
        if (positions is null)
        {
            return;
        }
        foreach (var pos in positions)
        {
            if (TryReadPosition(pos, out var v))
            {
                vertices.Add(v);
            }
        }
    }

    private static bool TryReadPosition(JsonNode? node, out Vertex vertex)
    {
        vertex = default;
        if (node is not JsonArray arr || arr.Count < 2)
        {
            return false;
        }
        try
        {
            // GeoJSON order: [longitude, latitude].
            var lon = arr[0]!.GetValue<double>();
            var lat = arr[1]!.GetValue<double>();
            vertex = new Vertex(lat, lon);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractFeatureId(JsonNode featureNode)
    {
        var topId = featureNode["id"];
        if (topId is not null)
        {
            return topId.ToString();
        }
        var propId = featureNode["properties"]?["id"];
        return propId?.ToString();
    }

    private readonly record struct Vertex(double Latitude, double Longitude);
}

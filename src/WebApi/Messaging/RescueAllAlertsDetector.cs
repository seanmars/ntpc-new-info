using System.Text.Json.Nodes;

using Rebus.Bus;

using WebApi.Discord;

namespace WebApi.Messaging;

public sealed class RescueAllAlertsDetector(
    IDiscordSettingsStore settingsStore,
    IBus bus,
    TimeProvider time,
    ILogger<RescueAllAlertsDetector> logger) : IRescueAllAlertsDetector
{
    private readonly object _seenGate = new();
    private HashSet<string> _seenFeatureIds = new(StringComparer.Ordinal);

    public async Task DetectAndPublishAsync(JsonNode snapshotData, DateTimeOffset snapshotFetchedAt, CancellationToken ct)
    {
        var settings = await settingsStore.GetAsync(ct);
        if (!settings.Enabled || !settings.NotifyAllAlerts)
        {
            lock (_seenGate)
            {
                if (_seenFeatureIds.Count > 0)
                {
                    _seenFeatureIds = new HashSet<string>(StringComparer.Ordinal);
                }
            }
            return;
        }

        if (snapshotData["features"] is not JsonArray features)
        {
            logger.LogWarning(
                "RescueAllAlertsDetector: snapshot has no 'features' array; skipping. SnapshotFetchedAt={SnapshotFetchedAt}",
                snapshotFetchedAt);
            return;
        }

        var currentIds = new HashSet<string>(StringComparer.Ordinal);
        var newAlerts = new List<NewAlert>();
        var skippedNoId = 0;

        var featureIndex = -1;
        foreach (var featureNode in features)
        {
            featureIndex++;
            if (featureNode is null)
            {
                continue;
            }

            var featureId = ExtractFeatureId(featureNode);
            if (string.IsNullOrEmpty(featureId))
            {
                skippedNoId++;
                continue;
            }

            currentIds.Add(featureId);

            bool isNew;
            lock (_seenGate)
            {
                isNew = !_seenFeatureIds.Contains(featureId);
            }
            if (isNew)
            {
                newAlerts.Add(new NewAlert(featureId, featureNode["properties"]));
            }
        }

        lock (_seenGate)
        {
            _seenFeatureIds = currentIds;
        }

        logger.LogInformation(
            "RescueAllAlertsDetector: scanned {Total} features (new={New}, skipped-no-id={Skipped}, current-ids={CurrentIds})",
            featureIndex + 1, newAlerts.Count, skippedNoId, currentIds.Count);

        if (newAlerts.Count == 0)
        {
            return;
        }

        var message = new RescueAllAlertsDetected(
            NewAlerts: newAlerts,
            TotalAlertCount: currentIds.Count,
            DetectedAt: time.GetUtcNow(),
            SnapshotFetchedAt: snapshotFetchedAt);

        try
        {
            await bus.Publish(message);
            logger.LogInformation(
                "RescueAllAlertsDetector: published {NewCount} new alerts (total {TotalCount})",
                newAlerts.Count, currentIds.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "RescueAllAlertsDetector: failed to publish RescueAllAlertsDetected with {NewCount} new alerts",
                newAlerts.Count);
        }
    }

    private static string? ExtractFeatureId(JsonNode featureNode)
    {
        var topId = featureNode["id"];
        if (topId is not null)
        {
            var s = topId.ToString();
            if (!string.IsNullOrEmpty(s)) return s;
        }

        var props = featureNode["properties"];
        if (props is null) return null;

        foreach (var key in IdKeys)
        {
            var node = props[key];
            if (node is null) continue;
            var s = node.ToString();
            if (!string.IsNullOrEmpty(s)) return s;
        }
        return null;
    }

    private static readonly string[] IdKeys = { "featureId", "id", "caseId", "objectId", "OBJECTID" };
}

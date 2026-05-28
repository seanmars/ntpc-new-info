using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

using Rebus.Handlers;

using WebApi.Messaging;

namespace WebApi.Discord;

public sealed class DiscordMonitorPointAlertHandler(
    IDiscordNotifier notifier,
    IDiscordSettingsStore settingsStore,
    ILogger<DiscordMonitorPointAlertHandler> logger) : IHandleMessages<MonitorPointEventDetected>
{
    private const int FeatureLineCap = 5;
    private const int VehicleNameCap = 5;

    // Discord hard limit is 2000 chars; stay below it with room for the trailing summary line.
    private const int MessageSoftCap = 1800;

    public async Task Handle(MonitorPointEventDetected message)
    {
        var content = FormatMessage(message);

        try
        {
            await notifier.SendAsync(content);
        }
        catch (Exception ex)
        {
            ulong channelId = 0UL;
            try
            {
                var snapshot = await settingsStore.GetAsync(CancellationToken.None);
                channelId = snapshot.ChannelId;
            }
            catch
            {
                // best-effort lookup for log context; ignore
            }

            logger.LogWarning(
                ex,
                "DiscordMonitorPointAlertHandler: failed to send Discord notification for monitor point {MonitorPointId} to channel {ChannelId}; dropping message",
                message.MonitorPointId,
                channelId);
        }
    }

    private static string FormatMessage(MonitorPointEventDetected message)
    {
        var sb = new StringBuilder();
        sb.Append("**").Append(message.MonitorPointName).Append("** (追蹤範圍 ")
          .Append(message.MonitorPointRadius)
          .Append("m)\n");
        sb.Append("命中 ")
          .Append(message.MatchedFeatures.Count)
          .Append(" 筆 @ ")
          .Append(message.DetectedAt.ToString("o", CultureInfo.InvariantCulture))
          .Append('\n');

        var emitted = 0;
        foreach (var f in message.MatchedFeatures)
        {
            if (emitted >= FeatureLineCap) break;

            var block = FormatFeature(f, emitted + 1);

            // Always emit at least one feature; otherwise stop before crossing the soft cap.
            if (emitted > 0 && sb.Length + block.Length > MessageSoftCap)
            {
                break;
            }

            sb.Append(block);
            emitted++;
        }

        var remaining = message.MatchedFeatures.Count - emitted;
        if (remaining > 0)
        {
            sb.Append("...and ").Append(remaining).Append(" more\n");
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static string FormatFeature(MatchedFeature f, int index)
    {
        var sb = new StringBuilder();

        var disasterType = GetDisasterType(f.Properties) ?? "救援事件";
        sb.Append("\n[").Append(index).Append("] ").Append(disasterType).Append('\n');

        var location = GetString(f.Properties, "endPointInfo");
        if (location is not null)
        {
            sb.Append("地點: ").Append(location).Append('\n');
        }

        sb.Append("事件編號: ");
        if (!string.IsNullOrEmpty(f.FeatureId))
        {
            sb.Append(f.FeatureId);
        }
        else
        {
            sb.Append(f.Latitude.ToString("0.######", CultureInfo.InvariantCulture))
              .Append(',')
              .Append(f.Longitude.ToString("0.######", CultureInfo.InvariantCulture));
        }
        sb.Append(" (距離 ")
          .Append((int)Math.Round(f.Distance))
          .Append("m)\n");

        var type = GetString(f.Properties, "type");
        var dataSource = GetString(f.Properties, "dataSource");
        if (type is not null || dataSource is not null)
        {
            var parts = new List<string>(2);
            if (type is not null) parts.Add("類型: " + type);
            if (dataSource is not null) parts.Add("來源: " + dataSource);
            sb.Append(string.Join(" | ", parts)).Append('\n');
        }

        var (total, names) = SummarizeVehicles(f.Properties);
        if (total > 0)
        {
            sb.Append("出勤車輛 (").Append(total).Append(')');
            if (names.Count > 0)
            {
                sb.Append(": ").Append(string.Join(", ", names));
                var remaining = total - names.Count;
                if (remaining > 0)
                {
                    sb.Append(" ...其他 ").Append(remaining).Append(" 車");
                }
            }
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string? GetDisasterType(JsonNode? properties)
        => GetString(properties, "fireType") ?? GetString(properties, "title");

    private static string? GetString(JsonNode? properties, string key)
    {
        var node = properties?[key];
        if (node is null) return null;
        var text = node.ToString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static (int Total, IReadOnlyList<string> Names) SummarizeVehicles(JsonNode? properties)
    {
        if (properties?["caseList"] is not JsonArray caseList)
        {
            return (0, Array.Empty<string>());
        }

        var names = new List<string>(VehicleNameCap);
        foreach (var item in caseList)
        {
            if (names.Count >= VehicleNameCap) break;
            var name = GetString(item, "startPointInfo");
            if (name is not null) names.Add(name);
        }

        return (caseList.Count, names);
    }
}

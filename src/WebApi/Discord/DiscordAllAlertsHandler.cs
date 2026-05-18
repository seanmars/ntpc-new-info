using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

using Rebus.Handlers;

using WebApi.Messaging;

namespace WebApi.Discord;

public sealed class DiscordAllAlertsHandler(
    IDiscordNotifier notifier,
    IDiscordSettingsStore settingsStore,
    ILogger<DiscordAllAlertsHandler> logger) : IHandleMessages<RescueAllAlertsDetected>
{
    private const int AlertLineCap = 10;

    public async Task Handle(RescueAllAlertsDetected message)
    {
        var snapshot = await settingsStore.GetAsync(CancellationToken.None);
        if (!snapshot.Enabled || !snapshot.NotifyAllAlerts)
        {
            // Settings flipped while message was in flight; drop silently.
            return;
        }

        var content = FormatMessage(message);

        try
        {
            await notifier.SendAsync(content);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "DiscordAllAlertsHandler: failed to send Discord notification with {NewCount} new alerts to channel {ChannelId}; dropping message",
                message.NewAlerts.Count,
                snapshot.ChannelId);
        }
    }

    private static string FormatMessage(RescueAllAlertsDetected message)
    {
        var sb = new StringBuilder();
        sb.Append("**全災訊通知**\n");
        sb.Append("新增 ")
          .Append(message.NewAlerts.Count)
          .Append(" 筆 / 目前共 ")
          .Append(message.TotalAlertCount)
          .Append(" 筆 @ ")
          .Append(message.DetectedAt.ToString("o", CultureInfo.InvariantCulture))
          .Append('\n');

        var emitted = 0;
        foreach (var alert in message.NewAlerts)
        {
            if (emitted >= AlertLineCap) break;
            sb.Append("- ").Append(alert.FeatureId);

            var label = ExtractLabel(alert.Properties);
            if (!string.IsNullOrEmpty(label))
            {
                sb.Append(": ").Append(label);
            }
            sb.Append('\n');
            emitted++;
        }

        var remaining = message.NewAlerts.Count - emitted;
        if (remaining > 0)
        {
            sb.Append("...and ").Append(remaining).Append(" more\n");
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static string? ExtractLabel(JsonNode? properties)
    {
        if (properties is null) return null;

        var head = PickFirst(properties, HeadKeys);
        var addr = PickFirst(properties, AddressKeys);

        string? label = head;
        if (!string.IsNullOrWhiteSpace(addr))
        {
            label = string.IsNullOrWhiteSpace(label) ? addr : $"{label} @ {addr}";
        }

        if (string.IsNullOrWhiteSpace(label)) return null;
        return label.Length > 100 ? label[..100] + "..." : label;
    }

    private static string? PickFirst(JsonNode properties, string[] keys)
    {
        foreach (var key in keys)
        {
            var node = properties[key];
            if (node is null) continue;
            var text = node.ToString();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return null;
    }

    private static readonly string[] HeadKeys =
    {
        "fireType", "title", "layerName", "type", "name",
    };

    private static readonly string[] AddressKeys =
    {
        "endPointInfo", "address", "location",
    };
}

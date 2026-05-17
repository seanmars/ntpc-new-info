using System.Globalization;
using System.Text;

using Rebus.Handlers;

using WebApi.Messaging;

namespace WebApi.Discord;

public sealed class DiscordMonitorPointAlertHandler(
    IDiscordNotifier notifier,
    IDiscordSettingsStore settingsStore,
    ILogger<DiscordMonitorPointAlertHandler> logger) : IHandleMessages<MonitorPointEventDetected>
{
    private const int FeatureLineCap = 5;

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
                "DiscordMonitorPointAlertHandler: failed to send Discord notification for monitor point {MonitorPointId} to channel {ChannelId}; dropping message.",
                message.MonitorPointId,
                channelId);
        }
    }

    private static string FormatMessage(MonitorPointEventDetected message)
    {
        var sb = new StringBuilder();
        sb.Append("**").Append(message.MonitorPointName).Append("**\n");
        sb.Append("Matched: ")
            .Append(message.MatchedFeatures.Count)
            .Append(" @ ")
            .Append(message.DetectedAt.ToString("o", CultureInfo.InvariantCulture))
            .Append('\n');

        var emitted = 0;
        foreach (var f in message.MatchedFeatures)
        {
            if (emitted >= FeatureLineCap) break;
            sb.Append("- ");
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
            sb.Append(" (")
              .Append(((int)Math.Round(f.Distance)).ToString(CultureInfo.InvariantCulture))
              .Append("m)\n");
            emitted++;
        }

        var remaining = message.MatchedFeatures.Count - emitted;
        if (remaining > 0)
        {
            sb.Append("...and ").Append(remaining).Append(" more\n");
        }

        return sb.ToString().TrimEnd('\n');
    }
}

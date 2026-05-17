using Discord;

namespace WebApi.Discord;

public sealed class DiscordRestNotifier(
    DiscordBotState state,
    IDiscordSettingsStore store,
    ILogger<DiscordRestNotifier> logger) : IDiscordNotifier
{
    public async Task SendAsync(string content, ulong? channelId = null, CancellationToken cancellationToken = default)
    {
        var snapshot = await store.GetAsync(cancellationToken);
        if (!snapshot.Enabled)
        {
            return;
        }

        var targetChannel = channelId ?? snapshot.ChannelId;
        if (targetChannel == 0UL)
        {
            logger.LogWarning("DiscordRestNotifier: dropped message; no channel id available (request channel id=null, store channel id=0).");
            return;
        }

        if (!state.IsReady || state.Client is null)
        {
            logger.LogWarning("DiscordRestNotifier: dropped message to channel {ChannelId}; bot not ready.", targetChannel);
            return;
        }

        var rawChannel = await state.Client.GetChannelAsync(targetChannel, options: null);
        if (rawChannel is not IMessageChannel messageChannel)
        {
            logger.LogWarning(
                "DiscordRestNotifier: channel {ChannelId} is not a message channel (resolved type={ChannelType}); dropped message.",
                targetChannel, rawChannel?.GetType().Name ?? "<null>");
            return;
        }

        await messageChannel.SendMessageAsync(content, options: null);
    }
}

namespace WebApi.Discord;

public interface IDiscordNotifier
{
    Task SendAsync(string content, ulong? channelId = null, CancellationToken cancellationToken = default);
}

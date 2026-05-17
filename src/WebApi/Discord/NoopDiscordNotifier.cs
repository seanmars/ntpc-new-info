namespace WebApi.Discord;

public sealed class NoopDiscordNotifier : IDiscordNotifier
{
    public Task SendAsync(string content, ulong? channelId = null, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

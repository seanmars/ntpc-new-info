using Discord.Rest;

namespace WebApi.Discord;

public sealed class DiscordBotState : IDisposable
{
    private volatile bool _isReady;

    public bool IsReady
    {
        get => _isReady;
        set => _isReady = value;
    }

    public DiscordRestClient? Client { get; set; }

    public string? CurrentLoggedInToken { get; set; }

    public SemaphoreSlim ReconfigGate { get; } = new(1, 1);

    public void Dispose()
    {
        ReconfigGate.Dispose();
        Client?.Dispose();
        Client = null;
    }
}

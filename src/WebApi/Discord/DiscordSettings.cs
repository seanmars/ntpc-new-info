namespace WebApi.Discord;

public sealed record DiscordSettings(bool Enabled, string BotToken, ulong ChannelId)
{
    public static DiscordSettings Default { get; } = new(false, string.Empty, 0UL);
}

public sealed record DiscordSettingsSnapshot(bool Enabled, string BotToken, ulong ChannelId)
{
    public bool HasToken => !string.IsNullOrEmpty(BotToken);

    public static DiscordSettingsSnapshot From(DiscordSettings settings)
        => new(settings.Enabled, settings.BotToken, settings.ChannelId);
}

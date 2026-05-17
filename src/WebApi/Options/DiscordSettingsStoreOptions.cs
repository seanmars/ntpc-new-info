namespace WebApi.Options;

public sealed class DiscordSettingsStoreOptions
{
    public const string SectionName = "DiscordSettingsStore";

    public string FilePath { get; set; } = "data/discord-settings.json";
}

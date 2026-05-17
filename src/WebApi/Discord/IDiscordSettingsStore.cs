namespace WebApi.Discord;

public interface IDiscordSettingsStore
{
    Task<DiscordSettingsSnapshot> GetAsync(CancellationToken ct);

    Task<DiscordSettingsSnapshot> UpdateAsync(DiscordSettings settings, CancellationToken ct);

    Task<DiscordSettingsSnapshot> ResetAsync(CancellationToken ct);

    event EventHandler<DiscordSettingsSnapshot>? SettingsChanged;
}

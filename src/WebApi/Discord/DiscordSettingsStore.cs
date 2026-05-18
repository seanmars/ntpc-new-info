using System.Text.Json;

using Microsoft.Extensions.Options;

using WebApi.Options;

namespace WebApi.Discord;

public sealed class DiscordSettingsStore : IDiscordSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IHostEnvironment _env;
    private readonly ILogger<DiscordSettingsStore> _logger;
    private readonly DiscordSettingsStoreOptions _options;
    private DiscordSettings _settings = DiscordSettings.Default;

    public event EventHandler<DiscordSettingsSnapshot>? SettingsChanged;

    public DiscordSettingsStore(
        IOptions<DiscordSettingsStoreOptions> options,
        IHostEnvironment env,
        ILogger<DiscordSettingsStore> logger)
    {
        _options = options.Value;
        _env = env;
        _logger = logger;

        LoadFromDiskSync();
    }

    public async Task<DiscordSettingsSnapshot> GetAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return DiscordSettingsSnapshot.From(_settings);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<DiscordSettingsSnapshot> UpdateAsync(DiscordSettings settings, CancellationToken ct)
    {
        DiscordSettingsSnapshot snapshot;
        bool changed;

        await _gate.WaitAsync(ct);
        try
        {
            changed = !_settings.Equals(settings);
            _settings = settings;
            await PersistLockedAsync(ct);
            snapshot = DiscordSettingsSnapshot.From(_settings);
        }
        finally
        {
            _gate.Release();
        }

        if (changed)
        {
            SettingsChanged?.Invoke(this, snapshot);
        }

        return snapshot;
    }

    public async Task<DiscordSettingsSnapshot> ResetAsync(CancellationToken ct)
    {
        DiscordSettingsSnapshot snapshot;
        bool changed;

        await _gate.WaitAsync(ct);
        try
        {
            changed = !_settings.Equals(DiscordSettings.Default);
            _settings = DiscordSettings.Default;
            DeleteFileLocked();
            snapshot = DiscordSettingsSnapshot.From(_settings);
        }
        finally
        {
            _gate.Release();
        }

        if (changed)
        {
            SettingsChanged?.Invoke(this, snapshot);
        }

        return snapshot;
    }

    private void DeleteFileLocked()
    {
        var path = ResolveFilePath();
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DiscordSettingsStore: failed to delete '{Path}' during reset; in-memory state is now default", path);
        }
    }

    private string ResolveFilePath()
    {
        var configured = _options.FilePath;
        return Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(_env.ContentRootPath, configured);
    }

    private void LoadFromDiskSync()
    {
        var path = ResolveFilePath();
        if (!File.Exists(path))
        {
            _logger.LogInformation("DiscordSettingsStore: file '{Path}' not found; starting with default disabled settings", path);
            return;
        }

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DiscordSettingsStore: failed to read '{Path}'; starting with default disabled settings", path);
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<DiscordSettings>(content, JsonOptions);
            if (parsed is not null)
            {
                _settings = parsed;
                _logger.LogInformation(
                    "DiscordSettingsStore: loaded settings from '{Path}' (enabled={Enabled}, hasToken={HasToken}, channelId={ChannelId})",
                    path, _settings.Enabled, !string.IsNullOrEmpty(_settings.BotToken), _settings.ChannelId);
            }
        }
        catch (JsonException ex)
        {
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
            var quarantine = $"{path}.corrupt-{stamp}";
            try
            {
                File.Move(path, quarantine, overwrite: false);
                _logger.LogError(
                    ex,
                    "DiscordSettingsStore: '{Path}' is not valid JSON; quarantined to '{Quarantine}'. Starting with default disabled settings",
                    path, quarantine);
            }
            catch (Exception moveEx)
            {
                _logger.LogError(
                    moveEx,
                    "DiscordSettingsStore: '{Path}' is not valid JSON and could not be quarantined. Starting with default disabled settings",
                    path);
            }
        }
    }

    private async Task PersistLockedAsync(CancellationToken ct)
    {
        var path = ResolveFilePath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = JsonSerializer.Serialize(_settings, JsonOptions);
        var temp = $"{path}.tmp";

        await File.WriteAllTextAsync(temp, payload, ct);

        try
        {
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch { /* ignore */ }
            throw;
        }
    }
}

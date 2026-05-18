using System.Text.Json;

using Microsoft.Extensions.Options;

using WebApi.Models;
using WebApi.Options;

namespace WebApi.Services;

public sealed class MonitorPointStore : IMonitorPointStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IHostEnvironment _env;
    private readonly TimeProvider _time;
    private readonly ILogger<MonitorPointStore> _logger;
    private readonly MonitorPointStoreOptions _options;
    private readonly List<MonitorPoint> _points = new();

    public MonitorPointStore(
        IOptions<MonitorPointStoreOptions> options,
        IHostEnvironment env,
        TimeProvider time,
        ILogger<MonitorPointStore> logger)
    {
        _options = options.Value;
        _env = env;
        _time = time;
        _logger = logger;

        LoadFromDiskSync();
    }

    public async Task<IReadOnlyList<MonitorPoint>> GetAllAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return _points
                .OrderBy(p => p.CreatedAt)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MonitorPoint> AddAsync(string name, double latitude, double longitude, int radius, CancellationToken ct)
    {
        var point = new MonitorPoint(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            Latitude: latitude,
            Longitude: longitude,
            CreatedAt: _time.GetUtcNow(),
            Radius: radius);

        await _gate.WaitAsync(ct);
        try
        {
            _points.Add(point);
            await PersistLockedAsync(ct);
            return point;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<MonitorPoint?> UpdateAsync(string id, string name, double latitude, double longitude, int radius, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var index = _points.FindIndex(p => p.Id == id);
            if (index < 0)
            {
                return null;
            }
            var existing = _points[index];
            var updated = existing with
            {
                Name = name,
                Latitude = latitude,
                Longitude = longitude,
                Radius = radius,
            };
            _points[index] = updated;
            await PersistLockedAsync(ct);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var index = _points.FindIndex(p => p.Id == id);
            if (index < 0)
            {
                return false;
            }
            _points.RemoveAt(index);
            await PersistLockedAsync(ct);
            return true;
        }
        finally
        {
            _gate.Release();
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
            _logger.LogInformation("MonitorPointStore: file '{Path}' not found; starting with empty list", path);
            return;
        }

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MonitorPointStore: failed to read '{Path}'; starting with empty list", path);
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<MonitorPoint>>(content, JsonOptions);
            if (parsed is not null)
            {
                _points.AddRange(parsed);
                _logger.LogInformation(
                    "MonitorPointStore: loaded {Count} monitor point(s) from '{Path}'",
                    _points.Count, path);
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
                    "MonitorPointStore: '{Path}' is not valid JSON; quarantined to '{Quarantine}'. Starting with empty list",
                    path, quarantine);
            }
            catch (Exception moveEx)
            {
                _logger.LogError(
                    moveEx,
                    "MonitorPointStore: '{Path}' is not valid JSON and could not be quarantined. Starting with empty list",
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

        var payload = JsonSerializer.Serialize(_points, JsonOptions);
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

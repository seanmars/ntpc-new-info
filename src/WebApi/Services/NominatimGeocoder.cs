using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Options;

using WebApi.Options;

namespace WebApi.Services;

public sealed record GeocodeResult(string DisplayName, double Latitude, double Longitude);

public sealed class NominatimGeocoder
{
    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly NominatimOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<NominatimGeocoder> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastSentAt = DateTimeOffset.MinValue;

    public NominatimGeocoder(
        HttpClient httpClient,
        IOptions<NominatimOptions> options,
        TimeProvider time,
        ILogger<NominatimGeocoder> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GeocodeResult>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("query is required", nameof(query));
        }

        var clampedLimit = Math.Clamp(limit, 1, 10);

        await ThrottleAsync(ct);

        var encodedQuery = Uri.EscapeDataString(query);
        var path = $"search?q={encodedQuery}&format=json&limit={clampedLimit}&accept-language=zh-TW";

        using var response = await _httpClient.GetAsync(path, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await SafeReadAsync(response, ct);
            _logger.LogWarning(
                "Nominatim returned non-success {Status}: {Body}",
                (int)response.StatusCode, body);
            throw new InvalidOperationException(
                $"Nominatim returned HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        List<NominatimItem>? items;
        try
        {
            items = await JsonSerializer.DeserializeAsync<List<NominatimItem>>(stream, DeserializeOptions, ct);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Nominatim returned a non-JSON body.", ex);
        }

        if (items is null)
        {
            return Array.Empty<GeocodeResult>();
        }

        var results = new List<GeocodeResult>(items.Count);
        foreach (var item in items)
        {
            if (item.DisplayName is null || item.Lat is null || item.Lon is null) continue;
            if (!double.TryParse(item.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) continue;
            if (!double.TryParse(item.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)) continue;

            results.Add(new GeocodeResult(item.DisplayName, lat, lng));
        }

        return results;
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = _time.GetUtcNow();
            var elapsed = now - _lastSentAt;
            var minInterval = TimeSpan.FromMilliseconds(_options.MinIntervalMs);
            if (elapsed < minInterval)
            {
                await Task.Delay(minInterval - elapsed, ct);
            }
            _lastSentAt = _time.GetUtcNow();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed record NominatimItem(
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("lat")] string? Lat,
        [property: JsonPropertyName("lon")] string? Lon);
}

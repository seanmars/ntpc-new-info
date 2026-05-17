using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WebApi.Services;

public sealed class RescueDataFetcher(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions EnvelopeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<JsonNode> FetchAsync(string url, CancellationToken ct)
    {
        using var response = await httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        Envelope? envelope;
        try
        {
            envelope = await JsonSerializer.DeserializeAsync<Envelope>(stream, EnvelopeOptions, ct);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Upstream returned a non-JSON body.", ex);
        }

        if (envelope is null)
        {
            throw new InvalidDataException("Upstream returned a null JSON body.");
        }

        if (envelope.Status is int status and not 200)
        {
            throw new InvalidDataException(
                $"Upstream returned non-success status {status} ({envelope.Message ?? "no message"}).");
        }

        if (string.IsNullOrEmpty(envelope.Data))
        {
            throw new InvalidDataException("Upstream 'data' field was missing or empty.");
        }

        // Upstream wraps the GeoJSON in {status, message, data: "<escaped JSON>"}.
        // Parse the inner string so consumers see real JSON, not a string.
        try
        {
            return JsonNode.Parse(envelope.Data)
                ?? throw new InvalidDataException("Upstream 'data' parsed to a null JSON node.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Inner upstream 'data' string is not valid JSON ({envelope.Data.Length} chars).", ex);
        }
    }

    private sealed record Envelope(
        [property: JsonPropertyName("status")] int? Status,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("data")] string? Data);
}

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WebApi.Services;

public sealed class RescueDataFetcher(HttpClient httpClient, ILogger<RescueDataFetcher> logger)
{
    private const int MaxUnwrapDepth = 5;

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
        // Sometimes 'data' is double-escaped (a JSON string whose value is itself
        // another JSON-encoded string). Unwrap until we reach a real object/array.
        JsonNode node;
        try
        {
            node = JsonNode.Parse(envelope.Data)
                ?? throw new InvalidDataException("Upstream 'data' parsed to a null JSON node.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Upstream 'data' string is not valid JSON ({envelope.Data.Length} chars).", ex);
        }

        var unwraps = 0;
        while (node is JsonValue value && value.GetValueKind() == JsonValueKind.String)
        {
            var inner = value.GetValue<string>();
            if (string.IsNullOrEmpty(inner))
            {
                throw new InvalidDataException(
                    $"Upstream 'data' unwrapped to an empty string at depth {unwraps + 1}.");
            }

            if (++unwraps > MaxUnwrapDepth)
            {
                throw new InvalidDataException(
                    $"Upstream 'data' is nested deeper than {MaxUnwrapDepth} escaped layers.");
            }

            try
            {
                node = JsonNode.Parse(inner)
                    ?? throw new InvalidDataException(
                        $"Upstream 'data' inner string parsed to null at depth {unwraps}.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"Upstream 'data' inner string at depth {unwraps} is not valid JSON ({inner.Length} chars).", ex);
            }
        }

        if (unwraps > 0)
        {
            logger.LogInformation(
                "RescueDataFetcher: upstream 'data' was escaped {Depth} extra level(s); final root is {Kind}.",
                unwraps, node.GetValueKind());
        }

        return node;
    }

    private sealed record Envelope(
        [property: JsonPropertyName("status")] int? Status,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("data")] string? Data);
}
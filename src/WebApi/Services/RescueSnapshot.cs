using System.Text.Json.Nodes;

namespace WebApi.Services;

public sealed record RescueSnapshot(
    JsonNode? Data,
    DateTimeOffset? FetchedAt,
    string? LastError,
    DateTimeOffset? LastErrorAt)
{
    public static readonly RescueSnapshot Empty = new(Data: null, FetchedAt: null, LastError: null, LastErrorAt: null);
}
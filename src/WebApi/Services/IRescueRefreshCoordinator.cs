using System.Text.Json.Nodes;

namespace WebApi.Services;

public enum RescueRefreshTrigger
{
    Scheduled,
    Manual
}

public enum RescueRefreshStatus
{
    Success,
    Failure,
    Throttled
}

public sealed record RescueRefreshOutcome(
    RescueRefreshStatus Status,
    JsonNode? Data,
    DateTimeOffset? FetchedAt,
    string? ErrorMessage,
    DateTimeOffset? ErrorAt,
    TimeSpan? RetryAfter);

public interface IRescueRefreshCoordinator
{
    Task<RescueRefreshOutcome> RefreshAsync(RescueRefreshTrigger trigger, CancellationToken ct);
}

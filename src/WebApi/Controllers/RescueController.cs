using Microsoft.AspNetCore.Mvc;

using WebApi.Services;

namespace WebApi.Controllers;

[ApiController]
[Route("api/rescue")]
public sealed class RescueController(
    IRescueSnapshotStore store,
    IRescueRefreshCoordinator coordinator) : ControllerBase
{
    [HttpGet("latest")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult GetLatest()
    {
        var snapshot = store.Current;

        if (snapshot.Data is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                error = "rescue data not yet available",
                lastError = snapshot.LastError,
                lastErrorAt = snapshot.LastErrorAt
            });
        }

        return new JsonResult(new
        {
            data = snapshot.Data,
            meta = new
            {
                fetchedAt = snapshot.FetchedAt,
                lastError = snapshot.LastError,
                lastErrorAt = snapshot.LastErrorAt
            }
        });
    }

    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ForceRefresh(CancellationToken ct)
    {
        var outcome = await coordinator.RefreshAsync(RescueRefreshTrigger.Manual, ct);

        switch (outcome.Status)
        {
            case RescueRefreshStatus.Success:
                return new JsonResult(new
                {
                    data = outcome.Data,
                    meta = new
                    {
                        fetchedAt = outcome.FetchedAt,
                        lastError = (string?)null,
                        lastErrorAt = (DateTimeOffset?)null
                    }
                });

            case RescueRefreshStatus.Throttled:
                var retrySeconds = Math.Max(1, (int)Math.Ceiling((outcome.RetryAfter ?? TimeSpan.FromSeconds(1)).TotalSeconds));
                Response.Headers["Retry-After"] = retrySeconds.ToString();
                return StatusCode(StatusCodes.Status429TooManyRequests, new
                {
                    error = outcome.ErrorMessage ?? "manual refresh throttled",
                    retryAfterSeconds = retrySeconds
                });

            case RescueRefreshStatus.Failure:
            default:
                return StatusCode(StatusCodes.Status502BadGateway, new
                {
                    error = "upstream fetch failed",
                    lastError = outcome.ErrorMessage,
                    lastErrorAt = outcome.ErrorAt
                });
        }
    }
}

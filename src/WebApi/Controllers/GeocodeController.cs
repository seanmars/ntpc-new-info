using Microsoft.AspNetCore.Mvc;

using WebApi.Services;

namespace WebApi.Controllers;

[ApiController]
[Route("api/geocode")]
public sealed class GeocodeController(NominatimGeocoder geocoder, ILogger<GeocodeController> logger) : ControllerBase
{
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<GeocodeResult>>> Search(
        [FromQuery] string? q,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return ValidationProblem(new ValidationProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "query is required",
                Errors = { ["q"] = new[] { "query parameter 'q' must not be empty" } },
            });
        }

        var effectiveLimit = limit ?? 5;

        try
        {
            var results = await geocoder.SearchAsync(q, effectiveLimit, ct);
            return Ok(results);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Geocode search failed for query '{Query}'.", q);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = "upstream geocoder failed",
                detail = ex.Message,
            });
        }
    }
}

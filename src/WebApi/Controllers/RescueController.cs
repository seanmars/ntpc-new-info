using Microsoft.AspNetCore.Mvc;

using WebApi.Services;

namespace WebApi.Controllers;

[ApiController]
[Route("api/rescue")]
public sealed class RescueController(IRescueSnapshotStore store) : ControllerBase
{
    [HttpGet("latest")]
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
}
using Microsoft.AspNetCore.Mvc;

using WebApi.Models;
using WebApi.Services;

namespace WebApi.Controllers;

[ApiController]
[Route("api/monitor-points")]
public sealed class MonitorPointsController(IMonitorPointStore store) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MonitorPoint>>> GetAll(CancellationToken ct)
    {
        var points = await store.GetAllAsync(ct);
        return Ok(points);
    }

    [HttpPost]
    public async Task<ActionResult<MonitorPoint>> Create(
        [FromBody] MonitorPointCreateRequest request,
        CancellationToken ct)
    {
        var point = await store.AddAsync(request.Name, request.Latitude, request.Longitude, request.Radius, ct);
        return CreatedAtAction(nameof(GetAll), new { id = point.Id }, point);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<MonitorPoint>> Update(
        string id,
        [FromBody] MonitorPointCreateRequest request,
        CancellationToken ct)
    {
        var updated = await store.UpdateAsync(id, request.Name, request.Latitude, request.Longitude, request.Radius, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var removed = await store.RemoveAsync(id, ct);
        return removed ? NoContent() : NotFound();
    }
}

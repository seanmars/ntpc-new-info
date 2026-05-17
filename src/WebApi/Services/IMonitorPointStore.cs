using WebApi.Models;

namespace WebApi.Services;

public interface IMonitorPointStore
{
    Task<IReadOnlyList<MonitorPoint>> GetAllAsync(CancellationToken ct);

    Task<MonitorPoint> AddAsync(string name, double latitude, double longitude, int radius, CancellationToken ct);

    Task<MonitorPoint?> UpdateAsync(string id, string name, double latitude, double longitude, int radius, CancellationToken ct);

    Task<bool> RemoveAsync(string id, CancellationToken ct);
}

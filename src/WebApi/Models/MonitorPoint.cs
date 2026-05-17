namespace WebApi.Models;

public sealed record MonitorPoint(
    string Id,
    string Name,
    double Latitude,
    double Longitude,
    DateTimeOffset CreatedAt,
    int Radius = 1000);

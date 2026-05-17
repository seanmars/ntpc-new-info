using System.ComponentModel.DataAnnotations;

namespace WebApi.Models;

public sealed class MonitorPointCreateRequest
{
    [Required]
    [StringLength(50, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Range(-90.0, 90.0)]
    public double Latitude { get; set; }

    [Required]
    [Range(-180.0, 180.0)]
    public double Longitude { get; set; }

    [Range(50, 50000)]
    public int Radius { get; set; } = 1000;
}

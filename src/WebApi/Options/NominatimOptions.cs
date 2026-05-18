namespace WebApi.Options;

public sealed class NominatimOptions
{
    public const string SectionName = "Nominatim";

    public string BaseUrl { get; set; } = "https://nominatim.openstreetmap.org";

    public string UserAgent { get; set; } = "ntpc-new-info-backend/0.1";

    public int RequestTimeoutSeconds { get; set; } = 10;

    public int MinIntervalMs { get; set; } = 1100;
}

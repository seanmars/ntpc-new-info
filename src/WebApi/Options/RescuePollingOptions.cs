namespace WebApi.Options;

public sealed class RescuePollingOptions
{
    public const string SectionName = "RescuePolling";

    public string UpstreamUrl { get; set; } = "https://e.ntpc.gov.tw/v3/api/map/dynamic/layer/rescue";

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

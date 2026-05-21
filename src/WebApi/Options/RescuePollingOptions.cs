namespace WebApi.Options;

public sealed class RescuePollingOptions
{
    public const string SectionName = "RescuePolling";

    public string UpstreamUrl { get; set; } = "https://e.ntpc.gov.tw/v3/api/map/dynamic/layer/rescue";

    public int IntervalSeconds { get; set; } = 300;

    public int RequestTimeoutSeconds { get; set; } = 30;

    public int ForceRefreshCooldownSeconds { get; set; } = 15;
}

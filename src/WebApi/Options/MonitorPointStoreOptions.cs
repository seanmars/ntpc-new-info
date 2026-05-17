namespace WebApi.Options;

public sealed class MonitorPointStoreOptions
{
    public const string SectionName = "MonitorPointStore";

    public string FilePath { get; set; } = "data/monitor-points.json";
}

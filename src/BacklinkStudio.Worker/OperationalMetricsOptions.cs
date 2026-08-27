namespace BacklinkStudio.Worker;

public sealed class OperationalMetricsOptions
{
    public const string SectionName = "OperationalMetrics";
    public int PollIntervalSeconds { get; init; } = 15;
    public int WorkerStaleSeconds { get; init; } = 60;
}

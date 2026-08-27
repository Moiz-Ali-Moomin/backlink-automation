namespace BacklinkStudio.Worker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";
    public bool Enabled { get; init; } = true;
    public int Concurrency { get; init; } = 4;
    public int BufferSize { get; init; } = 8;
    public int PollIntervalMilliseconds { get; init; } = 1_000;
    public int ClaimLeaseSeconds { get; init; } = 300;
    public int LeaseRenewalSeconds { get; init; } = 30;
    public int HeartbeatIntervalSeconds { get; init; } = 10;
    public int GlobalConcurrency { get; init; } = 64;
    public int PerProjectConcurrency { get; init; } = 4;
    public int PerCampaignConcurrency { get; init; } = 2;
    public int PerDomainConcurrency { get; init; } = 1;
    public int RetryBaseDelaySeconds { get; init; } = 2;
    public int RetryMaximumDelaySeconds { get; init; } = 900;
}

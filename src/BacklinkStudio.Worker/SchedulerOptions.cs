namespace BacklinkStudio.Worker;

public sealed class SchedulerOptions
{
    public const string SectionName = "Scheduler";
    public bool Enabled { get; init; }
    public int PollIntervalMilliseconds { get; init; } = 1_000;
    public int ClaimLeaseSeconds { get; init; } = 300;
    public int LeaseRenewalSeconds { get; init; } = 30;
    public int FailureRetrySeconds { get; init; } = 30;
}

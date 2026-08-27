namespace BacklinkStudio.Infrastructure.Persistence;

internal sealed class WorkerHeartbeatRow
{
    public string WorkerId { get; set; } = null!;
    public string MachineName { get; set; } = null!;
    public int ProcessId { get; set; }
    public int Concurrency { get; set; }
    public int BufferSize { get; set; }
    public int ActiveJobs { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastHeartbeatAt { get; set; }
    public DateTimeOffset? StoppedAt { get; set; }
}

internal sealed class DomainRateLimitRow
{
    public string ScopeKey { get; set; } = null!;
    public string Domain { get; set; } = null!;
    public Guid ProjectId { get; set; }
    public Guid? CampaignId { get; set; }
    public DateTimeOffset NextAllowedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

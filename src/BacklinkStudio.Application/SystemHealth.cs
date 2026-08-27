namespace BacklinkStudio.Application;

public sealed record SystemHealthDto(string Status, bool DatabaseReachable, bool SchemaCurrent, DateTimeOffset CheckedAt);

public sealed record OperationalMetricsSnapshot(
    long QueueDepth,
    long RunningJobs,
    long FailedJobs,
    long DeadLetterJobs,
    long OnlineWorkers,
    long DueSchedules);

public interface ISystemHealthService
{
    Task<SystemHealthDto> CheckAsync(CancellationToken cancellationToken);
}

public interface IOperationalMetricsReader
{
    Task<OperationalMetricsSnapshot> ReadAsync(
        DateTimeOffset now,
        DateTimeOffset activeWorkerCutoff,
        CancellationToken cancellationToken);
}

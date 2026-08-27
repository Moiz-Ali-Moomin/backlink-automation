using BacklinkStudio.Application;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.Worker;

public sealed class PersistentScheduler(
    IServiceScopeFactory scopeFactory,
    IOptions<SchedulerOptions> options,
    TimeProvider timeProvider,
    ILogger<PersistentScheduler> logger) : BackgroundService
{
    private readonly SchedulerOptions _options = options.Value;
    private readonly string _schedulerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SchedulerLog.Started(logger, _schedulerId);
        while (!stoppingToken.IsCancellationRequested)
        {
            ScheduleClaim? claim = null;
            try
            {
                await using (var scope = scopeFactory.CreateAsyncScope())
                {
                    var store = scope.ServiceProvider.GetRequiredService<IScheduleStore>();
                    var schedule = await store.ClaimDueAsync(_schedulerId, TimeSpan.FromSeconds(_options.ClaimLeaseSeconds), timeProvider.GetUtcNow(), stoppingToken);
                    if (schedule is not null)
                    {
                        BacklinkStudioTelemetry.SchedulesClaimed.Add(1, new KeyValuePair<string, object?>("schedule.action", schedule.ActionType.ToString()));
                        claim = new ScheduleClaim(schedule.Id, schedule);
                    }
                }

                if (claim is null)
                {
                    await Task.Delay(_options.PollIntervalMilliseconds, stoppingToken);
                    continue;
                }

                using var occurrenceCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var leaseMonitor = RenewLeaseAsync(claim.ScheduleId, occurrenceCancellation, stoppingToken);
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var runner = scope.ServiceProvider.GetRequiredService<IScheduleRunner>();
                    await runner.RunAsync(claim.Schedule, _schedulerId, occurrenceCancellation.Token);
                }
                finally
                {
                    occurrenceCancellation.Cancel();
                    await IgnoreCancellationAsync(leaseMonitor);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                if (claim is not null)
                {
                    await ReleaseSafelyAsync(claim.ScheduleId);
                }
                break;
            }
            catch (Exception exception)
            {
                BacklinkStudioTelemetry.ScheduleFailures.Add(1);
                SchedulerLog.OccurrenceFailed(logger, claim?.ScheduleId, exception.GetType().Name, exception);
                if (claim is not null)
                {
                    await FailSafelyAsync(claim.Schedule, exception);
                }
            }
        }
        SchedulerLog.Stopped(logger, _schedulerId);
    }

    private async Task RenewLeaseAsync(Guid scheduleId, CancellationTokenSource occurrenceCancellation, CancellationToken stoppingToken)
    {
        try
        {
            while (!occurrenceCancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(_options.LeaseRenewalSeconds), occurrenceCancellation.Token);
                await using var scope = scopeFactory.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IScheduleStore>();
                var renewed = await store.RenewAsync(scheduleId, _schedulerId, TimeSpan.FromSeconds(_options.ClaimLeaseSeconds), timeProvider.GetUtcNow(), occurrenceCancellation.Token);
                if (!renewed)
                {
                    BacklinkStudioTelemetry.ScheduleLeasesLost.Add(1);
                    SchedulerLog.LeaseLost(logger, scheduleId);
                    occurrenceCancellation.Cancel();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (occurrenceCancellation.IsCancellationRequested || stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task FailSafelyAsync(BacklinkStudio.Domain.Schedule schedule, Exception exception)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IScheduleStore>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditSink>();
            var now = timeProvider.GetUtcNow();
            var message = $"{exception.GetType().Name}: {exception.Message}";
            if (message.Length > 2_000)
            {
                message = message[..2_000];
            }
            audit.Append(new BacklinkStudio.Domain.AuditEvent(BacklinkStudio.Domain.ActorType.Scheduler, _schedulerId, null, "schedule.occurrence_failed", schedule.ProjectId, null, null, $"schedule:{schedule.Id}:{schedule.NextRunAt.UtcTicks}", $"scheduleId={schedule.Id};errorType={exception.GetType().Name}", "failed", null, now));
            await store.FailAsync(schedule.Id, _schedulerId, message, now.AddSeconds(_options.FailureRetrySeconds), now, CancellationToken.None);
        }
        catch (Exception failureException)
        {
            SchedulerLog.StatePersistenceFailed(logger, schedule.Id, failureException);
        }
    }

    private async Task ReleaseSafelyAsync(Guid scheduleId)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IScheduleStore>();
            await store.ReleaseAsync(scheduleId, _schedulerId, timeProvider.GetUtcNow(), CancellationToken.None);
        }
        catch (Exception exception)
        {
            SchedulerLog.StatePersistenceFailed(logger, scheduleId, exception);
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private sealed record ScheduleClaim(Guid ScheduleId, BacklinkStudio.Domain.Schedule Schedule);
}

internal static partial class SchedulerLog
{
    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Scheduler {SchedulerId} started.")]
    public static partial void Started(ILogger logger, string schedulerId);

    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Scheduler {SchedulerId} stopped.")]
    public static partial void Stopped(ILogger logger, string schedulerId);

    [LoggerMessage(EventId = 102, Level = LogLevel.Error, Message = "Schedule {ScheduleId} occurrence failed with {ErrorType}.")]
    public static partial void OccurrenceFailed(ILogger logger, Guid? scheduleId, string errorType, Exception exception);

    [LoggerMessage(EventId = 103, Level = LogLevel.Warning, Message = "Schedule {ScheduleId} lease was lost.")]
    public static partial void LeaseLost(ILogger logger, Guid scheduleId);

    [LoggerMessage(EventId = 104, Level = LogLevel.Error, Message = "Could not persist state for schedule {ScheduleId}.")]
    public static partial void StatePersistenceFailed(ILogger logger, Guid scheduleId, Exception exception);
}

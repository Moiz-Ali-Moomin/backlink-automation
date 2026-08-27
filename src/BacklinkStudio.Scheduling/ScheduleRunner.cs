using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Scheduling;

public sealed class ScheduleRunner(
    IScheduleStore schedules,
    IScheduleOccurrenceCalculator calculator,
    IDiscoveryService discovery,
    IOpportunityService opportunities,
    IVerificationService verification,
    IBacklinkRepository backlinks,
    IAuditSink audit,
    TimeProvider timeProvider) : IScheduleRunner
{
    public async Task RunAsync(Schedule schedule, string schedulerId, CancellationToken cancellationToken)
    {
        var scheduledFor = schedule.NextRunAt;
        var actor = new ActorContext(ActorType.Scheduler, "backlinkstudio-scheduler", null, $"schedule:{schedule.Id}:{scheduledFor.UtcTicks}", null);
        var action = ScheduleService.DeserializeAction(schedule.ActionPayload);
        if (action.ActionType != schedule.ActionType)
        {
            throw new ValidationException("Stored schedule action type does not match its payload.");
        }
        var jobsQueued = action.ActionType switch
        {
            ScheduleActionType.Discovery => await RunDiscoveryAsync(schedule, scheduledFor, action.Discovery!, actor, cancellationToken),
            ScheduleActionType.Analysis => await RunAnalysisAsync(schedule, scheduledFor, actor, cancellationToken),
            ScheduleActionType.Verification => await RunVerificationAsync(schedule, scheduledFor, action.Verification!, actor, cancellationToken),
            _ => throw new ValidationException("Stored schedule action type is unsupported.")
        };

        var now = timeProvider.GetUtcNow();
        var next = calculator.GetNext(schedule, now);
        BacklinkStudioTelemetry.ScheduleOccurrences.Add(1, new KeyValuePair<string, object?>("schedule.action", schedule.ActionType.ToString()));
        BacklinkStudioTelemetry.ScheduledJobs.Add(jobsQueued, new KeyValuePair<string, object?>("schedule.action", schedule.ActionType.ToString()));
        audit.Append(new AuditEvent(ActorType.Scheduler, schedulerId, null, "schedule.occurrence", schedule.ProjectId, null, null, actor.RequestId, $"scheduleId={schedule.Id};scheduledFor={scheduledFor:O};jobsQueued={jobsQueued}", "succeeded", null, now));
        await schedules.CompleteAsync(schedule.Id, schedulerId, scheduledFor, next, jobsQueued, now, cancellationToken);
    }

    private async Task<int> RunDiscoveryAsync(Schedule schedule, DateTimeOffset scheduledFor, ScheduleDiscoveryAction action, ActorContext actor, CancellationToken cancellationToken)
    {
        _ = await discovery.StartAsync(new StartDiscoveryCommand(schedule.ProjectId, action.Provider, action.Query, action.Content, action.Urls, action.MaximumResults, OccurrenceKey(schedule.Id, scheduledFor)), actor, cancellationToken);
        return 1;
    }

    private async Task<int> RunAnalysisAsync(Schedule schedule, DateTimeOffset scheduledFor, ActorContext actor, CancellationToken cancellationToken)
    {
        _ = await opportunities.StartAnalysisAsync(new StartAnalysisCommand(schedule.ProjectId, OccurrenceKey(schedule.Id, scheduledFor)), actor, cancellationToken);
        return 1;
    }

    private async Task<int> RunVerificationAsync(Schedule schedule, DateTimeOffset scheduledFor, ScheduleVerificationAction action, ActorContext actor, CancellationToken cancellationToken)
    {
        if (action.BacklinkId is { } backlinkId)
        {
            var backlink = await verification.GetAsync(backlinkId, cancellationToken) ?? throw new ResourceNotFoundException("Backlink", backlinkId);
            if (backlink.ProjectId != schedule.ProjectId)
            {
                throw new ValidationException("Scheduled backlink is outside the schedule project.");
            }
            _ = await verification.StartAsync(new StartVerificationCommand(backlinkId, OccurrenceKey(schedule.Id, scheduledFor, backlinkId)), actor, cancellationToken);
            return 1;
        }

        var queued = 0;
        PageCursor? cursor = null;
        while (queued < action.MaximumBacklinks)
        {
            var take = Math.Min(100, action.MaximumBacklinks - queued);
            var rows = await backlinks.ListAsync(schedule.ProjectId, action.Status, cursor, take, cancellationToken);
            if (rows.Count == 0)
            {
                break;
            }
            foreach (var backlink in rows)
            {
                _ = await verification.StartAsync(new StartVerificationCommand(backlink.Id, OccurrenceKey(schedule.Id, scheduledFor, backlink.Id)), actor, cancellationToken);
                queued++;
            }
            if (rows.Count < take)
            {
                break;
            }
            cursor = new PageCursor(rows[^1].CreatedAt, rows[^1].Id);
        }
        return queued;
    }

    private static string OccurrenceKey(Guid scheduleId, DateTimeOffset scheduledFor, Guid? resourceId = null) =>
        resourceId is null
            ? $"schedule:{scheduleId:N}:{scheduledFor.UtcTicks}"
            : $"schedule:{scheduleId:N}:{scheduledFor.UtcTicks}:{resourceId.Value:N}";
}

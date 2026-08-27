using System.Text.Json;
using System.Text.Json.Serialization;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Scheduling;

public sealed class ScheduleService(
    IProjectRepository projects,
    IBacklinkRepository backlinks,
    IScheduleStore schedules,
    IScheduleOccurrenceCalculator calculator,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IScheduleService
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task<ScheduleDto> CreateAsync(CreateScheduleCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        _ = await projects.GetAsync(command.ProjectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", command.ProjectId);
        await ValidateActionAsync(command.ProjectId, command.Action, cancellationToken);
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "schedule_create");
        var requestHash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var replay = await idempotency.FindAsync(scope, key, cancellationToken);
        if (replay is not null)
        {
            return Idempotency.ReadExisting<ScheduleDto>(replay, requestHash);
        }

        var now = timeProvider.GetUtcNow();
        var next = calculator.First(command.Timing, now);
        var payload = SerializeAction(command.Action);
        var schedule = new Schedule(command.ProjectId, command.Name, command.Action.ActionType, payload, command.Timing.RecurrenceType, command.Timing.StartsAt.ToUniversalTime(), command.Timing.OneTimeAt?.ToUniversalTime(), command.Timing.IntervalMinutes, command.Timing.TimeOfDayUtc, command.Timing.DayOfWeek, command.Timing.DayOfMonth, command.Timing.CronExpression, next, now);
        schedules.Add(schedule);
        var result = ToDto(schedule);
        idempotency.Add(new IdempotencyRecord(scope, key, requestHash, "schedule", schedule.Id, JsonSerializer.Serialize(result), now));
        audit.Append(CreateAudit(actor, "schedule.create", schedule, $"action={schedule.ActionType};recurrence={schedule.RecurrenceType};nextRunAt={schedule.NextRunAt:O}", "succeeded", now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<ScheduleDto> UpdateAsync(UpdateScheduleCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var schedule = await RequiredAsync(command.ScheduleId, true, cancellationToken);
        await ValidateActionAsync(schedule.ProjectId, command.Action, cancellationToken);
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "schedule_update");
        var requestHash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var replay = await idempotency.FindAsync(scope, key, cancellationToken);
        if (replay is not null)
        {
            return Idempotency.ReadExisting<ScheduleDto>(replay, requestHash);
        }

        var now = timeProvider.GetUtcNow();
        var next = calculator.First(command.Timing, now);
        schedule.UpdateDefinition(command.Name, command.Action.ActionType, SerializeAction(command.Action), command.Timing.RecurrenceType, command.Timing.StartsAt.ToUniversalTime(), command.Timing.OneTimeAt?.ToUniversalTime(), command.Timing.IntervalMinutes, command.Timing.TimeOfDayUtc, command.Timing.DayOfWeek, command.Timing.DayOfMonth, command.Timing.CronExpression, next, now);
        return await SaveMutationAsync(schedule, actor, scope, key, requestHash, "schedule.update", $"action={schedule.ActionType};recurrence={schedule.RecurrenceType};nextRunAt={schedule.NextRunAt:O}", cancellationToken);
    }

    public Task<ScheduleDto> PauseAsync(ChangeScheduleStateCommand command, ActorContext actor, CancellationToken cancellationToken) =>
        ChangeStateAsync(command, actor, "schedule_pause", "schedule.pause", static (schedule, _, now) => schedule.Pause(now), cancellationToken);

    public Task<ScheduleDto> ResumeAsync(ChangeScheduleStateCommand command, ActorContext actor, CancellationToken cancellationToken) =>
        ChangeStateAsync(command, actor, "schedule_resume", "schedule.resume", static (schedule, calculator, now) =>
        {
            var timing = new ScheduleTiming(schedule.RecurrenceType, schedule.StartsAt, schedule.OneTimeAt, schedule.IntervalMinutes, schedule.TimeOfDayUtc, schedule.DayOfWeek, schedule.DayOfMonth, schedule.CronExpression);
            schedule.Resume(calculator.First(timing, now), now);
        }, cancellationToken);

    public Task<ScheduleDto> DeleteAsync(ChangeScheduleStateCommand command, ActorContext actor, CancellationToken cancellationToken) =>
        ChangeStateAsync(command, actor, "schedule_delete", "schedule.delete", static (schedule, _, now) => schedule.Delete(now), cancellationToken);

    public async Task<ScheduleDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetAsync(id, false, cancellationToken);
        return schedule is null ? null : ToDto(schedule);
    }

    public async Task<PageResult<ScheduleDto>> ListAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken)
    {
        _ = await projects.GetAsync(projectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", projectId);
        var rows = await schedules.ListAsync(projectId, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var values = rows.Take(page.BoundedLimit).Select(ToDto).ToArray();
        var next = rows.Count > page.BoundedLimit && values.Length > 0 ? CursorCodec.Encode(new PageCursor(values[^1].CreatedAt, values[^1].Id)) : null;
        return new(values, next);
    }

    private async Task<ScheduleDto> ChangeStateAsync(ChangeScheduleStateCommand command, ActorContext actor, string operationScope, string operation, Action<Schedule, IScheduleOccurrenceCalculator, DateTimeOffset> change, CancellationToken cancellationToken)
    {
        var schedule = await RequiredAsync(command.ScheduleId, true, cancellationToken);
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, operationScope);
        var requestHash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var replay = await idempotency.FindAsync(scope, key, cancellationToken);
        if (replay is not null)
        {
            return Idempotency.ReadExisting<ScheduleDto>(replay, requestHash);
        }
        var now = timeProvider.GetUtcNow();
        change(schedule, calculator, now);
        return await SaveMutationAsync(schedule, actor, scope, key, requestHash, operation, $"status={schedule.Status}", cancellationToken);
    }

    private async Task<ScheduleDto> SaveMutationAsync(Schedule schedule, ActorContext actor, string scope, string key, string requestHash, string operation, string summary, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var result = ToDto(schedule);
        idempotency.Add(new IdempotencyRecord(scope, key, requestHash, "schedule", schedule.Id, JsonSerializer.Serialize(result), now));
        audit.Append(CreateAudit(actor, operation, schedule, summary, "succeeded", now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task ValidateActionAsync(Guid projectId, ScheduleActionConfiguration action, CancellationToken cancellationToken)
    {
        switch (action.ActionType)
        {
            case ScheduleActionType.Analysis when action.Discovery is null && action.Verification is null:
                return;
            case ScheduleActionType.Discovery when action.Discovery is not null && action.Verification is null:
                DiscoveryCommandValidation.Validate(new StartDiscoveryCommand(projectId, action.Discovery.Provider, action.Discovery.Query, action.Discovery.Content, action.Discovery.Urls, action.Discovery.MaximumResults, "schedule-validation"));
                return;
            case ScheduleActionType.Verification when action.Verification is not null && action.Discovery is null:
                if (action.Verification.MaximumBacklinks is < 1 or > 500)
                {
                    throw new ValidationException("maximumBacklinks must be between 1 and 500.");
                }
                if (action.Verification.BacklinkId is { } backlinkId)
                {
                    if (action.Verification.Status is not null)
                    {
                        throw new ValidationException("status cannot be combined with a specific backlinkId.");
                    }
                    var backlink = await backlinks.GetBacklinkAsync(backlinkId, false, cancellationToken) ?? throw new ResourceNotFoundException("Backlink", backlinkId);
                    if (backlink.ProjectId != projectId)
                    {
                        throw new ValidationException("The scheduled backlink must belong to the schedule project.");
                    }
                }
                return;
            default:
                throw new ValidationException("The schedule action configuration does not match its actionType.");
        }
    }

    private async Task<Schedule> RequiredAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
        await schedules.GetAsync(id, tracked, cancellationToken) ?? throw new ResourceNotFoundException("Schedule", id);

    internal static string SerializeAction(ScheduleActionConfiguration action)
    {
        var json = JsonSerializer.Serialize(action, JsonOptions);
        return json.Length <= 64 * 1_024 ? json : throw new ValidationException("Schedule action payload must not exceed 64 KiB.");
    }

    internal static ScheduleActionConfiguration DeserializeAction(string payload) =>
        JsonSerializer.Deserialize<ScheduleActionConfiguration>(payload, JsonOptions) ?? throw new ValidationException("Stored schedule action payload is invalid.");

    internal static ScheduleDto ToDto(Schedule value) => new(
        value.Id,
        value.ProjectId,
        value.Name,
        DeserializeAction(value.ActionPayload),
        new ScheduleTiming(value.RecurrenceType, value.StartsAt, value.OneTimeAt, value.IntervalMinutes, value.TimeOfDayUtc, value.DayOfWeek, value.DayOfMonth, value.CronExpression),
        value.Status,
        value.NextRunAt,
        value.LastScheduledFor,
        value.LastRunAt,
        value.LastJobCount,
        value.ConsecutiveFailures,
        value.RecoveryCount,
        value.LastError,
        value.CreatedAt,
        value.UpdatedAt);

    private static AuditEvent CreateAudit(ActorContext actor, string operation, Schedule schedule, string summary, string result, DateTimeOffset now) =>
        new(actor.ActorType, actor.ActorId, actor.CredentialId, operation, schedule.ProjectId, null, null, actor.RequestId, $"scheduleId={schedule.Id};{summary}", result, actor.SourceAddress, now);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public sealed record ScheduleTiming(
    ScheduleRecurrenceType RecurrenceType,
    DateTimeOffset StartsAt,
    DateTimeOffset? OneTimeAt,
    int? IntervalMinutes,
    TimeOnly? TimeOfDayUtc,
    DayOfWeek? DayOfWeek,
    int? DayOfMonth,
    string? CronExpression);

public sealed record ScheduleDiscoveryAction(
    DiscoveryProviderKind Provider,
    string? Query,
    string? Content,
    IReadOnlyList<string>? Urls,
    int MaximumResults = 1_000);

public sealed record ScheduleVerificationAction(
    Guid? BacklinkId,
    BacklinkStatus? Status,
    int MaximumBacklinks = 100);

public sealed record ScheduleActionConfiguration(
    ScheduleActionType ActionType,
    ScheduleDiscoveryAction? Discovery,
    ScheduleVerificationAction? Verification);

public sealed record ScheduleDto(
    Guid Id,
    Guid ProjectId,
    string Name,
    ScheduleActionConfiguration Action,
    ScheduleTiming Timing,
    ScheduleStatus Status,
    DateTimeOffset NextRunAt,
    DateTimeOffset? LastScheduledFor,
    DateTimeOffset? LastRunAt,
    int LastJobCount,
    int ConsecutiveFailures,
    int RecoveryCount,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateScheduleCommand(
    Guid ProjectId,
    string Name,
    ScheduleActionConfiguration Action,
    ScheduleTiming Timing,
    string IdempotencyKey);

public sealed record UpdateScheduleCommand(
    Guid ScheduleId,
    string Name,
    ScheduleActionConfiguration Action,
    ScheduleTiming Timing,
    string IdempotencyKey);

public sealed record ChangeScheduleStateCommand(Guid ScheduleId, string IdempotencyKey);

public interface IScheduleService
{
    Task<ScheduleDto> CreateAsync(CreateScheduleCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<ScheduleDto> UpdateAsync(UpdateScheduleCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<ScheduleDto> PauseAsync(ChangeScheduleStateCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<ScheduleDto> ResumeAsync(ChangeScheduleStateCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<ScheduleDto> DeleteAsync(ChangeScheduleStateCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<ScheduleDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<ScheduleDto>> ListAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);
}

public interface IScheduleStore
{
    void Add(Schedule schedule);
    Task<Schedule?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    Task<IReadOnlyList<Schedule>> ListAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken);
    Task<Schedule?> ClaimDueAsync(string schedulerId, TimeSpan lease, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> RenewAsync(Guid scheduleId, string schedulerId, TimeSpan lease, DateTimeOffset now, CancellationToken cancellationToken);
    Task CompleteAsync(Guid scheduleId, string schedulerId, DateTimeOffset scheduledFor, DateTimeOffset? nextRunAt, int jobsQueued, DateTimeOffset now, CancellationToken cancellationToken);
    Task FailAsync(Guid scheduleId, string schedulerId, string errorMessage, DateTimeOffset retryAt, DateTimeOffset now, CancellationToken cancellationToken);
    Task ReleaseAsync(Guid scheduleId, string schedulerId, DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IScheduleOccurrenceCalculator
{
    DateTimeOffset First(ScheduleTiming timing, DateTimeOffset now);
    DateTimeOffset? GetNext(Schedule schedule, DateTimeOffset after);
}

public interface IScheduleRunner
{
    Task RunAsync(Schedule schedule, string schedulerId, CancellationToken cancellationToken);
}

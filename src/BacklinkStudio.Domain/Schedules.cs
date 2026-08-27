namespace BacklinkStudio.Domain;

public enum ScheduleRecurrenceType
{
    OneTime,
    Interval,
    Daily,
    Weekly,
    Monthly,
    Cron
}

public enum ScheduleActionType
{
    Discovery,
    Analysis,
    Verification
}

public enum ScheduleStatus
{
    Active,
    Paused,
    Completed,
    Deleted
}

public sealed class Schedule
{
    private Schedule() { }

    public Schedule(
        Guid projectId,
        string name,
        ScheduleActionType actionType,
        string actionPayload,
        ScheduleRecurrenceType recurrenceType,
        DateTimeOffset startsAt,
        DateTimeOffset? oneTimeAt,
        int? intervalMinutes,
        TimeOnly? timeOfDayUtc,
        DayOfWeek? dayOfWeek,
        int? dayOfMonth,
        string? cronExpression,
        DateTimeOffset nextRunAt,
        DateTimeOffset now)
    {
        ValidateTiming(recurrenceType, oneTimeAt, intervalMinutes, timeOfDayUtc, dayOfWeek, dayOfMonth, cronExpression);
        if (nextRunAt < startsAt)
        {
            throw new DomainRuleException("Schedule next run cannot be before its start time.");
        }

        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        Name = Guard.Required(name, 200, nameof(Name));
        ActionType = actionType;
        ActionPayload = Guard.Required(actionPayload, 64 * 1_024, nameof(ActionPayload));
        RecurrenceType = recurrenceType;
        StartsAt = startsAt;
        OneTimeAt = oneTimeAt;
        IntervalMinutes = intervalMinutes;
        TimeOfDayUtc = timeOfDayUtc;
        DayOfWeek = dayOfWeek;
        DayOfMonth = dayOfMonth;
        CronExpression = Guard.Optional(cronExpression, 200, nameof(CronExpression));
        Status = ScheduleStatus.Active;
        NextRunAt = nextRunAt;
        AvailableAt = nextRunAt;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Name { get; private set; } = null!;
    public ScheduleActionType ActionType { get; private set; }
    public string ActionPayload { get; private set; } = null!;
    public ScheduleRecurrenceType RecurrenceType { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset? OneTimeAt { get; private set; }
    public int? IntervalMinutes { get; private set; }
    public TimeOnly? TimeOfDayUtc { get; private set; }
    public DayOfWeek? DayOfWeek { get; private set; }
    public int? DayOfMonth { get; private set; }
    public string? CronExpression { get; private set; }
    public ScheduleStatus Status { get; private set; }
    public DateTimeOffset NextRunAt { get; private set; }
    public DateTimeOffset AvailableAt { get; private set; }
    public DateTimeOffset? LastScheduledFor { get; private set; }
    public DateTimeOffset? LastRunAt { get; private set; }
    public int LastJobCount { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public int RecoveryCount { get; private set; }
    public string? LastError { get; private set; }
    public string? ClaimedBy { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public DateTimeOffset? ClaimExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void UpdateDefinition(
        string name,
        ScheduleActionType actionType,
        string actionPayload,
        ScheduleRecurrenceType recurrenceType,
        DateTimeOffset startsAt,
        DateTimeOffset? oneTimeAt,
        int? intervalMinutes,
        TimeOnly? timeOfDayUtc,
        DayOfWeek? dayOfWeek,
        int? dayOfMonth,
        string? cronExpression,
        DateTimeOffset nextRunAt,
        DateTimeOffset now)
    {
        if (Status == ScheduleStatus.Deleted)
        {
            throw new DomainRuleException("A deleted schedule cannot be updated.");
        }
        if (ClaimedBy is not null && ClaimExpiresAt > now)
        {
            throw new DomainRuleException("A claimed schedule cannot be updated until its occurrence finishes.");
        }

        ValidateTiming(recurrenceType, oneTimeAt, intervalMinutes, timeOfDayUtc, dayOfWeek, dayOfMonth, cronExpression);
        Name = Guard.Required(name, 200, nameof(Name));
        ActionType = actionType;
        ActionPayload = Guard.Required(actionPayload, 64 * 1_024, nameof(ActionPayload));
        RecurrenceType = recurrenceType;
        StartsAt = startsAt;
        OneTimeAt = oneTimeAt;
        IntervalMinutes = intervalMinutes;
        TimeOfDayUtc = timeOfDayUtc;
        DayOfWeek = dayOfWeek;
        DayOfMonth = dayOfMonth;
        CronExpression = Guard.Optional(cronExpression, 200, nameof(CronExpression));
        NextRunAt = nextRunAt;
        AvailableAt = nextRunAt;
        Status = ScheduleStatus.Active;
        ConsecutiveFailures = 0;
        LastError = null;
        ClearClaim();
        UpdatedAt = now;
    }

    public void Claim(string schedulerId, DateTimeOffset now, TimeSpan lease)
    {
        if (Status != ScheduleStatus.Active || NextRunAt > now || AvailableAt > now)
        {
            throw new DomainRuleException("Schedule is not due.");
        }
        if (ClaimedBy is not null && ClaimExpiresAt > now)
        {
            throw new DomainRuleException("Schedule already has an active claim.");
        }
        if (ClaimedBy is not null)
        {
            RecoveryCount++;
        }

        ClaimedBy = Guard.Required(schedulerId, 200, nameof(schedulerId));
        ClaimedAt = now;
        ClaimExpiresAt = now.Add(lease);
        UpdatedAt = now;
    }

    public void CompleteOccurrence(string schedulerId, DateTimeOffset scheduledFor, DateTimeOffset? nextRunAt, int jobsQueued, DateTimeOffset now)
    {
        RequireClaim(schedulerId, now);
        if (scheduledFor != NextRunAt || jobsQueued < 0)
        {
            throw new DomainRuleException("Schedule completion does not match the claimed occurrence.");
        }

        LastScheduledFor = scheduledFor;
        LastRunAt = now;
        LastJobCount = jobsQueued;
        ConsecutiveFailures = 0;
        LastError = null;
        if (nextRunAt is null)
        {
            Status = ScheduleStatus.Completed;
            NextRunAt = scheduledFor;
            AvailableAt = scheduledFor;
        }
        else
        {
            NextRunAt = nextRunAt.Value;
            AvailableAt = nextRunAt.Value;
        }
        ClearClaim();
        UpdatedAt = now;
    }

    public void RenewClaim(string schedulerId, DateTimeOffset now, TimeSpan lease)
    {
        RequireClaim(schedulerId, now);
        ClaimExpiresAt = now.Add(lease);
        UpdatedAt = now;
    }

    public void FailOccurrence(string schedulerId, string error, DateTimeOffset retryAt, DateTimeOffset now)
    {
        RequireClaim(schedulerId, now);
        LastError = Guard.Required(error, 2_000, nameof(error));
        ConsecutiveFailures++;
        AvailableAt = retryAt;
        if (ConsecutiveFailures >= 10)
        {
            Status = ScheduleStatus.Paused;
        }
        ClearClaim();
        UpdatedAt = now;
    }

    public void ReleaseClaim(string schedulerId, DateTimeOffset now)
    {
        RequireClaim(schedulerId, now);
        AvailableAt = now;
        ClearClaim();
        UpdatedAt = now;
    }

    public void Pause(DateTimeOffset now)
    {
        if (Status == ScheduleStatus.Deleted)
        {
            throw new DomainRuleException("A deleted schedule cannot be paused.");
        }
        if (ClaimedBy is not null && ClaimExpiresAt > now)
        {
            throw new DomainRuleException("A claimed schedule cannot be paused until its occurrence finishes.");
        }
        Status = ScheduleStatus.Paused;
        ClearClaim();
        UpdatedAt = now;
    }

    public void Resume(DateTimeOffset nextRunAt, DateTimeOffset now)
    {
        if (Status != ScheduleStatus.Paused)
        {
            throw new DomainRuleException("Only a paused schedule can be resumed.");
        }
        Status = ScheduleStatus.Active;
        NextRunAt = nextRunAt;
        AvailableAt = nextRunAt;
        ConsecutiveFailures = 0;
        LastError = null;
        UpdatedAt = now;
    }

    public void Delete(DateTimeOffset now)
    {
        if (Status == ScheduleStatus.Deleted)
        {
            return;
        }
        if (ClaimedBy is not null && ClaimExpiresAt > now)
        {
            throw new DomainRuleException("A claimed schedule cannot be deleted until its occurrence finishes.");
        }
        Status = ScheduleStatus.Deleted;
        ClearClaim();
        UpdatedAt = now;
    }

    private void RequireClaim(string schedulerId, DateTimeOffset now)
    {
        if (!string.Equals(ClaimedBy, schedulerId, StringComparison.Ordinal) || ClaimExpiresAt is null || ClaimExpiresAt <= now)
        {
            throw new DomainRuleException("Only the owning scheduler with an active lease can change this occurrence.");
        }
    }

    private void ClearClaim()
    {
        ClaimedBy = null;
        ClaimedAt = null;
        ClaimExpiresAt = null;
    }

    private static void ValidateTiming(
        ScheduleRecurrenceType recurrenceType,
        DateTimeOffset? oneTimeAt,
        int? intervalMinutes,
        TimeOnly? timeOfDayUtc,
        DayOfWeek? dayOfWeek,
        int? dayOfMonth,
        string? cronExpression)
    {
        var valid = recurrenceType switch
        {
            ScheduleRecurrenceType.OneTime => oneTimeAt is not null && intervalMinutes is null && timeOfDayUtc is null && dayOfWeek is null && dayOfMonth is null && cronExpression is null,
            ScheduleRecurrenceType.Interval => oneTimeAt is null && intervalMinutes is >= 1 and <= 525_600 && timeOfDayUtc is null && dayOfWeek is null && dayOfMonth is null && cronExpression is null,
            ScheduleRecurrenceType.Daily => oneTimeAt is null && intervalMinutes is null && timeOfDayUtc is not null && dayOfWeek is null && dayOfMonth is null && cronExpression is null,
            ScheduleRecurrenceType.Weekly => oneTimeAt is null && intervalMinutes is null && timeOfDayUtc is not null && dayOfWeek is not null && dayOfMonth is null && cronExpression is null,
            ScheduleRecurrenceType.Monthly => oneTimeAt is null && intervalMinutes is null && timeOfDayUtc is not null && dayOfWeek is null && dayOfMonth is >= 1 and <= 31 && cronExpression is null,
            ScheduleRecurrenceType.Cron => oneTimeAt is null && intervalMinutes is null && timeOfDayUtc is null && dayOfWeek is null && dayOfMonth is null && !string.IsNullOrWhiteSpace(cronExpression),
            _ => false
        };
        if (!valid)
        {
            throw new DomainRuleException($"Schedule timing is invalid for {recurrenceType} recurrence.");
        }
    }
}

using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using BacklinkStudio.Scheduling;

namespace BacklinkStudio.UnitTests;

public sealed class SchedulingTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 19, 10, 15, 0, TimeSpan.Zero);
    private readonly ScheduleOccurrenceCalculator _calculator = new();

    [Fact]
    public void Calculator_ProducesUtcOccurrencesForEverySupportedRecurrence()
    {
        Assert.Equal(new DateTimeOffset(2026, 8, 19, 11, 0, 0, TimeSpan.Zero), _calculator.First(Timing(ScheduleRecurrenceType.OneTime, oneTimeAt: Now.AddMinutes(45)), Now));
        Assert.Equal(new DateTimeOffset(2026, 8, 19, 10, 30, 0, TimeSpan.Zero), _calculator.First(Timing(ScheduleRecurrenceType.Interval, startsAt: Now.AddMinutes(-15), intervalMinutes: 30), Now));
        Assert.Equal(new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero), _calculator.First(Timing(ScheduleRecurrenceType.Daily, time: new TimeOnly(9, 0)), Now));
        Assert.Equal(new DateTimeOffset(2026, 8, 24, 8, 30, 0, TimeSpan.Zero), _calculator.First(Timing(ScheduleRecurrenceType.Weekly, time: new TimeOnly(8, 30), dayOfWeek: DayOfWeek.Monday), Now));
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 7, 0, 0, TimeSpan.Zero), _calculator.First(Timing(ScheduleRecurrenceType.Monthly, time: new TimeOnly(7, 0), dayOfMonth: 31), Now));
        Assert.Equal(new DateTimeOffset(2026, 8, 20, 2, 0, 0, TimeSpan.Zero), _calculator.First(Timing(ScheduleRecurrenceType.Cron, cron: "0 2 * * *"), Now));
    }

    [Fact]
    public void Calculator_CoalescesMissedIntervalsAndSkipsMonthsWithoutConfiguredDay()
    {
        var interval = Timing(ScheduleRecurrenceType.Interval, startsAt: Now.AddDays(-30), intervalMinutes: 60);
        Assert.Equal(Now, _calculator.First(interval, Now));

        var monthly = Timing(ScheduleRecurrenceType.Monthly, startsAt: new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero), time: new TimeOnly(6, 0), dayOfMonth: 31);
        Assert.Equal(new DateTimeOffset(2027, 3, 31, 6, 0, 0, TimeSpan.Zero), _calculator.First(monthly, new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Calculator_RejectsInvalidCronAndTimingCombinations()
    {
        Assert.Throws<ValidationException>(() => _calculator.First(Timing(ScheduleRecurrenceType.Cron, cron: "not a cron"), Now));
        Assert.Throws<DomainRuleException>(() => new Schedule(Guid.NewGuid(), "invalid", ScheduleActionType.Analysis, "{}", ScheduleRecurrenceType.Daily, Now, null, 5, new TimeOnly(9, 0), null, null, null, Now, Now));
    }

    [Fact]
    public void Schedule_ClaimsRecoversCompletesPausesAndResumesDeterministically()
    {
        var schedule = CreateSchedule(ScheduleRecurrenceType.Interval, intervalMinutes: 60);
        schedule.Claim("scheduler-a", Now, TimeSpan.FromMinutes(1));
        schedule.Claim("scheduler-b", Now.AddMinutes(2), TimeSpan.FromMinutes(1));
        Assert.Equal(1, schedule.RecoveryCount);
        schedule.CompleteOccurrence("scheduler-b", schedule.NextRunAt, Now.AddHours(1), 3, Now.AddMinutes(2).AddSeconds(1));
        Assert.Equal(3, schedule.LastJobCount);
        Assert.Equal(ScheduleStatus.Active, schedule.Status);

        schedule.Pause(Now.AddMinutes(3));
        Assert.Equal(ScheduleStatus.Paused, schedule.Status);
        schedule.Resume(Now.AddHours(2), Now.AddMinutes(4));
        Assert.Equal(ScheduleStatus.Active, schedule.Status);
    }

    [Fact]
    public void OneTimeSchedule_CompletesWithoutAnotherOccurrence()
    {
        var schedule = CreateSchedule(ScheduleRecurrenceType.OneTime, oneTimeAt: Now);
        schedule.Claim("scheduler", Now, TimeSpan.FromMinutes(1));
        schedule.CompleteOccurrence("scheduler", Now, null, 1, Now.AddSeconds(1));
        Assert.Equal(ScheduleStatus.Completed, schedule.Status);
    }

    private static Schedule CreateSchedule(ScheduleRecurrenceType recurrence, DateTimeOffset? oneTimeAt = null, int? intervalMinutes = null)
    {
        var timing = Timing(recurrence, oneTimeAt: oneTimeAt, intervalMinutes: intervalMinutes);
        return new Schedule(Guid.NewGuid(), "schedule", ScheduleActionType.Analysis, "{}", recurrence, timing.StartsAt, timing.OneTimeAt, timing.IntervalMinutes, timing.TimeOfDayUtc, timing.DayOfWeek, timing.DayOfMonth, timing.CronExpression, Now, Now);
    }

    private static ScheduleTiming Timing(
        ScheduleRecurrenceType recurrence,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? oneTimeAt = null,
        int? intervalMinutes = null,
        TimeOnly? time = null,
        DayOfWeek? dayOfWeek = null,
        int? dayOfMonth = null,
        string? cron = null) =>
        new(recurrence, startsAt ?? Now, oneTimeAt, intervalMinutes, time, dayOfWeek, dayOfMonth, cron);
}

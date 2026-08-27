using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Cronos;

namespace BacklinkStudio.Scheduling;

public sealed class ScheduleOccurrenceCalculator : IScheduleOccurrenceCalculator
{
    public DateTimeOffset First(ScheduleTiming timing, DateTimeOffset now)
    {
        var lowerBound = Max(now.ToUniversalTime(), timing.StartsAt.ToUniversalTime());
        return timing.RecurrenceType switch
        {
            ScheduleRecurrenceType.OneTime => FirstOneTime(timing, lowerBound),
            ScheduleRecurrenceType.Interval => FirstInterval(timing, lowerBound),
            ScheduleRecurrenceType.Daily => FirstDaily(timing, lowerBound),
            ScheduleRecurrenceType.Weekly => FirstWeekly(timing, lowerBound),
            ScheduleRecurrenceType.Monthly => FirstMonthly(timing, lowerBound),
            ScheduleRecurrenceType.Cron => FirstCron(timing, lowerBound),
            _ => throw new ValidationException("Unsupported schedule recurrence type.")
        };
    }

    public DateTimeOffset? GetNext(Schedule schedule, DateTimeOffset after)
    {
        if (schedule.RecurrenceType == ScheduleRecurrenceType.OneTime)
        {
            return null;
        }

        var timing = new ScheduleTiming(schedule.RecurrenceType, schedule.StartsAt, schedule.OneTimeAt, schedule.IntervalMinutes, schedule.TimeOfDayUtc, schedule.DayOfWeek, schedule.DayOfMonth, schedule.CronExpression);
        return First(timing, after.ToUniversalTime().AddTicks(1));
    }

    private static DateTimeOffset FirstOneTime(ScheduleTiming timing, DateTimeOffset lowerBound)
    {
        var value = timing.OneTimeAt?.ToUniversalTime() ?? throw new ValidationException("oneTimeAt is required.");
        if (value < timing.StartsAt.ToUniversalTime())
        {
            throw new ValidationException("oneTimeAt cannot be before startsAt.");
        }
        return Max(value, lowerBound);
    }

    private static DateTimeOffset FirstInterval(ScheduleTiming timing, DateTimeOffset lowerBound)
    {
        var minutes = timing.IntervalMinutes is >= 1 and <= 525_600
            ? timing.IntervalMinutes.Value
            : throw new ValidationException("intervalMinutes must be between 1 and 525600.");
        var start = timing.StartsAt.ToUniversalTime();
        if (start >= lowerBound)
        {
            return start;
        }

        var intervalTicks = TimeSpan.FromMinutes(minutes).Ticks;
        var elapsedTicks = lowerBound.UtcTicks - start.UtcTicks;
        var intervals = (elapsedTicks + intervalTicks - 1) / intervalTicks;
        return start.AddTicks(intervals * intervalTicks);
    }

    private static DateTimeOffset FirstDaily(ScheduleTiming timing, DateTimeOffset lowerBound)
    {
        var time = timing.TimeOfDayUtc ?? throw new ValidationException("timeOfDayUtc is required.");
        var candidate = AtUtc(lowerBound.Date, time);
        return candidate >= lowerBound ? candidate : candidate.AddDays(1);
    }

    private static DateTimeOffset FirstWeekly(ScheduleTiming timing, DateTimeOffset lowerBound)
    {
        var time = timing.TimeOfDayUtc ?? throw new ValidationException("timeOfDayUtc is required.");
        var day = timing.DayOfWeek ?? throw new ValidationException("dayOfWeek is required.");
        var days = ((int)day - (int)lowerBound.DayOfWeek + 7) % 7;
        var candidate = AtUtc(lowerBound.Date.AddDays(days), time);
        return candidate >= lowerBound ? candidate : candidate.AddDays(7);
    }

    private static DateTimeOffset FirstMonthly(ScheduleTiming timing, DateTimeOffset lowerBound)
    {
        var time = timing.TimeOfDayUtc ?? throw new ValidationException("timeOfDayUtc is required.");
        var day = timing.DayOfMonth is >= 1 and <= 31 ? timing.DayOfMonth.Value : throw new ValidationException("dayOfMonth must be between 1 and 31.");
        var month = new DateTime(lowerBound.Year, lowerBound.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var offset = 0; offset < 24; offset++)
        {
            var current = month.AddMonths(offset);
            if (day > DateTime.DaysInMonth(current.Year, current.Month))
            {
                continue;
            }
            var candidate = AtUtc(new DateTime(current.Year, current.Month, day, 0, 0, 0, DateTimeKind.Utc), time);
            if (candidate >= lowerBound)
            {
                return candidate;
            }
        }
        throw new ValidationException("The monthly schedule has no occurrence in the supported calculation horizon.");
    }

    private static DateTimeOffset FirstCron(ScheduleTiming timing, DateTimeOffset lowerBound)
    {
        if (string.IsNullOrWhiteSpace(timing.CronExpression) || timing.CronExpression.Length > 200)
        {
            throw new ValidationException("cronExpression is required and must not exceed 200 characters.");
        }

        try
        {
            var expression = CronExpression.Parse(timing.CronExpression, CronFormat.Standard);
            var occurrence = expression.GetNextOccurrence(lowerBound.UtcDateTime.AddTicks(-1), TimeZoneInfo.Utc, inclusive: false);
            return occurrence is null
                ? throw new ValidationException("The cron expression has no future UTC occurrence.")
                : new DateTimeOffset(DateTime.SpecifyKind(occurrence.Value, DateTimeKind.Utc));
        }
        catch (CronFormatException exception)
        {
            throw new ValidationException($"cronExpression is invalid: {exception.Message}");
        }
    }

    private static DateTimeOffset AtUtc(DateTime date, TimeOnly time) =>
        new(DateTime.SpecifyKind(date.Date.Add(time.ToTimeSpan()), DateTimeKind.Utc));

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;
}

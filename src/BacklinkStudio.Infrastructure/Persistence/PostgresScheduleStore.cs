using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.EntityFrameworkCore;

namespace BacklinkStudio.Infrastructure.Persistence;

public sealed class PostgresScheduleStore(BacklinkStudioDbContext db) : IScheduleStore
{
    public void Add(Schedule schedule) => db.Schedules.Add(schedule);

    public Task<Schedule?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        var query = tracked ? db.Schedules.AsQueryable() : db.Schedules.AsNoTracking();
        return query.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Schedule>> ListAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = db.Schedules.AsNoTracking().Where(x => x.ProjectId == projectId && x.Status != ScheduleStatus.Deleted);
        if (cursor is { } value)
        {
            query = query.Where(x => x.CreatedAt < value.CreatedAt || x.CreatedAt == value.CreatedAt && x.Id.CompareTo(value.Id) < 0);
        }
        return await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<Schedule?> ClaimDueAsync(string schedulerId, TimeSpan lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var schedule = await db.Schedules
            .FromSqlInterpolated($$"""
                SELECT *
                FROM schedules
                WHERE status = 'Active'
                  AND next_run_at <= {{now}}
                  AND available_at <= {{now}}
                  AND (claim_expires_at IS NULL OR claim_expires_at <= {{now}})
                ORDER BY next_run_at, created_at, id
                FOR UPDATE SKIP LOCKED
                LIMIT 1
                """)
            .SingleOrDefaultAsync(cancellationToken);
        if (schedule is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        schedule.Claim(schedulerId, now, lease);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return schedule;
    }

    public async Task CompleteAsync(Guid scheduleId, string schedulerId, DateTimeOffset scheduledFor, DateTimeOffset? nextRunAt, int jobsQueued, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var schedule = await TrackedAsync(scheduleId, cancellationToken);
        schedule.CompleteOccurrence(schedulerId, scheduledFor, nextRunAt, jobsQueued, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RenewAsync(Guid scheduleId, string schedulerId, TimeSpan lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var schedule = await db.Schedules.SingleOrDefaultAsync(x => x.Id == scheduleId, cancellationToken);
        if (schedule is null || !string.Equals(schedule.ClaimedBy, schedulerId, StringComparison.Ordinal) || schedule.ClaimExpiresAt <= now)
        {
            return false;
        }
        schedule.RenewClaim(schedulerId, now, lease);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task FailAsync(Guid scheduleId, string schedulerId, string errorMessage, DateTimeOffset retryAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var schedule = await TrackedAsync(scheduleId, cancellationToken);
        schedule.FailOccurrence(schedulerId, errorMessage, retryAt, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(Guid scheduleId, string schedulerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var schedule = await TrackedAsync(scheduleId, cancellationToken);
        schedule.ReleaseClaim(schedulerId, now);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Schedule> TrackedAsync(Guid id, CancellationToken cancellationToken) =>
        db.Schedules.Local.SingleOrDefault(x => x.Id == id)
        ?? await db.Schedules.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
        ?? throw new ResourceNotFoundException("Schedule", id);
}

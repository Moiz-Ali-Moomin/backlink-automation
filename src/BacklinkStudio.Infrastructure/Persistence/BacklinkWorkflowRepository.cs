using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.EntityFrameworkCore;

namespace BacklinkStudio.Infrastructure.Persistence;

public sealed class BacklinkWorkflowRepository(BacklinkStudioDbContext dbContext) : IBacklinkWorkflowRepository
{
    public void Add(BacklinkWorkflow workflow) => dbContext.BacklinkWorkflows.Add(workflow);
    public void AddSource(BacklinkWorkflowSource source) => dbContext.BacklinkWorkflowSources.Add(source);

    public Task<BacklinkWorkflow?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
        (tracked ? dbContext.BacklinkWorkflows : dbContext.BacklinkWorkflows.AsNoTracking())
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<BacklinkWorkflowSource>> ListSourcesAsync(Guid workflowId, bool tracked,
        PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = (tracked ? dbContext.BacklinkWorkflowSources : dbContext.BacklinkWorkflowSources.AsNoTracking())
            .Where(x => x.WorkflowId == workflowId);
        if (cursor is { } value)
            query = query.Where(x => x.CreatedAt > value.CreatedAt ||
                x.CreatedAt == value.CreatedAt && x.Id.CompareTo(value.Id) > 0);
        return await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }

    public async Task<BacklinkWorkflowSourceCounts> CountSourcesAsync(Guid workflowId,
        CancellationToken cancellationToken)
    {
        var values = await dbContext.BacklinkWorkflowSources.AsNoTracking()
            .Where(x => x.WorkflowId == workflowId)
            .GroupBy(x => x.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var counts = values.ToDictionary(x => x.Status, x => x.Count);
        int Count(BacklinkWorkflowSourceStatus status) => counts.GetValueOrDefault(status);
        return new(values.Sum(x => x.Count), Count(BacklinkWorkflowSourceStatus.Queued),
            Count(BacklinkWorkflowSourceStatus.Checking), Count(BacklinkWorkflowSourceStatus.Submitting),
            Count(BacklinkWorkflowSourceStatus.Submitted), Count(BacklinkWorkflowSourceStatus.PendingModeration),
            Count(BacklinkWorkflowSourceStatus.Verified), Count(BacklinkWorkflowSourceStatus.Failed),
            Count(BacklinkWorkflowSourceStatus.Rejected), Count(BacklinkWorkflowSourceStatus.CommentsClosed),
            Count(BacklinkWorkflowSourceStatus.AuthenticationRequired), Count(BacklinkWorkflowSourceStatus.RateLimited),
            Count(BacklinkWorkflowSourceStatus.NotAuthorized) + Count(BacklinkWorkflowSourceStatus.Blocked),
            Count(BacklinkWorkflowSourceStatus.Unsupported));
    }

    public async Task SetSourceStatusAsync(Guid workflowId, Guid submissionSourceId,
        BacklinkWorkflowSourceStatus status, string? reason, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await dbContext.BacklinkWorkflowSources.Where(x => x.WorkflowId == workflowId &&
                x.SubmissionSourceId == submissionSourceId &&
                x.Status != BacklinkWorkflowSourceStatus.Verified)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, status)
                .SetProperty(x => x.Reason, reason)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);
    }

    public async Task ApplyVerificationAsync(Guid workflowId, Guid submissionSourceId, bool verified,
        string? reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var query = dbContext.BacklinkWorkflowSources.Where(x => x.WorkflowId == workflowId &&
            x.SubmissionSourceId == submissionSourceId &&
            x.Status != BacklinkWorkflowSourceStatus.Verified);
        if (!verified)
            query = query.Where(x => x.Status != BacklinkWorkflowSourceStatus.PendingModeration);
        await query.ExecuteUpdateAsync(setters => setters
            .SetProperty(x => x.Status, verified
                ? BacklinkWorkflowSourceStatus.Verified
                : BacklinkWorkflowSourceStatus.Failed)
            .SetProperty(x => x.Reason, verified ? null : reason)
            .SetProperty(x => x.UpdatedAt, now), cancellationToken);
        await SettleAsync(workflowId, now, cancellationToken);
    }

    public async Task SettleAsync(Guid workflowId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var hasActive = await dbContext.BacklinkWorkflowSources.AsNoTracking()
            .AnyAsync(x => x.WorkflowId == workflowId &&
                (x.Status == BacklinkWorkflowSourceStatus.Queued ||
                 x.Status == BacklinkWorkflowSourceStatus.Checking ||
                 x.Status == BacklinkWorkflowSourceStatus.Submitting), cancellationToken);
        if (hasActive) return;
        var hasFailures = await dbContext.BacklinkWorkflowSources.AsNoTracking()
            .AnyAsync(x => x.WorkflowId == workflowId && x.Status != BacklinkWorkflowSourceStatus.Submitted &&
                x.Status != BacklinkWorkflowSourceStatus.Verified &&
                x.Status != BacklinkWorkflowSourceStatus.PendingModeration, cancellationToken);
        await dbContext.BacklinkWorkflows.Where(x => x.Id == workflowId &&
                (x.Status == BacklinkWorkflowStatus.Queued || x.Status == BacklinkWorkflowStatus.Running))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, hasFailures
                    ? BacklinkWorkflowStatus.CompletedWithFailures
                    : BacklinkWorkflowStatus.Completed)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);
    }
}

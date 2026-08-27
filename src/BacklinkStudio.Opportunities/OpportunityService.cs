using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Opportunities;

public sealed class OpportunityService(
    IProjectRepository projects,
    IOpportunityRepository opportunities,
    IJobQueue jobs,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IOpportunityService
{
    public Task<OpportunityDto?> GetAsync(Guid id, CancellationToken cancellationToken) => opportunities.GetAsync(id, cancellationToken);

    public async Task<PageResult<OpportunityDto>> ListAsync(Guid projectId, int? minimumQuality, int? maximumRisk, PageRequest page, CancellationToken cancellationToken)
    {
        ValidateScore(minimumQuality, nameof(minimumQuality));
        ValidateScore(maximumRisk, nameof(maximumRisk));
        var values = await opportunities.ListAsync(projectId, minimumQuality, maximumRisk, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var items = values.Take(page.BoundedLimit).ToArray();
        var next = values.Count > page.BoundedLimit && items.Length > 0
            ? CursorCodec.Encode(new PageCursor(items[^1].DetectedAt, items[^1].Id))
            : null;
        return new PageResult<OpportunityDto>(items, next);
    }

    public Task<OpportunitySummaryDto> SummaryAsync(Guid projectId, CancellationToken cancellationToken) => opportunities.SummaryAsync(projectId, cancellationToken);

    public async Task<JobAcceptedDto> StartAnalysisAsync(StartAnalysisCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        _ = await projects.GetAsync(command.ProjectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", command.ProjectId);
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var existing = await jobs.FindByIdempotencyAsync(command.ProjectId, JobType.Analysis, key, cancellationToken);
        if (existing is not null)
        {
            return new JobAcceptedDto(existing.Id, existing.Status.ToString().ToLowerInvariant());
        }

        var now = timeProvider.GetUtcNow();
        var payload = JsonSerializer.Serialize(new AnalysisJobPayload(1, command.ProjectId));
        var job = new PersistentJob(JobType.Analysis, command.ProjectId, null, payload, 0, now, 3, actor.RequestId, key, now);
        await jobs.EnqueueAsync(job, cancellationToken);
        BacklinkStudioTelemetry.JobsQueued.Add(1, new KeyValuePair<string, object?>("job.type", JobType.Analysis.ToString()));
        audit.Append(ProjectService.CreateAudit(actor, "analysis.enqueue", command.ProjectId, job.Id, $"jobId={job.Id}", now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new JobAcceptedDto(job.Id, "queued");
    }

    public async Task<ApproveOpportunitiesResult> ApproveAsync(ApproveOpportunitiesCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var ids = command.OpportunityIds.Distinct().ToArray();
        if (ids.Length is < 1 or > 100)
        {
            throw new ValidationException("Between 1 and 100 opportunity IDs are required.");
        }
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "opportunities_approve");
        var hash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null) return Idempotency.ReadExisting<ApproveOpportunitiesResult>(existing, hash);
        var values = await opportunities.GetTrackedAsync(command.ProjectId, ids, cancellationToken);
        if (values.Count != ids.Length)
        {
            throw new ValidationException("Every opportunity must exist in the specified project.");
        }
        var now = timeProvider.GetUtcNow();
        foreach (var opportunity in values)
        {
            opportunity.Approve(actor.ActorId, now);
        }
        var result = new ApproveOpportunitiesResult(values.Count);
        idempotency.Add(new IdempotencyRecord(scope, key, hash, "opportunity_approval", command.ProjectId, JsonSerializer.Serialize(result), now));
        audit.Append(ProjectService.CreateAudit(actor, "opportunities.approve", command.ProjectId, null, $"count={values.Count}", now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    private static void ValidateScore(int? score, string name)
    {
        if (score is < 0 or > 100)
        {
            throw new ValidationException($"{name} must be between 0 and 100.");
        }
    }
}

public sealed record AnalysisJobPayload(int Version, Guid ProjectId);

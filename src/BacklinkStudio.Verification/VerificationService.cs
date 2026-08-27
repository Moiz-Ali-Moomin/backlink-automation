using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Verification;

public sealed class VerificationService(
    IBacklinkRepository backlinks,
    IJobQueue jobs,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IVerificationService
{
    public async Task<JobAcceptedDto> StartAsync(StartVerificationCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var backlink = await backlinks.GetBacklinkAsync(command.BacklinkId, false, cancellationToken) ?? throw new ResourceNotFoundException("Backlink", command.BacklinkId);
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "verification_start");
        var requestHash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null) return Idempotency.ReadExisting<JobAcceptedDto>(existing, requestHash);
        var now = timeProvider.GetUtcNow();
        var queueKey = Idempotency.HashRequest(new { actor.ActorType, actor.ActorId, Key = key });
        var job = new PersistentJob(JobType.Verification, backlink.ProjectId, backlink.CampaignId, JsonSerializer.Serialize(new VerificationJobPayload(1, backlink.Id)), 0, now, 3, actor.RequestId, queueKey, now, backlink.Domain);
        await jobs.EnqueueAsync(job, cancellationToken);
        var result = new JobAcceptedDto(job.Id, "queued");
        idempotency.Add(new IdempotencyRecord(scope, key, requestHash, "job", job.Id, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "verification.enqueue", backlink.ProjectId, backlink.CampaignId, job.Id, actor.RequestId, $"backlinkId={backlink.Id}", "queued", actor.SourceAddress, now));
        BacklinkStudioTelemetry.JobsQueued.Add(1, new KeyValuePair<string, object?>("job.type", JobType.Verification.ToString()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<BacklinkDto?> GetAsync(Guid id, CancellationToken cancellationToken) => (await backlinks.GetBacklinkAsync(id, false, cancellationToken))?.ToDto();

    public async Task<PageResult<BacklinkDto>> ListAsync(Guid projectId, BacklinkStatus? status, PageRequest page, CancellationToken cancellationToken)
    {
        var rows = await backlinks.ListAsync(projectId, status, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var values = rows.Take(page.BoundedLimit).ToArray();
        var next = rows.Count > page.BoundedLimit && values.Length > 0 ? CursorCodec.Encode(new PageCursor(values[^1].CreatedAt, values[^1].Id)) : null;
        return new(values.Select(x => x.ToDto()).ToArray(), next);
    }

    public async Task<PageResult<VerificationCheckDto>> HistoryAsync(Guid backlinkId, PageRequest page, CancellationToken cancellationToken)
    {
        _ = await backlinks.GetBacklinkAsync(backlinkId, false, cancellationToken) ?? throw new ResourceNotFoundException("Backlink", backlinkId);
        var rows = await backlinks.ListHistoryAsync(backlinkId, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var values = rows.Take(page.BoundedLimit).ToArray();
        var next = rows.Count > page.BoundedLimit && values.Length > 0 ? CursorCodec.Encode(new PageCursor(values[^1].CheckedAt, values[^1].Id)) : null;
        return new(values.Select(x => x.ToDto()).ToArray(), next);
    }
}

public sealed record VerificationJobPayload(int Version, Guid BacklinkId);

using System.Text.Json;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public sealed class JobService(
    IJobQueue jobs,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IJobService
{
    public async Task<JobDto?> GetAsync(Guid id, CancellationToken cancellationToken) => (await jobs.GetAsync(id, cancellationToken))?.ToDto();

    public async Task<PageResult<JobDto>> ListAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken)
    {
        var values = await jobs.ListAsync(projectId, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var items = values.Take(page.BoundedLimit).ToArray();
        var next = values.Count > page.BoundedLimit && items.Length > 0
            ? CursorCodec.Encode(new PageCursor(items[^1].CreatedAt, items[^1].Id))
            : null;
        return new PageResult<JobDto>(items.Select(x => x.ToDto()).ToArray(), next);
    }

    public Task<JobDto> PauseAsync(ChangeJobStateCommand command, ActorContext actor, CancellationToken cancellationToken) =>
        ChangeStateAsync(command, actor, "job_pause", "job.pause", jobs.PauseAsync, cancellationToken);

    public Task<JobDto> ResumeAsync(ChangeJobStateCommand command, ActorContext actor, CancellationToken cancellationToken) =>
        ChangeStateAsync(command, actor, "job_resume", "job.resume", jobs.ResumeAsync, cancellationToken);

    public Task<JobDto> RedriveAsync(ChangeJobStateCommand command, ActorContext actor, CancellationToken cancellationToken) =>
        ChangeStateAsync(command, actor, "job_redrive", "job.redrive", jobs.RedriveAsync, cancellationToken);

    private async Task<JobDto> ChangeStateAsync(
        ChangeJobStateCommand command,
        ActorContext actor,
        string idempotencyOperation,
        string auditOperation,
        Func<Guid, DateTimeOffset, CancellationToken, Task<PersistentJob>> transition,
        CancellationToken cancellationToken)
    {
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, idempotencyOperation);
        var requestHash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null)
        {
            return Idempotency.ReadExisting<JobDto>(existing, requestHash);
        }

        var now = timeProvider.GetUtcNow();
        var job = await transition(command.JobId, now, cancellationToken);
        var result = job.ToDto();
        idempotency.Add(new IdempotencyRecord(scope, key, requestHash, "job", job.Id, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, auditOperation, job.ProjectId, job.CampaignId, job.Id, actor.RequestId, $"status={job.Status}", "succeeded", actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }
}

using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Discovery;

public sealed class DiscoveryService(
    IProjectRepository projects,
    IDiscoveryRepository discovery,
    IJobQueue jobs,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IDiscoveryService
{
    public async Task<DiscoveryAcceptedDto> StartAsync(StartDiscoveryCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        _ = await projects.GetAsync(command.ProjectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", command.ProjectId);
        DiscoveryCommandValidation.Validate(command);
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "discovery_start");
        var requestHash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null)
        {
            return Idempotency.ReadExisting<DiscoveryAcceptedDto>(existing, requestHash);
        }

        var now = timeProvider.GetUtcNow();
        var input = new DiscoveryInput(command.Query?.Trim(), command.Content, command.Urls?.ToArray() ?? [], command.MaximumResults);
        var inputJson = JsonSerializer.Serialize(input);
        if (inputJson.Length > 1_048_576)
        {
            throw new ValidationException("Discovery input must not exceed 1 MiB.");
        }

        var query = new DiscoveryQuery(command.ProjectId, command.Provider, command.Query, inputJson, command.MaximumResults, now);
        var run = new DiscoveryRun(command.ProjectId, query.Id, now);
        var payload = JsonSerializer.Serialize(new DiscoveryJobPayload(1, run.Id));
        var queueKey = Idempotency.HashRequest(new { actor.ActorType, actor.ActorId, Key = key });
        var job = new PersistentJob(JobType.Discovery, command.ProjectId, null, payload, 0, now, 3, actor.RequestId, queueKey, now);
        run.AttachJob(job.Id);
        discovery.Add(query);
        discovery.Add(run);
        await jobs.EnqueueAsync(job, cancellationToken);

        var result = new DiscoveryAcceptedDto(run.Id, job.Id, "queued");
        idempotency.Add(new IdempotencyRecord(scope, key, requestHash, "discovery_run", run.Id, JsonSerializer.Serialize(result), now));
        audit.Append(ProjectService.CreateAudit(actor, "discovery.enqueue", command.ProjectId, job.Id, $"provider={DiscoveryProviderNames.For(command.Provider)};maximumResults={command.MaximumResults};urlInputs={input.Urls.Length};hasContent={input.Content is not null}", now));
        BacklinkStudioTelemetry.JobsQueued.Add(1, new KeyValuePair<string, object?>("job.type", JobType.Discovery.ToString()));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public Task<DiscoveryRunDto?> GetRunAsync(Guid id, CancellationToken cancellationToken) => discovery.GetAsync(id, cancellationToken);

    public async Task<PageResult<DiscoveryRunDto>> ListRunsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken)
    {
        _ = await projects.GetAsync(projectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", projectId);
        var values = await discovery.ListAsync(projectId, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var items = values.Take(page.BoundedLimit).ToArray();
        var next = values.Count > page.BoundedLimit && items.Length > 0
            ? CursorCodec.Encode(new PageCursor(items[^1].CreatedAt, items[^1].Id))
            : null;
        return new PageResult<DiscoveryRunDto>(items, next);
    }

}

public sealed record DiscoveryInput(string? Query, string? Content, string[] Urls, int MaximumResults);
public sealed record DiscoveryJobPayload(int Version, Guid DiscoveryRunId);

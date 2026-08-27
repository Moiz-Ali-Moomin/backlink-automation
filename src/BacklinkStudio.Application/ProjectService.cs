using System.Text.Json;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public sealed class ProjectService(
    IProjectRepository projects,
    IPolicyRepository policies,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    IUrlNormalizer urlNormalizer,
    TimeProvider timeProvider) : IProjectService
{
    public async Task<ProjectDto> CreateAsync(CreateProjectCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "project_create");
        var requestHash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null)
        {
            return Idempotency.ReadExisting<ProjectDto>(existing, requestHash);
        }

        var domain = NormalizeDomain(command.PrimaryDomain);
        var now = timeProvider.GetUtcNow();
        var project = new Project(command.Name, domain, command.Description, now);
        var result = project.ToDto();
        projects.Add(project);
        policies.Add(new PolicyDefinition(project.Id, now));
        idempotency.Add(new IdempotencyRecord(scope, key, requestHash, "project", project.Id, JsonSerializer.Serialize(result), now));
        audit.Append(CreateAudit(actor, "project.create", project.Id, null, $"name={project.Name};domain={project.PrimaryDomain}", now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<ProjectDto?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (await projects.GetAsync(id, cancellationToken))?.ToDto();

    public async Task<PageResult<ProjectDto>> ListAsync(PageRequest page, CancellationToken cancellationToken)
    {
        var items = await projects.ListAsync(CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        return ToPage(items, page.BoundedLimit, x => x.CreatedAt, x => x.Id, x => x.ToDto());
    }

    public async Task<TargetDto> AddTargetAsync(AddTargetCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        _ = await projects.GetAsync(command.ProjectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", command.ProjectId);
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "target_add");
        var requestHash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null)
        {
            return Idempotency.ReadExisting<TargetDto>(existing, requestHash);
        }

        var normalized = urlNormalizer.Normalize(command.Url);
        if (!normalized.IsValid)
        {
            throw new ValidationException(normalized.Error!);
        }

        var now = timeProvider.GetUtcNow();
        var target = new ProjectTarget(command.ProjectId, command.Url, normalized.NormalizedUrl!, command.Label, command.Keywords?.ToArray() ?? [], command.PreferredAnchor, command.Category, command.Priority, now);
        var result = target.ToDto();
        projects.AddTarget(target);
        idempotency.Add(new IdempotencyRecord(scope, key, requestHash, "project_target", target.Id, JsonSerializer.Serialize(result), now));
        audit.Append(CreateAudit(actor, "target.add", command.ProjectId, null, $"targetId={target.Id};domain={normalized.Domain}", now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<PageResult<TargetDto>> ListTargetsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken)
    {
        _ = await projects.GetAsync(projectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", projectId);
        var items = await projects.ListTargetsAsync(projectId, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        return ToPage(items, page.BoundedLimit, x => x.CreatedAt, x => x.Id, x => x.ToDto());
    }

    private string NormalizeDomain(string input)
    {
        var value = input.Contains("://", StringComparison.Ordinal) ? input : $"https://{input}";
        var normalized = urlNormalizer.Normalize(value);
        if (!normalized.IsValid)
        {
            throw new ValidationException("Primary domain is invalid.");
        }

        return normalized.Domain!;
    }

    private static PageResult<TOut> ToPage<TIn, TOut>(IReadOnlyList<TIn> items, int limit, Func<TIn, DateTimeOffset> date, Func<TIn, Guid> id, Func<TIn, TOut> map)
    {
        var values = items.Take(limit).ToArray();
        var next = items.Count > limit && values.Length > 0 ? CursorCodec.Encode(new PageCursor(date(values[^1]), id(values[^1]))) : null;
        return new PageResult<TOut>(values.Select(map).ToArray(), next);
    }

    public static AuditEvent CreateAudit(ActorContext actor, string operation, Guid? projectId, Guid? jobId, string summary, DateTimeOffset now) =>
        new(actor.ActorType, actor.ActorId, actor.CredentialId, operation, projectId, null, jobId, actor.RequestId, summary, "succeeded", actor.SourceAddress, now);
}

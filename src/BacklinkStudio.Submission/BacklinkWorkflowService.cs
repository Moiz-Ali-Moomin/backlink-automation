using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Submission;

public sealed class BacklinkWorkflowService(
    IProjectRepository projects,
    ISubmissionSourceRepository sources,
    ISubmissionContentRepository content,
    IBacklinkWorkflowRepository workflows,
    IUrlNormalizer urlNormalizer,
    IJobQueue jobs,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IBacklinkWorkflowService
{
    private const int MaximumInlineSources = 10_000;
    private const int MaximumPoolMembers = 1_000;

    public async Task<BacklinkWorkflowAcceptedDto> StartAsync(StartBacklinkWorkflowCommand command,
        ActorContext actor, CancellationToken cancellationToken)
    {
        ValidateShape(command);
        var key = Idempotency.RequireKey(command.ClientRequestKey);
        var scope = Idempotency.Scope(actor, "backlink_workflow_start");
        var hash = Idempotency.HashRequest(command with { ClientRequestKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null) return Idempotency.ReadExisting<BacklinkWorkflowAcceptedDto>(existing, hash);

        _ = await projects.GetAsync(command.ProjectId, cancellationToken)
            ?? throw new ResourceNotFoundException("Project", command.ProjectId);

        var now = timeProvider.GetUtcNow();
        var identityPoolId = await ResolveIdentityPoolAsync(command, now, cancellationToken);
        var templatePoolId = await ResolveTemplatePoolAsync(command, now, cancellationToken);
        var target = urlNormalizer.Normalize(command.TargetUrl);
        if (!target.IsValid) throw new ValidationException("Target URL must be an absolute HTTP(S) URL without user information.");

        var global = command.GlobalConcurrency ?? 100;
        var perDomain = command.PerDomainConcurrency ?? 2;
        var delay = command.PerDomainDelayMilliseconds ?? 1_000;
        var attempts = command.MaximumAttempts ?? 3;
        var verificationDelay = command.VerificationDelaySeconds ?? 3_600;
        var workflow = new BacklinkWorkflow(command.ProjectId, command.SourceImportId, identityPoolId, templatePoolId,
            target.NormalizedUrl!, global, perDomain, delay, attempts, verificationDelay, now);
        workflows.Add(workflow);

        var candidates = command.SourceImportId is { } importId
            ? await FromImportAsync(command.ProjectId, importId, cancellationToken)
            : NormalizeInline(command.SourceUrls!);
        foreach (var candidate in candidates)
            workflows.AddSource(new BacklinkWorkflowSource(workflow.Id, candidate.OriginalUrl,
                candidate.NormalizedUrl, candidate.Domain, candidate.Host, BacklinkWorkflowSourceStatus.Queued,
                null, now));

        var payload = new BacklinkWorkflowJobPayload(1, workflow.Id);
        var job = new PersistentJob(JobType.BacklinkWorkflow, command.ProjectId, null,
            JsonSerializer.Serialize(payload), 10, now, 5, workflow.Id.ToString("D"),
            $"backlink-workflow:{workflow.Id}:orchestrate", now);
        await jobs.EnqueueAsync(job, cancellationToken);
        workflow.Queue(job.Id, [], now);
        var result = new BacklinkWorkflowAcceptedDto(workflow.Id, null, job.Id, "queued");
        idempotency.Add(new IdempotencyRecord(scope, key, hash, "backlink_workflow", workflow.Id,
            JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "backlink_workflow.queued",
            command.ProjectId, result.CampaignId, job.Id, actor.RequestId,
            $"workflowId={workflow.Id};sources={candidates.Count};authorization=pending",
            "queued", actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<BacklinkWorkflowStatusDto?> GetAsync(Guid workflowId, PageRequest page,
        CancellationToken cancellationToken)
    {
        var workflow = await workflows.GetAsync(workflowId, false, cancellationToken);
        if (workflow is null) return null;
        var counts = await workflows.CountSourcesAsync(workflowId, cancellationToken);
        var rows = await workflows.ListSourcesAsync(workflowId, false, CursorCodec.Decode(page.Cursor),
            page.BoundedLimit + 1, cancellationToken);
        var values = rows.Take(page.BoundedLimit).ToArray();
        var next = rows.Count > page.BoundedLimit && values.Length > 0
            ? CursorCodec.Encode(new PageCursor(values[^1].CreatedAt, values[^1].Id)) : null;
        var mapped = new PageResult<BacklinkWorkflowSourceResultDto>(values.Select(value =>
            new BacklinkWorkflowSourceResultDto(value.Id, value.OriginalUrl, value.Status, value.Reason,
                value.UpdatedAt)).ToArray(), next);
        return new(workflow.Id, workflow.Status, counts.Total, counts.Queued, counts.Checking, counts.Submitting,
            counts.Submitted, counts.PendingModeration, counts.Verified, counts.Failed, counts.Rejected,
            counts.CommentsClosed, counts.AuthenticationRequired, counts.RateLimited, counts.NotAuthorized,
            counts.Unsupported, mapped);
    }

    private async Task<Guid> ResolveIdentityPoolAsync(StartBacklinkWorkflowCommand command, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (command.IdentityPoolId is { } existingId)
        {
            var existing = await content.GetIdentityPoolAsync(existingId, false, cancellationToken)
                ?? throw new ResourceNotFoundException("SubmissionIdentityPool", existingId);
            if (existing.ProjectId != command.ProjectId || !existing.Enabled)
                throw new ValidationException("The identity pool must be enabled and belong to the project.");
            return existing.Id;
        }
        var values = command.Identities!;
        if (values.Count is < 1 or > MaximumPoolMembers)
            throw new ValidationException("Direct identities must contain between 1 and 1000 name/email pairs.");
        var pool = new SubmissionIdentityPool(command.ProjectId, $"Workflow identities {Guid.CreateVersion7(now):N}",
            PoolSelectionStrategy.RoundRobin, IdentityEmailStrategy.Fixed, null, null, true, now);
        content.AddIdentityPool(pool);
        foreach (var value in values)
            content.AddIdentity(new SubmissionIdentity(pool.Id, value.Name, value.Email, null, null, true, 1, now));
        return pool.Id;
    }

    private async Task<Guid> ResolveTemplatePoolAsync(StartBacklinkWorkflowCommand command, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (command.TemplatePoolId is { } existingId)
        {
            var existing = await content.GetTemplatePoolAsync(existingId, false, cancellationToken)
                ?? throw new ResourceNotFoundException("SubmissionTemplatePool", existingId);
            if (existing.ProjectId != command.ProjectId || !existing.Enabled)
                throw new ValidationException("The template pool must be enabled and belong to the project.");
            return existing.Id;
        }
        var values = command.Comments!;
        if (values.Count is < 1 or > MaximumPoolMembers)
            throw new ValidationException("Direct comments must contain between 1 and 1000 templates.");
        var pool = new SubmissionTemplatePool(command.ProjectId, $"Workflow comments {Guid.CreateVersion7(now):N}",
            SubmissionTemplateType.WordPressComment, PoolSelectionStrategy.RoundRobin,
            BacklinkPlacementMethod.WebsiteField, true, now);
        content.AddTemplatePool(pool);
        for (var index = 0; index < values.Count; index++)
            content.AddTemplate(new SubmissionTemplate(pool.Id, $"Comment {index + 1}", values[index], [], [], [], [],
                true, 1, now));
        return pool.Id;
    }

    private async Task<IReadOnlyList<SourceCandidate>> FromImportAsync(Guid projectId, Guid importId,
        CancellationToken cancellationToken)
    {
        var sourceImport = await sources.GetImportAsync(importId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionSourceImport", importId);
        if (sourceImport.ProjectId != projectId) throw new ValidationException("The source import belongs to another project.");
        if (sourceImport.Status != SubmissionSourceImportStatus.Completed)
            throw new ConflictException("The selected source import must be completed before starting a workflow.");
        var rows = await sources.ListByImportAsync(importId, MaximumInlineSources + 1, cancellationToken);
        if (rows.Count is < 1 or > MaximumInlineSources)
            throw new ValidationException("The source import must contain between 1 and 10000 sources for the simple workflow.");
        return rows.Select(source => new SourceCandidate(source.OriginalUrl, source.NormalizedUrl, source.Domain,
            source.Host)).ToArray();
    }

    private SourceCandidate[] NormalizeInline(IReadOnlyList<string> sourceUrls)
    {
        if (sourceUrls.Count is < 1 or > MaximumInlineSources)
            throw new ValidationException("Direct source URLs must contain between 1 and 10000 values.");
        var values = new Dictionary<string, SourceCandidate>(StringComparer.Ordinal);
        foreach (var value in sourceUrls)
        {
            var normalized = urlNormalizer.Normalize(value);
            if (!normalized.IsValid) throw new ValidationException("Every source URL must be an absolute HTTP(S) URL without user information.");
            var uri = new Uri(normalized.NormalizedUrl!, UriKind.Absolute);
            values.TryAdd(normalized.NormalizedUrl!, new(value.Trim(), normalized.NormalizedUrl!, normalized.Domain!,
                uri.IdnHost.ToLowerInvariant()));
        }
        return values.Values.ToArray();
    }

    private static void ValidateShape(StartBacklinkWorkflowCommand command)
    {
        if ((command.SourceUrls is null || command.SourceUrls.Count == 0) == (command.SourceImportId is null))
            throw new ValidationException("Provide exactly one of sourceUrls or sourceImportId.");
        if ((command.Identities is null || command.Identities.Count == 0) == (command.IdentityPoolId is null))
            throw new ValidationException("Provide exactly one of identities or identityPoolId.");
        if ((command.Comments is null || command.Comments.Count == 0) == (command.TemplatePoolId is null))
            throw new ValidationException("Provide exactly one of comments or templatePoolId.");
    }

    private sealed record SourceCandidate(string OriginalUrl, string NormalizedUrl, string Domain, string Host);
}

public sealed record BacklinkWorkflowJobPayload(int Version, Guid WorkflowId);

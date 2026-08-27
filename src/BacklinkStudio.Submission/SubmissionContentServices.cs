using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Submission;

public sealed class SubmissionIdentityService(
    IProjectRepository projects,
    ISubmissionContentRepository repository,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ISubmissionIdentityService
{
    public async Task<IdentityPoolDto> CreatePoolAsync(CreateIdentityPoolCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var replay = await ReplayAsync<IdentityPoolDto>(command.IdempotencyKey, actor, "identity_pool_create", command with { IdempotencyKey = string.Empty }, cancellationToken);
        if (replay!.Value.Result is not null) return replay.Value.Result;
        _ = await projects.GetAsync(command.ProjectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", command.ProjectId);
        if (await repository.FindIdentityPoolByNameAsync(command.ProjectId, command.Name.Trim(), cancellationToken) is not null)
            throw new ConflictException("An identity pool with this name already exists in the project.");
        var now = timeProvider.GetUtcNow();
        var pool = new SubmissionIdentityPool(command.ProjectId, command.Name, command.SelectionStrategy,
            command.EmailStrategy, command.EmailBaseAddress, command.CatchAllDomain, command.Enabled, now);
        repository.AddIdentityPool(pool);
        var result = pool.ToDto();
        CompleteMutation(replay!.Value, actor, "identity_pool.create", pool.ProjectId, pool.Id, result, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<IdentityPoolDto> UpdatePoolAsync(UpdateIdentityPoolCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var replay = await ReplayAsync<IdentityPoolDto>(command.IdempotencyKey, actor, "identity_pool_update", command with { IdempotencyKey = string.Empty }, cancellationToken);
        if (replay!.Value.Result is not null) return replay.Value.Result;
        var pool = await repository.GetIdentityPoolAsync(command.PoolId, true, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionIdentityPool", command.PoolId);
        var duplicate = await repository.FindIdentityPoolByNameAsync(pool.ProjectId, command.Name.Trim(), cancellationToken);
        if (duplicate is not null && duplicate.Id != pool.Id) throw new ConflictException("An identity pool with this name already exists in the project.");
        var now = timeProvider.GetUtcNow();
        pool.Update(command.Name, command.SelectionStrategy, command.EmailStrategy, command.EmailBaseAddress, command.CatchAllDomain, command.Enabled, now);
        var result = pool.ToDto();
        CompleteMutation(replay!.Value, actor, "identity_pool.update", pool.ProjectId, pool.Id, result, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<SubmissionIdentityDto> CreateIdentityAsync(CreateIdentityCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var replay = await ReplayAsync<SubmissionIdentityDto>(command.IdempotencyKey, actor, "identity_create", command with { IdempotencyKey = string.Empty }, cancellationToken);
        if (replay!.Value.Result is not null) return replay.Value.Result;
        var pool = await repository.GetIdentityPoolAsync(command.PoolId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionIdentityPool", command.PoolId);
        var now = timeProvider.GetUtcNow();
        var identity = new SubmissionIdentity(pool.Id, command.DisplayName, command.Email, command.Website, command.Organization, command.Enabled, command.Weight, now);
        repository.AddIdentity(identity);
        var result = identity.ToDto();
        CompleteMutation(replay!.Value, actor, "identity.create", pool.ProjectId, identity.Id, result, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<SubmissionIdentityDto> UpdateIdentityAsync(UpdateIdentityCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var replay = await ReplayAsync<SubmissionIdentityDto>(command.IdempotencyKey, actor, "identity_update", command with { IdempotencyKey = string.Empty }, cancellationToken);
        if (replay!.Value.Result is not null) return replay.Value.Result;
        var identity = await repository.GetIdentityAsync(command.IdentityId, true, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionIdentity", command.IdentityId);
        var pool = await repository.GetIdentityPoolAsync(identity.PoolId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionIdentityPool", identity.PoolId);
        var now = timeProvider.GetUtcNow();
        identity.Update(command.DisplayName, command.Email, command.Website, command.Organization, command.Enabled, command.Weight, now);
        var result = identity.ToDto();
        CompleteMutation(replay!.Value, actor, "identity.update", pool.ProjectId, identity.Id, result, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<IdentityPoolDto?> GetPoolAsync(Guid id, CancellationToken cancellationToken) =>
        (await repository.GetIdentityPoolAsync(id, false, cancellationToken))?.ToDto();

    public async Task<SubmissionIdentityDto?> GetIdentityAsync(Guid id, CancellationToken cancellationToken) =>
        (await repository.GetIdentityAsync(id, false, cancellationToken))?.ToDto();

    public async Task<PageResult<IdentityPoolDto>> ListPoolsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken)
    {
        var rows = await repository.ListIdentityPoolsAsync(projectId, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        return Page(rows, page, x => x.ToDto());
    }

    public async Task<PageResult<SubmissionIdentityDto>> ListIdentitiesAsync(Guid poolId, PageRequest page, CancellationToken cancellationToken)
    {
        var rows = await repository.ListIdentitiesAsync(poolId, false, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        return Page(rows, page, x => x.ToDto());
    }

    private async Task<(string Scope, string Key, string Hash, T Result)?> ReplayAsync<T>(string inputKey, ActorContext actor,
        string operation, object request, CancellationToken cancellationToken)
    {
        var key = Idempotency.RequireKey(inputKey);
        var scope = Idempotency.Scope(actor, operation);
        var hash = Idempotency.HashRequest(request);
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        return existing is null ? (scope, key, hash, default(T)!) : (scope, key, hash, Idempotency.ReadExisting<T>(existing, hash));
    }

    private void CompleteMutation<T>((string Scope, string Key, string Hash, T Result) state, ActorContext actor,
        string action, Guid projectId, Guid entityId, T result, DateTimeOffset now)
    {
        idempotency.Add(new IdempotencyRecord(state.Scope, state.Key, state.Hash, action, entityId, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, action, projectId, null, null,
            actor.RequestId, $"entityId={entityId}", "succeeded", actor.SourceAddress, now));
    }

    private static PageResult<TDto> Page<TEntity, TDto>(IReadOnlyList<TEntity> rows, PageRequest page, Func<TEntity, TDto> map)
        where TEntity : class
    {
        var values = rows.Take(page.BoundedLimit).ToArray();
        var next = rows.Count > page.BoundedLimit && values.Length > 0
            ? CursorCodec.Encode(new PageCursor((DateTimeOffset)values[^1].GetType().GetProperty("CreatedAt")!.GetValue(values[^1])!,
                (Guid)values[^1].GetType().GetProperty("Id")!.GetValue(values[^1])!)) : null;
        return new(values.Select(map).ToArray(), next);
    }
}

public sealed class SubmissionTemplateService(
    IProjectRepository projects,
    ISubmissionContentRepository repository,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ISubmissionTemplateService
{
    public async Task<TemplatePoolDto> CreatePoolAsync(CreateTemplatePoolCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var state = await BeginAsync<TemplatePoolDto>(command.IdempotencyKey, actor, "template_pool_create", command with { IdempotencyKey = string.Empty }, cancellationToken);
        if (state.Replayed) return state.Result!;
        _ = await projects.GetAsync(command.ProjectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", command.ProjectId);
        if (await repository.FindTemplatePoolByNameAsync(command.ProjectId, command.Name.Trim(), cancellationToken) is not null)
            throw new ConflictException("A template pool with this name already exists in the project.");
        var now = timeProvider.GetUtcNow();
        var pool = new SubmissionTemplatePool(command.ProjectId, command.Name, command.TemplateType, command.SelectionStrategy, command.PlacementMethod, command.Enabled, now);
        repository.AddTemplatePool(pool);
        var result = pool.ToDto();
        Finish(state, actor, "template_pool.create", pool.ProjectId, pool.Id, result, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<TemplatePoolDto> UpdatePoolAsync(UpdateTemplatePoolCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var state = await BeginAsync<TemplatePoolDto>(command.IdempotencyKey, actor, "template_pool_update", command with { IdempotencyKey = string.Empty }, cancellationToken);
        if (state.Replayed) return state.Result!;
        var pool = await repository.GetTemplatePoolAsync(command.PoolId, true, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionTemplatePool", command.PoolId);
        var duplicate = await repository.FindTemplatePoolByNameAsync(pool.ProjectId, command.Name.Trim(), cancellationToken);
        if (duplicate is not null && duplicate.Id != pool.Id) throw new ConflictException("A template pool with this name already exists in the project.");
        var now = timeProvider.GetUtcNow();
        pool.Update(command.Name, command.TemplateType, command.SelectionStrategy, command.PlacementMethod, command.Enabled, now);
        var result = pool.ToDto();
        Finish(state, actor, "template_pool.update", pool.ProjectId, pool.Id, result, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<SubmissionTemplateDto> CreateTemplateAsync(CreateTemplateCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var state = await BeginAsync<SubmissionTemplateDto>(command.IdempotencyKey, actor, "template_create", command with { IdempotencyKey = string.Empty }, cancellationToken);
        if (state.Replayed) return state.Result!;
        var pool = await repository.GetTemplatePoolAsync(command.PoolId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionTemplatePool", command.PoolId);
        var now = timeProvider.GetUtcNow();
        var template = new SubmissionTemplate(pool.Id, command.Name, command.Body, command.PrefixVariants, command.SuffixVariants,
            command.AnchorVariants, command.TargetUrlVariants, command.Enabled, command.Weight, now);
        repository.AddTemplate(template);
        var result = template.ToDto();
        Finish(state, actor, "template.create", pool.ProjectId, template.Id, result, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<SubmissionTemplateDto> UpdateTemplateAsync(UpdateTemplateCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var state = await BeginAsync<SubmissionTemplateDto>(command.IdempotencyKey, actor, "template_update", command with { IdempotencyKey = string.Empty }, cancellationToken);
        if (state.Replayed) return state.Result!;
        var template = await repository.GetTemplateAsync(command.TemplateId, true, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionTemplate", command.TemplateId);
        var pool = await repository.GetTemplatePoolAsync(template.PoolId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionTemplatePool", template.PoolId);
        var now = timeProvider.GetUtcNow();
        template.Update(command.Name, command.Body, command.PrefixVariants, command.SuffixVariants, command.AnchorVariants,
            command.TargetUrlVariants, command.Enabled, command.Weight, now);
        var result = template.ToDto();
        Finish(state, actor, "template.update", pool.ProjectId, template.Id, result, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<TemplatePoolDto?> GetPoolAsync(Guid id, CancellationToken cancellationToken) =>
        (await repository.GetTemplatePoolAsync(id, false, cancellationToken))?.ToDto();
    public async Task<SubmissionTemplateDto?> GetTemplateAsync(Guid id, CancellationToken cancellationToken) =>
        (await repository.GetTemplateAsync(id, false, cancellationToken))?.ToDto();

    public async Task<PageResult<TemplatePoolDto>> ListPoolsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken)
    {
        var rows = await repository.ListTemplatePoolsAsync(projectId, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var values = rows.Take(page.BoundedLimit).ToArray();
        return new(values.Select(x => x.ToDto()).ToArray(), Next(rows.Count, page, values.Select(x => (x.CreatedAt, x.Id)).LastOrDefault()));
    }

    public async Task<PageResult<SubmissionTemplateDto>> ListTemplatesAsync(Guid poolId, PageRequest page, CancellationToken cancellationToken)
    {
        var rows = await repository.ListTemplatesAsync(poolId, false, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var values = rows.Take(page.BoundedLimit).ToArray();
        return new(values.Select(x => x.ToDto()).ToArray(), Next(rows.Count, page, values.Select(x => (x.CreatedAt, x.Id)).LastOrDefault()));
    }

    private sealed record Mutation<T>(string Scope, string Key, string Hash, bool Replayed, T? Result);
    private async Task<Mutation<T>> BeginAsync<T>(string inputKey, ActorContext actor, string operation, object request, CancellationToken cancellationToken)
    {
        var key = Idempotency.RequireKey(inputKey);
        var scope = Idempotency.Scope(actor, operation);
        var hash = Idempotency.HashRequest(request);
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        return existing is null ? new(scope, key, hash, false, default) : new(scope, key, hash, true, Idempotency.ReadExisting<T>(existing, hash));
    }

    private void Finish<T>(Mutation<T> state, ActorContext actor, string action, Guid projectId, Guid entityId, T result, DateTimeOffset now)
    {
        idempotency.Add(new IdempotencyRecord(state.Scope, state.Key, state.Hash, action, entityId, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, action, projectId, null, null,
            actor.RequestId, $"entityId={entityId}", "succeeded", actor.SourceAddress, now));
    }

    private static string? Next(int count, PageRequest page, (DateTimeOffset CreatedAt, Guid Id) last) =>
        count > page.BoundedLimit ? CursorCodec.Encode(new PageCursor(last.CreatedAt, last.Id)) : null;
}

public sealed class SubmissionContentResolver(
    ISubmissionContentRepository content,
    ISubmissionSourceRepository sources) : ISubmissionContentResolver
{
    private const int MaximumPoolMembers = 1_000;

    public async Task<ResolvedSubmissionContent> ResolveAsync(ResolveSubmissionContentCommand command, CancellationToken cancellationToken)
    {
        if (command.AttemptNumber is < 1 or > 1_000) throw new ValidationException("Attempt number must be between 1 and 1000.");
        var source = await sources.GetAsync(command.SubmissionSourceId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionSource", command.SubmissionSourceId);
        var identityPool = await content.GetIdentityPoolAsync(command.IdentityPoolId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionIdentityPool", command.IdentityPoolId);
        var templatePool = await content.GetTemplatePoolAsync(command.TemplatePoolId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionTemplatePool", command.TemplatePoolId);
        if (!identityPool.Enabled || !templatePool.Enabled) throw new ConflictException("Selected identity and template pools must be enabled.");
        if (identityPool.ProjectId != source.ProjectId || templatePool.ProjectId != source.ProjectId)
            throw new ValidationException("Source, identity pool, and template pool must belong to the same project.");
        var identities = await content.ListIdentitiesAsync(identityPool.Id, true, null, MaximumPoolMembers + 1, cancellationToken);
        var templates = await content.ListTemplatesAsync(templatePool.Id, true, null, MaximumPoolMembers + 1, cancellationToken);
        if (identities.Count is 0 || identities.Count > MaximumPoolMembers) throw new ValidationException("Identity pool must contain between 1 and 1000 enabled identities.");
        if (templates.Count is 0 || templates.Count > MaximumPoolMembers) throw new ValidationException("Template pool must contain between 1 and 1000 enabled templates.");
        if (!Uri.TryCreate(command.TargetUrl, UriKind.Absolute, out var target) ||
            (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(target.UserInfo))
            throw new ValidationException("Target URL must be an absolute HTTP(S) URL without user information.");

        var campaignId = command.CampaignId ?? Guid.Empty;
        var identity = Select(identities, identityPool.SelectionStrategy, x => x.Weight, campaignId, source.Id, command.AttemptNumber, "identity");
        var template = Select(templates, templatePool.SelectionStrategy, x => x.Weight, campaignId, source.Id, command.AttemptNumber, "template");
        var resolvedTarget = Pick(template.TargetUrlVariants, campaignId, source.Id, command.AttemptNumber, "target") ?? target.AbsoluteUri;
        if (!Uri.TryCreate(resolvedTarget, UriKind.Absolute, out var variantTarget) ||
            (variantTarget.Scheme != Uri.UriSchemeHttp && variantTarget.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(variantTarget.UserInfo))
            throw new ValidationException("Configured target URL variants must be absolute HTTP(S) URLs without user information.");
        var prefix = Pick(template.PrefixVariants, campaignId, source.Id, command.AttemptNumber, "prefix");
        var suffix = Pick(template.SuffixVariants, campaignId, source.Id, command.AttemptNumber, "suffix");
        var anchor = Pick(template.AnchorVariants, campaignId, source.Id, command.AttemptNumber, "anchor") ?? variantTarget.IdnHost;
        var body = Render(template.Body, source, identity, variantTarget, anchor);
        var comment = string.Join(' ', new[] { prefix, body, suffix }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        if (comment.Length > 12_000) throw new ValidationException("Resolved submission content exceeds 12000 characters.");
        var email = ResolveEmail(identityPool, identity, campaignId, source.Id, command.AttemptNumber);
        var website = templatePool.PlacementMethod == BacklinkPlacementMethod.WebsiteField ? variantTarget.AbsoluteUri : identity.Website;
        var evidence = $"strategy={identityPool.SelectionStrategy};seed={campaignId:N}:{source.Id:N}:{command.AttemptNumber};identity={identity.Id};template={template.Id}";
        return new(identity.Id, template.Id, identity.DisplayName, email, website, variantTarget.AbsoluteUri,
            comment, templatePool.PlacementMethod, evidence);
    }

    private static T Select<T>(IReadOnlyList<T> values, PoolSelectionStrategy strategy, Func<T, int> weight,
        Guid campaignId, Guid sourceId, int attempt, string purpose)
    {
        var hash = Hash(campaignId, sourceId, attempt, purpose);
        if (strategy != PoolSelectionStrategy.WeightedDeterministicRandom)
            return values[(int)(hash % (ulong)values.Count)];
        var total = values.Aggregate(0L, (sum, value) => checked(sum + weight(value)));
        var selected = (long)(hash % (ulong)total);
        foreach (var value in values)
        {
            selected -= weight(value);
            if (selected < 0) return value;
        }
        return values[^1];
    }

    private static string? Pick(string[] values, Guid campaignId, Guid sourceId, int attempt, string purpose) =>
        values.Length == 0 ? null : values[(int)(Hash(campaignId, sourceId, attempt, purpose) % (ulong)values.Length)];

    private static ulong Hash(Guid campaignId, Guid sourceId, int attempt, string purpose)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{campaignId:N}:{sourceId:N}:{attempt}:{purpose}"));
        return BinaryPrimitives.ReadUInt64BigEndian(bytes);
    }

    private static string ResolveEmail(SubmissionIdentityPool pool, SubmissionIdentity identity, Guid campaignId, Guid sourceId, int attempt)
    {
        var token = Hash(campaignId, sourceId, attempt, "email").ToString("x12", CultureInfo.InvariantCulture)[..12];
        if (pool.EmailStrategy == IdentityEmailStrategy.PlusAddressing)
        {
            var baseAddress = pool.EmailBaseAddress!;
            var at = baseAddress.LastIndexOf('@');
            return $"{baseAddress[..at]}+{token}{baseAddress[at..]}";
        }
        return pool.EmailStrategy == IdentityEmailStrategy.CatchAll ? $"submission-{token}@{pool.CatchAllDomain}" : identity.Email;
    }

    private static string Render(string input, SubmissionSource source, SubmissionIdentity identity, Uri target, string anchor) => input
        .Replace("{{target_url}}", target.AbsoluteUri, StringComparison.Ordinal)
        .Replace("{{target_domain}}", target.IdnHost, StringComparison.Ordinal)
        .Replace("{{brand}}", identity.Organization ?? target.IdnHost, StringComparison.Ordinal)
        .Replace("{{display_name}}", identity.DisplayName, StringComparison.Ordinal)
        .Replace("{{source_domain}}", source.Domain, StringComparison.Ordinal)
        .Replace("{{source_title}}", source.PageTitle ?? source.Domain, StringComparison.Ordinal)
        .Replace("{{anchor}}", anchor, StringComparison.Ordinal);
}

public sealed class SubmissionPreviewService(
    ISubmissionSourceRepository sources,
    IOwnedNetworkRepository networks,
    ISubmissionContentRepository content,
    ISubmissionContentResolver resolver,
    IOwnedNetworkExecutionAuthorizer ownedNetworkAuthorizer) : ISubmissionPreviewService
{
    public async Task<SubmissionPreviewDto> PreviewAsync(SubmissionPreviewCommand command, CancellationToken cancellationToken)
    {
        var source = await sources.GetAsync(command.SubmissionSourceId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionSource", command.SubmissionSourceId);
        if (source.ProjectId != command.ProjectId) throw new ResourceNotFoundException("SubmissionSource", command.SubmissionSourceId);
        var network = await networks.GetAsync(source.OwnedNetworkProfileId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("OwnedNetworkProfile", source.OwnedNetworkProfileId);
        var identityPoolId = command.IdentityPoolId ?? network.DefaultIdentityPoolId
            ?? throw new ValidationException("An identity pool is required because the owned network has no default.");
        var templatePoolId = command.TemplatePoolId ?? network.DefaultTemplatePoolId
            ?? throw new ValidationException("A template pool is required because the owned network has no default.");
        var resolved = await resolver.ResolveAsync(new(source.Id, command.CampaignId, identityPoolId, templatePoolId,
            command.TargetUrl, command.AttemptNumber), cancellationToken);
        var identity = await content.GetIdentityAsync(resolved.IdentityId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionIdentity", resolved.IdentityId);
        var template = await content.GetTemplateAsync(resolved.TemplateId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionTemplate", resolved.TemplateId);
        var ownership = (await ownedNetworkAuthorizer.AuthorizeSourceAsync(command.ProjectId, source,
            cancellationToken)).Allowed;
        var strategy = source.RequiresManualAction ? WordPressSubmissionMode.ManualActionRequired.ToString()
            : source.SupportsOwnedWordPressApi ? WordPressSubmissionMode.DirectApi.ToString()
            : source.RequiresBrowser ? WordPressSubmissionMode.ControlledBrowser.ToString()
            : source.SupportsWordPressComment ? WordPressSubmissionMode.StandardComment.ToString()
            : WordPressSubmissionMode.ManualActionRequired.ToString();
        return new(source.AdapterName ?? "OwnedWordPressCommentAdapter", source.ToDto(), resolved.ResolvedTargetUrl,
            identity.ToDto() with { Email = resolved.Email, Website = resolved.Website }, template.ToDto(), resolved.ResolvedComment,
            resolved.PlacementMethod, ownership, source.TechnicalCompatibility, strategy, resolved.SelectionEvidence);
    }
}

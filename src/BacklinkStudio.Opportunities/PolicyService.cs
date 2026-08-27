using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Opportunities;

public sealed class PolicyService(
    IProjectRepository projects,
    IPolicyRepository policies,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    IUrlNormalizer urlNormalizer,
    TimeProvider timeProvider) : IPolicyService
{
    public async Task<PolicyDefinitionDto> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        _ = await projects.GetAsync(projectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", projectId);
        var policy = await policies.GetAsync(projectId, false, cancellationToken)
            ?? throw new InvalidOperationException("Project policy invariant is missing.");
        return policy.ToDto();
    }

    public async Task<PolicyDefinitionDto> UpdateAsync(UpdatePolicyCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        _ = await projects.GetAsync(command.ProjectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", command.ProjectId);
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "policy_update");
        var requestHash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var replay = await idempotency.FindAsync(scope, key, cancellationToken);
        if (replay is not null)
        {
            return Idempotency.ReadExisting<PolicyDefinitionDto>(replay, requestHash);
        }
        var now = timeProvider.GetUtcNow();
        var policy = await policies.GetAsync(command.ProjectId, true, cancellationToken);
        if (policy is null)
        {
            policy = new PolicyDefinition(command.ProjectId, now);
            policies.Add(policy);
        }
        policy.Update(command.AutomationEnabled, command.MinimumQualityScore, command.MaximumRiskScore, command.ManualReviewRequired, command.HourlyActionLimit, command.DailyActionLimit, command.PerDomainActionLimit, now);
        var result = policy.ToDto();
        idempotency.Add(new IdempotencyRecord(scope, key, requestHash, "policy_definition", policy.Id, JsonSerializer.Serialize(result), now));
        audit.Append(ProjectService.CreateAudit(actor, "policy.update", command.ProjectId, null, "project policy updated", now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<PageResult<BlocklistEntryDto>> ListBlocklistAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken)
    {
        _ = await projects.GetAsync(projectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", projectId);
        var values = await policies.ListBlocklistAsync(projectId, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var items = values.Take(page.BoundedLimit).Select(x => x.ToDto()).ToArray();
        var next = values.Count > page.BoundedLimit && items.Length > 0 ? CursorCodec.Encode(new PageCursor(items[^1].CreatedAt, items[^1].Id)) : null;
        return new(items, next);
    }

    public async Task<BlocklistEntryDto> AddBlocklistAsync(AddBlocklistEntryCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        _ = await projects.GetAsync(command.ProjectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", command.ProjectId);
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "blocklist_add");
        var requestHash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var replay = await idempotency.FindAsync(scope, key, cancellationToken);
        if (replay is not null)
        {
            return Idempotency.ReadExisting<BlocklistEntryDto>(replay, requestHash);
        }
        var value = NormalizeValue(command.MatchType, command.Value);
        if (await policies.FindBlocklistAsync(command.ProjectId, command.MatchType, value, cancellationToken) is not null)
        {
            throw new ConflictException("An equivalent blocklist entry already exists.");
        }
        var now = timeProvider.GetUtcNow();
        var entry = new BlocklistEntry(command.ProjectId, command.MatchType, value, command.Reason, now);
        var result = entry.ToDto();
        policies.Add(entry);
        idempotency.Add(new IdempotencyRecord(scope, key, requestHash, "blocklist_entry", entry.Id, JsonSerializer.Serialize(result), now));
        audit.Append(ProjectService.CreateAudit(actor, "blocklist.add", command.ProjectId, null, $"matchType={command.MatchType};value={value}", now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    private string NormalizeValue(BlocklistMatchType matchType, string value)
    {
        if (matchType is BlocklistMatchType.Domain or BlocklistMatchType.Host)
        {
            var candidate = value.Trim().TrimEnd('.').ToLowerInvariant();
            var normalized = urlNormalizer.Normalize("https://" + candidate + "/");
            if (!normalized.IsValid || normalized.Domain is null || !string.Equals(candidate, normalized.Domain, StringComparison.Ordinal))
            {
                throw new ValidationException("Blocklist domain or host is invalid.");
            }
            return normalized.Domain;
        }

        if (matchType is BlocklistMatchType.SubmissionSource or BlocklistMatchType.OwnedNetworkProfile or BlocklistMatchType.Campaign)
        {
            return Guid.TryParse(value, out var id) && id != Guid.Empty
                ? id.ToString("D")
                : throw new ValidationException("The blocklist identifier must be a non-empty UUID.");
        }

        var url = urlNormalizer.Normalize(value);
        return url.IsValid ? url.NormalizedUrl! : throw new ValidationException(url.Error ?? "Blocklist URL prefix is invalid.");
    }
}

using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Submission;

public sealed class OwnedNetworkService(
    IProjectRepository projects,
    IOwnedNetworkRepository networks,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IOwnedNetworkService
{
    public async Task<OwnedNetworkProfileDto> CreateAsync(CreateOwnedNetworkCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "owned_network_create");
        var hash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null) return Idempotency.ReadExisting<OwnedNetworkProfileDto>(existing, hash);
        _ = await projects.GetAsync(command.ProjectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", command.ProjectId);
        if (await networks.FindByNameAsync(command.ProjectId, command.Name.Trim(), cancellationToken) is not null)
            throw new ConflictException("An owned network with this name already exists in the project.");

        var now = timeProvider.GetUtcNow();
        var profile = new OwnedNetworkProfile(command.ProjectId, command.Name, command.Description, command.OwnershipStatus,
            command.AutomationPermitted, command.OptionalNetworkTag, command.DefaultIdentityPoolId, command.DefaultTemplatePoolId,
            command.MaxConcurrency, command.PerDomainConcurrency, command.PerDomainDelayMilliseconds, command.Enabled, now);
        var domains = BuildDomains(profile.Id, command.Domains, now);
        networks.Add(profile);
        foreach (var domain in domains) networks.AddDomain(domain);
        var result = profile.ToDto(domains);
        idempotency.Add(new IdempotencyRecord(scope, key, hash, "owned_network", profile.Id, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "owned_network.create", profile.ProjectId,
            null, null, actor.RequestId, $"ownedNetworkId={profile.Id};domains={domains.Length};ownership={profile.OwnershipStatus};automationPermitted={profile.AutomationPermitted}",
            "succeeded", actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<OwnedNetworkProfileDto> UpdateAsync(UpdateOwnedNetworkCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "owned_network_update");
        var hash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null) return Idempotency.ReadExisting<OwnedNetworkProfileDto>(existing, hash);
        var profile = await networks.GetAsync(command.OwnedNetworkProfileId, true, cancellationToken)
            ?? throw new ResourceNotFoundException("OwnedNetworkProfile", command.OwnedNetworkProfileId);
        var duplicateName = await networks.FindByNameAsync(profile.ProjectId, command.Name.Trim(), cancellationToken);
        if (duplicateName is not null && duplicateName.Id != profile.Id)
            throw new ConflictException("An owned network with this name already exists in the project.");

        var now = timeProvider.GetUtcNow();
        profile.Update(command.Name, command.Description, command.OwnershipStatus, command.AutomationPermitted,
            command.OptionalNetworkTag, command.DefaultIdentityPoolId, command.DefaultTemplatePoolId, command.MaxConcurrency,
            command.PerDomainConcurrency, command.PerDomainDelayMilliseconds, command.Enabled, now);
        var previous = await networks.ListDomainsAsync(profile.Id, true, cancellationToken);
        networks.RemoveDomains(previous);
        var domains = BuildDomains(profile.Id, command.Domains, now);
        foreach (var domain in domains) networks.AddDomain(domain);
        var result = profile.ToDto(domains);
        idempotency.Add(new IdempotencyRecord(scope, key, hash, "owned_network", profile.Id, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "owned_network.update", profile.ProjectId,
            null, null, actor.RequestId, $"ownedNetworkId={profile.Id};domains={domains.Length};ownership={profile.OwnershipStatus};automationPermitted={profile.AutomationPermitted};enabled={profile.Enabled}",
            "succeeded", actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<OwnedNetworkProfileDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var profile = await networks.GetAsync(id, false, cancellationToken);
        return profile is null ? null : profile.ToDto(await networks.ListDomainsAsync(id, false, cancellationToken));
    }

    public async Task<PageResult<OwnedNetworkProfileDto>> ListAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken)
    {
        var rows = await networks.ListAsync(projectId, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var values = rows.Take(page.BoundedLimit).ToArray();
        var result = new List<OwnedNetworkProfileDto>(values.Length);
        foreach (var value in values)
            result.Add(value.ToDto(await networks.ListDomainsAsync(value.Id, false, cancellationToken)));
        var next = rows.Count > page.BoundedLimit && values.Length > 0
            ? CursorCodec.Encode(new PageCursor(values[^1].CreatedAt, values[^1].Id))
            : null;
        return new(result, next);
    }

    private static OwnedNetworkDomain[] BuildDomains(Guid profileId, IReadOnlyList<OwnedNetworkDomainInput> inputs, DateTimeOffset now)
    {
        if (inputs.Count is < 1 or > 1_000) throw new ValidationException("Between 1 and 1000 owned network domain rules are required.");
        var domains = inputs.Select(x => new OwnedNetworkDomain(profileId, x.Domain, x.MatchType, x.Enabled, now)).ToArray();
        if (domains.Select(x => (x.Domain, x.MatchType)).Distinct().Count() != domains.Length)
            throw new ValidationException("Owned network domain rules must be unique by normalized domain and match type.");
        return domains;
    }
}

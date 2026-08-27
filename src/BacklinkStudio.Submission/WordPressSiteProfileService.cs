using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Submission;

public sealed class WordPressSiteProfileService(
    IOwnedNetworkRepository networks,
    IWordPressSiteProfileRepository profiles,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IWordPressSiteProfileService
{
    public async Task<WordPressSiteProfileDto> CreateAsync(CreateWordPressSiteProfileCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "wordpress_site_profile_create");
        var hash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null) return Idempotency.ReadExisting<WordPressSiteProfileDto>(existing, hash);
        var network = await networks.GetAsync(command.OwnedNetworkProfileId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("OwnedNetworkProfile", command.OwnedNetworkProfileId);
        var rules = await networks.ListDomainsAsync(network.Id, false, cancellationToken);
        var normalizedDomain = command.Domain.Trim().TrimEnd('.').ToLowerInvariant();
        if (!rules.Any(x => x.Matches(normalizedDomain)))
            throw new UnauthorizedAccessException("The WordPress site domain is outside the selected owned network.");
        if (await profiles.FindAnyForSourceAsync(network.Id, normalizedDomain, cancellationToken) is not null)
            throw new ConflictException("A WordPress site profile already exists for this owned-network domain.");
        var now = timeProvider.GetUtcNow();
        var profile = new WordPressSiteProfile(network.Id, normalizedDomain, command.ApiBaseUrl,
            command.CredentialReference, command.SubmissionMode, command.Enabled, now);
        profiles.Add(profile);
        var result = Map(profile);
        idempotency.Add(new IdempotencyRecord(scope, key, hash, "wordpress_site_profile", profile.Id, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "wordpress_site_profile.create",
            network.ProjectId, null, null, actor.RequestId,
            $"wordpressSiteProfileId={profile.Id};ownedNetworkId={network.Id};domain={profile.Domain};mode={profile.SubmissionMode};credentialReferenceConfigured={profile.CredentialReference is not null}",
            "succeeded", actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<WordPressSiteProfileDto?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await profiles.GetAsync(id, false, cancellationToken) is { } profile ? Map(profile) : null;

    public async Task<WordPressSiteProfileDto> UpdateAsync(UpdateWordPressSiteProfileCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "wordpress_site_profile_update");
        var hash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null) return Idempotency.ReadExisting<WordPressSiteProfileDto>(existing, hash);
        var profile = await profiles.GetAsync(command.ProfileId, true, cancellationToken)
            ?? throw new ResourceNotFoundException("WordPressSiteProfile", command.ProfileId);
        var network = await networks.GetAsync(profile.OwnedNetworkProfileId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("OwnedNetworkProfile", profile.OwnedNetworkProfileId);
        var now = timeProvider.GetUtcNow();
        profile.Update(command.ApiBaseUrl, command.CredentialReference, command.SubmissionMode, command.Enabled, now);
        var result = Map(profile);
        idempotency.Add(new IdempotencyRecord(scope, key, hash, "wordpress_site_profile", profile.Id, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "wordpress_site_profile.update",
            network.ProjectId, null, null, actor.RequestId,
            $"wordpressSiteProfileId={profile.Id};mode={profile.SubmissionMode};enabled={profile.Enabled};credentialReferenceConfigured={profile.CredentialReference is not null}",
            "succeeded", actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<PageResult<WordPressSiteProfileDto>> ListAsync(Guid ownedNetworkProfileId, PageRequest page, CancellationToken cancellationToken)
    {
        var rows = await profiles.ListAsync(ownedNetworkProfileId, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var values = rows.Take(page.BoundedLimit).ToArray();
        var next = rows.Count > page.BoundedLimit && values.Length > 0
            ? CursorCodec.Encode(new(values[^1].CreatedAt, values[^1].Id)) : null;
        return new(values.Select(Map).ToArray(), next);
    }

    private static WordPressSiteProfileDto Map(WordPressSiteProfile value) => new(value.Id, value.OwnedNetworkProfileId,
        value.Domain, value.ApiBaseUrl, value.CredentialReference, value.SubmissionMode, value.Enabled, value.CreatedAt, value.UpdatedAt);
}

using System.Text.Json;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public sealed record AgentCredentialDto(
    Guid Id,
    Guid UserId,
    string Name,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt);

public sealed record AgentCredentialSecretDto(AgentCredentialDto Credential, string? ApiKey, bool Displayed);
public sealed record CreateAgentCredentialCommand(string Name, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAt, string IdempotencyKey);
public sealed record RotateAgentCredentialCommand(Guid CredentialId, DateTimeOffset? ExpiresAt, string IdempotencyKey);
public sealed record RevokeAgentCredentialCommand(Guid CredentialId, string IdempotencyKey);
public sealed record AgentApiKeyMaterial(string Plaintext, string LookupHash, byte[] Hash, byte[] Salt);

public interface IAgentCredentialService
{
    Task<AgentCredentialSecretDto> CreateAsync(CreateAgentCredentialCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<AgentCredentialSecretDto> RotateAsync(RotateAgentCredentialCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<AgentCredentialDto> RevokeAsync(RevokeAgentCredentialCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<AgentCredentialDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<AgentCredentialDto>> ListAsync(PageRequest page, CancellationToken cancellationToken);
}

public interface IAgentCredentialRepository
{
    void Add(User user);
    void Add(AgentCredential credential);
    Task<AgentCredential?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    Task<IReadOnlyList<AgentCredential>> ListAsync(PageCursor? cursor, int take, CancellationToken cancellationToken);
}

public interface IAgentApiKeyFactory
{
    AgentApiKeyMaterial Create();
}

public sealed class AgentCredentialService(
    IAgentCredentialRepository credentials,
    IAgentApiKeyFactory apiKeys,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IAgentCredentialService
{
    public async Task<AgentCredentialSecretDto> CreateAsync(CreateAgentCredentialCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "agent_credential_create");
        var hash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null)
        {
            return Idempotency.ReadExisting<AgentCredentialSecretDto>(existing, hash);
        }

        var now = timeProvider.GetUtcNow();
        var scopes = ValidateScopes(command.Scopes);
        ValidateExpiry(command.ExpiresAt, now);
        var material = apiKeys.Create();
        var user = new User(command.Name, now);
        var credential = new AgentCredential(user.Id, command.Name, material.LookupHash, material.Hash, material.Salt, scopes, command.ExpiresAt, now);
        credentials.Add(user);
        credentials.Add(credential);
        var metadata = ToDto(credential);
        var redactedReplay = new AgentCredentialSecretDto(metadata, null, false);
        idempotency.Add(new IdempotencyRecord(scope, key, hash, "agentCredential", credential.Id, JsonSerializer.Serialize(redactedReplay), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "credential.create", null, null, null, actor.RequestId, $"credentialId={credential.Id};name={credential.Name};scopes={credential.Scopes.Length};expires={credential.ExpiresAt is not null}", "succeeded", actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new AgentCredentialSecretDto(metadata, material.Plaintext, true);
    }

    public async Task<AgentCredentialSecretDto> RotateAsync(RotateAgentCredentialCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "agent_credential_rotate");
        var hash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null)
        {
            return Idempotency.ReadExisting<AgentCredentialSecretDto>(existing, hash);
        }

        var current = await credentials.GetAsync(command.CredentialId, true, cancellationToken)
            ?? throw new ResourceNotFoundException("AgentCredential", command.CredentialId);
        var now = timeProvider.GetUtcNow();
        if (current.RevokedAt is not null)
        {
            throw new ConflictException("A revoked credential cannot be rotated.");
        }

        var expiry = command.ExpiresAt ?? current.ExpiresAt;
        ValidateExpiry(expiry, now);
        var material = apiKeys.Create();
        current.Revoke(now);
        var replacement = new AgentCredential(current.UserId, current.Name, material.LookupHash, material.Hash, material.Salt, current.Scopes, expiry, now);
        credentials.Add(replacement);
        var metadata = ToDto(replacement);
        var redactedReplay = new AgentCredentialSecretDto(metadata, null, false);
        idempotency.Add(new IdempotencyRecord(scope, key, hash, "agentCredential", replacement.Id, JsonSerializer.Serialize(redactedReplay), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "credential.rotate", null, null, null, actor.RequestId, $"previousCredentialId={current.Id};credentialId={replacement.Id};expires={replacement.ExpiresAt is not null}", "succeeded", actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new AgentCredentialSecretDto(metadata, material.Plaintext, true);
    }

    public async Task<AgentCredentialDto> RevokeAsync(RevokeAgentCredentialCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "agent_credential_revoke");
        var hash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null)
        {
            return Idempotency.ReadExisting<AgentCredentialDto>(existing, hash);
        }

        if (actor.CredentialId == command.CredentialId)
        {
            throw new ValidationException("A credential cannot revoke itself. Rotate it or use a different administrator credential.");
        }

        var credential = await credentials.GetAsync(command.CredentialId, true, cancellationToken)
            ?? throw new ResourceNotFoundException("AgentCredential", command.CredentialId);
        var now = timeProvider.GetUtcNow();
        credential.Revoke(now);
        var result = ToDto(credential);
        idempotency.Add(new IdempotencyRecord(scope, key, hash, "agentCredential", credential.Id, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "credential.revoke", null, null, null, actor.RequestId, $"credentialId={credential.Id}", "succeeded", actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<AgentCredentialDto?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        (await credentials.GetAsync(id, false, cancellationToken)) is { } credential ? ToDto(credential) : null;

    public async Task<PageResult<AgentCredentialDto>> ListAsync(PageRequest page, CancellationToken cancellationToken)
    {
        var rows = await credentials.ListAsync(CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var values = rows.Take(page.BoundedLimit).ToArray();
        var next = rows.Count > page.BoundedLimit && values.Length > 0
            ? CursorCodec.Encode(new PageCursor(values[^1].CreatedAt, values[^1].Id))
            : null;
        return new PageResult<AgentCredentialDto>(values.Select(ToDto).ToArray(), next);
    }

    private static string[] ValidateScopes(IReadOnlyList<string> requested)
    {
        if (requested.Count is < 1 or > 50)
        {
            throw new ValidationException("Between 1 and 50 authorization scopes are required.");
        }

        var scopes = requested.Select(x => x?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal).Order().ToArray();
        var invalid = scopes.Where(x => !AuthorizationScopes.All.Contains(x, StringComparer.Ordinal)).ToArray();
        if (invalid.Length > 0)
        {
            throw new ValidationException($"Unknown authorization scope: {invalid[0]}.");
        }

        return scopes;
    }

    private static void ValidateExpiry(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (expiresAt is not null && (expiresAt <= now || expiresAt > now.AddYears(5)))
        {
            throw new ValidationException("Credential expiry must be in the future and no more than five years away.");
        }
    }

    private static AgentCredentialDto ToDto(AgentCredential credential) => new(
        credential.Id,
        credential.UserId,
        credential.Name,
        credential.Scopes,
        credential.ExpiresAt,
        credential.RevokedAt,
        credential.LastUsedAt,
        credential.CreatedAt);
}

namespace BacklinkStudio.Domain;

public sealed class User
{
    private User() { }

    public User(string displayName, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        DisplayName = Guard.Required(displayName, 200, nameof(DisplayName));
        Enabled = true;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public string DisplayName { get; private set; } = null!;
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Disable() => Enabled = false;
    public void Enable() => Enabled = true;
}

public sealed class AgentCredential
{
    private AgentCredential() { }

    public AgentCredential(
        Guid userId,
        string name,
        string lookupHash,
        byte[] keyHash,
        byte[] keySalt,
        string[] scopes,
        DateTimeOffset? expiresAt,
        DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        UserId = userId;
        Name = Guard.Required(name, 200, nameof(Name));
        LookupHash = Guard.Required(lookupHash, 64, nameof(LookupHash));
        KeyHash = keyHash.Length > 0 ? keyHash : throw new DomainRuleException("Key hash is required.");
        KeySalt = keySalt.Length > 0 ? keySalt : throw new DomainRuleException("Key salt is required.");
        Scopes = scopes.Select(x => Guard.Required(x, 100, nameof(Scopes))).Distinct(StringComparer.Ordinal).Order().ToArray();
        if (Scopes.Length == 0)
        {
            throw new DomainRuleException("At least one authorization scope is required.");
        }
        if (expiresAt is not null && expiresAt <= now)
        {
            throw new DomainRuleException("Credential expiry must be in the future.");
        }
        ExpiresAt = expiresAt;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = null!;
    public string LookupHash { get; private set; } = null!;
    public byte[] KeyHash { get; private set; } = [];
    public byte[] KeySalt { get; private set; } = [];
    public string[] Scopes { get; private set; } = [];
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && (ExpiresAt is null || ExpiresAt > now);

    public void RecordUse(DateTimeOffset now) => LastUsedAt = now;

    public void Revoke(DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            throw new DomainRuleException("Credential is already revoked.");
        }

        RevokedAt = now;
    }
}

public sealed class AuditEvent
{
    private AuditEvent() { }

    public AuditEvent(
        ActorType actorType,
        string actorId,
        Guid? credentialId,
        string operation,
        Guid? projectId,
        Guid? campaignId,
        Guid? jobId,
        string requestId,
        string inputSummary,
        string result,
        string? sourceAddress,
        DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        Timestamp = now;
        ActorType = actorType;
        ActorId = Guard.Required(actorId, 200, nameof(ActorId));
        CredentialId = credentialId;
        Operation = Guard.Required(operation, 200, nameof(Operation));
        ProjectId = projectId;
        CampaignId = campaignId;
        JobId = jobId;
        RequestId = Guard.Required(requestId, 100, nameof(RequestId));
        InputSummary = Guard.Required(inputSummary, 2_000, nameof(InputSummary));
        Result = Guard.Required(result, 100, nameof(Result));
        SourceAddress = Guard.Optional(sourceAddress, 100, nameof(SourceAddress));
    }

    public Guid Id { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
    public ActorType ActorType { get; private set; }
    public string ActorId { get; private set; } = null!;
    public Guid? CredentialId { get; private set; }
    public string Operation { get; private set; } = null!;
    public Guid? ProjectId { get; private set; }
    public Guid? CampaignId { get; private set; }
    public Guid? JobId { get; private set; }
    public string RequestId { get; private set; } = null!;
    public string InputSummary { get; private set; } = null!;
    public string Result { get; private set; } = null!;
    public string? SourceAddress { get; private set; }
}

public sealed class IdempotencyRecord
{
    private IdempotencyRecord() { }

    public IdempotencyRecord(
        string scope,
        string key,
        string requestHash,
        string resourceType,
        Guid? resourceId,
        string responseJson,
        DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        Scope = Guard.Required(scope, 300, nameof(Scope));
        Key = Guard.Required(key, 200, nameof(Key));
        RequestHash = Guard.Required(requestHash, 64, nameof(RequestHash));
        ResourceType = Guard.Required(resourceType, 100, nameof(ResourceType));
        ResourceId = resourceId;
        ResponseJson = Guard.Required(responseJson, 64 * 1_024, nameof(ResponseJson));
        CreatedAt = now;
        ExpiresAt = now.AddDays(7);
    }

    public Guid Id { get; private set; }
    public string Scope { get; private set; } = null!;
    public string Key { get; private set; } = null!;
    public string RequestHash { get; private set; } = null!;
    public string ResourceType { get; private set; } = null!;
    public Guid? ResourceId { get; private set; }
    public string ResponseJson { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
}

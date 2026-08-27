namespace BacklinkStudio.Domain;

public sealed class OwnedNetworkProfile
{
    private OwnedNetworkProfile() { }

    public OwnedNetworkProfile(
        Guid projectId,
        string name,
        string? description,
        OwnershipStatus ownershipStatus,
        bool automationPermitted,
        string? optionalNetworkTag,
        Guid? defaultIdentityPoolId,
        Guid? defaultTemplatePoolId,
        int maxConcurrency,
        int perDomainConcurrency,
        int perDomainDelayMilliseconds,
        bool enabled,
        DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        Apply(name, description, ownershipStatus, automationPermitted, optionalNetworkTag, defaultIdentityPoolId,
            defaultTemplatePoolId, maxConcurrency, perDomainConcurrency, perDomainDelayMilliseconds, enabled, now);
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public OwnershipStatus OwnershipStatus { get; private set; }
    public bool AutomationPermitted { get; private set; }
    public string? OptionalNetworkTag { get; private set; }
    public Guid? DefaultIdentityPoolId { get; private set; }
    public Guid? DefaultTemplatePoolId { get; private set; }
    public int MaxConcurrency { get; private set; }
    public int PerDomainConcurrency { get; private set; }
    public int PerDomainDelayMilliseconds { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool AllowsAutomaticExecution =>
        Enabled && AutomationPermitted && OwnershipStatus != OwnershipStatus.Unverified;

    public void Update(
        string name,
        string? description,
        OwnershipStatus ownershipStatus,
        bool automationPermitted,
        string? optionalNetworkTag,
        Guid? defaultIdentityPoolId,
        Guid? defaultTemplatePoolId,
        int maxConcurrency,
        int perDomainConcurrency,
        int perDomainDelayMilliseconds,
        bool enabled,
        DateTimeOffset now) =>
        Apply(name, description, ownershipStatus, automationPermitted, optionalNetworkTag, defaultIdentityPoolId,
            defaultTemplatePoolId, maxConcurrency, perDomainConcurrency, perDomainDelayMilliseconds, enabled, now);

    private void Apply(
        string name,
        string? description,
        OwnershipStatus ownershipStatus,
        bool automationPermitted,
        string? optionalNetworkTag,
        Guid? defaultIdentityPoolId,
        Guid? defaultTemplatePoolId,
        int maxConcurrency,
        int perDomainConcurrency,
        int perDomainDelayMilliseconds,
        bool enabled,
        DateTimeOffset now)
    {
        if (maxConcurrency is < 1 or > 10_000)
            throw new DomainRuleException("Owned network maximum concurrency must be between 1 and 10000.");
        if (perDomainConcurrency is < 1 or > 1_000 || perDomainConcurrency > maxConcurrency)
            throw new DomainRuleException("Per-domain concurrency must be between 1 and the network maximum concurrency.");
        if (perDomainDelayMilliseconds is < 0 or > 86_400_000)
            throw new DomainRuleException("Per-domain delay must be between 0 and 86400000 milliseconds.");
        if (automationPermitted && ownershipStatus == OwnershipStatus.Unverified)
            throw new DomainRuleException("Automation cannot be permitted for an unverified owned network.");

        Name = Guard.Required(name, 200, nameof(Name));
        Description = Guard.Optional(description, 2_000, nameof(Description));
        OwnershipStatus = ownershipStatus;
        AutomationPermitted = automationPermitted;
        OptionalNetworkTag = Guard.Optional(optionalNetworkTag, 100, nameof(OptionalNetworkTag));
        DefaultIdentityPoolId = defaultIdentityPoolId;
        DefaultTemplatePoolId = defaultTemplatePoolId;
        MaxConcurrency = maxConcurrency;
        PerDomainConcurrency = perDomainConcurrency;
        PerDomainDelayMilliseconds = perDomainDelayMilliseconds;
        Enabled = enabled;
        UpdatedAt = now;
    }
}

public sealed class OwnedNetworkDomain
{
    private OwnedNetworkDomain() { }

    public OwnedNetworkDomain(Guid ownedNetworkProfileId, string domain, OwnedNetworkDomainMatchType matchType, bool enabled, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        OwnedNetworkProfileId = ownedNetworkProfileId;
        Domain = NormalizeHost(domain);
        MatchType = matchType;
        Enabled = enabled;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid OwnedNetworkProfileId { get; private set; }
    public string Domain { get; private set; } = null!;
    public OwnedNetworkDomainMatchType MatchType { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool Matches(string host)
    {
        if (!Enabled) return false;
        var normalizedHost = NormalizeHost(host);
        return MatchType switch
        {
            OwnedNetworkDomainMatchType.ExactHost or OwnedNetworkDomainMatchType.ExactDomain =>
                string.Equals(normalizedHost, Domain, StringComparison.Ordinal),
            OwnedNetworkDomainMatchType.SubdomainOf =>
                normalizedHost.EndsWith('.' + Domain, StringComparison.Ordinal),
            _ => false
        };
    }

    public static string NormalizeHost(string value)
    {
        var normalized = Guard.Required(value, 253, nameof(Domain)).TrimEnd('.').ToLowerInvariant();
        if (normalized.Length is < 1 or > 253 || normalized.Contains('/') || normalized.Contains(':') ||
            !Uri.CheckHostName(normalized).Equals(UriHostNameType.Dns))
            throw new DomainRuleException("Owned network domain must be a valid DNS host name.");
        return new UriBuilder(Uri.UriSchemeHttps, normalized).Uri.IdnHost.ToLowerInvariant();
    }
}

public sealed class SubmissionSource
{
    private SubmissionSource() { }

    public SubmissionSource(
        Guid projectId,
        Guid? ownedNetworkProfileId,
        string originalUrl,
        string normalizedUrl,
        string domain,
        string host,
        OwnershipStatus ownershipStatus,
        bool automationPermitted,
        string? tag,
        bool enabled,
        DateTimeOffset now,
        Guid? sourceImportId = null)
    {
        if (automationPermitted && (ownershipStatus == OwnershipStatus.Unverified || ownedNetworkProfileId is null))
            throw new DomainRuleException("Automation requires a server-authorized owned-network association.");

        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        OwnedNetworkProfileId = ownedNetworkProfileId;
        SourceImportId = sourceImportId;
        OriginalUrl = Guard.Required(originalUrl, 2_048, nameof(OriginalUrl));
        NormalizedUrl = Guard.Required(normalizedUrl, 2_048, nameof(NormalizedUrl));
        Domain = Guard.Required(domain, 253, nameof(Domain)).ToLowerInvariant();
        Host = Guard.Required(host, 253, nameof(Host)).ToLowerInvariant();
        OwnershipStatus = ownershipStatus;
        AutomationPermitted = automationPermitted;
        Tag = Guard.Optional(tag, 100, nameof(Tag));
        Platform = SourcePlatform.Unknown;
        CmsType = CmsType.Unknown;
        OpportunityType = OpportunityType.Unknown;
        TechnicalCompatibility = TechnicalCompatibility.Unknown;
        ValidationStatus = SubmissionSourceValidationStatus.Pending;
        Enabled = enabled;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid? OwnedNetworkProfileId { get; private set; }
    public Guid? SourceImportId { get; private set; }
    public string OriginalUrl { get; private set; } = null!;
    public string NormalizedUrl { get; private set; } = null!;
    public string Domain { get; private set; } = null!;
    public string Host { get; private set; } = null!;
    public SourcePlatform Platform { get; private set; }
    public CmsType CmsType { get; private set; }
    public OpportunityType OpportunityType { get; private set; }
    public string? AdapterName { get; private set; }
    public OwnershipStatus OwnershipStatus { get; private set; }
    public bool AutomationPermitted { get; private set; }
    public TechnicalCompatibility TechnicalCompatibility { get; private set; }
    public SubmissionSourceValidationStatus ValidationStatus { get; private set; }
    public string? ValidationReason { get; private set; }
    public string? DetectionReason { get; private set; }
    public bool RequiresBrowser { get; private set; }
    public bool RequiresAuthentication { get; private set; }
    public bool RequiresManualAction { get; private set; }
    public bool SupportsWordPressComment { get; private set; }
    public bool SupportsOwnedWordPressApi { get; private set; }
    public bool SupportsOwnedPropertyPlacement { get; private set; }
    public long? PostId { get; private set; }
    public string? CommentEndpoint { get; private set; }
    public string? DetectedFormAction { get; private set; }
    public string? PageTitle { get; private set; }
    public string? FinalUrl { get; private set; }
    public string? CanonicalUrl { get; private set; }
    public int? LastHttpStatus { get; private set; }
    public string? LastContentType { get; private set; }
    public long? LastContentLength { get; private set; }
    public string[] RedirectChain { get; private set; } = [];
    public bool CommentsEnabled { get; private set; }
    public string? CommentAuthorField { get; private set; }
    public string? CommentEmailField { get; private set; }
    public string? CommentWebsiteField { get; private set; }
    public string? CommentContentField { get; private set; }
    public string? CommentPostIdField { get; private set; }
    public string[] AdditionalRequiredFields { get; private set; } = [];
    public bool RequiresCookies { get; private set; }
    public bool RequiresNonce { get; private set; }
    public string? ModerationSignal { get; private set; }
    public string? Tag { get; private set; }
    public DateTimeOffset? LastValidatedAt { get; private set; }
    public DateTimeOffset? LastSubmissionAt { get; private set; }
    public DateTimeOffset? LastSuccessfulSubmissionAt { get; private set; }
    public long SuccessCount { get; private set; }
    public long FailureCount { get; private set; }
    public long PendingModerationCount { get; private set; }
    public long VerifiedCount { get; private set; }
    public long LostCount { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool AllowsAutomaticExecution =>
        Enabled && OwnedNetworkProfileId is not null && AutomationPermitted &&
        OwnershipStatus != OwnershipStatus.Unverified &&
        TechnicalCompatibility == TechnicalCompatibility.Compatible;

    public void QueueValidation(DateTimeOffset now)
    {
        ValidationStatus = SubmissionSourceValidationStatus.Queued;
        UpdatedAt = now;
    }

    public void ApplyOwnership(OwnershipStatus ownershipStatus, bool automationPermitted, DateTimeOffset now)
    {
        if (automationPermitted && (ownershipStatus == OwnershipStatus.Unverified || OwnedNetworkProfileId is null))
            throw new DomainRuleException("Automation requires a server-authorized owned-network association.");
        OwnershipStatus = ownershipStatus;
        AutomationPermitted = automationPermitted;
        UpdatedAt = now;
    }

    public void AssociateWithOwnedNetwork(Guid ownedNetworkProfileId, OwnershipStatus ownershipStatus,
        bool automationPermitted, DateTimeOffset now)
    {
        if (ownershipStatus == OwnershipStatus.Unverified || !automationPermitted)
            throw new DomainRuleException("Only a server-authorized owned network can be associated for automation.");
        OwnedNetworkProfileId = ownedNetworkProfileId;
        ApplyOwnership(ownershipStatus, true, now);
    }

    public void RecordSubmissionOutcome(SubmissionStatus status, DateTimeOffset now)
    {
        LastSubmissionAt = now;
        if (status is SubmissionStatus.Submitted or SubmissionStatus.Approved)
        {
            SuccessCount = checked(SuccessCount + 1);
            LastSuccessfulSubmissionAt = now;
        }
        else if (status == SubmissionStatus.PendingModeration)
        {
            PendingModerationCount = checked(PendingModerationCount + 1);
        }
        else if (status is SubmissionStatus.Failed or SubmissionStatus.Rejected or SubmissionStatus.Duplicate)
        {
            FailureCount = checked(FailureCount + 1);
        }
        UpdatedAt = now;
    }

    public void ApplyValidation(SubmissionSourceValidation result, DateTimeOffset now)
    {
        Platform = result.Platform;
        CmsType = result.CmsType;
        OpportunityType = result.OpportunityType;
        AdapterName = Guard.Optional(result.AdapterName, 100, nameof(AdapterName));
        TechnicalCompatibility = result.TechnicalCompatibility;
        ValidationStatus = result.ValidationStatus;
        ValidationReason = Guard.Optional(result.ValidationReason, 2_000, nameof(ValidationReason));
        DetectionReason = Guard.Optional(result.DetectionReason, 2_000, nameof(DetectionReason));
        RequiresBrowser = result.RequiresBrowser;
        RequiresAuthentication = result.RequiresAuthentication;
        RequiresManualAction = result.RequiresManualAction;
        SupportsWordPressComment = result.SupportsWordPressComment;
        SupportsOwnedWordPressApi = result.SupportsOwnedWordPressApi;
        SupportsOwnedPropertyPlacement = result.SupportsOwnedPropertyPlacement;
        PostId = result.PostId;
        CommentEndpoint = Guard.Optional(result.CommentEndpoint, 2_048, nameof(CommentEndpoint));
        DetectedFormAction = Guard.Optional(result.DetectedFormAction, 2_048, nameof(DetectedFormAction));
        PageTitle = Guard.Optional(result.PageTitle, 500, nameof(PageTitle));
        FinalUrl = Guard.Optional(result.FinalUrl, 2_048, nameof(FinalUrl));
        CanonicalUrl = Guard.Optional(result.CanonicalUrl, 2_048, nameof(CanonicalUrl));
        LastHttpStatus = result.HttpStatus;
        LastContentType = Guard.Optional(result.ContentType, 200, nameof(LastContentType));
        LastContentLength = result.ContentLength;
        RedirectChain = result.RedirectChain.Take(10).Select(x => Guard.Required(x, 2_048, nameof(RedirectChain))).ToArray();
        CommentsEnabled = result.CommentsEnabled;
        CommentAuthorField = Guard.Optional(result.CommentAuthorField, 100, nameof(CommentAuthorField));
        CommentEmailField = Guard.Optional(result.CommentEmailField, 100, nameof(CommentEmailField));
        CommentWebsiteField = Guard.Optional(result.CommentWebsiteField, 100, nameof(CommentWebsiteField));
        CommentContentField = Guard.Optional(result.CommentContentField, 100, nameof(CommentContentField));
        CommentPostIdField = Guard.Optional(result.CommentPostIdField, 100, nameof(CommentPostIdField));
        AdditionalRequiredFields = result.AdditionalRequiredFields.Take(32)
            .Select(x => Guard.Required(x, 100, nameof(AdditionalRequiredFields))).Distinct(StringComparer.Ordinal).ToArray();
        RequiresCookies = result.RequiresCookies;
        RequiresNonce = result.RequiresNonce;
        ModerationSignal = Guard.Optional(result.ModerationSignal, 500, nameof(ModerationSignal));
        LastValidatedAt = now;
        UpdatedAt = now;
    }
}

public sealed record SubmissionSourceValidation(
    SourcePlatform Platform,
    CmsType CmsType,
    OpportunityType OpportunityType,
    string? AdapterName,
    TechnicalCompatibility TechnicalCompatibility,
    SubmissionSourceValidationStatus ValidationStatus,
    string? ValidationReason,
    string? DetectionReason,
    bool RequiresBrowser,
    bool RequiresAuthentication,
    bool RequiresManualAction,
    bool SupportsWordPressComment,
    bool SupportsOwnedWordPressApi,
    bool SupportsOwnedPropertyPlacement,
    long? PostId,
    string? CommentEndpoint,
    string? DetectedFormAction,
    string? PageTitle,
    string? FinalUrl,
    string? CanonicalUrl,
    int? HttpStatus,
    string? ContentType,
    long? ContentLength,
    IReadOnlyList<string> RedirectChain,
    bool CommentsEnabled,
    string? CommentAuthorField,
    string? CommentEmailField,
    string? CommentWebsiteField,
    string? CommentContentField,
    string? CommentPostIdField,
    IReadOnlyList<string> AdditionalRequiredFields,
    bool RequiresCookies,
    bool RequiresNonce,
    string? ModerationSignal);

public sealed class SubmissionSourceImport
{
    private SubmissionSourceImport() { }

    public SubmissionSourceImport(Guid projectId, Guid? ownedNetworkProfileId, SubmissionSourceImportFormat format, string fileName, string? tag, string idempotencyKey, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        OwnedNetworkProfileId = ownedNetworkProfileId;
        Format = format;
        FileName = Guard.Required(Path.GetFileName(fileName), 255, nameof(FileName));
        Tag = Guard.Optional(tag, 100, nameof(Tag));
        IdempotencyKey = Guard.Required(idempotencyKey, 200, nameof(IdempotencyKey));
        Status = SubmissionSourceImportStatus.Staging;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid? OwnedNetworkProfileId { get; private set; }
    public Guid? JobId { get; private set; }
    public SubmissionSourceImportFormat Format { get; private set; }
    public SubmissionSourceImportStatus Status { get; private set; }
    public string FileName { get; private set; } = null!;
    public string? Tag { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string? Sha256 { get; private set; }
    public long ByteLength { get; private set; }
    public int TotalLines { get; private set; }
    public int Accepted { get; private set; }
    public int Duplicates { get; private set; }
    public int Invalid { get; private set; }
    public int Errors { get; private set; }
    public string? SafeError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public void SealStaging(long byteLength, string sha256, DateTimeOffset now)
    {
        if (Status != SubmissionSourceImportStatus.Staging || byteLength < 1)
            throw new DomainRuleException("Only a non-empty staged import can be sealed.");
        ByteLength = byteLength;
        Sha256 = Guard.Required(sha256, 64, nameof(Sha256)).ToLowerInvariant();
        if (Sha256.Length != 64 || Sha256.Any(character => !char.IsAsciiHexDigit(character)))
            throw new DomainRuleException("Import SHA-256 must contain 64 hexadecimal characters.");
        UpdatedAt = now;
    }

    public void Queue(Guid jobId, DateTimeOffset now)
    {
        if (Status != SubmissionSourceImportStatus.Staging)
            throw new DomainRuleException("Only a staged import can be queued.");
        JobId = jobId;
        Status = SubmissionSourceImportStatus.Queued;
        UpdatedAt = now;
    }

    public void Start(DateTimeOffset now)
    {
        Status = SubmissionSourceImportStatus.Processing;
        UpdatedAt = now;
    }

    public void Complete(int totalLines, int accepted, int duplicates, int invalid, int errors, DateTimeOffset now)
    {
        if (totalLines < 0 || accepted < 0 || duplicates < 0 || invalid < 0 || errors < 0)
            throw new DomainRuleException("Import counters cannot be negative.");
        TotalLines = totalLines;
        Accepted = accepted;
        Duplicates = duplicates;
        Invalid = invalid;
        Errors = errors;
        Status = SubmissionSourceImportStatus.Completed;
        UpdatedAt = now;
        CompletedAt = now;
    }

    public void Fail(string safeError, DateTimeOffset now)
    {
        SafeError = Guard.Required(safeError, 2_000, nameof(SafeError));
        Status = SubmissionSourceImportStatus.Failed;
        UpdatedAt = now;
        CompletedAt = now;
    }
}

public sealed class SubmissionSourceImportChunk
{
    private SubmissionSourceImportChunk() { }

    public SubmissionSourceImportChunk(Guid importId, int sequence, byte[] content, DateTimeOffset now)
    {
        if (sequence < 0) throw new DomainRuleException("Import chunk sequence cannot be negative.");
        if (content.Length is < 1 or > 64 * 1_024) throw new DomainRuleException("Import chunks must contain between 1 and 65536 bytes.");
        Id = Guid.CreateVersion7(now);
        ImportId = importId;
        Sequence = sequence;
        Content = content.ToArray();
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ImportId { get; private set; }
    public int Sequence { get; private set; }
    public byte[] Content { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
}

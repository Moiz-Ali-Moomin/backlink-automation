namespace BacklinkStudio.Domain;

public sealed class Campaign
{
    private Campaign() { }

    public Campaign(Guid projectId, string name, CampaignApprovalMode approvalMode, string authorizationProfileKey, string authorizationReference, int dailyActionLimit, DateTimeOffset now)
    {
        if (dailyActionLimit is < 1 or > 100_000)
        {
            throw new DomainRuleException("Campaign daily action limit must be between 1 and 100000.");
        }

        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        Name = Guard.Required(name, 200, nameof(Name));
        ApprovalMode = approvalMode;
        AuthorizationProfileKey = Guard.Required(authorizationProfileKey, 100, nameof(AuthorizationProfileKey));
        AuthorizationReference = Guard.Required(authorizationReference, 500, nameof(AuthorizationReference));
        DailyActionLimit = dailyActionLimit;
        Status = CampaignStatus.Draft;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Name { get; private set; } = null!;
    public CampaignApprovalMode ApprovalMode { get; private set; }
    public string AuthorizationProfileKey { get; private set; } = null!;
    public string AuthorizationReference { get; private set; } = null!;
    public int DailyActionLimit { get; private set; }
    public CampaignStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Start(DateTimeOffset now)
    {
        if (Status == CampaignStatus.Running)
        {
            return;
        }

        if (Status != CampaignStatus.Draft)
        {
            throw new DomainRuleException($"Campaign in state {Status} cannot be started.");
        }
        Status = CampaignStatus.Running;
        UpdatedAt = now;
    }

    public void Update(string name, CampaignApprovalMode approvalMode, int dailyActionLimit, DateTimeOffset now)
    {
        if (Status != CampaignStatus.Draft)
        {
            throw new DomainRuleException("Only a draft campaign can be updated.");
        }
        if (dailyActionLimit is < 1 or > 100_000)
        {
            throw new DomainRuleException("Campaign daily action limit must be between 1 and 100000.");
        }

        Name = Guard.Required(name, 200, nameof(Name));
        ApprovalMode = approvalMode;
        DailyActionLimit = dailyActionLimit;
        UpdatedAt = now;
    }

    public void Pause(DateTimeOffset now)
    {
        if (Status != CampaignStatus.Running)
        {
            throw new DomainRuleException($"Campaign in state {Status} cannot be paused.");
        }
        Status = CampaignStatus.Paused;
        UpdatedAt = now;
    }

    public void Resume(DateTimeOffset now)
    {
        if (Status != CampaignStatus.Paused)
        {
            throw new DomainRuleException($"Campaign in state {Status} cannot be resumed.");
        }
        Status = CampaignStatus.Running;
        UpdatedAt = now;
    }

    public void Stop(DateTimeOffset now)
    {
        if (Status is CampaignStatus.Completed or CampaignStatus.Stopped)
        {
            throw new DomainRuleException($"Campaign in state {Status} cannot be stopped.");
        }
        Status = CampaignStatus.Stopped;
        UpdatedAt = now;
    }
}

public sealed class CampaignTarget
{
    private CampaignTarget() { }
    public CampaignTarget(Guid campaignId, Guid projectTargetId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        CampaignId = campaignId;
        ProjectTargetId = projectTargetId;
        CreatedAt = now;
    }
    public Guid Id { get; private set; }
    public Guid CampaignId { get; private set; }
    public Guid ProjectTargetId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class CampaignOpportunity
{
    private CampaignOpportunity() { }
    public CampaignOpportunity(Guid campaignId, Guid opportunityId, bool explicitlyApproved, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        CampaignId = campaignId;
        OpportunityId = opportunityId;
        ExplicitlyApproved = explicitlyApproved;
        CreatedAt = now;
    }
    public Guid Id { get; private set; }
    public Guid CampaignId { get; private set; }
    public Guid OpportunityId { get; private set; }
    public bool ExplicitlyApproved { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public void Approve() => ExplicitlyApproved = true;
}

public sealed class SubmissionJob
{
    private SubmissionJob() { }
    public SubmissionJob(Guid projectId, Guid campaignId, Guid campaignOpportunityId, Guid persistentJobId, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        CampaignId = campaignId;
        CampaignOpportunityId = campaignOpportunityId;
        PersistentJobId = persistentJobId;
        Status = SubmissionStatus.Queued;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public SubmissionJob(Guid projectId, Guid campaignId, Guid submissionSourceId, Guid persistentJobId,
        string targetUrl, BacklinkPlacementMethod placementType, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        CampaignId = campaignId;
        SubmissionSourceId = submissionSourceId;
        PersistentJobId = persistentJobId;
        TargetUrl = Guard.Required(targetUrl, 2_048, nameof(TargetUrl));
        PlacementType = placementType;
        Status = SubmissionStatus.Queued;
        CreatedAt = now;
        UpdatedAt = now;
    }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid CampaignId { get; private set; }
    public Guid? CampaignOpportunityId { get; private set; }
    public Guid? SubmissionSourceId { get; private set; }
    public Guid PersistentJobId { get; private set; }
    public string? TargetUrl { get; private set; }
    public BacklinkPlacementMethod? PlacementType { get; private set; }
    public SubmissionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkProcessing(DateTimeOffset now) => SetStatus(SubmissionStatus.Processing, now);
    public void MarkSubmitted(bool pendingModeration, DateTimeOffset now) => SetStatus(pendingModeration ? SubmissionStatus.PendingModeration : SubmissionStatus.Submitted, now);
    public void MarkRejected(DateTimeOffset now) => SetStatus(SubmissionStatus.Rejected, now);
    public void MarkFailed(bool manualActionRequired, DateTimeOffset now) => SetStatus(manualActionRequired ? SubmissionStatus.ManualActionRequired : SubmissionStatus.Failed, now);
    public void MarkCancelled(DateTimeOffset now) => SetStatus(SubmissionStatus.Cancelled, now);
    public void ApplyOutcome(SubmissionStatus status, DateTimeOffset now)
    {
        if (status is SubmissionStatus.Queued or SubmissionStatus.Claimed or SubmissionStatus.Running or SubmissionStatus.Processing)
            throw new DomainRuleException("A submission outcome must be terminal or awaiting reconciliation, moderation, or verification.");
        SetStatus(status, now);
    }
    private void SetStatus(SubmissionStatus status, DateTimeOffset now) { Status = status; UpdatedAt = now; }
}

public sealed class SubmissionAttempt
{
    private SubmissionAttempt() { }
    public SubmissionAttempt(Guid submissionJobId, int attemptNumber, string adapter, DateTimeOffset startedAt)
    {
        if (attemptNumber < 1) throw new DomainRuleException("Submission attempt number must be positive.");
        Id = Guid.CreateVersion7(startedAt);
        SubmissionJobId = submissionJobId;
        AttemptNumber = attemptNumber;
        Adapter = Guard.Required(adapter, 100, nameof(Adapter));
        StartedAt = startedAt;
    }

    public SubmissionAttempt(Guid submissionJobId, Guid persistentJobId, Guid projectId, Guid campaignId,
        Guid submissionSourceId, string targetUrl, int attemptNumber, string adapter, string strategy,
        Guid identityId, Guid templateId, string resolvedDisplayName, string resolvedEmail, string? resolvedWebsite,
        DateTimeOffset startedAt) : this(submissionJobId, attemptNumber, adapter, startedAt)
    {
        PersistentJobId = persistentJobId;
        ProjectId = projectId;
        CampaignId = campaignId;
        SubmissionSourceId = submissionSourceId;
        TargetUrl = Guard.Required(targetUrl, 2_048, nameof(TargetUrl));
        Strategy = Guard.Required(strategy, 100, nameof(Strategy));
        IdentityId = identityId;
        TemplateId = templateId;
        ResolvedDisplayName = Guard.Required(resolvedDisplayName, 200, nameof(ResolvedDisplayName));
        ResolvedEmail = Guard.Required(resolvedEmail, 320, nameof(ResolvedEmail));
        ResolvedWebsite = Guard.Optional(resolvedWebsite, 2_048, nameof(ResolvedWebsite));
    }
    public Guid Id { get; private set; }
    public Guid SubmissionJobId { get; private set; }
    public Guid? PersistentJobId { get; private set; }
    public Guid? ProjectId { get; private set; }
    public Guid? CampaignId { get; private set; }
    public Guid? SubmissionSourceId { get; private set; }
    public string? TargetUrl { get; private set; }
    public int AttemptNumber { get; private set; }
    public string Adapter { get; private set; } = null!;
    public string? Strategy { get; private set; }
    public Guid? IdentityId { get; private set; }
    public Guid? TemplateId { get; private set; }
    public string? ResolvedDisplayName { get; private set; }
    public string? ResolvedEmail { get; private set; }
    public string? ResolvedWebsite { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public SubmissionStatus? Result { get; private set; }
    public int? HttpStatus { get; private set; }
    public string? ExternalReference { get; private set; }
    public string? Error { get; private set; }
    public string? Endpoint { get; private set; }
    public string? RedirectDestination { get; private set; }
    public ModerationStatus ModerationStatus { get; private set; }
    public SubmissionFailureKind FailureKind { get; private set; }
    public void Complete(SubmissionStatus result, int? httpStatus, string? externalReference, string? error, DateTimeOffset now)
    {
        if (FinishedAt is not null) throw new DomainRuleException("Submission attempt is already complete.");
        Result = result;
        HttpStatus = httpStatus;
        ExternalReference = Guard.Optional(externalReference, 500, nameof(ExternalReference));
        Error = Guard.Optional(error, 2_000, nameof(Error));
        FinishedAt = now;
    }

    public void Complete(OwnedWordPressSubmissionResultValue result, DateTimeOffset now)
    {
        Complete(result.Status, result.HttpStatus, result.ExternalReference, result.SafeError, now);
        ModerationStatus = result.ModerationStatus;
        FailureKind = result.FailureKind;
        Strategy = Guard.Optional(result.Strategy, 100, nameof(Strategy)) ?? Strategy;
        Endpoint = Guard.Optional(result.Endpoint, 2_048, nameof(Endpoint));
        RedirectDestination = Guard.Optional(result.RedirectDestination, 2_048, nameof(RedirectDestination));
    }
}

public sealed record OwnedWordPressSubmissionResultValue(SubmissionStatus Status, ModerationStatus ModerationStatus,
    string Strategy, int? HttpStatus, string? ExternalReference, SubmissionFailureKind FailureKind, string? SafeError,
    string? Endpoint = null, string? RedirectDestination = null);

public sealed class OwnedNetworkCampaignConfiguration
{
    private OwnedNetworkCampaignConfiguration() { }

    public OwnedNetworkCampaignConfiguration(Guid campaignId, Guid projectId, Guid ownedNetworkProfileId, string targetUrl,
        Guid identityPoolId, Guid templatePoolId, int globalConcurrency, int perDomainConcurrency,
        int perDomainDelayMilliseconds, int maximumAttempts, int verificationDelaySeconds, OwnedNetworkCampaignMode mode,
        string? domain, SourcePlatform? platform, CmsType? cmsType, TechnicalCompatibility technicalCompatibility,
        SubmissionSourceValidationStatus? validationStatus, string? tag, DateTimeOffset now)
        : this(campaignId, projectId, ownedNetworkProfileId, targetUrl, identityPoolId, templatePoolId,
            globalConcurrency, perDomainConcurrency, perDomainDelayMilliseconds, maximumAttempts,
            verificationDelaySeconds, mode, domain, platform, cmsType, technicalCompatibility,
            validationStatus, tag, now, null, null)
    {
    }

    public OwnedNetworkCampaignConfiguration(Guid campaignId, Guid projectId, Guid ownedNetworkProfileId, string targetUrl,
        Guid identityPoolId, Guid templatePoolId, int globalConcurrency, int perDomainConcurrency,
        int perDomainDelayMilliseconds, int maximumAttempts, int verificationDelaySeconds, OwnedNetworkCampaignMode mode,
        string? domain, SourcePlatform? platform, CmsType? cmsType, TechnicalCompatibility technicalCompatibility,
        SubmissionSourceValidationStatus? validationStatus, string? tag, DateTimeOffset now,
        SubmissionStatus? previousSubmissionStatus, BacklinkStatus? previousVerificationStatus)
    {
        if (globalConcurrency is < 1 or > 10_000) throw new DomainRuleException("Campaign global concurrency must be between 1 and 10000.");
        if (perDomainConcurrency is < 1 || perDomainConcurrency > globalConcurrency) throw new DomainRuleException("Campaign per-domain concurrency must be positive and not exceed global concurrency.");
        if (perDomainDelayMilliseconds is < 0 or > 86_400_000) throw new DomainRuleException("Campaign per-domain delay must be between 0 and 86400000 milliseconds.");
        if (maximumAttempts is < 1 or > 20) throw new DomainRuleException("Campaign maximum attempts must be between 1 and 20.");
        if (verificationDelaySeconds is < 0 or > 2_592_000) throw new DomainRuleException("Verification delay must be between 0 and 2592000 seconds.");
        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var target) ||
            (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(target.UserInfo))
            throw new DomainRuleException("Campaign target must be an absolute HTTP(S) URL without user information.");
        CampaignId = campaignId;
        ProjectId = projectId;
        OwnedNetworkProfileId = ownedNetworkProfileId;
        TargetUrl = target.AbsoluteUri;
        IdentityPoolId = identityPoolId;
        TemplatePoolId = templatePoolId;
        GlobalConcurrency = globalConcurrency;
        PerDomainConcurrency = perDomainConcurrency;
        PerDomainDelayMilliseconds = perDomainDelayMilliseconds;
        MaximumAttempts = maximumAttempts;
        VerificationDelaySeconds = verificationDelaySeconds;
        Mode = mode;
        Domain = Guard.Optional(domain?.Trim().TrimEnd('.').ToLowerInvariant(), 253, nameof(Domain));
        Platform = platform;
        CmsType = cmsType;
        TechnicalCompatibility = technicalCompatibility;
        ValidationStatus = validationStatus;
        Tag = Guard.Optional(tag, 100, nameof(Tag));
        PreviousSubmissionStatus = previousSubmissionStatus;
        PreviousVerificationStatus = previousVerificationStatus;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid CampaignId { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid OwnedNetworkProfileId { get; private set; }
    public string TargetUrl { get; private set; } = null!;
    public Guid IdentityPoolId { get; private set; }
    public Guid TemplatePoolId { get; private set; }
    public int GlobalConcurrency { get; private set; }
    public int PerDomainConcurrency { get; private set; }
    public int PerDomainDelayMilliseconds { get; private set; }
    public int MaximumAttempts { get; private set; }
    public int VerificationDelaySeconds { get; private set; }
    public OwnedNetworkCampaignMode Mode { get; private set; }
    public string? Domain { get; private set; }
    public SourcePlatform? Platform { get; private set; }
    public CmsType? CmsType { get; private set; }
    public TechnicalCompatibility TechnicalCompatibility { get; private set; }
    public SubmissionSourceValidationStatus? ValidationStatus { get; private set; }
    public string? Tag { get; private set; }
    public SubmissionStatus? PreviousSubmissionStatus { get; private set; }
    public BacklinkStatus? PreviousVerificationStatus { get; private set; }
    public DateTimeOffset? LastSourceCreatedAt { get; private set; }
    public Guid? LastSourceId { get; private set; }
    public long SourcesQueued { get; private set; }
    public bool ExpansionCompleted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Advance(DateTimeOffset sourceCreatedAt, Guid sourceId, int queued, DateTimeOffset now)
    {
        LastSourceCreatedAt = sourceCreatedAt;
        LastSourceId = sourceId;
        SourcesQueued = checked(SourcesQueued + queued);
        UpdatedAt = now;
    }

    public void CompleteExpansion(DateTimeOffset now)
    {
        ExpansionCompleted = true;
        UpdatedAt = now;
    }
}

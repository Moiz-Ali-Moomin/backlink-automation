namespace BacklinkStudio.Domain;

public sealed class BacklinkWorkflow
{
    private BacklinkWorkflow() { }

    public BacklinkWorkflow(Guid projectId, Guid? sourceImportId, Guid identityPoolId, Guid templatePoolId,
        string targetUrl, int globalConcurrency, int perDomainConcurrency, int perDomainDelayMilliseconds,
        int maximumAttempts, int verificationDelaySeconds, DateTimeOffset now)
    {
        if (globalConcurrency is < 1 or > 10_000) throw new DomainRuleException("Global concurrency must be between 1 and 10000.");
        if (perDomainConcurrency is < 1 or > 1_000 || perDomainConcurrency > globalConcurrency)
            throw new DomainRuleException("Per-domain concurrency must be between 1 and global concurrency.");
        if (perDomainDelayMilliseconds is < 0 or > 86_400_000)
            throw new DomainRuleException("Per-domain delay must be between 0 and 86400000 milliseconds.");
        if (maximumAttempts is < 1 or > 20) throw new DomainRuleException("Maximum attempts must be between 1 and 20.");
        if (verificationDelaySeconds is < 0 or > 2_592_000)
            throw new DomainRuleException("Verification delay must be between 0 and 2592000 seconds.");

        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        SourceImportId = sourceImportId;
        IdentityPoolId = identityPoolId;
        TemplatePoolId = templatePoolId;
        TargetUrl = Guard.Required(targetUrl, 2_048, nameof(TargetUrl));
        GlobalConcurrency = globalConcurrency;
        PerDomainConcurrency = perDomainConcurrency;
        PerDomainDelayMilliseconds = perDomainDelayMilliseconds;
        MaximumAttempts = maximumAttempts;
        VerificationDelaySeconds = verificationDelaySeconds;
        Status = BacklinkWorkflowStatus.Queued;
        CampaignIds = [];
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid? SourceImportId { get; private set; }
    public Guid IdentityPoolId { get; private set; }
    public Guid TemplatePoolId { get; private set; }
    public string TargetUrl { get; private set; } = null!;
    public int GlobalConcurrency { get; private set; }
    public int PerDomainConcurrency { get; private set; }
    public int PerDomainDelayMilliseconds { get; private set; }
    public int MaximumAttempts { get; private set; }
    public int VerificationDelaySeconds { get; private set; }
    public Guid[] CampaignIds { get; private set; } = [];
    public Guid? OrchestrationJobId { get; private set; }
    public BacklinkWorkflowStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Queue(Guid orchestrationJobId, IEnumerable<Guid> campaignIds, DateTimeOffset now)
    {
        OrchestrationJobId = orchestrationJobId;
        CampaignIds = campaignIds.Distinct().Take(1_000).ToArray();
        UpdatedAt = now;
    }

    public void AddCampaigns(IEnumerable<Guid> campaignIds, DateTimeOffset now)
    {
        CampaignIds = CampaignIds.Concat(campaignIds).Distinct().Take(1_000).ToArray();
        UpdatedAt = now;
    }

    public void MarkRunning(DateTimeOffset now)
    {
        Status = BacklinkWorkflowStatus.Running;
        UpdatedAt = now;
    }

    public void Complete(bool hasFailures, DateTimeOffset now)
    {
        Status = hasFailures ? BacklinkWorkflowStatus.CompletedWithFailures : BacklinkWorkflowStatus.Completed;
        UpdatedAt = now;
    }

    public void Fail(string reason, DateTimeOffset now)
    {
        Status = BacklinkWorkflowStatus.Failed;
        FailureReason = Guard.Required(reason, 2_000, nameof(FailureReason));
        UpdatedAt = now;
    }
}

public sealed class BacklinkWorkflowSource
{
    private BacklinkWorkflowSource() { }

    public BacklinkWorkflowSource(Guid workflowId, string originalUrl, string normalizedUrl, string domain,
        string host, BacklinkWorkflowSourceStatus status, string? reason, DateTimeOffset now,
        Guid? submissionSourceId = null, Guid? campaignId = null)
    {
        Id = Guid.CreateVersion7(now);
        WorkflowId = workflowId;
        SubmissionSourceId = submissionSourceId;
        CampaignId = campaignId;
        OriginalUrl = Guard.Required(originalUrl, 2_048, nameof(OriginalUrl));
        NormalizedUrl = Guard.Required(normalizedUrl, 2_048, nameof(NormalizedUrl));
        Domain = Guard.Required(domain, 253, nameof(Domain)).ToLowerInvariant();
        Host = Guard.Required(host, 253, nameof(Host)).ToLowerInvariant();
        Status = status;
        Reason = Guard.Optional(reason, 2_000, nameof(Reason));
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid WorkflowId { get; private set; }
    public Guid? SubmissionSourceId { get; private set; }
    public Guid? CampaignId { get; private set; }
    public string OriginalUrl { get; private set; } = null!;
    public string NormalizedUrl { get; private set; } = null!;
    public string Domain { get; private set; } = null!;
    public string Host { get; private set; } = null!;
    public BacklinkWorkflowSourceStatus Status { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Link(Guid submissionSourceId, Guid campaignId, DateTimeOffset now)
    {
        SubmissionSourceId = submissionSourceId;
        CampaignId = campaignId;
        Status = BacklinkWorkflowSourceStatus.Queued;
        Reason = null;
        UpdatedAt = now;
    }

    public void SetStatus(BacklinkWorkflowSourceStatus status, string? reason, DateTimeOffset now)
    {
        Status = status;
        Reason = Guard.Optional(reason, 2_000, nameof(Reason));
        UpdatedAt = now;
    }
}

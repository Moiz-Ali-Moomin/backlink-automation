namespace BacklinkStudio.Domain;

public sealed class Report
{
    private Report() { }

    public Report(Guid projectId, Guid? campaignId, ReportKind kind, ReportFormat format, DateTimeOffset now)
    {
        if (kind == ReportKind.CampaignPerformance && campaignId is null)
        {
            throw new DomainRuleException("Campaign performance reports require a campaign.");
        }

        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        CampaignId = campaignId;
        Kind = kind;
        Format = format;
        Status = ReportStatus.Queued;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid? CampaignId { get; private set; }
    public Guid? JobId { get; private set; }
    public ReportKind Kind { get; private set; }
    public ReportFormat Format { get; private set; }
    public ReportStatus Status { get; private set; }
    public string? ArtifactName { get; private set; }
    public string? ContentType { get; private set; }
    public long? ByteLength { get; private set; }
    public string? Sha256 { get; private set; }
    public int? RowCount { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public void AttachJob(Guid jobId, DateTimeOffset now)
    {
        if (JobId is not null && JobId != jobId)
        {
            throw new DomainRuleException("Report is already attached to a different job.");
        }

        JobId = jobId;
        UpdatedAt = now;
    }

    public void Start(DateTimeOffset now)
    {
        if (Status is not (ReportStatus.Queued or ReportStatus.Failed or ReportStatus.Generating))
        {
            throw new DomainRuleException($"Report in state {Status} cannot start generation.");
        }

        Status = ReportStatus.Generating;
        StartedAt = now;
        CompletedAt = null;
        Error = null;
        UpdatedAt = now;
    }

    public void Complete(string artifactName, string contentType, long byteLength, string sha256, int rowCount, DateTimeOffset now)
    {
        if (Status != ReportStatus.Generating)
        {
            throw new DomainRuleException($"Report in state {Status} cannot complete.");
        }
        if (byteLength < 0) throw new DomainRuleException("Report byte length cannot be negative.");
        if (rowCount < 0) throw new DomainRuleException("Report row count cannot be negative.");

        ArtifactName = Guard.Required(artifactName, 255, nameof(ArtifactName));
        ContentType = Guard.Required(contentType, 200, nameof(ContentType));
        ByteLength = byteLength;
        Sha256 = Guard.Required(sha256, 64, nameof(Sha256)).ToLowerInvariant();
        RowCount = rowCount;
        Status = ReportStatus.Completed;
        Error = null;
        CompletedAt = now;
        UpdatedAt = now;
    }

    public void Fail(string error, DateTimeOffset now)
    {
        if (Status != ReportStatus.Generating)
        {
            throw new DomainRuleException($"Report in state {Status} cannot fail.");
        }

        Error = Guard.Required(error, 2_000, nameof(Error));
        Status = ReportStatus.Failed;
        CompletedAt = now;
        UpdatedAt = now;
    }
}

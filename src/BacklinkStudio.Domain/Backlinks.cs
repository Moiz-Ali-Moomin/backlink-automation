namespace BacklinkStudio.Domain;

public sealed class Backlink
{
    private Backlink() { }

    public Backlink(
        Guid projectId,
        Guid? campaignId,
        Guid? submissionJobId,
        string sourceUrl,
        string normalizedSourceUrl,
        string targetUrl,
        string normalizedTargetUrl,
        string domain,
        DateTimeOffset now,
        Guid? submissionSourceId = null,
        Guid? submissionAttemptId = null)
    {
        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        CampaignId = campaignId;
        SubmissionJobId = submissionJobId;
        SubmissionSourceId = submissionSourceId;
        SubmissionAttemptId = submissionAttemptId;
        SourceUrl = Guard.Required(sourceUrl, 2_048, nameof(SourceUrl));
        NormalizedSourceUrl = Guard.Required(normalizedSourceUrl, 2_048, nameof(NormalizedSourceUrl));
        TargetUrl = Guard.Required(targetUrl, 2_048, nameof(TargetUrl));
        NormalizedTargetUrl = Guard.Required(normalizedTargetUrl, 2_048, nameof(NormalizedTargetUrl));
        Domain = Guard.Required(domain, 253, nameof(Domain)).ToLowerInvariant();
        Status = BacklinkStatus.PendingVerification;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid? CampaignId { get; private set; }
    public Guid? SubmissionJobId { get; private set; }
    public Guid? SubmissionSourceId { get; private set; }
    public Guid? SubmissionAttemptId { get; private set; }
    public string SourceUrl { get; private set; } = null!;
    public string NormalizedSourceUrl { get; private set; } = null!;
    public string TargetUrl { get; private set; } = null!;
    public string NormalizedTargetUrl { get; private set; } = null!;
    public string Domain { get; private set; } = null!;
    public BacklinkStatus Status { get; private set; }
    public string? AnchorText { get; private set; }
    public string[] Rel { get; private set; } = [];
    public bool Nofollow { get; private set; }
    public bool Ugc { get; private set; }
    public bool Sponsored { get; private set; }
    public int? HttpStatus { get; private set; }
    public string? CanonicalUrl { get; private set; }
    public DateTimeOffset? FirstSeenAt { get; private set; }
    public DateTimeOffset? LastSeenAt { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void ApplyVerification(
        bool found,
        int? httpStatus,
        string? anchorText,
        IEnumerable<string> rel,
        string? canonicalUrl,
        string? error,
        DateTimeOffset checkedAt)
    {
        HttpStatus = httpStatus;
        CanonicalUrl = Guard.Optional(canonicalUrl, 2_048, nameof(CanonicalUrl));
        LastCheckedAt = checkedAt;
        UpdatedAt = checkedAt;

        if (error is not null)
        {
            Status = BacklinkStatus.Error;
            return;
        }

        if (!found)
        {
            Status = FirstSeenAt is null ? BacklinkStatus.Missing : BacklinkStatus.Lost;
            return;
        }

        AnchorText = Guard.Optional(anchorText, 500, nameof(AnchorText));
        Rel = rel.Select(x => Guard.Required(x, 50, nameof(Rel)).ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Take(50)
            .ToArray();
        Nofollow = Rel.Contains("nofollow", StringComparer.Ordinal);
        Ugc = Rel.Contains("ugc", StringComparer.Ordinal);
        Sponsored = Rel.Contains("sponsored", StringComparer.Ordinal);
        FirstSeenAt ??= checkedAt;
        LastSeenAt = checkedAt;
        Status = BacklinkStatus.Verified;
    }
}

public sealed class VerificationCheck
{
    private VerificationCheck() { }

    public VerificationCheck(
        Guid backlinkId,
        DateTimeOffset checkedAt,
        bool found,
        int? httpStatus,
        string? anchor,
        IEnumerable<string> rel,
        string? error,
        TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new DomainRuleException("Verification duration cannot be negative.");
        Id = Guid.CreateVersion7(checkedAt);
        BacklinkId = backlinkId;
        CheckedAt = checkedAt;
        Found = found;
        HttpStatus = httpStatus;
        Anchor = Guard.Optional(anchor, 500, nameof(Anchor));
        Rel = rel.Select(x => Guard.Required(x, 50, nameof(Rel)).ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Take(50)
            .ToArray();
        Error = Guard.Optional(error, 2_000, nameof(Error));
        DurationMilliseconds = checked((int)Math.Min(duration.TotalMilliseconds, int.MaxValue));
    }

    public Guid Id { get; private set; }
    public Guid BacklinkId { get; private set; }
    public DateTimeOffset CheckedAt { get; private set; }
    public bool Found { get; private set; }
    public int? HttpStatus { get; private set; }
    public string? Anchor { get; private set; }
    public string[] Rel { get; private set; } = [];
    public string? Error { get; private set; }
    public int DurationMilliseconds { get; private set; }
}

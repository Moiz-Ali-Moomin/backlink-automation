namespace BacklinkStudio.Domain;

public sealed class CandidateSite
{
    private CandidateSite() { }

    public CandidateSite(Guid projectId, string domain, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        Domain = Guard.Required(domain, 253, nameof(Domain)).ToLowerInvariant();
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Domain { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
}

public sealed class CandidatePage
{
    private CandidatePage() { }

    public CandidatePage(Guid projectId, Guid candidateSiteId, string url, string normalizedUrl, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        CandidateSiteId = candidateSiteId;
        Url = Guard.Required(url, 2_048, nameof(Url));
        NormalizedUrl = Guard.Required(normalizedUrl, 2_048, nameof(NormalizedUrl));
        AnalysisStatus = CandidateAnalysisStatus.New;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid CandidateSiteId { get; private set; }
    public string Url { get; private set; } = null!;
    public string NormalizedUrl { get; private set; } = null!;
    public string? FinalUrl { get; private set; }
    public int? HttpStatus { get; private set; }
    public string? ContentType { get; private set; }
    public string? Title { get; private set; }
    public string? CanonicalUrl { get; private set; }
    public string? Cms { get; private set; }
    public string? RobotsDirectives { get; private set; }
    public bool ExistingTargetLink { get; private set; }
    public string[] EligibleSignals { get; private set; } = [];
    public bool RequiresJavaScript { get; private set; }
    public string? AnalysisError { get; private set; }
    public CandidateAnalysisStatus AnalysisStatus { get; private set; }
    public DateTimeOffset? LastAnalyzedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void ApplyAnalysis(
        string? finalUrl,
        int? httpStatus,
        string? contentType,
        string? title,
        string? canonicalUrl,
        string? cms,
        string? robotsDirectives,
        bool existingTargetLink,
        IReadOnlyCollection<string> eligibleSignals,
        bool requiresJavaScript,
        string? error,
        DateTimeOffset now)
    {
        FinalUrl = Guard.Optional(finalUrl, 2_048, nameof(FinalUrl));
        HttpStatus = httpStatus;
        ContentType = Guard.Optional(contentType, 200, nameof(ContentType));
        Title = Guard.Optional(title, 500, nameof(Title));
        CanonicalUrl = Guard.Optional(canonicalUrl, 2_048, nameof(CanonicalUrl));
        Cms = Guard.Optional(cms, 100, nameof(Cms));
        RobotsDirectives = Guard.Optional(robotsDirectives, 500, nameof(RobotsDirectives));
        ExistingTargetLink = existingTargetLink;
        EligibleSignals = eligibleSignals.Distinct(StringComparer.Ordinal).Take(20).Select(x => Guard.Required(x, 100, nameof(EligibleSignals))).ToArray();
        RequiresJavaScript = requiresJavaScript;
        AnalysisError = Guard.Optional(error, 2_000, nameof(AnalysisError));
        AnalysisStatus = error is null ? CandidateAnalysisStatus.Analyzed : CandidateAnalysisStatus.Error;
        LastAnalyzedAt = now;
    }

    public void MarkBlocked(string reason, DateTimeOffset now)
    {
        AnalysisError = Guard.Required(reason, 2_000, nameof(reason));
        AnalysisStatus = CandidateAnalysisStatus.Blocked;
        LastAnalyzedAt = now;
    }
}

public sealed class Opportunity
{
    private readonly List<OpportunityScoreReason> _scoreReasons = [];
    private Opportunity() { }

    public Opportunity(Guid projectId, Guid candidatePageId, string sourceUrl, string domain, string reason, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        CandidatePageId = candidatePageId;
        Type = OpportunityType.Unknown;
        SourceUrl = Guard.Required(sourceUrl, 2_048, nameof(SourceUrl));
        Domain = Guard.Required(domain, 253, nameof(Domain)).ToLowerInvariant();
        QualityScore = 0;
        RiskScore = 0;
        AutomationStatus = AutomationStatus.Unreviewed;
        AnalysisReason = Guard.Required(reason, 2_000, nameof(AnalysisReason));
        DetectedAt = now;
        LastAnalyzedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid CandidatePageId { get; private set; }
    public OpportunityType Type { get; private set; }
    public string SourceUrl { get; private set; } = null!;
    public string Domain { get; private set; } = null!;
    public int QualityScore { get; private set; }
    public int RiskScore { get; private set; }
    public AutomationStatus AutomationStatus { get; private set; }
    public string AnalysisReason { get; private set; } = null!;
    public DateTimeOffset DetectedAt { get; private set; }
    public DateTimeOffset LastAnalyzedAt { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public string? ApprovedBy { get; private set; }
    public IReadOnlyCollection<OpportunityScoreReason> ScoreReasons => _scoreReasons.AsReadOnly();

    public void ApplyAnalysis(
        OpportunityType type,
        int qualityScore,
        int riskScore,
        AutomationStatus automationStatus,
        string reason,
        IEnumerable<OpportunityScoreReason> scoreReasons,
        DateTimeOffset now)
    {
        if (qualityScore is < 0 or > 100 || riskScore is < 0 or > 100)
        {
            throw new DomainRuleException("Opportunity scores must be between 0 and 100.");
        }

        Type = type;
        QualityScore = qualityScore;
        RiskScore = riskScore;
        AutomationStatus = automationStatus;
        AnalysisReason = Guard.Required(reason, 2_000, nameof(reason));
        LastAnalyzedAt = now;
        _scoreReasons.Clear();
        _scoreReasons.AddRange(scoreReasons);
    }

    public void Approve(string actorId, DateTimeOffset now)
    {
        if (AutomationStatus is not (AutomationStatus.ApprovalRequired or AutomationStatus.Approved))
        {
            throw new DomainRuleException($"Opportunity in state {AutomationStatus} cannot be approved for automation.");
        }

        AutomationStatus = AutomationStatus.Approved;
        ApprovedBy = Guard.Required(actorId, 200, nameof(actorId));
        ApprovedAt = now;
    }
}

public sealed class OpportunityScoreReason
{
    private OpportunityScoreReason() { }

    public OpportunityScoreReason(Guid opportunityId, ScoreKind kind, string code, int points, string explanation, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        OpportunityId = opportunityId;
        Kind = kind;
        Code = Guard.Required(code, 100, nameof(code));
        Points = points;
        Explanation = Guard.Required(explanation, 500, nameof(explanation));
        CreatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid OpportunityId { get; private set; }
    public ScoreKind Kind { get; private set; }
    public string Code { get; private set; } = null!;
    public int Points { get; private set; }
    public string Explanation { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
}

using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public sealed record AnalyzedLink(string NormalizedUrl, string AnchorText, IReadOnlyList<string> Rel);

public sealed record SiteDocumentSignals(
    string? Generator,
    IReadOnlyList<string> AssetUrls,
    IReadOnlyList<string> Markers);

public sealed record CmsDetectionResult(string Name, int Confidence, string Reason);

public interface ICmsDetector
{
    string Name { get; }
    CmsDetectionResult? Detect(SiteDocumentSignals signals);
}

public sealed record SiteAnalysisResult(
    string OriginalUrl,
    string? FinalUrl,
    int? HttpStatus,
    string? ContentType,
    string? Title,
    string? CanonicalUrl,
    string Domain,
    string? Cms,
    string? RobotsDirectives,
    IReadOnlyList<AnalyzedLink> Links,
    IReadOnlyList<string> EligibleSignals,
    bool RequiresJavaScript,
    int OutboundLinkCount,
    int ExternalLinkCount,
    int WordCount,
    DateTimeOffset AnalyzedAt,
    string? Error,
    TimeSpan Duration);

public interface ISiteAnalyzer
{
    Task<SiteAnalysisResult> AnalyzeAsync(Uri url, CancellationToken cancellationToken);
}

public sealed record OpportunityClassification(OpportunityType Type, string Reason);

public interface IOpportunityClassifier
{
    OpportunityClassification Classify(SiteAnalysisResult analysis);
}

public sealed record ScoreReason(ScoreKind Kind, string Code, int Points, string Explanation);
public sealed record OpportunityScore(int QualityScore, int RiskScore, IReadOnlyList<ScoreReason> Reasons);

public interface IOpportunityScorer
{
    OpportunityScore Score(SiteAnalysisResult analysis, OpportunityClassification classification);
}

public sealed record PolicyEvaluationContext(
    OpportunityType OpportunityType,
    string SourceUrl,
    string Domain,
    int QualityScore,
    int RiskScore,
    CampaignApprovalMode CampaignMode,
    bool SourceAuthorized,
    bool ExplicitlyApproved,
    bool DuplicateAction,
    int HourlyActions,
    int DailyActions,
    int DomainActions);

public sealed record PolicyEvaluationResult(PolicyDecision Decision, IReadOnlyList<string> Reasons);

public interface IPolicyEvaluator
{
    PolicyEvaluationResult Evaluate(PolicyDefinition policy, IReadOnlyCollection<BlocklistEntry> blocklist, PolicyEvaluationContext context);
}

public sealed record SubmissionContext(Guid SubmissionJobId, Guid CampaignId, Guid ProjectId, string SourceUrl, string TargetUrl, string? AnchorText, string AuthorizationProfileKey, string IdempotencyKey);
public sealed record SubmissionResult(bool Accepted, bool PendingModeration, int? HttpStatus, string? ExternalReference, string? Error);

public interface ISubmissionAdapter
{
    string Name { get; }
    bool CanHandle(string authorizationProfileKey, OpportunityType opportunityType);
    Task<SubmissionResult> SubmitAsync(SubmissionContext context, CancellationToken cancellationToken);
}

public sealed record SubmissionAuthorization(string ProfileKey, string SourceDomain, IReadOnlySet<OpportunityType> AllowedTypes);

public interface ISubmissionAuthorizationResolver
{
    SubmissionAuthorization Resolve(string profileKey);
}

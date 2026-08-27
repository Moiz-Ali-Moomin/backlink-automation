using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Opportunities;

public sealed class WordPressDetector : ICmsDetector
{
    public string Name => "WordPress";

    public CmsDetectionResult? Detect(SiteDocumentSignals signals)
    {
        if (signals.Generator?.Contains("wordpress", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new CmsDetectionResult(Name, 100, "The generator metadata identifies WordPress.");
        }

        if (signals.AssetUrls.Any(x => x.Contains("/wp-content/", StringComparison.OrdinalIgnoreCase) || x.Contains("/wp-includes/", StringComparison.OrdinalIgnoreCase)))
        {
            return new CmsDetectionResult(Name, 90, "Document assets use WordPress paths.");
        }

        return signals.Markers.Any(x => x.Equals("wordpress", StringComparison.OrdinalIgnoreCase))
            ? new CmsDetectionResult(Name, 70, "Document markup contains a WordPress marker.")
            : null;
    }
}

public sealed class GenericDetector : ICmsDetector
{
    public string Name => "Generic";
    public CmsDetectionResult? Detect(SiteDocumentSignals signals) => new(Name, 1, "No supported CMS signature was detected.");
}

public sealed class OpportunityClassifier : IOpportunityClassifier
{
    public OpportunityClassification Classify(SiteAnalysisResult analysis)
    {
        var signals = analysis.EligibleSignals.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (signals.Contains("directory-listing"))
        {
            return new(OpportunityType.DirectoryListing, "The page exposes directory or listing contribution signals; authorization is still required.");
        }
        if (signals.Contains("resource-page"))
        {
            return new(OpportunityType.ResourcePage, "The page appears to curate external resources.");
        }
        if (signals.Contains("partner-page"))
        {
            return new(OpportunityType.PartnerPage, "The page contains partner contribution signals.");
        }
        if (signals.Contains("profile"))
        {
            return new(OpportunityType.Profile, "The page contains profile or member signals.");
        }
        if (signals.Contains("comment-form"))
        {
            return new(OpportunityType.PermittedComment, "A comment form was detected; detection does not establish permission to submit.");
        }

        return new(OpportunityType.Unknown, "No supported opportunity type could be classified deterministically.");
    }
}

public sealed class OpportunityScorer : IOpportunityScorer
{
    public OpportunityScore Score(SiteAnalysisResult analysis, OpportunityClassification classification)
    {
        var reasons = new List<ScoreReason>();
        var quality = 0;
        var risk = 0;

        Add(ScoreKind.Quality, "reachable", analysis.HttpStatus is >= 200 and < 300 ? 25 : 0, "A successful document response improves opportunity quality.");
        Add(ScoreKind.Quality, "html", analysis.ContentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true ? 15 : 0, "HTML content can be evaluated for backlink placement.");
        Add(ScoreKind.Quality, "indexable", analysis.HttpStatus is >= 200 and < 300 && !IsNoIndex(analysis.RobotsDirectives) ? 20 : 0, "Indexable pages have greater placement value.");
        Add(ScoreKind.Quality, "content-depth", analysis.WordCount >= 250 ? 15 : analysis.WordCount >= 75 ? 8 : 0, "Substantive page content improves quality.");
        Add(ScoreKind.Quality, "classified", classification.Type == OpportunityType.Unknown ? 0 : 15, "A supported opportunity classification is actionable.");
        Add(ScoreKind.Quality, "https", analysis.FinalUrl?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true ? 10 : 0, "HTTPS is a positive transport signal.");

        Add(ScoreKind.Risk, "fetch-error", analysis.Error is null ? 0 : 50, "Fetch or parsing errors increase uncertainty.");
        Add(ScoreKind.Risk, "http-status", analysis.HttpStatus is >= 400 or null ? 25 : 0, "An unsuccessful response increases risk.");
        Add(ScoreKind.Risk, "noindex", IsNoIndex(analysis.RobotsDirectives) ? 25 : 0, "A noindex directive reduces placement value.");
        Add(ScoreKind.Risk, "javascript", analysis.RequiresJavaScript ? 10 : 0, "A JavaScript-only workflow requires manual review.");
        Add(ScoreKind.Risk, "outbound-density", analysis.OutboundLinkCount >= 100 || analysis.ExternalLinkCount >= 75 ? 25 : 0, "High outbound-link density can indicate a low-quality placement environment.");
        Add(ScoreKind.Risk, "unknown-type", classification.Type == OpportunityType.Unknown ? 15 : 0, "Unknown opportunity types require additional review.");

        return new(Math.Clamp(quality, 0, 100), Math.Clamp(risk, 0, 100), reasons);

        void Add(ScoreKind kind, string code, int points, string explanation)
        {
            reasons.Add(new ScoreReason(kind, code, points, explanation));
            if (kind == ScoreKind.Quality)
            {
                quality += points;
            }
            else
            {
                risk += points;
            }
        }
    }

    private static bool IsNoIndex(string? robots) => robots?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(x => x.Equals("noindex", StringComparison.OrdinalIgnoreCase)) == true;
}

public sealed class PolicyEvaluator : IPolicyEvaluator
{
    public PolicyEvaluationResult Evaluate(PolicyDefinition policy, IReadOnlyCollection<BlocklistEntry> blocklist, PolicyEvaluationContext context)
    {
        var reasons = new List<string>();
        if (BlocklistMatcher.IsBlocked(blocklist, context.SourceUrl, context.Domain))
        {
            return new(PolicyDecision.Rejected, ["The source matches an enabled project blocklist entry."]);
        }
        if (context.DuplicateAction)
        {
            return new(PolicyDecision.Rejected, ["A duplicate action is not permitted."]);
        }
        if (context.QualityScore < policy.MinimumQualityScore)
        {
            reasons.Add($"Quality score {context.QualityScore} is below the required {policy.MinimumQualityScore}.");
        }
        if (context.RiskScore > policy.MaximumRiskScore)
        {
            reasons.Add($"Risk score {context.RiskScore} exceeds the allowed {policy.MaximumRiskScore}.");
        }
        if (reasons.Count > 0)
        {
            return new(PolicyDecision.Rejected, reasons);
        }
        if (!context.SourceAuthorized)
        {
            return new(PolicyDecision.ManualActionRequired, ["Source ownership or explicit submission authorization has not been established."]);
        }
        if (context.HourlyActions >= policy.HourlyActionLimit || context.DailyActions >= policy.DailyActionLimit || context.DomainActions >= policy.PerDomainActionLimit)
        {
            return new(PolicyDecision.Rejected, ["A configured action limit has been reached."]);
        }
        if (context.ExplicitlyApproved)
        {
            return new(PolicyDecision.Allowed, ["The authorized action was explicitly approved and satisfies current policy limits."]);
        }
        if (!policy.AutomationEnabled || context.CampaignMode == CampaignApprovalMode.Manual || policy.ManualReviewRequired)
        {
            return new(PolicyDecision.ApprovalRequired, ["Project policy requires approval before execution."]);
        }

        return new(PolicyDecision.Allowed, ["The authorized action satisfies configured policy thresholds and limits."]);
    }
}

using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using BacklinkStudio.Opportunities;

namespace BacklinkStudio.UnitTests;

public sealed class AnalysisRulesTests
{
    [Fact]
    public void CmsDetection_UsesSpecificDetectorBeforeGenericFallback()
    {
        var wordpress = new WordPressDetector();
        var generic = new GenericDetector();
        var signals = new SiteDocumentSignals("WordPress 6.x", [], []);

        Assert.Equal("WordPress", wordpress.Detect(signals)?.Name);
        Assert.Equal("Generic", generic.Detect(signals)?.Name);
    }

    [Fact]
    public void Scoring_IsBoundedAndEveryPointHasAReason()
    {
        var analysis = Result(status: 200, contentType: "text/html", robots: null, wordCount: 500, outbound: 12, external: 4, signals: ["resource-page"]);
        var classification = new OpportunityClassifier().Classify(analysis);
        var score = new OpportunityScorer().Score(analysis, classification);

        Assert.Equal(OpportunityType.ResourcePage, classification.Type);
        Assert.InRange(score.QualityScore, 0, 100);
        Assert.InRange(score.RiskScore, 0, 100);
        Assert.Equal(score.QualityScore, score.Reasons.Where(x => x.Kind == ScoreKind.Quality).Sum(x => x.Points));
        Assert.Equal(score.RiskScore, score.Reasons.Where(x => x.Kind == ScoreKind.Risk).Sum(x => x.Points));
        Assert.All(score.Reasons, reason => Assert.False(string.IsNullOrWhiteSpace(reason.Explanation)));
    }

    [Fact]
    public void Policy_RejectsBlocklistAndThresholdFailures()
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.CreateVersion7();
        var policy = new PolicyDefinition(projectId, now);
        var block = new BlocklistEntry(projectId, BlocklistMatchType.Domain, "blocked.example", "Not eligible", now);
        var evaluator = new PolicyEvaluator();

        var blocked = evaluator.Evaluate(policy, [block], Context("child.blocked.example", 90, 5, true));
        var lowQuality = evaluator.Evaluate(policy, [], Context("allowed.example", 59, 5, true));

        Assert.Equal(PolicyDecision.Rejected, blocked.Decision);
        Assert.Equal(PolicyDecision.Rejected, lowQuality.Decision);
    }

    [Fact]
    public void Policy_NeverAllowsAnUnauthorizedSource()
    {
        var now = DateTimeOffset.UtcNow;
        var policy = new PolicyDefinition(Guid.CreateVersion7(), now);
        policy.Update(true, 0, 100, false, 10, 50, 1, now);

        var result = new PolicyEvaluator().Evaluate(policy, [], Context("allowed.example", 100, 0, false, CampaignApprovalMode.Automatic));

        Assert.Equal(PolicyDecision.ManualActionRequired, result.Decision);
    }

    private static PolicyEvaluationContext Context(string domain, int quality, int risk, bool authorized, CampaignApprovalMode mode = CampaignApprovalMode.Manual) =>
        new(OpportunityType.ResourcePage, $"https://{domain}/resources", domain, quality, risk, mode, authorized, false, false, 0, 0, 0);

    private static SiteAnalysisResult Result(int status, string contentType, string? robots, int wordCount, int outbound, int external, IReadOnlyList<string> signals) =>
        new("https://source.example/", "https://source.example/", status, contentType, "Resources", null, "source.example", "Generic", robots, [], signals, false, outbound, external, wordCount, DateTimeOffset.UtcNow, null, TimeSpan.FromMilliseconds(5));
}

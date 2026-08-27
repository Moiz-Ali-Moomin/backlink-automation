using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Opportunities;

public sealed class AnalysisJobExecutor(
    ICandidateRepository candidates,
    IOpportunityRepository opportunities,
    IProjectRepository projects,
    IPolicyRepository policies,
    ISiteAnalyzer siteAnalyzer,
    IDomainRateLimiter rateLimiter,
    IOpportunityClassifier classifier,
    IOpportunityScorer scorer,
    IPolicyEvaluator policyEvaluator,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IJobExecutor
{
    private const int BatchSize = 250;
    public JobType JobType => JobType.Analysis;

    public async Task ExecuteAsync(PersistentJob job, string workerId, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<AnalysisJobPayload>(job.Payload);
        if (payload is null || payload.Version != 1 || payload.ProjectId != job.ProjectId)
        {
            throw new ValidationException("Analysis job payload is invalid or unsupported.");
        }

        var created = 0;
        var blocked = 0;
        var errors = 0;
        var targets = await projects.ListEnabledTargetsAsync(job.ProjectId, cancellationToken);
        var policy = await policies.GetAsync(job.ProjectId, false, cancellationToken)
            ?? throw new InvalidOperationException("Project policy invariant is missing.");
        var blocklist = await policies.ListEnabledBlocklistAsync(job.ProjectId, cancellationToken);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = await candidates.GetUnanalyzedAsync(job.ProjectId, BatchSize, cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            var now = timeProvider.GetUtcNow();
            foreach (var candidate in batch)
            {
                var preliminary = policyEvaluator.Evaluate(policy, blocklist, new PolicyEvaluationContext(
                    OpportunityType.Unknown, candidate.Page.NormalizedUrl, candidate.Domain, 100, 0,
                    CampaignApprovalMode.Manual, false, false, false, 0, 0, 0));
                if (preliminary.Decision == PolicyDecision.Rejected)
                {
                    candidates.MarkBlocked(candidate.Page, string.Join(' ', preliminary.Reasons), now);
                    blocked++;
                    continue;
                }

                await rateLimiter.WaitAsync(job.ProjectId, job.CampaignId, candidate.Domain, cancellationToken);
                var analysis = await siteAnalyzer.AnalyzeAsync(new Uri(candidate.Page.NormalizedUrl), cancellationToken);
                var domainTag = new KeyValuePair<string, object?>("server.address", analysis.Domain);
                BacklinkStudioTelemetry.AnalysisRequests.Add(1, domainTag);
                BacklinkStudioTelemetry.AnalysisDuration.Record(analysis.Duration.TotalSeconds, domainTag);
                if (analysis.Error is not null)
                {
                    BacklinkStudioTelemetry.AnalysisFailures.Add(1, domainTag);
                }
                var existingTargetLink = analysis.Links.Any(link => targets.Any(target => string.Equals(target.NormalizedUrl, link.NormalizedUrl, StringComparison.Ordinal)));
                candidates.ApplyAnalysis(candidate.Page, analysis, existingTargetLink);
                var classification = classifier.Classify(analysis);
                var score = scorer.Score(analysis, classification);
                var evaluation = policyEvaluator.Evaluate(policy, blocklist, new PolicyEvaluationContext(
                    classification.Type, analysis.FinalUrl ?? candidate.Page.NormalizedUrl, analysis.Domain,
                    score.QualityScore, score.RiskScore, CampaignApprovalMode.Manual, false, false, false, 0, 0, 0));

                if (!await opportunities.ExistsForCandidateAsync(candidate.Page.Id, cancellationToken))
                {
                    var opportunity = new Opportunity(
                        job.ProjectId,
                        candidate.Page.Id,
                        analysis.FinalUrl ?? candidate.Page.NormalizedUrl,
                        analysis.Domain,
                        classification.Reason,
                        now);
                    var automationStatus = evaluation.Decision switch
                    {
                        PolicyDecision.Allowed => AutomationStatus.Approved,
                        PolicyDecision.Rejected => AutomationStatus.Rejected,
                        PolicyDecision.ApprovalRequired => AutomationStatus.ApprovalRequired,
                        _ => AutomationStatus.ManualActionRequired
                    };
                    var scoreReasons = score.Reasons.Select(reason => new OpportunityScoreReason(opportunity.Id, reason.Kind, reason.Code, reason.Points, reason.Explanation, now));
                    opportunity.ApplyAnalysis(classification.Type, score.QualityScore, score.RiskScore, automationStatus, $"{classification.Reason} Policy: {string.Join(' ', evaluation.Reasons)}", scoreReasons, now);
                    opportunities.Add(opportunity);
                    created++;
                }
                if (analysis.Error is not null)
                {
                    errors++;
                }
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var completedAt = timeProvider.GetUtcNow();
        audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "analysis.complete", job.ProjectId, job.CampaignId, job.Id, job.CorrelationId, $"opportunitiesCreated={created};blocked={blocked};errors={errors}", "succeeded", null, completedAt));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

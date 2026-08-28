using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Submission;

public sealed class SubmissionJobExecutor(
    ISubmissionRepository submissions,
    IBacklinkRepository backlinks,
    IPolicyRepository policies,
    ISubmissionAuthorizationResolver authorizations,
    IEnumerable<ISubmissionAdapter> adapters,
    IPolicyEvaluator policyEvaluator,
    IDomainRateLimiter rateLimiter,
    IUrlNormalizer urlNormalizer,
    IOwnedNetworkCampaignRepository ownedCampaigns,
    IOwnedNetworkExecutionAuthorizer ownedNetworkAuthorizer,
    ISubmissionContentRepository submissionContent,
    ISubmissionContentResolver contentResolver,
    IOwnedWordPressCommentAdapter ownedWordPress,
    IBacklinkWorkflowRepository workflows,
    IJobQueue jobs,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IJobExecutor
{
    public JobType JobType => JobType.Submission;

    public async Task ExecuteAsync(PersistentJob job, string workerId, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(job.Payload);
        var version = document.RootElement.TryGetProperty("Version", out var property) ? property.GetInt32() : 0;
        if (version == 2)
        {
            await ExecuteOwnedNetworkAsync(job, workerId, cancellationToken);
            return;
        }
        await ExecuteLegacyAsync(job, workerId, cancellationToken);
    }

    private async Task ExecuteLegacyAsync(PersistentJob job, string workerId, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SubmissionJobPayload>(job.Payload);
        if (payload?.Version != 1 || job.CampaignId is null || job.Domain is null) throw new ValidationException("Submission job payload is invalid or unsupported.");
        var work = await submissions.GetWorkItemAsync(job.Id, cancellationToken) ?? throw new ValidationException("Submission work item is missing.");
        var lifecycleNow = timeProvider.GetUtcNow();
        if (work.Campaign.Status == CampaignStatus.Stopped)
        {
            work.Submission.MarkCancelled(lifecycleNow);
            audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "submission.campaign_stopped", job.ProjectId, work.Campaign.Id, job.Id, job.CorrelationId, $"submissionId={work.Submission.Id}", "cancelled", null, lifecycleNow));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new PolicyRejectedException("The campaign has been stopped.");
        }
        if (work.Campaign.Status != CampaignStatus.Running)
        {
            throw new RateLimitExceededException("The campaign is not currently running.");
        }
        var authorization = authorizations.Resolve(work.Campaign.AuthorizationProfileKey);
        var sourceAuthorized = string.Equals(authorization.SourceDomain, work.Opportunity.Domain, StringComparison.OrdinalIgnoreCase) && authorization.AllowedTypes.Contains(work.Opportunity.Type);
        var policy = await policies.GetAsync(job.ProjectId, false, cancellationToken) ?? throw new ValidationException("Project policy is missing.");
        var blocklist = await policies.ListEnabledBlocklistAsync(job.ProjectId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var hourly = await submissions.CountSuccessfulActionsAsync(job.ProjectId, null, null, now.AddHours(-1), cancellationToken);
        var daily = await submissions.CountSuccessfulActionsAsync(job.ProjectId, null, null, now.AddDays(-1), cancellationToken);
        var domain = await submissions.CountSuccessfulActionsAsync(job.ProjectId, null, work.Opportunity.Domain, now.AddDays(-1), cancellationToken);
        var campaignDaily = await submissions.CountSuccessfulActionsAsync(job.ProjectId, work.Campaign.Id, null, now.AddDays(-1), cancellationToken);
        if (campaignDaily >= work.Campaign.DailyActionLimit) throw new RateLimitExceededException("The campaign daily action limit has been reached.");
        var duplicate = await submissions.HasSuccessfulSubmissionAsync(job.ProjectId, work.Opportunity.Id, work.Submission.Id, cancellationToken);
        var decision = policyEvaluator.Evaluate(policy, blocklist, new PolicyEvaluationContext(work.Opportunity.Type, work.Opportunity.SourceUrl, work.Opportunity.Domain, work.Opportunity.QualityScore, work.Opportunity.RiskScore, work.Campaign.ApprovalMode, sourceAuthorized, work.CampaignOpportunity.ExplicitlyApproved, duplicate, hourly, daily, domain));
        if (decision.Decision != PolicyDecision.Allowed)
        {
            if (decision.Decision == PolicyDecision.Rejected) work.Submission.MarkRejected(now);
            else work.Submission.MarkFailed(true, now);
            audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "submission.policy_rejected", job.ProjectId, work.Campaign.Id, job.Id, job.CorrelationId, $"decision={decision.Decision};submissionId={work.Submission.Id}", "rejected", null, now));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new PolicyRejectedException(string.Join(' ', decision.Reasons));
        }
        var adapter = adapters.SingleOrDefault(x => x.CanHandle(work.Campaign.AuthorizationProfileKey, work.Opportunity.Type)) ?? throw new NotSupportedException("No permitted submission adapter handles this opportunity.");
        work.Submission.MarkProcessing(now);
        var attempt = new SubmissionAttempt(work.Submission.Id, job.AttemptCount, adapter.Name, now);
        submissions.AddAttempt(attempt);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        SubmissionResult result;
        try
        {
            await rateLimiter.WaitAsync(job.ProjectId, work.Campaign.Id, work.Opportunity.Domain, cancellationToken);
            result = await adapter.SubmitAsync(new SubmissionContext(work.Submission.Id, work.Campaign.Id, job.ProjectId, work.Opportunity.SourceUrl, work.Target.NormalizedUrl, work.Target.PreferredAnchor, work.Campaign.AuthorizationProfileKey, work.Submission.Id.ToString("N")), cancellationToken);
        }
        catch
        {
            var failed = timeProvider.GetUtcNow();
            attempt.Complete(SubmissionStatus.Failed, null, null, "The authorized endpoint request failed.", failed);
            work.Submission.MarkFailed(false, failed);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        var finished = timeProvider.GetUtcNow();
        var status = result.Accepted ? (result.PendingModeration ? SubmissionStatus.PendingModeration : SubmissionStatus.Submitted) : SubmissionStatus.Rejected;
        attempt.Complete(status, result.HttpStatus, result.ExternalReference, result.Error, finished);
        if (result.Accepted)
        {
            work.Submission.MarkSubmitted(result.PendingModeration, finished);
            if (await backlinks.FindBySubmissionAsync(work.Submission.Id, cancellationToken) is null)
            {
                var source = urlNormalizer.Normalize(work.Opportunity.SourceUrl);
                if (!source.IsValid) throw new ValidationException("Submitted opportunity source URL is invalid.");
                if (await backlinks.FindBySourceTargetAsync(job.ProjectId, source.NormalizedUrl!, work.Target.NormalizedUrl, cancellationToken) is null)
                {
                    backlinks.Add(new Backlink(job.ProjectId, work.Campaign.Id, work.Submission.Id, work.Opportunity.SourceUrl, source.NormalizedUrl!, work.Target.Url, work.Target.NormalizedUrl, work.Opportunity.Domain, finished,
                        submissionAttemptId: attempt.Id));
                }
            }
        }
        else work.Submission.MarkRejected(finished);
        audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "submission.complete", job.ProjectId, work.Campaign.Id, job.Id, job.CorrelationId, $"submissionId={work.Submission.Id};status={status};httpStatus={result.HttpStatus}", result.Accepted ? "succeeded" : "rejected", null, finished));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (!result.Accepted) throw new PolicyRejectedException(result.Error ?? "The authorized endpoint rejected the submission.");
    }

    private async Task ExecuteOwnedNetworkAsync(PersistentJob job, string workerId, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<OwnedNetworkSubmissionJobPayload>(job.Payload);
        if (payload?.Version != 2 || job.CampaignId != payload.CampaignId || job.Domain is null)
            throw new ValidationException("Owned-network submission payload is invalid or unsupported.");
        var work = await ownedCampaigns.GetWorkItemAsync(job.Id, cancellationToken)
            ?? throw new ValidationException("Owned-network submission work item is missing.");
        var now = timeProvider.GetUtcNow();
        var simpleWorkflow = Guid.TryParse(job.CorrelationId, out var workflowId) &&
            await workflows.GetAsync(workflowId, tracked: false, cancellationToken) is not null;
        if (work.Campaign.Status == CampaignStatus.Stopped)
        {
            work.Submission.MarkCancelled(now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new PolicyRejectedException("The campaign has been stopped.");
        }
        if (work.Campaign.Status != CampaignStatus.Running)
            throw new RateLimitExceededException("The campaign is not currently running.");
        var ownership = await ownedNetworkAuthorizer.AuthorizeSourceAsync(job.ProjectId, work.Source,
            cancellationToken);
        if (!ownership.Allowed || ownership.Profile is null)
        {
            if (await StopSimpleWorkflowAsync(simpleWorkflow, job, work, workerId,
                    BacklinkWorkflowSourceStatus.NotAuthorized,
                    "Owned-network authorization no longer permits this source.", now, cancellationToken)) return;
            throw new PolicyRejectedException("Owned-network authorization no longer permits this source.");
        }
        if (!OwnedNetworkExecutionEligibility.IsTechnicallyExecutable(work.Source))
        {
            if (await StopSimpleWorkflowAsync(simpleWorkflow, job, work, workerId,
                    BacklinkWorkflowSourceStatus.Unsupported,
                    "No supported public WordPress comment workflow is available.", now, cancellationToken)) return;
            throw new PolicyRejectedException("The source is not technically compatible with automatic WordPress submission.");
        }

        // [Removed]: Blocklist check & rate limit action counts have been stripped out here as requested.

        var history = await submissions.ListAttemptsAsync(work.Submission.Id, 100, cancellationToken);
        if (history.Any(x => x.FinishedAt is null))
        {
            work.Submission.ApplyOutcome(SubmissionStatus.ReconciliationRequired, now);
            var uncertainAttemptId = history.First(x => x.FinishedAt is null).Id;
            var uncertainBacklink = await EnsureBacklinkAsync(work, payload, now, uncertainAttemptId, cancellationToken);
            await QueueVerificationAsync(uncertainBacklink, payload.VerificationDelaySeconds, job, now, "reconcile", cancellationToken);
            audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "submission.reconciliation_required", job.ProjectId,
                work.Campaign.Id, job.Id, job.CorrelationId, $"submissionId={work.Submission.Id};sourceId={work.Source.Id}",
                "reconciliationRequired", null, now));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return;
        }

        var resolved = await contentResolver.ResolveAsync(new(work.Source.Id, work.Campaign.Id, payload.IdentityPoolId,
            payload.TemplatePoolId, payload.TargetUrl, job.AttemptCount), cancellationToken);
        work.Submission.MarkProcessing(now);
        var attempt = new SubmissionAttempt(work.Submission.Id, job.Id, job.ProjectId, work.Campaign.Id, work.Source.Id,
            resolved.ResolvedTargetUrl, job.AttemptCount, work.Source.AdapterName ?? ownedWordPress.Name, "PendingSelection", resolved.IdentityId,
            resolved.TemplateId, resolved.DisplayName, resolved.Email, resolved.Website, now);
        submissions.AddAttempt(attempt);
        await SetWorkflowStatusAsync(job, work.Source.Id, BacklinkWorkflowSourceStatus.Submitting, null,
            now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        OwnedWordPressSubmissionResult result;
        try
        {
            await rateLimiter.WaitAsync(job.ProjectId, work.Campaign.Id, work.Source.Domain, cancellationToken,
                work.Configuration.PerDomainDelayMilliseconds);
            result = await ownedWordPress.SubmitAsync(new(work.Source, ownership.Allowed, work.Campaign.Id, work.Submission.Id,
                resolved.ResolvedTargetUrl, resolved.DisplayName, resolved.Email, resolved.Website,
                resolved.ResolvedComment, resolved.PlacementMethod, work.Submission.Id.ToString("N")), cancellationToken);
        }
        catch
        {
            var failedAt = timeProvider.GetUtcNow();
            attempt.Complete(new(SubmissionStatus.ReconciliationRequired, ModerationStatus.Unknown, "Unknown",
                null, null, SubmissionFailureKind.Uncertain, "The WordPress request outcome is uncertain."), failedAt);
            work.Submission.ApplyOutcome(SubmissionStatus.ReconciliationRequired, failedAt);
            work.Source.RecordSubmissionOutcome(SubmissionStatus.ReconciliationRequired, failedAt);
            var uncertainBacklink = await EnsureBacklinkAsync(work, payload with { TargetUrl = resolved.ResolvedTargetUrl },
                failedAt, attempt.Id, CancellationToken.None);
            await QueueVerificationAsync(uncertainBacklink, 0, job, failedAt, "uncertain", CancellationToken.None);
            await SetWorkflowStatusAsync(job, work.Source.Id, BacklinkWorkflowSourceStatus.Failed,
                "UncertainOutcome: an independent check was queued before any retry.", failedAt,
                CancellationToken.None);
            audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "submission.wordpress_uncertain",
                job.ProjectId, work.Campaign.Id, job.Id, job.CorrelationId,
                $"submissionId={work.Submission.Id};sourceId={work.Source.Id};verificationQueued=true",
                "reconciliationRequired", null, failedAt));
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            return;
        }

        var finished = timeProvider.GetUtcNow();
        attempt.Complete(new(result.Status, result.ModerationStatus, result.Strategy.ToString(), result.HttpStatus,
            result.ExternalReference, result.FailureKind, result.SafeError, result.Endpoint, result.RedirectDestination), finished);
        RecordSubmissionMetrics(result.Status, finished - attempt.StartedAt, result.Strategy);
        work.Submission.ApplyOutcome(result.Status, finished);
        work.Source.RecordSubmissionOutcome(result.Status, finished);
        var workflowStatus = ToWorkflowStatus(result);
        await SetWorkflowStatusAsync(job, work.Source.Id, workflowStatus, result.SafeError, finished,
            cancellationToken);
        var identity = await submissionContent.GetIdentityAsync(resolved.IdentityId, true, cancellationToken);
        var template = await submissionContent.GetTemplateAsync(resolved.TemplateId, true, cancellationToken);
        identity?.RecordUsage(finished);
        template?.RecordUsage(finished);
        if (result.MayHaveCreatedBacklink)
        {
            var backlink = await EnsureBacklinkAsync(work, payload with { TargetUrl = resolved.ResolvedTargetUrl }, finished,
                attempt.Id, cancellationToken);
            BacklinkStudioTelemetry.BacklinksPendingVerification.Add(1);
            await QueueVerificationAsync(backlink, payload.VerificationDelaySeconds, job, finished, "submission", cancellationToken);
        }
        audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "submission.wordpress_complete", job.ProjectId,
            work.Campaign.Id, job.Id, job.CorrelationId,
            $"submissionId={work.Submission.Id};sourceId={work.Source.Id};status={result.Status};strategy={result.Strategy};httpStatus={result.HttpStatus}",
            result.MayHaveCreatedBacklink ? "succeeded" : "rejected", null, finished));
        var retryScheduled = result.FailureKind is SubmissionFailureKind.RateLimited or SubmissionFailureKind.Temporary &&
            job.AttemptCount < job.MaxAttempts;
        if (!result.MayHaveCreatedBacklink && !retryScheduled)
            await SettleWorkflowAsync(job, finished, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (result.FailureKind ==SubmissionFailureKind.RateLimited)
            throw new RateLimitExceededException(result.SafeError ?? "WordPress rate limited the request.",
                result.RetryAfter);
        if (result.FailureKind == SubmissionFailureKind.Temporary)
            throw new HttpRequestException(result.SafeError ?? "WordPress is temporarily unavailable.");
        if (!result.MayHaveCreatedBacklink && result.Status is not SubmissionStatus.Duplicate)
            throw new PolicyRejectedException(result.SafeError ?? "WordPress rejected the submission.");
    }

    private async Task<Backlink> EnsureBacklinkAsync(OwnedNetworkSubmissionWorkItem work,
        OwnedNetworkSubmissionJobPayload payload, DateTimeOffset now, Guid? submissionAttemptId,
        CancellationToken cancellationToken)
    {
        var existing = await backlinks.FindBySubmissionAsync(work.Submission.Id, cancellationToken);
        if (existing is not null) return existing;
        var source = urlNormalizer.Normalize(work.Source.NormalizedUrl);
        var target = urlNormalizer.Normalize(payload.TargetUrl);
        if (!source.IsValid || !target.IsValid) throw new ValidationException("Submission backlink URLs are invalid.");
        var equivalent = await backlinks.FindBySourceTargetAsync(work.Campaign.ProjectId, source.NormalizedUrl!, target.NormalizedUrl!, cancellationToken);
        if (equivalent is not null) return equivalent;
        var backlink = new Backlink(work.Campaign.ProjectId, work.Campaign.Id, work.Submission.Id,
            work.Source.OriginalUrl, source.NormalizedUrl!, payload.TargetUrl, target.NormalizedUrl!, work.Source.Domain, now,
            work.Source.Id, submissionAttemptId);
        backlinks.Add(backlink);
        return backlink;
    }

    private async Task QueueVerificationAsync(Backlink backlink, int delaySeconds, PersistentJob parent,
        DateTimeOffset now, string reason, CancellationToken cancellationToken)
    {
        var key = $"backlink:{backlink.Id}:{reason}";
        if (await jobs.FindByIdempotencyAsync(backlink.ProjectId, JobType.Verification, key, cancellationToken) is not null) return;
        var verification = new PersistentJob(JobType.Verification, backlink.ProjectId, backlink.CampaignId,
            JsonSerializer.Serialize(new { Version = 1, BacklinkId = backlink.Id }), 0, now.AddSeconds(delaySeconds), 3,
            parent.CorrelationId, key, now, backlink.Domain);
        await jobs.EnqueueAsync(verification, cancellationToken);
    }

    private Task SetWorkflowStatusAsync(PersistentJob job, Guid submissionSourceId,
        BacklinkWorkflowSourceStatus status, string? reason, DateTimeOffset now,
        CancellationToken cancellationToken) => Guid.TryParse(job.CorrelationId, out var workflowId)
        ? workflows.SetSourceStatusAsync(workflowId, submissionSourceId, status, reason, now, cancellationToken)
        : Task.CompletedTask;

    private Task SettleWorkflowAsync(PersistentJob job, DateTimeOffset now,
        CancellationToken cancellationToken) => Guid.TryParse(job.CorrelationId, out var workflowId)
        ? workflows.SettleAsync(workflowId, now, cancellationToken)
        : Task.CompletedTask;

    private static void RecordSubmissionMetrics(SubmissionStatus status, TimeSpan duration, WordPressSubmissionMode strategy)
    {
        var strategyTag = new KeyValuePair<string, object?>("submission.strategy", strategy.ToString());
        BacklinkStudioTelemetry.SubmissionDuration.Record(duration.TotalSeconds, strategyTag);
        switch (status)
        {
            case SubmissionStatus.Submitted: BacklinkStudioTelemetry.BacklinkSubmissionsSubmitted.Add(1, strategyTag); break;
            case SubmissionStatus.PendingModeration: BacklinkStudioTelemetry.BacklinkSubmissionsPendingModeration.Add(1, strategyTag); break;
            case SubmissionStatus.Approved: BacklinkStudioTelemetry.BacklinkSubmissionsApproved.Add(1, strategyTag); break;
            case SubmissionStatus.Duplicate: BacklinkStudioTelemetry.BacklinkSubmissionsDuplicate.Add(1, strategyTag); break;
            case SubmissionStatus.Rejected: BacklinkStudioTelemetry.BacklinkSubmissionsRejected.Add(1, strategyTag); break;
            case SubmissionStatus.Failed: BacklinkStudioTelemetry.BacklinkSubmissionsFailed.Add(1, strategyTag); break;
        }
    }

    private static BacklinkWorkflowSourceStatus ToWorkflowStatus(OwnedWordPressSubmissionResult result) =>
        result.Status switch
        {
            SubmissionStatus.PendingModeration => BacklinkWorkflowSourceStatus.PendingModeration,
            SubmissionStatus.Submitted or SubmissionStatus.Approved => BacklinkWorkflowSourceStatus.Submitted,
            _ => result.FailureKind switch
            {
                SubmissionFailureKind.CommentsClosed => BacklinkWorkflowSourceStatus.CommentsClosed,
                SubmissionFailureKind.LoginRequired => BacklinkWorkflowSourceStatus.AuthenticationRequired,
                SubmissionFailureKind.RateLimited => BacklinkWorkflowSourceStatus.RateLimited,
                SubmissionFailureKind.AuthorizationDenied => BacklinkWorkflowSourceStatus.NotAuthorized,
                SubmissionFailureKind.PolicyRejected => BacklinkWorkflowSourceStatus.Unsupported,
                SubmissionFailureKind.UnsupportedForm or SubmissionFailureKind.EndpointNotFound or SubmissionFailureKind.BrowserRequired => BacklinkWorkflowSourceStatus.Unsupported,
                SubmissionFailureKind.Permanent or SubmissionFailureKind.InvalidPost or SubmissionFailureKind.Duplicate => BacklinkWorkflowSourceStatus.Rejected,
                _ => BacklinkWorkflowSourceStatus.Failed
            }
        };

    private async Task<bool> StopSimpleWorkflowAsync(bool simpleWorkflow, PersistentJob job,
        OwnedNetworkSubmissionWorkItem work, string workerId, BacklinkWorkflowSourceStatus status, string reason,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!simpleWorkflow) return false;
        work.Submission.ApplyOutcome(status == BacklinkWorkflowSourceStatus.NotAuthorized
            ? SubmissionStatus.Rejected : SubmissionStatus.ManualActionRequired, now);
        work.Source.RecordSubmissionOutcome(work.Submission.Status, now);
        await SetWorkflowStatusAsync(job, work.Source.Id, status, reason, now, cancellationToken);
        await SettleWorkflowAsync(job, now, cancellationToken);
        audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "backlink_workflow.source_stopped",
            job.ProjectId, work.Campaign.Id, job.Id, job.CorrelationId,
            $"submissionId={work.Submission.Id};sourceId={work.Source.Id};status={status}",
            status.ToString(), null, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
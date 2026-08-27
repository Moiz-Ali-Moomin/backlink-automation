using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Submission;

public sealed class BacklinkWorkflowJobExecutor(
    IBacklinkWorkflowRepository workflows,
    ISubmissionSourceRepository sources,
    ISubmissionRepository submissions,
    IOwnedNetworkCampaignRepository ownedCampaigns,
    IOwnedNetworkExecutionAuthorizer authorizer,
    ISubmissionContentRepository content,
    IJobQueue jobs,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IJobExecutor
{
    private const int BatchSize = 500;
    public JobType JobType => JobType.BacklinkWorkflow;

    public async Task ExecuteAsync(PersistentJob job, string workerId, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<BacklinkWorkflowJobPayload>(job.Payload);
        if (payload?.Version != 1) throw new ValidationException("Backlink workflow payload is invalid or unsupported.");
        var workflow = await workflows.GetAsync(payload.WorkflowId, true, cancellationToken)
            ?? throw new ValidationException("Backlink workflow is missing.");
        if (workflow.ProjectId != job.ProjectId || workflow.OrchestrationJobId != job.Id &&
            !job.IdempotencyKey.Contains($"backlink-workflow:{workflow.Id}", StringComparison.Ordinal))
            throw new ValidationException("Backlink workflow does not match its durable job.");

        var all = await ReadAllAsync(workflow.Id, true, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var campaignByProfile = new Dictionary<Guid, Guid>();
        foreach (var existingCampaignId in all.Where(value => value.CampaignId is not null)
                     .Select(value => value.CampaignId!.Value).Distinct())
        {
            var configuration = await ownedCampaigns.GetAsync(existingCampaignId, false, cancellationToken);
            if (configuration is not null)
                campaignByProfile[configuration.OwnedNetworkProfileId] = existingCampaignId;
        }

        var authorized = 0;
        var notAuthorized = 0;
        foreach (var item in all.Where(value => value.SubmissionSourceId is null &&
                     value.Status == BacklinkWorkflowSourceStatus.Queued))
        {
            var authorization = await authorizer.AuthorizeSourceAsync(workflow.ProjectId, item, cancellationToken);
            if (!authorization.Allowed || authorization.Profile is null)
            {
                item.SetStatus(BacklinkWorkflowSourceStatus.NotAuthorized,
                    "The source is not authorized by the persisted project/network policy.", now);
                notAuthorized++;
                continue;
            }

            var network = authorization.Profile;
            var source = await sources.FindByNormalizedUrlAsync(workflow.ProjectId, item.NormalizedUrl, true,
                cancellationToken);
            if (source is null)
            {
                var sourceOwnership = authorization.TestOwnershipOverrideApplied
                    ? OwnershipStatus.Unverified
                    : network.OwnershipStatus;
                source = new SubmissionSource(workflow.ProjectId, network.Id, item.OriginalUrl,
                    item.NormalizedUrl, item.Domain, item.Host, sourceOwnership,
                    !authorization.TestOwnershipOverrideApplied && network.AutomationPermitted,
                    null, true, now);
                sources.Add(source);
            }
            else if (!authorization.TestOwnershipOverrideApplied &&
                     (source.OwnedNetworkProfileId != network.Id || !source.AutomationPermitted ||
                      source.OwnershipStatus == OwnershipStatus.Unverified))
            {
                source.AssociateWithOwnedNetwork(network.Id, network.OwnershipStatus, true, now);
            }
            else if (authorization.TestOwnershipOverrideApplied && source.OwnedNetworkProfileId != network.Id)
            {
                item.SetStatus(BacklinkWorkflowSourceStatus.NotAuthorized,
                    "The existing source is associated with a different server-side network profile.", now);
                notAuthorized++;
                continue;
            }

            if (!campaignByProfile.TryGetValue(network.Id, out var workflowCampaignId))
            {
                var campaign = new Campaign(workflow.ProjectId, $"Backlink workflow {workflow.Id:N}",
                    CampaignApprovalMode.Automatic, "owned-network", network.Id.ToString("N"), 100_000, now);
                var campaignGlobal = Math.Min(workflow.GlobalConcurrency, network.MaxConcurrency);
                var campaignPerDomain = Math.Min(
                    Math.Min(workflow.PerDomainConcurrency, network.PerDomainConcurrency), campaignGlobal);
                var configuration = new OwnedNetworkCampaignConfiguration(campaign.Id, workflow.ProjectId,
                    network.Id, workflow.TargetUrl, workflow.IdentityPoolId, workflow.TemplatePoolId,
                    campaignGlobal, campaignPerDomain,
                    Math.Max(workflow.PerDomainDelayMilliseconds, network.PerDomainDelayMilliseconds),
                    workflow.MaximumAttempts, workflow.VerificationDelaySeconds,
                    OwnedNetworkCampaignMode.AutomaticOwnedNetwork, null, null, null,
                    TechnicalCompatibility.Unknown, SubmissionSourceValidationStatus.Valid, null, now);
                submissions.AddCampaign(campaign);
                ownedCampaigns.Add(configuration);
                workflowCampaignId = campaign.Id;
                campaignByProfile.Add(network.Id, workflowCampaignId);
            }

            item.Link(source.Id, workflowCampaignId, now);
            authorized++;
        }
        workflow.AddCampaigns(campaignByProfile.Values, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        all = await ReadAllAsync(workflow.Id, true, cancellationToken);

        var dispatched = 0;
        foreach (var item in all.Where(value => value.SubmissionSourceId is not null &&
                     value.Status == BacklinkWorkflowSourceStatus.Queued))
        {
            var source = await sources.GetAsync(item.SubmissionSourceId!.Value, true, cancellationToken)
                ?? throw new ValidationException("Workflow submission source is missing.");
            var validationKey = $"backlink-workflow:{workflow.Id}:validate:{source.Id}";
            if (await jobs.FindByIdempotencyAsync(job.ProjectId, JobType.SubmissionSourceValidation,
                    validationKey, cancellationToken) is not null)
                continue;
            // A simple workflow is a fresh execution request. Revalidate catalog entries even when an earlier
            // run left a terminal or now-stale result (for example the legacy oversized-static classification).
            // The workflow-scoped idempotency key keeps the durable validation dispatch bounded.
            source.QueueValidation(now);
            item.SetStatus(BacklinkWorkflowSourceStatus.Checking, null, now);
            var child = new PersistentJob(JobType.SubmissionSourceValidation, job.ProjectId, null,
                JsonSerializer.Serialize(new SubmissionSourceValidationJobPayload(1, workflow.Id,
                    source.OwnedNetworkProfileId, source.Id, null, null, 1)), 5, now, 3,
                workflow.Id.ToString("D"), validationKey, now,
                source.Domain);
            await jobs.EnqueueAsync(child, cancellationToken);
            dispatched++;
        }
        if (authorized + notAuthorized > 0)
            audit.Append(new AuditEvent(ActorType.Worker, workerId, null,
                "backlink_workflow.authorization_resolved", workflow.ProjectId, null, job.Id,
                job.CorrelationId,
                $"workflowId={workflow.Id};authorized={authorized};notAuthorized={notAuthorized}",
                "succeeded", null, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var validation = await jobs.GetCorrelationSummaryAsync(job.ProjectId,
            JobType.SubmissionSourceValidation, workflow.Id.ToString("D"), cancellationToken);
        if (dispatched > 0 || validation.Active > 0)
        {
            await ContinueAsync(job, workflow, "validation", cancellationToken);
            return;
        }

        all = await ReadAllAsync(workflow.Id, true, cancellationToken);
        var queued = 0;
        foreach (var group in all.Where(value => value.SubmissionSourceId is not null && value.CampaignId is not null)
                     .GroupBy(value => value.CampaignId!.Value))
        {
            var campaign = await submissions.GetCampaignAsync(group.Key, true, cancellationToken)
                ?? throw new ValidationException("Workflow campaign is missing.");
            var configuration = await ownedCampaigns.GetAsync(group.Key, true, cancellationToken)
                ?? throw new ValidationException("Workflow campaign configuration is missing.");
            var templatePool = await content.GetTemplatePoolAsync(configuration.TemplatePoolId, false, cancellationToken)
                ?? throw new ValidationException("Workflow template pool is missing.");
            var candidates = new List<(BacklinkWorkflowSource Item, SubmissionSource Source)>();
            foreach (var item in group)
            {
                var source = await sources.GetAsync(item.SubmissionSourceId!.Value, false, cancellationToken)
                    ?? throw new ValidationException("Workflow source is missing.");
                var final = ClassifyValidation(source);
                if (final is not null)
                {
                    item.SetStatus(final.Value.Status, final.Value.Reason, now);
                    continue;
                }
                candidates.Add((item, source));
            }
            var existing = await ownedCampaigns.ListExistingSubmissionSourceIdsAsync(campaign.Id,
                candidates.Select(value => value.Source.Id).ToArray(), workflow.TargetUrl,
                templatePool.PlacementMethod, cancellationToken);
            foreach (var (item, source) in candidates)
            {
                if (existing.Contains(source.Id)) continue;
                var submissionPayload = new OwnedNetworkSubmissionJobPayload(2, campaign.Id, source.Id,
                    workflow.IdentityPoolId, workflow.TemplatePoolId, workflow.TargetUrl,
                    templatePool.PlacementMethod, workflow.VerificationDelaySeconds);
                var persistent = new PersistentJob(JobType.Submission, workflow.ProjectId, campaign.Id,
                    JsonSerializer.Serialize(submissionPayload), 0, now, workflow.MaximumAttempts,
                    workflow.Id.ToString("D"), $"backlink-workflow:{workflow.Id}:submit:{source.Id}", now,
                    source.Domain);
                await jobs.EnqueueAsync(persistent, cancellationToken);
                submissions.AddSubmission(new SubmissionJob(workflow.ProjectId, campaign.Id, source.Id,
                    persistent.Id, workflow.TargetUrl, templatePool.PlacementMethod, now));
                item.SetStatus(BacklinkWorkflowSourceStatus.Queued, null, now);
                queued++;
            }
            if (campaign.Status == CampaignStatus.Draft && candidates.Count > 0) campaign.Start(now);
            configuration.CompleteExpansion(now);
        }

        if (queued > 0) workflow.MarkRunning(now);
        else workflow.Complete(true, now);
        audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "backlink_workflow.orchestrated",
            workflow.ProjectId, workflow.CampaignIds.FirstOrDefault() is { } campaignId && campaignId != Guid.Empty ? campaignId : null,
            job.Id, job.CorrelationId, $"workflowId={workflow.Id};submissionJobs={queued};validationFailures={validation.Failed}",
            queued > 0 ? "running" : "settled", null, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ContinueAsync(PersistentJob current, BacklinkWorkflow workflow, string stage,
        CancellationToken cancellationToken)
    {
        var key = $"backlink-workflow:{workflow.Id}:continue:{stage}:{current.Id}";
        if (await jobs.FindByIdempotencyAsync(workflow.ProjectId, JobType.BacklinkWorkflow, key,
                cancellationToken) is not null) return;
        var now = timeProvider.GetUtcNow();
        var continuation = new PersistentJob(JobType.BacklinkWorkflow, workflow.ProjectId, null, current.Payload,
            current.Priority, now.AddSeconds(2), 5, workflow.Id.ToString("D"), key, now);
        await jobs.EnqueueAsync(continuation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<BacklinkWorkflowSource>> ReadAllAsync(Guid workflowId, bool tracked,
        CancellationToken cancellationToken)
    {
        var result = new List<BacklinkWorkflowSource>();
        PageCursor? cursor = null;
        while (true)
        {
            var page = await workflows.ListSourcesAsync(workflowId, tracked, cursor, BatchSize, cancellationToken);
            result.AddRange(page);
            if (page.Count < BatchSize) break;
            cursor = new(page[^1].CreatedAt, page[^1].Id);
        }
        return result;
    }

    private static (BacklinkWorkflowSourceStatus Status, string Reason)? ClassifyValidation(SubmissionSource source)
    {
        if (source.LastHttpStatus == 403)
            return (BacklinkWorkflowSourceStatus.Rejected, source.ValidationReason ?? "The source permanently rejected validation.");
        if (source.ValidationStatus == SubmissionSourceValidationStatus.Error)
            return (BacklinkWorkflowSourceStatus.Failed, source.ValidationReason ?? "Source validation failed.");
        if (source.RequiresAuthentication)
            return (BacklinkWorkflowSourceStatus.AuthenticationRequired, source.ValidationReason ?? "Authentication is required.");
        if (source.ValidationReason?.Contains("comments are closed", StringComparison.OrdinalIgnoreCase) == true)
            return (BacklinkWorkflowSourceStatus.CommentsClosed, source.ValidationReason);
        if (source.RequiresManualAction && source.ValidationReason?.Contains("captcha", StringComparison.OrdinalIgnoreCase) == true)
            return (BacklinkWorkflowSourceStatus.Unsupported, source.ValidationReason);
        if (!OwnedNetworkExecutionEligibility.IsTechnicallyExecutable(source))
            return (BacklinkWorkflowSourceStatus.Unsupported, source.ValidationReason ?? "No supported public comment flow was found.");
        return null;
    }
}

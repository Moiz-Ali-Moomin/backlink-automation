using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Submission;

public sealed class OwnedNetworkCampaignExpansionJobExecutor(
    ISubmissionRepository submissions,
    IOwnedNetworkCampaignRepository ownedCampaigns,
    ISubmissionContentRepository content,
    ISubmissionSourceRepository sources,
    IOwnedNetworkExecutionAuthorizer ownedNetworkAuthorizer,
    IJobQueue jobs,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IJobExecutor
{
    private const int BatchSize = 500;
    public JobType JobType => JobType.OwnedNetworkCampaignExpansion;

    public async Task ExecuteAsync(PersistentJob job, string workerId, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<OwnedNetworkCampaignExpansionPayload>(job.Payload);
        if (payload?.Version != 1 || job.CampaignId != payload.CampaignId)
            throw new ValidationException("Owned-network campaign expansion payload is invalid or unsupported.");
        var campaign = await submissions.GetCampaignAsync(payload.CampaignId, true, cancellationToken)
            ?? throw new ResourceNotFoundException("Campaign", payload.CampaignId);
        var configuration = await ownedCampaigns.GetAsync(campaign.Id, true, cancellationToken)
            ?? throw new ValidationException("Owned-network campaign configuration is missing.");
        if (configuration.ExpansionCompleted) return;
        if (campaign.Status == CampaignStatus.Stopped) throw new PolicyRejectedException("The campaign has been stopped.");
        if (campaign.Status != CampaignStatus.Running) throw new RateLimitExceededException("The campaign is not currently running.");
        var profileAuthorization = await ownedNetworkAuthorizer.AuthorizeProfileAsync(campaign.ProjectId,
            configuration.OwnedNetworkProfileId, cancellationToken);
        if (!profileAuthorization.Allowed)
            throw new PolicyRejectedException("The owned network is no longer approved for automatic execution.");
        var templatePool = await content.GetTemplatePoolAsync(configuration.TemplatePoolId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionTemplatePool", configuration.TemplatePoolId);
        var cursor = configuration.LastSourceCreatedAt is { } createdAt && configuration.LastSourceId is { } sourceId
            ? new PageCursor(createdAt, sourceId) : (PageCursor?)null;
        var totalQueued = 0L;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filter = new SubmissionSourceFilter(configuration.OwnedNetworkProfileId, configuration.Domain,
                configuration.Platform, configuration.CmsType, OwnershipStatus: null,
                AutomationPermitted: null,
                TechnicalCompatibility: configuration.TechnicalCompatibility, ValidationStatus: configuration.ValidationStatus,
                Enabled: true, Tag: configuration.Tag, PreviousSubmissionStatus: configuration.PreviousSubmissionStatus,
                PreviousVerificationStatus: configuration.PreviousVerificationStatus);
            var page = await sources.ListAsync(campaign.ProjectId, filter, cursor, BatchSize, cancellationToken);
            if (page.Count == 0)
            {
                configuration.CompleteExpansion(timeProvider.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken);
                break;
            }
            var existingSourceIds = await ownedCampaigns.ListExistingSubmissionSourceIdsAsync(campaign.Id,
                page.Select(x => x.Id).ToArray(), configuration.TargetUrl, templatePool.PlacementMethod,
                cancellationToken);
            var queued = 0;
            foreach (var source in page)
            {
                var ownership = await ownedNetworkAuthorizer.AuthorizeSourceAsync(campaign.ProjectId, source,
                    cancellationToken);
                if (!ownership.Allowed || !OwnedNetworkExecutionEligibility.IsTechnicallyExecutable(source)) continue;
                if (existingSourceIds.Contains(source.Id)) continue;
                var now = timeProvider.GetUtcNow();
                var payloadValue = new OwnedNetworkSubmissionJobPayload(2, campaign.Id, source.Id,
                    configuration.IdentityPoolId, configuration.TemplatePoolId, configuration.TargetUrl,
                    templatePool.PlacementMethod, configuration.VerificationDelaySeconds);
                var persistent = new PersistentJob(JobType.Submission, campaign.ProjectId, campaign.Id,
                    JsonSerializer.Serialize(payloadValue), 0, now, configuration.MaximumAttempts, job.CorrelationId,
                    $"campaign:{campaign.Id}:source:{source.Id}", now, source.Domain);
                await jobs.EnqueueAsync(persistent, cancellationToken);
                submissions.AddSubmission(new SubmissionJob(campaign.ProjectId, campaign.Id, source.Id, persistent.Id,
                    configuration.TargetUrl, templatePool.PlacementMethod, now));
                BacklinkStudioTelemetry.BacklinkSubmissionJobsQueued.Add(1);
                queued++;
            }
            var last = page[^1];
            var batchNow = timeProvider.GetUtcNow();
            configuration.Advance(last.CreatedAt, last.Id, queued, batchNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            totalQueued += queued;
            cursor = new(last.CreatedAt, last.Id);
            if (page.Count < BatchSize)
            {
                configuration.CompleteExpansion(timeProvider.GetUtcNow());
                await unitOfWork.SaveChangesAsync(cancellationToken);
                break;
            }
        }
        var finished = timeProvider.GetUtcNow();
        audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "campaign.expand_owned_network", campaign.ProjectId,
            campaign.Id, job.Id, job.CorrelationId, $"campaignId={campaign.Id};queuedThisRun={totalQueued};queuedTotal={configuration.SourcesQueued}",
            "succeeded", null, finished));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

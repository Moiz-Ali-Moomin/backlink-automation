using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Submission;

public sealed class OwnedNetworkWorkflowJobExecutor(
    ISubmissionSourceService sourceService,
    ICampaignService campaigns,
    IJobQueue jobs,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IJobExecutor
{
    public JobType JobType => JobType.OwnedNetworkWorkflow;

    public async Task ExecuteAsync(PersistentJob job, string workerId, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<OwnedNetworkWorkflowPayload>(job.Payload);
        if (payload?.Version != 1 || payload.Campaign.ProjectId != job.ProjectId ||
            payload.Campaign.OwnedNetworkProfileId == Guid.Empty)
            throw new ValidationException("Owned-network workflow payload is invalid or unsupported.");
        var sourceImport = await sourceService.GetImportAsync(payload.ImportId, cancellationToken)
            ?? throw new ValidationException("Owned-network workflow import is missing.");
        if (sourceImport.ProjectId != job.ProjectId)
            throw new ValidationException("Owned-network workflow import belongs to another project.");
        if (sourceImport.Status == SubmissionSourceImportStatus.Failed)
            throw new PolicyRejectedException(sourceImport.SafeError ?? "Owned-network source import failed.");
        if (sourceImport.Status != SubmissionSourceImportStatus.Completed)
        {
            await ContinueAsync(job, payload, "import", cancellationToken);
            return;
        }

        var workflowRequestId = $"owned-network-workflow:{payload.WorkflowId:D}";
        var actor = ActorContext.System(workflowRequestId);
        _ = await sourceService.ValidateAsync(new(job.ProjectId, payload.Campaign.OwnedNetworkProfileId,
            payload.MaximumSources, $"workflow:{payload.WorkflowId:D}:validate"), actor, cancellationToken);
        var validation = await jobs.GetCorrelationSummaryAsync(job.ProjectId, JobType.SubmissionSourceValidation,
            workflowRequestId, cancellationToken);
        if (validation.Failed > 0)
            throw new PolicyRejectedException("One or more owned-network validation jobs failed permanently.");
        if (validation.Active > 0)
        {
            await ContinueAsync(job, payload, "validation", cancellationToken);
            return;
        }

        var campaign = await campaigns.CreateOwnedNetworkAsync(payload.Campaign with
        {
            IdempotencyKey = $"workflow:{payload.WorkflowId:D}:campaign"
        }, actor, cancellationToken);
        var started = await campaigns.StartAsync(new(campaign.Id, $"workflow:{payload.WorkflowId:D}:start"),
            actor, cancellationToken);
        var now = timeProvider.GetUtcNow();
        audit.Append(new AuditEvent(ActorType.Worker, workerId, null, "owned_network_workflow.completed",
            job.ProjectId, campaign.Id, job.Id, job.CorrelationId,
            $"workflowId={payload.WorkflowId};importId={payload.ImportId};campaignId={campaign.Id};expansionJobs={started.JobsQueued}",
            "succeeded", null, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task ContinueAsync(PersistentJob current, OwnedNetworkWorkflowPayload payload, string stage,
        CancellationToken cancellationToken)
    {
        var key = $"owned-network-workflow:{payload.WorkflowId:D}:{stage}:{current.Id:D}";
        if (await jobs.FindByIdempotencyAsync(current.ProjectId, JobType.OwnedNetworkWorkflow, key, cancellationToken) is not null)
            return;
        var now = timeProvider.GetUtcNow();
        var continuation = new PersistentJob(JobType.OwnedNetworkWorkflow, current.ProjectId, null,
            current.Payload, current.Priority, now.AddSeconds(5), 3, current.CorrelationId, key, now);
        await jobs.EnqueueAsync(continuation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

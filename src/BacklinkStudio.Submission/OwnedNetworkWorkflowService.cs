using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Submission;

public sealed class OwnedNetworkWorkflowService(
    ISubmissionSourceService sourceService,
    IJobQueue jobs,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IOwnedNetworkWorkflowService
{
    public async Task<OwnedNetworkWorkflowAcceptedDto> StartAsync(StartOwnedNetworkWorkflowCommand command,
        Stream content, ActorContext actor, CancellationToken cancellationToken)
    {
        if (command.MaximumSources is < 1 or > 1_000_000)
            throw new ValidationException("Maximum workflow sources must be between 1 and 1000000.");
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var import = await sourceService.ImportAsync(new(command.ProjectId, command.OwnedNetworkProfileId,
            command.Format, command.FileName, command.Tag, key), content, actor, cancellationToken);
        var scope = Idempotency.Scope(actor, "owned_network_workflow_start");
        var hash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null) return Idempotency.ReadExisting<OwnedNetworkWorkflowAcceptedDto>(existing, hash);

        var now = timeProvider.GetUtcNow();
        var workflowId = Guid.CreateVersion7(now);
        var campaign = new CreateOwnedNetworkCampaignCommand(command.ProjectId, command.CampaignName,
            command.OwnedNetworkProfileId, command.TargetUrl, command.IdentityPoolId, command.TemplatePoolId,
            command.GlobalConcurrency, command.PerDomainConcurrency, command.PerDomainDelayMilliseconds,
            command.MaximumAttempts, command.VerificationDelaySeconds, OwnedNetworkCampaignMode.AutomaticOwnedNetwork,
            command.Domain, command.Platform, command.CmsType, TechnicalCompatibility.Compatible,
            SubmissionSourceValidationStatus.Valid, command.SourceTag, command.DailyActionLimit, string.Empty);
        var payload = new OwnedNetworkWorkflowPayload(1, workflowId, import.ImportId, command.MaximumSources, campaign);
        var job = new PersistentJob(JobType.OwnedNetworkWorkflow, command.ProjectId, null,
            JsonSerializer.Serialize(payload), 1, now, 3, workflowId.ToString("D"),
            $"owned-network-workflow:{workflowId}:start", now);
        await jobs.EnqueueAsync(job, cancellationToken);
        var result = new OwnedNetworkWorkflowAcceptedDto(job.Id, import.ImportId, import.JobId, "queued");
        idempotency.Add(new(scope, key, hash, "owned_network_workflow", workflowId, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "owned_network_workflow.queued",
            command.ProjectId, null, job.Id, actor.RequestId,
            $"workflowId={workflowId};importId={import.ImportId};networkId={command.OwnedNetworkProfileId}",
            "queued", actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }
}

public sealed record OwnedNetworkWorkflowPayload(int Version, Guid WorkflowId, Guid ImportId, int MaximumSources,
    CreateOwnedNetworkCampaignCommand Campaign);

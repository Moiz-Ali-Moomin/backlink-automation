using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public sealed record StartOwnedNetworkWorkflowCommand(Guid ProjectId, Guid OwnedNetworkProfileId,
    SubmissionSourceImportFormat Format, string FileName, string? Tag, string CampaignName, string TargetUrl,
    Guid? IdentityPoolId, Guid? TemplatePoolId, int GlobalConcurrency, int PerDomainConcurrency,
    int PerDomainDelayMilliseconds, int MaximumAttempts, int VerificationDelaySeconds, int MaximumSources,
    string? Domain, SourcePlatform? Platform, CmsType? CmsType, string? SourceTag, int DailyActionLimit,
    string IdempotencyKey);

public sealed record OwnedNetworkWorkflowAcceptedDto(Guid WorkflowJobId, Guid ImportId, Guid ImportJobId, string Status);

public interface IOwnedNetworkWorkflowService
{
    Task<OwnedNetworkWorkflowAcceptedDto> StartAsync(StartOwnedNetworkWorkflowCommand command, Stream content,
        ActorContext actor, CancellationToken cancellationToken);
}

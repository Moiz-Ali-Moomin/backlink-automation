using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public sealed record BacklinkWorkflowIdentityInput(string Name, string Email);

public sealed record StartBacklinkWorkflowCommand(
    Guid ProjectId,
    IReadOnlyList<string>? SourceUrls,
    Guid? SourceImportId,
    IReadOnlyList<BacklinkWorkflowIdentityInput>? Identities,
    Guid? IdentityPoolId,
    IReadOnlyList<string>? Comments,
    Guid? TemplatePoolId,
    string TargetUrl,
    int? GlobalConcurrency,
    int? PerDomainConcurrency,
    int? PerDomainDelayMilliseconds,
    int? MaximumAttempts,
    int? VerificationDelaySeconds,
    string ClientRequestKey);

public sealed record BacklinkWorkflowAcceptedDto(
    Guid WorkflowId,
    Guid? CampaignId,
    Guid JobId,
    string Status);

public sealed record BacklinkWorkflowSourceResultDto(
    Guid Id,
    string SourceUrl,
    BacklinkWorkflowSourceStatus Status,
    string? Reason,
    DateTimeOffset UpdatedAt);

public sealed record BacklinkWorkflowStatusDto(
    Guid WorkflowId,
    BacklinkWorkflowStatus Status,
    int Total,
    int Queued,
    int Checking,
    int Submitting,
    int Submitted,
    int PendingModeration,
    int Verified,
    int Failed,
    int Rejected,
    int CommentsClosed,
    int AuthenticationRequired,
    int RateLimited,
    int NotAuthorized,
    int Unsupported,
    PageResult<BacklinkWorkflowSourceResultDto> Sources);

public interface IBacklinkWorkflowService
{
    Task<BacklinkWorkflowAcceptedDto> StartAsync(StartBacklinkWorkflowCommand command, ActorContext actor,
        CancellationToken cancellationToken);
    Task<BacklinkWorkflowStatusDto?> GetAsync(Guid workflowId, PageRequest page,
        CancellationToken cancellationToken);
}

public sealed record BacklinkWorkflowSourceCounts(
    int Total,
    int Queued,
    int Checking,
    int Submitting,
    int Submitted,
    int PendingModeration,
    int Verified,
    int Failed,
    int Rejected,
    int CommentsClosed,
    int AuthenticationRequired,
    int RateLimited,
    int NotAuthorized,
    int Unsupported);

public interface IBacklinkWorkflowRepository
{
    void Add(BacklinkWorkflow workflow);
    void AddSource(BacklinkWorkflowSource source);
    Task<BacklinkWorkflow?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    Task<IReadOnlyList<BacklinkWorkflowSource>> ListSourcesAsync(Guid workflowId, bool tracked,
        PageCursor? cursor, int take, CancellationToken cancellationToken);
    Task<BacklinkWorkflowSourceCounts> CountSourcesAsync(Guid workflowId, CancellationToken cancellationToken);
    Task SetSourceStatusAsync(Guid workflowId, Guid submissionSourceId, BacklinkWorkflowSourceStatus status, string? reason,
        DateTimeOffset now, CancellationToken cancellationToken);
    Task SettleAsync(Guid workflowId, DateTimeOffset now, CancellationToken cancellationToken);
    Task ApplyVerificationAsync(Guid workflowId, Guid submissionSourceId, bool verified, string? reason,
        DateTimeOffset now, CancellationToken cancellationToken) => SetSourceStatusAsync(workflowId, submissionSourceId,
            verified ? BacklinkWorkflowSourceStatus.Verified : BacklinkWorkflowSourceStatus.Failed, reason, now,
            cancellationToken);
}

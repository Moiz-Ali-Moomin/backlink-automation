using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public sealed record ProjectDto(Guid Id, string Name, string PrimaryDomain, string? Description, ProjectStatus Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record TargetDto(Guid Id, Guid ProjectId, string Url, string NormalizedUrl, string? Label, IReadOnlyList<string> Keywords, string? PreferredAnchor, string? Category, int Priority, bool Enabled, DateTimeOffset CreatedAt);
public sealed record CandidateDto(Guid Id, Guid ProjectId, Guid CandidateSiteId, string Url, string NormalizedUrl, string Domain, CandidateAnalysisStatus AnalysisStatus, string? FinalUrl, int? HttpStatus, string? ContentType, string? Title, string? CanonicalUrl, string? Cms, string? RobotsDirectives, bool ExistingTargetLink, IReadOnlyList<string> EligibleSignals, bool RequiresJavaScript, string? AnalysisError, DateTimeOffset? LastAnalyzedAt, DateTimeOffset CreatedAt);
public sealed record ScoreReasonDto(ScoreKind Kind, string Code, int Points, string Explanation);
public sealed record OpportunityDto(Guid Id, Guid ProjectId, Guid CandidatePageId, OpportunityType Type, string SourceUrl, string Domain, int QualityScore, int RiskScore, AutomationStatus AutomationStatus, string AnalysisReason, IReadOnlyList<ScoreReasonDto> ScoreReasons, DateTimeOffset DetectedAt, DateTimeOffset LastAnalyzedAt);
public sealed record CampaignDto(Guid Id, Guid ProjectId, string Name, CampaignApprovalMode ApprovalMode, string AuthorizationProfileKey, string AuthorizationReference, int DailyActionLimit, CampaignStatus Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, OwnedNetworkCampaignConfigurationDto? OwnedNetwork = null);
public sealed record OwnedNetworkCampaignConfigurationDto(Guid CampaignId, Guid OwnedNetworkProfileId, string TargetUrl,
    Guid IdentityPoolId, Guid TemplatePoolId, int GlobalConcurrency, int PerDomainConcurrency, int PerDomainDelayMilliseconds,
    int MaximumAttempts, int VerificationDelaySeconds, OwnedNetworkCampaignMode Mode, string? Domain, SourcePlatform? Platform,
    CmsType? CmsType, TechnicalCompatibility TechnicalCompatibility, SubmissionSourceValidationStatus? ValidationStatus,
    string? Tag, long SourcesQueued, bool ExpansionCompleted, SubmissionStatus? PreviousSubmissionStatus = null,
    BacklinkStatus? PreviousVerificationStatus = null);
public sealed record SubmissionJobDto(Guid Id, Guid ProjectId, Guid CampaignId, Guid? CampaignOpportunityId, Guid PersistentJobId, SubmissionStatus Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Guid? SubmissionSourceId = null, string? TargetUrl = null, BacklinkPlacementMethod? PlacementType = null);
public sealed record SubmissionAttemptDto(Guid Id, Guid SubmissionJobId, int AttemptNumber, string Adapter, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, SubmissionStatus? Result, int? HttpStatus, string? ExternalReference, string? Error,
    Guid? PersistentJobId = null, Guid? SubmissionSourceId = null, string? TargetUrl = null, string? Strategy = null,
    Guid? IdentityId = null, Guid? TemplateId = null, string? ResolvedDisplayName = null, string? ResolvedEmail = null,
    string? ResolvedWebsite = null, ModerationStatus ModerationStatus = ModerationStatus.Unknown,
    SubmissionFailureKind FailureKind = SubmissionFailureKind.None, string? Endpoint = null,
    string? RedirectDestination = null);
public sealed record BacklinkDto(Guid Id, Guid ProjectId, Guid? CampaignId, Guid? SubmissionJobId, string SourceUrl, string NormalizedSourceUrl, string TargetUrl, string NormalizedTargetUrl, string Domain, BacklinkStatus Status, string? AnchorText, IReadOnlyList<string> Rel, bool Nofollow, bool Ugc, bool Sponsored, int? HttpStatus, string? CanonicalUrl, DateTimeOffset? FirstSeenAt, DateTimeOffset? LastSeenAt, DateTimeOffset? LastCheckedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Guid? SubmissionSourceId = null, Guid? SubmissionAttemptId = null);
public sealed record VerificationCheckDto(Guid Id, Guid BacklinkId, DateTimeOffset CheckedAt, bool Found, int? HttpStatus, string? Anchor, IReadOnlyList<string> Rel, string? Error, int DurationMilliseconds);
public sealed record OpportunitySummaryDto(int Total, IReadOnlyDictionary<string, int> ByType, IReadOnlyDictionary<string, int> ByAutomationStatus);
public sealed record JobDto(Guid Id, JobType Type, Guid ProjectId, Guid? CampaignId, JobStatus Status, int Priority, string? Domain, DateTimeOffset CreatedAt, DateTimeOffset AvailableAt, DateTimeOffset? ClaimedAt, DateTimeOffset? ClaimExpiresAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, DateTimeOffset? LastHeartbeatAt, DateTimeOffset? PauseRequestedAt, DateTimeOffset? PausedAt, int AttemptCount, int MaxAttempts, int RecoveryCount, JobFailureKind? LastFailureKind, string? LastError, string CorrelationId);
public sealed record DiscoveryRunDto(Guid Id, Guid ProjectId, Guid DiscoveryQueryId, Guid? JobId, DiscoveryProviderKind Provider, string? Query, DiscoveryRunStatus Status, int UrlsDiscovered, int UrlsAccepted, int Duplicates, int Blocked, int Invalid, int Errors, IReadOnlyList<string> ErrorDetails, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);
public sealed record ReportDto(Guid Id, Guid ProjectId, Guid? CampaignId, Guid? JobId, ReportKind Kind, ReportFormat Format, ReportStatus Status, string? ArtifactName, string? ContentType, long? ByteLength, string? Sha256, int? RowCount, string? Error, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt);
public sealed record ReportSummaryDto(int Candidates, int Eligible, int Approved, int Queued, int Processing,
    int Submitted, int Pending, int Verified, int Rejected, int Failed, int Lost, int Follow, int Nofollow, int Ugc,
    int Sponsored, int ApprovedSubmissions = 0, int Duplicate = 0, int PendingVerification = 0, int Missing = 0,
    int VerificationError = 0, int AttemptCount = 0, double SubmissionSuccessRate = 0,
    double VerificationRate = 0, IReadOnlyList<ReportPerformanceBreakdownDto>? DomainPerformance = null,
    IReadOnlyList<ReportPerformanceBreakdownDto>? TemplatePerformance = null,
    IReadOnlyList<ReportPerformanceBreakdownDto>? IdentityPerformance = null);
public sealed record ReportPerformanceBreakdownDto(string Key, int AttemptCount, int SuccessfulSubmissions,
    int VerifiedBacklinks, double SubmissionSuccessRate, double VerificationRate);
public sealed record ReportRowDto(string SourceUrl, string TargetUrl, string? Anchor, OpportunityType? OpportunityType,
    SubmissionStatus? SubmissionStatus, BacklinkStatus? VerificationStatus, IReadOnlyList<string> Rel, int? HttpStatus,
    DateTimeOffset? SubmittedAt, DateTimeOffset? FirstSeenAt, DateTimeOffset? LastSeenAt, DateTimeOffset? LastCheckedAt,
    string? Error, string? Domain = null, string? Network = null, SourcePlatform? Platform = null, CmsType? CmsType = null,
    string? Identity = null, string? Template = null, ModerationStatus? ModerationStatus = null,
    DateTimeOffset? QueuedAt = null, DateTimeOffset? StartedAt = null, DateTimeOffset? CompletedAt = null);
public sealed record ReportDownload(Stream Content, string ContentType, string FileName, long Length, string Sha256);

public sealed record CreateProjectCommand(string Name, string PrimaryDomain, string? Description, string IdempotencyKey);
public sealed record AddTargetCommand(Guid ProjectId, string Url, string? Label, IReadOnlyList<string>? Keywords, string? PreferredAnchor, string? Category, int Priority, string IdempotencyKey);
public sealed record ImportCandidatesCommand(Guid ProjectId, IReadOnlyList<string> Urls, string IdempotencyKey);
public sealed record ImportCandidatesResult(int Submitted, int Accepted, int Duplicates, int Invalid, IReadOnlyList<string> Errors);
public sealed record StartAnalysisCommand(Guid ProjectId, string IdempotencyKey);
public sealed record JobAcceptedDto(Guid JobId, string Status);
public sealed record StartDiscoveryCommand(Guid ProjectId, DiscoveryProviderKind Provider, string? Query, string? Content, IReadOnlyList<string>? Urls, int MaximumResults, string IdempotencyKey);
public sealed record DiscoveryAcceptedDto(Guid DiscoveryRunId, Guid JobId, string Status);
public sealed record ChangeJobStateCommand(Guid JobId, string IdempotencyKey);
public sealed record BlocklistEntryDto(Guid Id, Guid ProjectId, BlocklistMatchType MatchType, string Value, string Reason, bool Enabled, DateTimeOffset CreatedAt);
public sealed record PolicyDefinitionDto(Guid Id, Guid ProjectId, bool AutomationEnabled, int MinimumQualityScore, int MaximumRiskScore, bool ManualReviewRequired, int HourlyActionLimit, int DailyActionLimit, int PerDomainActionLimit, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record AddBlocklistEntryCommand(Guid ProjectId, BlocklistMatchType MatchType, string Value, string Reason, string IdempotencyKey);
public sealed record UpdatePolicyCommand(Guid ProjectId, bool AutomationEnabled, int MinimumQualityScore, int MaximumRiskScore, bool ManualReviewRequired, int HourlyActionLimit, int DailyActionLimit, int PerDomainActionLimit, string IdempotencyKey);
public sealed record ApproveOpportunitiesCommand(Guid ProjectId, IReadOnlyList<Guid> OpportunityIds, string IdempotencyKey);
public sealed record ApproveOpportunitiesResult(int Approved);
public sealed record CreateCampaignCommand(Guid ProjectId, string Name, CampaignApprovalMode ApprovalMode, string AuthorizationProfileKey, string AuthorizationReference, int DailyActionLimit, Guid TargetId, IReadOnlyList<Guid> OpportunityIds, string IdempotencyKey);
public sealed record CreateOwnedNetworkCampaignCommand(Guid ProjectId, string Name, Guid OwnedNetworkProfileId, string TargetUrl,
    Guid? IdentityPoolId, Guid? TemplatePoolId, int GlobalConcurrency, int PerDomainConcurrency, int PerDomainDelayMilliseconds,
    int MaximumAttempts, int VerificationDelaySeconds, OwnedNetworkCampaignMode Mode, string? Domain, SourcePlatform? Platform,
    CmsType? CmsType, TechnicalCompatibility TechnicalCompatibility, SubmissionSourceValidationStatus? ValidationStatus,
    string? Tag, int DailyActionLimit, string IdempotencyKey, SubmissionStatus? PreviousSubmissionStatus = null,
    BacklinkStatus? PreviousVerificationStatus = null);
public sealed record UpdateCampaignCommand(Guid CampaignId, string Name, CampaignApprovalMode ApprovalMode, int DailyActionLimit, string IdempotencyKey);
public sealed record StartCampaignCommand(Guid CampaignId, string IdempotencyKey);
public sealed record ChangeCampaignStateCommand(Guid CampaignId, string IdempotencyKey);
public sealed record StartCampaignResult(Guid CampaignId, int JobsQueued, IReadOnlyList<Guid> JobIds, int ApprovalRequired, int Rejected, int ManualActionRequired, string Status);
public sealed record StartVerificationCommand(Guid BacklinkId, string IdempotencyKey);
public sealed record GenerateReportCommand(Guid ProjectId, Guid? CampaignId, ReportKind Kind, ReportFormat Format, string IdempotencyKey);
public sealed record ReportAcceptedDto(Guid ReportId, Guid JobId, string Status);

public interface IProjectService
{
    Task<ProjectDto> CreateAsync(CreateProjectCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<ProjectDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<ProjectDto>> ListAsync(PageRequest page, CancellationToken cancellationToken);
    Task<TargetDto> AddTargetAsync(AddTargetCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<PageResult<TargetDto>> ListTargetsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);
}

public interface ICandidateService
{
    Task<ImportCandidatesResult> ImportAsync(ImportCandidatesCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<CandidateDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<CandidateDto>> ListAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);
}

public interface IOpportunityService
{
    Task<OpportunityDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<OpportunityDto>> ListAsync(Guid projectId, int? minimumQuality, int? maximumRisk, PageRequest page, CancellationToken cancellationToken);
    Task<OpportunitySummaryDto> SummaryAsync(Guid projectId, CancellationToken cancellationToken);
    Task<JobAcceptedDto> StartAnalysisAsync(StartAnalysisCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<ApproveOpportunitiesResult> ApproveAsync(ApproveOpportunitiesCommand command, ActorContext actor, CancellationToken cancellationToken);
}

public interface ICampaignService
{
    Task<CampaignDto> CreateAsync(CreateCampaignCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<CampaignDto> CreateOwnedNetworkAsync(CreateOwnedNetworkCampaignCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<CampaignDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<CampaignDto>> ListAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);
    Task<CampaignDto> UpdateAsync(UpdateCampaignCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<StartCampaignResult> StartAsync(StartCampaignCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<CampaignDto> PauseAsync(ChangeCampaignStateCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<CampaignDto> ResumeAsync(ChangeCampaignStateCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<CampaignDto> StopAsync(ChangeCampaignStateCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<SubmissionJobDto?> GetSubmissionAsync(Guid submissionJobId, CancellationToken cancellationToken);
    Task<PageResult<SubmissionJobDto>> ListSubmissionsAsync(Guid campaignId, PageRequest page, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubmissionAttemptDto>> ListAttemptsAsync(Guid submissionJobId, CancellationToken cancellationToken);
}

public interface IVerificationService
{
    Task<JobAcceptedDto> StartAsync(StartVerificationCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<BacklinkDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<BacklinkDto>> ListAsync(Guid projectId, BacklinkStatus? status, PageRequest page, CancellationToken cancellationToken);
    Task<PageResult<VerificationCheckDto>> HistoryAsync(Guid backlinkId, PageRequest page, CancellationToken cancellationToken);
}

public interface IJobService
{
    Task<JobDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<JobDto>> ListAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);
    Task<JobDto> PauseAsync(ChangeJobStateCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<JobDto> ResumeAsync(ChangeJobStateCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<JobDto> RedriveAsync(ChangeJobStateCommand command, ActorContext actor, CancellationToken cancellationToken);
}

public interface IDiscoveryService
{
    Task<DiscoveryAcceptedDto> StartAsync(StartDiscoveryCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<DiscoveryRunDto?> GetRunAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<DiscoveryRunDto>> ListRunsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);
}

public interface IReportService
{
    Task<ReportAcceptedDto> GenerateAsync(GenerateReportCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<ReportDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<ReportDto>> ListAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);
    Task<ReportDownload?> DownloadAsync(Guid id, CancellationToken cancellationToken);
}

public interface IPolicyService
{
    Task<PolicyDefinitionDto> GetAsync(Guid projectId, CancellationToken cancellationToken);
    Task<PolicyDefinitionDto> UpdateAsync(UpdatePolicyCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<PageResult<BlocklistEntryDto>> ListBlocklistAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);
    Task<BlocklistEntryDto> AddBlocklistAsync(AddBlocklistEntryCommand command, ActorContext actor, CancellationToken cancellationToken);
}

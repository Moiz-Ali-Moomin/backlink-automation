namespace BacklinkStudio.Api.Contracts;

public sealed record CreateProjectRequest(string Name, string PrimaryDomain, string? Description);
public sealed record AddTargetRequest(string Url, string? Label, IReadOnlyList<string>? Keywords, string? PreferredAnchor, string? Category, int Priority = 50);
public sealed record ImportCandidatesRequest(IReadOnlyList<string> Urls);
public sealed record AddBlocklistEntryRequest(BacklinkStudio.Domain.BlocklistMatchType MatchType, string Value, string Reason);
public sealed record UpdatePolicyRequest(bool AutomationEnabled, int MinimumQualityScore, int MaximumRiskScore, bool ManualReviewRequired, int HourlyActionLimit, int DailyActionLimit, int PerDomainActionLimit);
public sealed record StartDiscoveryRequest(Guid ProjectId, BacklinkStudio.Domain.DiscoveryProviderKind Provider, string? Query, string? Content, IReadOnlyList<string>? Urls, int MaximumResults = 1_000);
public sealed record ApproveOpportunitiesRequest(Guid ProjectId, IReadOnlyList<Guid> OpportunityIds);
public sealed record CreateCampaignRequest(Guid ProjectId, string Name, BacklinkStudio.Domain.CampaignApprovalMode ApprovalMode, string AuthorizationProfileKey, string AuthorizationReference, int DailyActionLimit, Guid TargetId, IReadOnlyList<Guid> OpportunityIds);
public sealed record CreateOwnedNetworkCampaignRequest(Guid ProjectId, string Name, Guid OwnedNetworkProfileId, string TargetUrl,
    Guid? IdentityPoolId, Guid? TemplatePoolId, int GlobalConcurrency = 100, int PerDomainConcurrency = 2,
    int PerDomainDelayMilliseconds = 1_000, int MaximumAttempts = 3, int VerificationDelaySeconds = 3_600,
    BacklinkStudio.Domain.OwnedNetworkCampaignMode Mode = BacklinkStudio.Domain.OwnedNetworkCampaignMode.AutomaticOwnedNetwork,
    string? Domain = null, BacklinkStudio.Domain.SourcePlatform? Platform = null, BacklinkStudio.Domain.CmsType? CmsType = null,
    BacklinkStudio.Domain.TechnicalCompatibility TechnicalCompatibility = BacklinkStudio.Domain.TechnicalCompatibility.Compatible,
    BacklinkStudio.Domain.SubmissionSourceValidationStatus? ValidationStatus = BacklinkStudio.Domain.SubmissionSourceValidationStatus.Valid,
    string? Tag = null, int DailyActionLimit = 100_000,
    BacklinkStudio.Domain.SubmissionStatus? PreviousSubmissionStatus = null,
    BacklinkStudio.Domain.BacklinkStatus? PreviousVerificationStatus = null);
public sealed record UpdateCampaignRequest(string Name, BacklinkStudio.Domain.CampaignApprovalMode ApprovalMode, int DailyActionLimit);
public sealed record StartVerificationRequest(Guid BacklinkId);
public sealed record CreateScheduleRequest(Guid ProjectId, string Name, BacklinkStudio.Application.ScheduleActionConfiguration Action, BacklinkStudio.Application.ScheduleTiming Timing);
public sealed record UpdateScheduleRequest(string Name, BacklinkStudio.Application.ScheduleActionConfiguration Action, BacklinkStudio.Application.ScheduleTiming Timing);
public sealed record CreateAgentCredentialRequest(string Name, IReadOnlyList<string> Scopes, DateTimeOffset? ExpiresAt);
public sealed record RotateAgentCredentialRequest(DateTimeOffset? ExpiresAt);
public sealed record GenerateReportRequest(Guid ProjectId, Guid? CampaignId, BacklinkStudio.Domain.ReportKind Kind, BacklinkStudio.Domain.ReportFormat Format);
public sealed record OwnedNetworkDomainRequest(string Domain, BacklinkStudio.Domain.OwnedNetworkDomainMatchType MatchType, bool Enabled = true);
public sealed record CreateOwnedNetworkRequest(
    Guid ProjectId,
    string Name,
    string? Description,
    BacklinkStudio.Domain.OwnershipStatus OwnershipStatus,
    bool AutomationPermitted,
    IReadOnlyList<OwnedNetworkDomainRequest> Domains,
    string? OptionalNetworkTag,
    Guid? DefaultIdentityPoolId,
    Guid? DefaultTemplatePoolId,
    int MaxConcurrency = 100,
    int PerDomainConcurrency = 2,
    int PerDomainDelayMilliseconds = 1_000,
    bool Enabled = true);
public sealed record UpdateOwnedNetworkRequest(
    string Name,
    string? Description,
    BacklinkStudio.Domain.OwnershipStatus OwnershipStatus,
    bool AutomationPermitted,
    IReadOnlyList<OwnedNetworkDomainRequest> Domains,
    string? OptionalNetworkTag,
    Guid? DefaultIdentityPoolId,
    Guid? DefaultTemplatePoolId,
    int MaxConcurrency,
    int PerDomainConcurrency,
    int PerDomainDelayMilliseconds,
    bool Enabled);
public sealed record ValidateSubmissionSourcesRequest(Guid ProjectId, Guid? OwnedNetworkProfileId, int MaximumSources = 100_000);
public sealed record CreateIdentityPoolRequest(Guid ProjectId, string Name, BacklinkStudio.Domain.PoolSelectionStrategy SelectionStrategy,
    BacklinkStudio.Domain.IdentityEmailStrategy EmailStrategy, string? EmailBaseAddress, string? CatchAllDomain, bool Enabled = true);
public sealed record UpdateIdentityPoolRequest(string Name, BacklinkStudio.Domain.PoolSelectionStrategy SelectionStrategy,
    BacklinkStudio.Domain.IdentityEmailStrategy EmailStrategy, string? EmailBaseAddress, string? CatchAllDomain, bool Enabled);
public sealed record CreateIdentityRequest(string DisplayName, string Email, string? Website, string? Organization, bool Enabled = true, int Weight = 1);
public sealed record UpdateIdentityRequest(string DisplayName, string Email, string? Website, string? Organization, bool Enabled, int Weight);
public sealed record CreateTemplatePoolRequest(Guid ProjectId, string Name, BacklinkStudio.Domain.SubmissionTemplateType TemplateType,
    BacklinkStudio.Domain.PoolSelectionStrategy SelectionStrategy, BacklinkStudio.Domain.BacklinkPlacementMethod PlacementMethod, bool Enabled = true);
public sealed record UpdateTemplatePoolRequest(string Name, BacklinkStudio.Domain.SubmissionTemplateType TemplateType,
    BacklinkStudio.Domain.PoolSelectionStrategy SelectionStrategy, BacklinkStudio.Domain.BacklinkPlacementMethod PlacementMethod, bool Enabled);
public sealed record CreateSubmissionTemplateRequest(string Name, string Body, IReadOnlyList<string>? PrefixVariants,
    IReadOnlyList<string>? SuffixVariants, IReadOnlyList<string>? AnchorVariants, IReadOnlyList<string>? TargetUrlVariants,
    bool Enabled = true, int Weight = 1);
public sealed record UpdateSubmissionTemplateRequest(string Name, string Body, IReadOnlyList<string>? PrefixVariants,
    IReadOnlyList<string>? SuffixVariants, IReadOnlyList<string>? AnchorVariants, IReadOnlyList<string>? TargetUrlVariants,
    bool Enabled, int Weight);
public sealed record SubmissionPreviewRequest(Guid ProjectId, Guid SubmissionSourceId, Guid? CampaignId,
    Guid? IdentityPoolId, Guid? TemplatePoolId, string TargetUrl, int AttemptNumber = 1);
public sealed record CreateWordPressSiteProfileRequest(Guid OwnedNetworkProfileId, string Domain, string ApiBaseUrl,
    string? CredentialReference, BacklinkStudio.Domain.WordPressSubmissionMode SubmissionMode, bool Enabled = true);
public sealed record UpdateWordPressSiteProfileRequest(string ApiBaseUrl, string? CredentialReference,
    BacklinkStudio.Domain.WordPressSubmissionMode SubmissionMode, bool Enabled);
public sealed record BacklinkWorkflowIdentityRequest(string Name, string Email);
public sealed record StartBacklinkWorkflowRequest(Guid ProjectId, IReadOnlyList<string>? SourceUrls,
    Guid? SourceImportId, IReadOnlyList<BacklinkWorkflowIdentityRequest>? Identities, Guid? IdentityPoolId,
    IReadOnlyList<string>? Comments, Guid? TemplatePoolId, string TargetUrl, int? GlobalConcurrency = null,
    int? PerDomainConcurrency = null, int? PerDomainDelayMilliseconds = null, int? MaximumAttempts = null,
    int? VerificationDelaySeconds = null, string? ClientRequestKey = null);

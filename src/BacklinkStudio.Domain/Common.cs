namespace BacklinkStudio.Domain;

public sealed class DomainRuleException(string message) : InvalidOperationException(message);

internal static class Guard
{
    public static string Required(string? value, int maximumLength, string name)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new DomainRuleException($"{name} is required.");
        }

        if (trimmed.Length > maximumLength)
        {
            throw new DomainRuleException($"{name} must not exceed {maximumLength} characters.");
        }

        return trimmed;
    }

    public static string? Optional(string? value, int maximumLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Required(value, maximumLength, name);
    }
}

public enum ProjectStatus
{
    Active,
    Archived
}

public enum CandidateAnalysisStatus
{
    New,
    Analyzed,
    Error,
    Blocked
}

public enum OpportunityType
{
    DirectoryListing,
    Citation,
    OwnedProperty,
    PartnerPage,
    ResourcePage,
    Profile,
    PermittedComment,
    WordPressComment,
    GuestContribution,
    ManualOutreach,
    Unknown
}

public enum OwnershipStatus
{
    Unverified,
    Owned,
    Controlled,
    PartnerControlled,
    ExplicitPermission
}

public enum OwnedNetworkDomainMatchType
{
    ExactHost,
    ExactDomain,
    SubdomainOf
}

public enum SourcePlatform
{
    Unknown,
    WordPress,
    OwnedProperty,
    GenericWeb
}

public enum CmsType
{
    Unknown,
    WordPress,
    Other
}

public enum TechnicalCompatibility
{
    Unknown,
    Compatible,
    FallbackCandidate,
    Incompatible,
    ManualActionRequired
}

public enum SubmissionSourceValidationStatus
{
    Pending,
    Queued,
    Running,
    Valid,
    Invalid,
    Error
}

public enum SubmissionSourceImportFormat
{
    Txt,
    Csv
}

public enum SubmissionSourceImportStatus
{
    Staging,
    Queued,
    Processing,
    Completed,
    Failed
}

public enum AutomationStatus
{
    Unreviewed,
    ApprovalRequired,
    ManualActionRequired,
    Approved,
    Rejected
}

public enum ScoreKind
{
    Quality,
    Risk
}

public enum PolicyDecision
{
    Allowed,
    Rejected,
    ApprovalRequired,
    ManualActionRequired
}

public enum CampaignApprovalMode
{
    Manual,
    SemiAutomatic,
    Automatic
}

public enum CampaignStatus
{
    Draft,
    Running,
    Paused,
    Completed,
    Stopped
}

public enum SubmissionStatus
{
    Queued,
    Claimed,
    Processing,
    Running,
    Submitted,
    PendingModeration,
    Approved,
    Rejected,
    Duplicate,
    Failed,
    ReconciliationRequired,
    Cancelled,
    ManualActionRequired
}

public enum ModerationStatus
{
    Unknown,
    NotRequired,
    Pending,
    Approved,
    Rejected
}

public enum SubmissionFailureKind
{
    None,
    Temporary,
    Permanent,
    RateLimited,
    Duplicate,
    CommentsClosed,
    LoginRequired,
    InvalidPost,
    PolicyRejected,
    AuthorizationDenied,
    EndpointNotFound,
    UnsupportedForm,
    BrowserRequired,
    ValidationFailed,
    NetworkError,
    Uncertain
}

public enum BacklinkWorkflowStatus
{
    Queued,
    Running,
    Completed,
    CompletedWithFailures,
    Failed
}

public enum BacklinkWorkflowSourceStatus
{
    Queued,
    Checking,
    Submitting,
    Submitted,
    PendingModeration,
    Verified,
    Failed,
    Rejected,
    CommentsClosed,
    AuthenticationRequired,
    RateLimited,
    NotAuthorized,
    // Retained only so rows written by pre-Backlink-PRO builds remain readable.
    // New normal-workflow results must use NotAuthorized.
    Blocked,
    Unsupported
}

public enum PoolSelectionStrategy
{
    RoundRobin,
    DeterministicRandom,
    WeightedDeterministicRandom
}

public enum IdentityEmailStrategy
{
    Fixed,
    AliasPool,
    PlusAddressing,
    CatchAll,
    PreCreatedSynthetic
}

public enum SubmissionTemplateType
{
    WordPressComment,
    OwnedProperty,
    GenericOwnedNetwork
}

public enum BacklinkPlacementMethod
{
    WebsiteField,
    CommentBody
}

public enum WordPressSubmissionMode
{
    DirectApi,
    AuthenticatedIntegration,
    StandardComment,
    FallbackComment,
    ControlledBrowser,
    ManualActionRequired
}

public enum OwnedNetworkCampaignMode
{
    Preview,
    ManualApproval,
    AutomaticOwnedNetwork
}

public enum BacklinkStatus
{
    PendingVerification,
    Verified,
    Missing,
    Lost,
    Error
}

public enum ReportKind
{
    CampaignPerformance,
    BacklinkInventory
}

public enum ReportFormat
{
    Json,
    Csv,
    Xlsx,
    Html
}

public enum ReportStatus
{
    Queued,
    Generating,
    Completed,
    Failed
}

public enum BlocklistMatchType
{
    Domain,
    Host,
    Url,
    UrlPrefix,
    SubmissionSource,
    OwnedNetworkProfile,
    Campaign
}

public enum DiscoveryProviderKind
{
    ManualUrl,
    TxtImport,
    CsvImport,
    Sitemap,
    Serper,
    CompetitorBacklinkImport
}

public enum DiscoveryRunStatus
{
    Queued,
    Running,
    Succeeded,
    Failed
}

public enum ActorType
{
    User,
    Agent,
    Worker,
    Scheduler,
    System
}

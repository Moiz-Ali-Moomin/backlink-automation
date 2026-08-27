using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public sealed record OwnedNetworkDomainDto(
    Guid Id,
    Guid OwnedNetworkProfileId,
    string Domain,
    OwnedNetworkDomainMatchType MatchType,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record OwnedNetworkProfileDto(
    Guid Id,
    Guid ProjectId,
    string Name,
    string? Description,
    OwnershipStatus OwnershipStatus,
    bool AutomationPermitted,
    string? OptionalNetworkTag,
    Guid? DefaultIdentityPoolId,
    Guid? DefaultTemplatePoolId,
    int MaxConcurrency,
    int PerDomainConcurrency,
    int PerDomainDelayMilliseconds,
    bool Enabled,
    bool AllowsAutomaticExecution,
    IReadOnlyList<OwnedNetworkDomainDto> Domains,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record OwnedNetworkDomainInput(string Domain, OwnedNetworkDomainMatchType MatchType, bool Enabled = true);

public sealed record CreateOwnedNetworkCommand(
    Guid ProjectId,
    string Name,
    string? Description,
    OwnershipStatus OwnershipStatus,
    bool AutomationPermitted,
    IReadOnlyList<OwnedNetworkDomainInput> Domains,
    string? OptionalNetworkTag,
    Guid? DefaultIdentityPoolId,
    Guid? DefaultTemplatePoolId,
    int MaxConcurrency,
    int PerDomainConcurrency,
    int PerDomainDelayMilliseconds,
    bool Enabled,
    string IdempotencyKey);

public sealed record UpdateOwnedNetworkCommand(
    Guid OwnedNetworkProfileId,
    string Name,
    string? Description,
    OwnershipStatus OwnershipStatus,
    bool AutomationPermitted,
    IReadOnlyList<OwnedNetworkDomainInput> Domains,
    string? OptionalNetworkTag,
    Guid? DefaultIdentityPoolId,
    Guid? DefaultTemplatePoolId,
    int MaxConcurrency,
    int PerDomainConcurrency,
    int PerDomainDelayMilliseconds,
    bool Enabled,
    string IdempotencyKey);

public sealed record SubmissionSourceDto(
    Guid Id,
    Guid ProjectId,
    Guid OwnedNetworkProfileId,
    Guid? SourceImportId,
    string OriginalUrl,
    string NormalizedUrl,
    string Domain,
    string Host,
    SourcePlatform Platform,
    CmsType CmsType,
    OpportunityType OpportunityType,
    string? AdapterName,
    OwnershipStatus OwnershipStatus,
    bool AutomationPermitted,
    TechnicalCompatibility TechnicalCompatibility,
    SubmissionSourceValidationStatus ValidationStatus,
    string? ValidationReason,
    string? DetectionReason,
    bool RequiresBrowser,
    bool RequiresAuthentication,
    bool RequiresManualAction,
    bool SupportsWordPressComment,
    bool SupportsOwnedWordPressApi,
    bool SupportsOwnedPropertyPlacement,
    long? PostId,
    string? CommentEndpoint,
    string? DetectedFormAction,
    string? PageTitle,
    string? FinalUrl,
    string? CanonicalUrl,
    int? LastHttpStatus,
    string? LastContentType,
    long? LastContentLength,
    IReadOnlyList<string> RedirectChain,
    bool CommentsEnabled,
    string? CommentAuthorField,
    string? CommentEmailField,
    string? CommentWebsiteField,
    string? CommentContentField,
    string? CommentPostIdField,
    IReadOnlyList<string> AdditionalRequiredFields,
    bool RequiresCookies,
    bool RequiresNonce,
    string? ModerationSignal,
    string? Tag,
    DateTimeOffset? LastValidatedAt,
    DateTimeOffset? LastSubmissionAt,
    DateTimeOffset? LastSuccessfulSubmissionAt,
    long SuccessCount,
    long FailureCount,
    long PendingModerationCount,
    long VerifiedCount,
    long LostCount,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SubmissionSourceImportDto(
    Guid Id,
    Guid ProjectId,
    Guid OwnedNetworkProfileId,
    Guid? JobId,
    SubmissionSourceImportFormat Format,
    SubmissionSourceImportStatus Status,
    string FileName,
    string? Tag,
    long ByteLength,
    string? Sha256,
    int TotalLines,
    int Accepted,
    int Duplicates,
    int Invalid,
    int Errors,
    string? SafeError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public sealed record ImportSubmissionSourcesCommand(
    Guid ProjectId,
    Guid OwnedNetworkProfileId,
    SubmissionSourceImportFormat Format,
    string FileName,
    string? Tag,
    string IdempotencyKey);

public sealed record SubmissionSourceImportAcceptedDto(Guid ImportId, Guid JobId, string Status);
public sealed record ValidateSubmissionSourcesCommand(Guid ProjectId, Guid? OwnedNetworkProfileId, int MaximumSources, string IdempotencyKey);
public sealed record SubmissionSourceValidationAcceptedDto(Guid JobId, string Status);

public sealed record SubmissionSourceFilter(
    Guid? OwnedNetworkProfileId = null,
    string? Domain = null,
    SourcePlatform? Platform = null,
    CmsType? CmsType = null,
    string? AdapterName = null,
    OwnershipStatus? OwnershipStatus = null,
    bool? AutomationPermitted = null,
    TechnicalCompatibility? TechnicalCompatibility = null,
    SubmissionSourceValidationStatus? ValidationStatus = null,
    bool? Enabled = null,
    string? Tag = null,
    SubmissionStatus? PreviousSubmissionStatus = null,
    BacklinkStatus? PreviousVerificationStatus = null);

public interface IOwnedNetworkService
{
    Task<OwnedNetworkProfileDto> CreateAsync(CreateOwnedNetworkCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<OwnedNetworkProfileDto> UpdateAsync(UpdateOwnedNetworkCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<OwnedNetworkProfileDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<OwnedNetworkProfileDto>> ListAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);
}

public interface ISubmissionSourceService
{
    Task<SubmissionSourceImportAcceptedDto> ImportAsync(ImportSubmissionSourcesCommand command, Stream content, ActorContext actor, CancellationToken cancellationToken);
    Task<SubmissionSourceImportDto?> GetImportAsync(Guid id, CancellationToken cancellationToken);
    Task<SubmissionSourceDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<SubmissionSourceDto>> ListAsync(Guid projectId, SubmissionSourceFilter filter, PageRequest page, CancellationToken cancellationToken);
    Task<SubmissionSourceValidationAcceptedDto> ValidateAsync(ValidateSubmissionSourcesCommand command, ActorContext actor, CancellationToken cancellationToken);
}

public interface ISubmissionSourceInspector
{
    Task<SubmissionSourceValidation> InspectAsync(Uri source, bool effectiveOwnershipAuthorized, CancellationToken cancellationToken);
}

public static class OwnedNetworkMapping
{
    public static OwnedNetworkDomainDto ToDto(this OwnedNetworkDomain value) =>
        new(value.Id, value.OwnedNetworkProfileId, value.Domain, value.MatchType, value.Enabled, value.CreatedAt, value.UpdatedAt);

    public static OwnedNetworkProfileDto ToDto(this OwnedNetworkProfile value, IReadOnlyList<OwnedNetworkDomain> domains) =>
        new(value.Id, value.ProjectId, value.Name, value.Description, value.OwnershipStatus, value.AutomationPermitted,
            value.OptionalNetworkTag, value.DefaultIdentityPoolId, value.DefaultTemplatePoolId, value.MaxConcurrency,
            value.PerDomainConcurrency, value.PerDomainDelayMilliseconds, value.Enabled, value.AllowsAutomaticExecution,
            domains.Select(ToDto).ToArray(), value.CreatedAt, value.UpdatedAt);

    public static SubmissionSourceDto ToDto(this SubmissionSource value) =>
        new(value.Id, value.ProjectId, value.OwnedNetworkProfileId, value.SourceImportId, value.OriginalUrl, value.NormalizedUrl, value.Domain,
            value.Host, value.Platform, value.CmsType, value.OpportunityType, value.AdapterName, value.OwnershipStatus,
            value.AutomationPermitted, value.TechnicalCompatibility, value.ValidationStatus, value.ValidationReason,
            value.DetectionReason, value.RequiresBrowser, value.RequiresAuthentication, value.RequiresManualAction,
            value.SupportsWordPressComment, value.SupportsOwnedWordPressApi, value.SupportsOwnedPropertyPlacement,
            value.PostId, value.CommentEndpoint, value.DetectedFormAction, value.PageTitle, value.FinalUrl, value.CanonicalUrl,
            value.LastHttpStatus, value.LastContentType, value.LastContentLength, value.RedirectChain, value.CommentsEnabled,
            value.CommentAuthorField, value.CommentEmailField, value.CommentWebsiteField, value.CommentContentField,
            value.CommentPostIdField, value.AdditionalRequiredFields, value.RequiresCookies, value.RequiresNonce,
            value.ModerationSignal, value.Tag,
            value.LastValidatedAt, value.LastSubmissionAt, value.LastSuccessfulSubmissionAt, value.SuccessCount,
            value.FailureCount, value.PendingModerationCount, value.VerifiedCount, value.LostCount, value.Enabled,
            value.CreatedAt, value.UpdatedAt);

    public static SubmissionSourceImportDto ToDto(this SubmissionSourceImport value) =>
        new(value.Id, value.ProjectId, value.OwnedNetworkProfileId, value.JobId, value.Format, value.Status, value.FileName,
            value.Tag, value.ByteLength, value.Sha256, value.TotalLines, value.Accepted, value.Duplicates, value.Invalid,
            value.Errors, value.SafeError, value.CreatedAt, value.UpdatedAt, value.CompletedAt);
}

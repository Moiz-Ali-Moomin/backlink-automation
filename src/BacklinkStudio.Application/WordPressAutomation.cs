using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public sealed record OwnedWordPressSubmissionRequest(
    SubmissionSource Source,
    bool EffectiveOwnershipAuthorized,
    Guid CampaignId,
    Guid SubmissionJobId,
    string TargetUrl,
    string DisplayName,
    string Email,
    string? Website,
    string Comment,
    BacklinkPlacementMethod PlacementMethod,
    string IdempotencyKey);

public sealed record OwnedNetworkExecutionAuthorization(
    bool Allowed,
    bool TestOwnershipOverrideApplied,
    string Reason,
    OwnedNetworkProfile? Profile = null);

public sealed record OwnedNetworkSourceAuthorizationRequest(
    string Host,
    Guid? OwnedNetworkProfileId = null,
    bool Enabled = true);

public interface IOwnedNetworkExecutionAuthorizer
{
    Task<OwnedNetworkExecutionAuthorization> AuthorizeProfileAsync(
        Guid projectId,
        Guid ownedNetworkProfileId,
        CancellationToken cancellationToken);
    Task<OwnedNetworkExecutionAuthorization> AuthorizeSourceAsync(
        Guid projectId,
        OwnedNetworkSourceAuthorizationRequest source,
        CancellationToken cancellationToken);

    Task<OwnedNetworkExecutionAuthorization> AuthorizeSourceAsync(
        Guid projectId,
        SubmissionSource source,
        CancellationToken cancellationToken) => AuthorizeSourceAsync(projectId,
            new OwnedNetworkSourceAuthorizationRequest(source.Host, source.OwnedNetworkProfileId, source.Enabled),
            cancellationToken);

    Task<OwnedNetworkExecutionAuthorization> AuthorizeSourceAsync(
        Guid projectId,
        BacklinkWorkflowSource source,
        CancellationToken cancellationToken) => AuthorizeSourceAsync(projectId,
            new OwnedNetworkSourceAuthorizationRequest(source.Host), cancellationToken);
}

public static class OwnedNetworkExecutionEligibility
{
    public static bool IsTechnicallyExecutable(SubmissionSource source) =>
        source.Enabled &&
        source.TechnicalCompatibility is TechnicalCompatibility.Compatible or TechnicalCompatibility.FallbackCandidate &&
        source.ValidationStatus == SubmissionSourceValidationStatus.Valid &&
        source.CmsType == CmsType.WordPress &&
        source.PostId is not null &&
        !source.RequiresAuthentication &&
        !source.RequiresManualAction;
}

public sealed record OwnedWordPressSubmissionResult(
    SubmissionStatus Status,
    ModerationStatus ModerationStatus,
    WordPressSubmissionMode Strategy,
    int? HttpStatus,
    string? ExternalReference,
    SubmissionFailureKind FailureKind,
    string? SafeError,
    string? Endpoint = null,
    string? RedirectDestination = null,
    TimeSpan? RetryAfter = null)
{
    public bool MayHaveCreatedBacklink => Status is SubmissionStatus.Submitted or SubmissionStatus.PendingModeration or SubmissionStatus.Approved;
}

public interface IOwnedWordPressCommentAdapter
{
    string Name { get; }
    Task<OwnedWordPressSubmissionResult> SubmitAsync(OwnedWordPressSubmissionRequest request, CancellationToken cancellationToken);
}

public interface IWordPressSubmissionGateway
{
    Task<OwnedWordPressSubmissionResult> SubmitAsync(OwnedWordPressSubmissionRequest request, CancellationToken cancellationToken);
}

public interface IOwnedWordPressFallbackCommentAdapter
{
    string Name { get; }
    Task<OwnedWordPressSubmissionResult> SubmitAsync(OwnedWordPressSubmissionRequest request, CancellationToken cancellationToken);
}

public interface IControlledBrowserValidationAdapter
{
    Task<SubmissionSourceValidation> InspectAsync(
        SubmissionSource source,
        bool effectiveOwnershipAuthorized,
        CancellationToken cancellationToken);
}

public interface IControlledBrowserCommentAdapter
{
    string Name { get; }
    Task<OwnedWordPressSubmissionResult> SubmitAsync(
        OwnedWordPressSubmissionRequest request,
        WordPressSiteProfile? profile,
        CancellationToken cancellationToken);
}

public interface IWordPressSiteProfileRepository
{
    void Add(WordPressSiteProfile profile);
    Task<WordPressSiteProfile?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    Task<WordPressSiteProfile?> FindForSourceAsync(Guid ownedNetworkProfileId, string domain, CancellationToken cancellationToken);
    Task<WordPressSiteProfile?> FindAnyForSourceAsync(Guid ownedNetworkProfileId, string domain, CancellationToken cancellationToken);
    Task<IReadOnlyList<WordPressSiteProfile>> ListAsync(Guid ownedNetworkProfileId, PageCursor? cursor, int take, CancellationToken cancellationToken);
}

public sealed record CreateWordPressSiteProfileCommand(Guid OwnedNetworkProfileId, string Domain, string ApiBaseUrl,
    string? CredentialReference, WordPressSubmissionMode SubmissionMode, bool Enabled, string IdempotencyKey);
public sealed record UpdateWordPressSiteProfileCommand(Guid ProfileId, string ApiBaseUrl,
    string? CredentialReference, WordPressSubmissionMode SubmissionMode, bool Enabled, string IdempotencyKey);
public sealed record WordPressSiteProfileDto(Guid Id, Guid OwnedNetworkProfileId, string Domain, string ApiBaseUrl,
    string? CredentialReference, WordPressSubmissionMode SubmissionMode, bool Enabled, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public interface IWordPressSiteProfileService
{
    Task<WordPressSiteProfileDto> CreateAsync(CreateWordPressSiteProfileCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<WordPressSiteProfileDto> UpdateAsync(UpdateWordPressSiteProfileCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<WordPressSiteProfileDto?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<WordPressSiteProfileDto>> ListAsync(Guid ownedNetworkProfileId, PageRequest page, CancellationToken cancellationToken);
}

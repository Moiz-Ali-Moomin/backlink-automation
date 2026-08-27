using BacklinkStudio.Domain;

namespace BacklinkStudio.Application;

public sealed record CreateIdentityPoolCommand(Guid ProjectId, string Name, PoolSelectionStrategy SelectionStrategy,
    IdentityEmailStrategy EmailStrategy, string? EmailBaseAddress, string? CatchAllDomain, bool Enabled, string IdempotencyKey);
public sealed record UpdateIdentityPoolCommand(Guid PoolId, string Name, PoolSelectionStrategy SelectionStrategy,
    IdentityEmailStrategy EmailStrategy, string? EmailBaseAddress, string? CatchAllDomain, bool Enabled, string IdempotencyKey);
public sealed record CreateIdentityCommand(Guid PoolId, string DisplayName, string Email, string? Website,
    string? Organization, bool Enabled, int Weight, string IdempotencyKey);
public sealed record UpdateIdentityCommand(Guid IdentityId, string DisplayName, string Email, string? Website,
    string? Organization, bool Enabled, int Weight, string IdempotencyKey);

public sealed record IdentityPoolDto(Guid Id, Guid ProjectId, string Name, PoolSelectionStrategy SelectionStrategy,
    IdentityEmailStrategy EmailStrategy, string? EmailBaseAddress, string? CatchAllDomain, bool Enabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record SubmissionIdentityDto(Guid Id, Guid PoolId, string DisplayName, string Email, string? Website,
    string? Organization, bool Enabled, int Weight, long UsageCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public interface ISubmissionIdentityService
{
    Task<IdentityPoolDto> CreatePoolAsync(CreateIdentityPoolCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<IdentityPoolDto> UpdatePoolAsync(UpdateIdentityPoolCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<IdentityPoolDto?> GetPoolAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<IdentityPoolDto>> ListPoolsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);
    Task<SubmissionIdentityDto> CreateIdentityAsync(CreateIdentityCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<SubmissionIdentityDto> UpdateIdentityAsync(UpdateIdentityCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<SubmissionIdentityDto?> GetIdentityAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<SubmissionIdentityDto>> ListIdentitiesAsync(Guid poolId, PageRequest page, CancellationToken cancellationToken);
}

public sealed record CreateTemplatePoolCommand(Guid ProjectId, string Name, SubmissionTemplateType TemplateType,
    PoolSelectionStrategy SelectionStrategy, BacklinkPlacementMethod PlacementMethod, bool Enabled, string IdempotencyKey);
public sealed record UpdateTemplatePoolCommand(Guid PoolId, string Name, SubmissionTemplateType TemplateType,
    PoolSelectionStrategy SelectionStrategy, BacklinkPlacementMethod PlacementMethod, bool Enabled, string IdempotencyKey);
public sealed record CreateTemplateCommand(Guid PoolId, string Name, string Body, IReadOnlyList<string>? PrefixVariants,
    IReadOnlyList<string>? SuffixVariants, IReadOnlyList<string>? AnchorVariants, IReadOnlyList<string>? TargetUrlVariants,
    bool Enabled, int Weight, string IdempotencyKey);
public sealed record UpdateTemplateCommand(Guid TemplateId, string Name, string Body, IReadOnlyList<string>? PrefixVariants,
    IReadOnlyList<string>? SuffixVariants, IReadOnlyList<string>? AnchorVariants, IReadOnlyList<string>? TargetUrlVariants,
    bool Enabled, int Weight, string IdempotencyKey);

public sealed record TemplatePoolDto(Guid Id, Guid ProjectId, string Name, SubmissionTemplateType TemplateType,
    PoolSelectionStrategy SelectionStrategy, BacklinkPlacementMethod PlacementMethod, bool Enabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record SubmissionTemplateDto(Guid Id, Guid PoolId, string Name, string Body,
    IReadOnlyList<string> PrefixVariants, IReadOnlyList<string> SuffixVariants, IReadOnlyList<string> AnchorVariants,
    IReadOnlyList<string> TargetUrlVariants, bool Enabled, int Weight, long UsageCount,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public interface ISubmissionTemplateService
{
    Task<TemplatePoolDto> CreatePoolAsync(CreateTemplatePoolCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<TemplatePoolDto> UpdatePoolAsync(UpdateTemplatePoolCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<TemplatePoolDto?> GetPoolAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<TemplatePoolDto>> ListPoolsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);
    Task<SubmissionTemplateDto> CreateTemplateAsync(CreateTemplateCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<SubmissionTemplateDto> UpdateTemplateAsync(UpdateTemplateCommand command, ActorContext actor, CancellationToken cancellationToken);
    Task<SubmissionTemplateDto?> GetTemplateAsync(Guid id, CancellationToken cancellationToken);
    Task<PageResult<SubmissionTemplateDto>> ListTemplatesAsync(Guid poolId, PageRequest page, CancellationToken cancellationToken);
}

public sealed record ResolveSubmissionContentCommand(Guid SubmissionSourceId, Guid? CampaignId, Guid IdentityPoolId,
    Guid TemplatePoolId, string TargetUrl, int AttemptNumber);
public sealed record ResolvedSubmissionContent(Guid IdentityId, Guid TemplateId, string DisplayName, string Email,
    string? Website, string ResolvedTargetUrl, string ResolvedComment, BacklinkPlacementMethod PlacementMethod,
    string SelectionEvidence);
public sealed record SubmissionPreviewCommand(Guid ProjectId, Guid SubmissionSourceId, Guid? CampaignId,
    Guid? IdentityPoolId, Guid? TemplatePoolId, string TargetUrl, int AttemptNumber);
public sealed record SubmissionPreviewDto(string Adapter, SubmissionSourceDto Source, string TargetUrl,
    SubmissionIdentityDto Identity, SubmissionTemplateDto Template, string ResolvedComment,
    BacklinkPlacementMethod PlacementMethod, bool OwnershipPermitted, TechnicalCompatibility TechnicalCompatibility,
    string ExpectedExecutionStrategy, string SelectionEvidence);

public interface ISubmissionContentResolver
{
    Task<ResolvedSubmissionContent> ResolveAsync(ResolveSubmissionContentCommand command, CancellationToken cancellationToken);
}

public interface ISubmissionPreviewService
{
    Task<SubmissionPreviewDto> PreviewAsync(SubmissionPreviewCommand command, CancellationToken cancellationToken);
}

public interface ISubmissionContentRepository
{
    void AddIdentityPool(SubmissionIdentityPool pool);
    Task<SubmissionIdentityPool?> GetIdentityPoolAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    Task<SubmissionIdentityPool?> FindIdentityPoolByNameAsync(Guid projectId, string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubmissionIdentityPool>> ListIdentityPoolsAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken);
    void AddIdentity(SubmissionIdentity identity);
    Task<SubmissionIdentity?> GetIdentityAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubmissionIdentity>> ListIdentitiesAsync(Guid poolId, bool enabledOnly, PageCursor? cursor, int take, CancellationToken cancellationToken);

    void AddTemplatePool(SubmissionTemplatePool pool);
    Task<SubmissionTemplatePool?> GetTemplatePoolAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    Task<SubmissionTemplatePool?> FindTemplatePoolByNameAsync(Guid projectId, string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubmissionTemplatePool>> ListTemplatePoolsAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken);
    void AddTemplate(SubmissionTemplate submissionTemplate);
    Task<SubmissionTemplate?> GetTemplateAsync(Guid id, bool tracked, CancellationToken cancellationToken);
    Task<IReadOnlyList<SubmissionTemplate>> ListTemplatesAsync(Guid poolId, bool enabledOnly, PageCursor? cursor, int take, CancellationToken cancellationToken);
}

public static class SubmissionContentMappings
{
    public static IdentityPoolDto ToDto(this SubmissionIdentityPool value) =>
        new(value.Id, value.ProjectId, value.Name, value.SelectionStrategy, value.EmailStrategy, value.EmailBaseAddress,
            value.CatchAllDomain, value.Enabled, value.CreatedAt, value.UpdatedAt);

    public static SubmissionIdentityDto ToDto(this SubmissionIdentity value) =>
        new(value.Id, value.PoolId, value.DisplayName, value.Email, value.Website, value.Organization, value.Enabled,
            value.Weight, value.UsageCount, value.CreatedAt, value.UpdatedAt);

    public static TemplatePoolDto ToDto(this SubmissionTemplatePool value) =>
        new(value.Id, value.ProjectId, value.Name, value.TemplateType, value.SelectionStrategy, value.PlacementMethod,
            value.Enabled, value.CreatedAt, value.UpdatedAt);

    public static SubmissionTemplateDto ToDto(this SubmissionTemplate value) =>
        new(value.Id, value.PoolId, value.Name, value.Body, value.PrefixVariants, value.SuffixVariants,
            value.AnchorVariants, value.TargetUrlVariants, value.Enabled, value.Weight, value.UsageCount,
            value.CreatedAt, value.UpdatedAt);
}

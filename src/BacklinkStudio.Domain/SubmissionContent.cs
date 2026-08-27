using System.Net.Mail;

namespace BacklinkStudio.Domain;

public sealed class SubmissionIdentityPool
{
    private SubmissionIdentityPool() { }

    public SubmissionIdentityPool(Guid projectId, string name, PoolSelectionStrategy selectionStrategy,
        IdentityEmailStrategy emailStrategy, string? emailBaseAddress, string? catchAllDomain, bool enabled, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        Name = Guard.Required(name, 200, nameof(Name));
        SelectionStrategy = selectionStrategy;
        EmailStrategy = emailStrategy;
        EmailBaseAddress = ValidateEmail(emailBaseAddress, nameof(EmailBaseAddress));
        CatchAllDomain = ValidateDomain(catchAllDomain);
        if (emailStrategy == IdentityEmailStrategy.PlusAddressing && EmailBaseAddress is null)
            throw new DomainRuleException("Plus-addressing requires an email base address.");
        if (emailStrategy == IdentityEmailStrategy.CatchAll && CatchAllDomain is null)
            throw new DomainRuleException("Catch-all email strategy requires a catch-all domain.");
        Enabled = enabled;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Name { get; private set; } = null!;
    public PoolSelectionStrategy SelectionStrategy { get; private set; }
    public IdentityEmailStrategy EmailStrategy { get; private set; }
    public string? EmailBaseAddress { get; private set; }
    public string? CatchAllDomain { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string name, PoolSelectionStrategy selectionStrategy, IdentityEmailStrategy emailStrategy,
        string? emailBaseAddress, string? catchAllDomain, bool enabled, DateTimeOffset now)
    {
        Name = Guard.Required(name, 200, nameof(Name));
        SelectionStrategy = selectionStrategy;
        EmailStrategy = emailStrategy;
        EmailBaseAddress = ValidateEmail(emailBaseAddress, nameof(EmailBaseAddress));
        CatchAllDomain = ValidateDomain(catchAllDomain);
        if (emailStrategy == IdentityEmailStrategy.PlusAddressing && EmailBaseAddress is null)
            throw new DomainRuleException("Plus-addressing requires an email base address.");
        if (emailStrategy == IdentityEmailStrategy.CatchAll && CatchAllDomain is null)
            throw new DomainRuleException("Catch-all email strategy requires a catch-all domain.");
        Enabled = enabled;
        UpdatedAt = now;
    }

    private static string? ValidateEmail(string? value, string name)
    {
        var email = Guard.Optional(value, 320, name);
        if (email is not null && (!MailAddress.TryCreate(email, out var parsed) || !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase)))
            throw new DomainRuleException($"{name} must be a valid email address.");
        return email?.ToLowerInvariant();
    }

    private static string? ValidateDomain(string? value)
    {
        var domain = Guard.Optional(value, 253, nameof(CatchAllDomain))?.TrimEnd('.').ToLowerInvariant();
        if (domain is not null && Uri.CheckHostName(domain) != UriHostNameType.Dns)
            throw new DomainRuleException("Catch-all domain must be a valid DNS host name.");
        return domain;
    }
}

public sealed class SubmissionIdentity
{
    private SubmissionIdentity() { }

    public SubmissionIdentity(Guid poolId, string displayName, string email, string? website, string? organization,
        bool enabled, int weight, DateTimeOffset now)
    {
        if (weight is < 1 or > 10_000) throw new DomainRuleException("Identity weight must be between 1 and 10000.");
        Id = Guid.CreateVersion7(now);
        PoolId = poolId;
        DisplayName = Guard.Required(displayName, 200, nameof(DisplayName));
        Email = Guard.Required(email, 320, nameof(Email)).ToLowerInvariant();
        if (!MailAddress.TryCreate(Email, out var parsed) || !string.Equals(parsed.Address, Email, StringComparison.OrdinalIgnoreCase))
            throw new DomainRuleException("Identity email must be a valid email address.");
        Website = ValidateWebsite(website);
        Organization = Guard.Optional(organization, 200, nameof(Organization));
        Enabled = enabled;
        Weight = weight;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid PoolId { get; private set; }
    public string DisplayName { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string? Website { get; private set; }
    public string? Organization { get; private set; }
    public bool Enabled { get; private set; }
    public int Weight { get; private set; }
    public long UsageCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string displayName, string email, string? website, string? organization,
        bool enabled, int weight, DateTimeOffset now)
    {
        if (weight is < 1 or > 10_000) throw new DomainRuleException("Identity weight must be between 1 and 10000.");
        DisplayName = Guard.Required(displayName, 200, nameof(DisplayName));
        Email = Guard.Required(email, 320, nameof(Email)).ToLowerInvariant();
        if (!MailAddress.TryCreate(Email, out var parsed) || !string.Equals(parsed.Address, Email, StringComparison.OrdinalIgnoreCase))
            throw new DomainRuleException("Identity email must be a valid email address.");
        Website = ValidateWebsite(website);
        Organization = Guard.Optional(organization, 200, nameof(Organization));
        Enabled = enabled;
        Weight = weight;
        UpdatedAt = now;
    }

    public void RecordUsage(DateTimeOffset now)
    {
        UsageCount = checked(UsageCount + 1);
        UpdatedAt = now;
    }

    private static string? ValidateWebsite(string? value)
    {
        var website = Guard.Optional(value, 2_048, nameof(Website));
        if (website is not null && (!Uri.TryCreate(website, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new DomainRuleException("Identity website must be an absolute HTTP(S) URL without user information.");
        return website;
    }
}

public sealed class SubmissionTemplatePool
{
    private SubmissionTemplatePool() { }

    public SubmissionTemplatePool(Guid projectId, string name, SubmissionTemplateType templateType,
        PoolSelectionStrategy selectionStrategy, BacklinkPlacementMethod placementMethod, bool enabled, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        ProjectId = projectId;
        Name = Guard.Required(name, 200, nameof(Name));
        TemplateType = templateType;
        SelectionStrategy = selectionStrategy;
        PlacementMethod = placementMethod;
        Enabled = enabled;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Name { get; private set; } = null!;
    public SubmissionTemplateType TemplateType { get; private set; }
    public PoolSelectionStrategy SelectionStrategy { get; private set; }
    public BacklinkPlacementMethod PlacementMethod { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string name, SubmissionTemplateType templateType, PoolSelectionStrategy selectionStrategy,
        BacklinkPlacementMethod placementMethod, bool enabled, DateTimeOffset now)
    {
        Name = Guard.Required(name, 200, nameof(Name));
        TemplateType = templateType;
        SelectionStrategy = selectionStrategy;
        PlacementMethod = placementMethod;
        Enabled = enabled;
        UpdatedAt = now;
    }
}

public sealed class SubmissionTemplate
{
    private SubmissionTemplate() { }

    public SubmissionTemplate(Guid poolId, string name, string body, IEnumerable<string>? prefixVariants,
        IEnumerable<string>? suffixVariants, IEnumerable<string>? anchorVariants, IEnumerable<string>? targetUrlVariants,
        bool enabled, int weight, DateTimeOffset now)
    {
        if (weight is < 1 or > 10_000) throw new DomainRuleException("Template weight must be between 1 and 10000.");
        Id = Guid.CreateVersion7(now);
        PoolId = poolId;
        Name = Guard.Required(name, 200, nameof(Name));
        Body = Guard.Required(body, 10_000, nameof(Body));
        PrefixVariants = Variants(prefixVariants, nameof(PrefixVariants), 2_000);
        SuffixVariants = Variants(suffixVariants, nameof(SuffixVariants), 2_000);
        AnchorVariants = Variants(anchorVariants, nameof(AnchorVariants), 500);
        TargetUrlVariants = Variants(targetUrlVariants, nameof(TargetUrlVariants), 2_048);
        Enabled = enabled;
        Weight = weight;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid PoolId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public string[] PrefixVariants { get; private set; } = [];
    public string[] SuffixVariants { get; private set; } = [];
    public string[] AnchorVariants { get; private set; } = [];
    public string[] TargetUrlVariants { get; private set; } = [];
    public bool Enabled { get; private set; }
    public int Weight { get; private set; }
    public long UsageCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string name, string body, IEnumerable<string>? prefixVariants,
        IEnumerable<string>? suffixVariants, IEnumerable<string>? anchorVariants, IEnumerable<string>? targetUrlVariants,
        bool enabled, int weight, DateTimeOffset now)
    {
        if (weight is < 1 or > 10_000) throw new DomainRuleException("Template weight must be between 1 and 10000.");
        Name = Guard.Required(name, 200, nameof(Name));
        Body = Guard.Required(body, 10_000, nameof(Body));
        PrefixVariants = Variants(prefixVariants, nameof(PrefixVariants), 2_000);
        SuffixVariants = Variants(suffixVariants, nameof(SuffixVariants), 2_000);
        AnchorVariants = Variants(anchorVariants, nameof(AnchorVariants), 500);
        TargetUrlVariants = Variants(targetUrlVariants, nameof(TargetUrlVariants), 2_048);
        Enabled = enabled;
        Weight = weight;
        UpdatedAt = now;
    }

    public void RecordUsage(DateTimeOffset now)
    {
        UsageCount = checked(UsageCount + 1);
        UpdatedAt = now;
    }

    private static string[] Variants(IEnumerable<string>? values, string name, int maximumLength)
    {
        var result = values?.Select(x => Guard.Required(x, maximumLength, name)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        if (result.Length > 100) throw new DomainRuleException($"{name} must contain at most 100 values.");
        return result;
    }
}

public sealed class WordPressSiteProfile
{
    private WordPressSiteProfile() { }

    public WordPressSiteProfile(Guid ownedNetworkProfileId, string domain, string apiBaseUrl,
        string? credentialReference, WordPressSubmissionMode submissionMode, bool enabled, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        OwnedNetworkProfileId = ownedNetworkProfileId;
        Domain = Guard.Required(domain, 253, nameof(Domain)).TrimEnd('.').ToLowerInvariant();
        if (Uri.CheckHostName(Domain) == UriHostNameType.Unknown) throw new DomainRuleException("WordPress site domain is invalid.");
        ApiBaseUrl = Guard.Required(apiBaseUrl, 2_048, nameof(ApiBaseUrl));
        if (!Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var api) ||
            (api.Scheme != Uri.UriSchemeHttps && api.Scheme != Uri.UriSchemeHttp) ||
            !string.Equals(api.IdnHost, Domain, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(api.UserInfo) ||
            !string.IsNullOrEmpty(api.Query) || !string.IsNullOrEmpty(api.Fragment))
            throw new DomainRuleException("WordPress API base URL must use HTTP or HTTPS on the configured domain without user information.");
        CredentialReference = Guard.Optional(credentialReference, 200, nameof(CredentialReference));
        if (submissionMode is WordPressSubmissionMode.DirectApi or WordPressSubmissionMode.AuthenticatedIntegration &&
            CredentialReference is null)
            throw new DomainRuleException("A credential reference is required for direct API and authenticated integration modes.");
        SubmissionMode = submissionMode;
        Enabled = enabled;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; private set; }
    public Guid OwnedNetworkProfileId { get; private set; }
    public string Domain { get; private set; } = null!;
    public string ApiBaseUrl { get; private set; } = null!;
    public string? CredentialReference { get; private set; }
    public WordPressSubmissionMode SubmissionMode { get; private set; }
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(string apiBaseUrl, string? credentialReference, WordPressSubmissionMode submissionMode,
        bool enabled, DateTimeOffset now)
    {
        var candidate = new WordPressSiteProfile(OwnedNetworkProfileId, Domain, apiBaseUrl, credentialReference,
            submissionMode, enabled, now);
        ApiBaseUrl = candidate.ApiBaseUrl;
        CredentialReference = candidate.CredentialReference;
        SubmissionMode = candidate.SubmissionMode;
        Enabled = candidate.Enabled;
        UpdatedAt = now;
    }
}

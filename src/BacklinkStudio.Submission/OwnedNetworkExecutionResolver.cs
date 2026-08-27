using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Submission;

// Replaces the removed OwnedNetworkExecutionAuthorizer. Execution is no longer gated on project
// automation policy, profile ownership state, or a pre-registered domain rule: this type only resolves
// which owned-network profile a source is recorded against, so workflow, validation, expansion,
// submission, preview, and verification always proceed. A source the operator explicitly disabled is
// still refused, because that is a direct instruction rather than an ownership check.
public sealed class OwnedNetworkExecutionResolver(
    IOwnedNetworkRepository networks,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IOwnedNetworkExecutionAuthorizer
{
    public const string ExecutionAuthorizedReason = "execution_authorized";
    public const string SourceDisabledReason = "source_disabled";
    public const string DefaultProfileName = "Default execution network";
    private const int MaximumNetworks = 1_000;
    // Domain-model ceilings, so the default network never lowers a caller's own concurrency settings.
    private const int DefaultMaxConcurrency = 10_000;
    private const int DefaultPerDomainConcurrency = 1_000;
    private readonly Dictionary<Guid, Task<IReadOnlyList<ProfileRules>>> snapshots = [];
    private readonly Dictionary<Guid, OwnedNetworkProfile> defaults = [];

    public async Task<OwnedNetworkExecutionAuthorization> AuthorizeProfileAsync(
        Guid projectId,
        Guid ownedNetworkProfileId,
        CancellationToken cancellationToken)
    {
        var profiles = await GetProfilesAsync(projectId, cancellationToken);
        var requested = profiles
            .Select(value => value.Profile)
            .FirstOrDefault(value => value.Id == ownedNetworkProfileId);
        return Allowed(requested ?? await DefaultProfileAsync(projectId, profiles, cancellationToken));
    }

    public async Task<OwnedNetworkExecutionAuthorization> AuthorizeSourceAsync(
        Guid projectId,
        OwnedNetworkSourceAuthorizationRequest source,
        CancellationToken cancellationToken)
    {
        if (!source.Enabled) return new(false, SourceDisabledReason);
        var profiles = await GetProfilesAsync(projectId, cancellationToken);
        var requested = source.OwnedNetworkProfileId is { } profileId
            ? profiles.Select(value => value.Profile).FirstOrDefault(value => value.Id == profileId)
            : null;
        // Association writes reject an unverified profile in the domain model and in the database
        // check constraints, so an unusable match falls through to the default execution network
        // instead of failing the source.
        var profile = Usable(requested)
            ?? Usable(MatchByRule(profiles, source.Host))
            ?? await DefaultProfileAsync(projectId, profiles, cancellationToken);
        return Allowed(profile);
    }

    private static OwnedNetworkProfile? Usable(OwnedNetworkProfile? profile) =>
        profile is { Enabled: true, OwnershipStatus: not OwnershipStatus.Unverified } ? profile : null;

    private static OwnedNetworkProfile? MatchByRule(IReadOnlyList<ProfileRules> profiles, string host)
    {
        var matches = new List<(OwnedNetworkProfile Profile, OwnedNetworkDomain Rule)>();
        foreach (var entry in profiles.Where(value => value.Profile.Enabled))
        {
            var rule = entry.Rules.Where(value => value.Matches(host))
                .OrderBy(value => value.MatchType == OwnedNetworkDomainMatchType.SubdomainOf ? 1 : 0)
                .ThenBy(value => value.CreatedAt)
                .FirstOrDefault();
            if (rule is not null) matches.Add((entry.Profile, rule));
        }

        return matches
            .OrderBy(value => value.Profile.AllowsAutomaticExecution ? 0 : 1)
            .ThenBy(value => value.Rule.MatchType == OwnedNetworkDomainMatchType.SubdomainOf ? 1 : 0)
            .ThenBy(value => value.Profile.CreatedAt)
            .FirstOrDefault().Profile;
    }

    // Created once per project and persisted immediately: the CSV/TXT import path writes submission
    // sources through raw SQL in its own transaction, so the profile row must already exist.
    private async Task<OwnedNetworkProfile> DefaultProfileAsync(
        Guid projectId,
        IReadOnlyList<ProfileRules> profiles,
        CancellationToken cancellationToken)
    {
        if (defaults.TryGetValue(projectId, out var cached)) return cached;
        var profile = profiles.Select(value => value.Profile)
                .FirstOrDefault(value => value.Name == DefaultProfileName)
            ?? await networks.FindByNameAsync(projectId, DefaultProfileName, cancellationToken);
        if (profile is null)
        {
            var now = timeProvider.GetUtcNow();
            profile = new OwnedNetworkProfile(projectId, DefaultProfileName,
                "Created automatically so sources execute without a pre-registered owned-network domain rule.",
                OwnershipStatus.Owned, true, null, null, null,
                DefaultMaxConcurrency, DefaultPerDomainConcurrency, 0, true, now);
            networks.Add(profile);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        defaults.Add(projectId, profile);
        return profile;
    }

    private Task<IReadOnlyList<ProfileRules>> GetProfilesAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (snapshots.TryGetValue(projectId, out var existing)) return existing;
        var created = LoadProfilesAsync(projectId, cancellationToken);
        snapshots.Add(projectId, created);
        return created;
    }

    private async Task<IReadOnlyList<ProfileRules>> LoadProfilesAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var profiles = await networks.ListAsync(projectId, null, MaximumNetworks, cancellationToken);
        var entries = new List<ProfileRules>(profiles.Count);
        foreach (var profile in profiles)
            entries.Add(new(profile, await networks.ListDomainsAsync(profile.Id, false, cancellationToken)));
        return entries;
    }

    private static OwnedNetworkExecutionAuthorization Allowed(OwnedNetworkProfile profile) =>
        new(true, ExecutionAuthorizedReason, profile);

    private sealed record ProfileRules(
        OwnedNetworkProfile Profile,
        IReadOnlyList<OwnedNetworkDomain> Rules);
}

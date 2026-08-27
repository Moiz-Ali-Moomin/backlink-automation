using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Submission;

public sealed class OwnedNetworkExecutionAuthorizer(
    IPolicyRepository policies,
    IOwnedNetworkRepository networks) : IOwnedNetworkExecutionAuthorizer
{
    public const string OwnershipNotAuthorizedReason = "ownership_not_authorized";
    private const int MaximumNetworks = 1_000;
    private readonly Dictionary<Guid, Task<AuthorizationSnapshot>> snapshots = [];

    public async Task<OwnedNetworkExecutionAuthorization> AuthorizeProfileAsync(
        Guid projectId,
        Guid ownedNetworkProfileId,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(projectId, cancellationToken);
        if (!snapshot.ProjectExecutionAllowed) return Denied("project_automation_not_authorized");
        if (snapshot.NetworkLimitExceeded) return Denied("owned_network_profile_limit_exceeded");
        var profile = snapshot.Profiles
            .Select(value => value.Profile)
            .SingleOrDefault(value => value.Id == ownedNetworkProfileId);
        if (profile is null)
            return Denied("owned_network_profile_not_found");
        return profile.AllowsAutomaticExecution
            ? Allowed(profile)
            : Denied(OwnershipNotAuthorizedReason, profile);
    }

    public async Task<OwnedNetworkExecutionAuthorization> AuthorizeSourceAsync(
        Guid projectId,
        OwnedNetworkSourceAuthorizationRequest source,
        CancellationToken cancellationToken)
    {
        if (!source.Enabled) return Denied("source_disabled");

        var snapshot = await GetSnapshotAsync(projectId, cancellationToken);
        if (!snapshot.ProjectExecutionAllowed) return Denied("project_automation_not_authorized");
        if (snapshot.NetworkLimitExceeded) return Denied("owned_network_profile_limit_exceeded");
        var profiles = source.OwnedNetworkProfileId is { } profileId
            ? snapshot.Profiles.Where(value => value.Profile.Id == profileId)
            : snapshot.Profiles;
        var matches = new List<(OwnedNetworkProfile Profile, OwnedNetworkDomain Rule)>();
        foreach (var entry in profiles.Where(value => value.Profile.Enabled))
        {
            var rule = entry.Rules.Where(value => value.Matches(source.Host))
                .OrderBy(value => value.MatchType == OwnedNetworkDomainMatchType.SubdomainOf ? 1 : 0)
                .ThenBy(value => value.CreatedAt)
                .FirstOrDefault();
            if (rule is not null) matches.Add((entry.Profile, rule));
        }

        var match = matches
            .OrderBy(value => value.Profile.AllowsAutomaticExecution ? 0 : 1)
            .ThenBy(value => value.Rule.MatchType == OwnedNetworkDomainMatchType.SubdomainOf ? 1 : 0)
            .ThenBy(value => value.Profile.CreatedAt)
            .FirstOrDefault();
        if (match.Profile is null) return Denied(OwnershipNotAuthorizedReason);
        return match.Profile.AllowsAutomaticExecution
            ? Allowed(match.Profile)
            : Denied(OwnershipNotAuthorizedReason, match.Profile);
    }

    private Task<AuthorizationSnapshot> GetSnapshotAsync(Guid projectId, CancellationToken cancellationToken)
    {
        if (snapshots.TryGetValue(projectId, out var existing)) return existing;
        var created = LoadSnapshotAsync(projectId, cancellationToken);
        snapshots.Add(projectId, created);
        return created;
    }

    private async Task<AuthorizationSnapshot> LoadSnapshotAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var policy = await policies.GetAsync(projectId, false, cancellationToken);
        if (policy is not { AutomationEnabled: true, ManualReviewRequired: false })
            return new(false, false, []);
        var profiles = await networks.ListAsync(projectId, null, MaximumNetworks + 1, cancellationToken);
        if (profiles.Count > MaximumNetworks) return new(true, true, []);
        var entries = new List<ProfileRules>(profiles.Count);
        foreach (var profile in profiles)
        {
            var rules = await networks.ListDomainsAsync(profile.Id, false, cancellationToken);
            entries.Add(new(profile, rules));
        }
        return new(true, false, entries);
    }

    private static OwnedNetworkExecutionAuthorization Allowed(OwnedNetworkProfile profile) =>
        new(true, false, "normal_ownership_authorization", profile);

    private static OwnedNetworkExecutionAuthorization Denied(
        string reason,
        OwnedNetworkProfile? profile = null) => new(false, false, reason, profile);

    private sealed record AuthorizationSnapshot(
        bool ProjectExecutionAllowed,
        bool NetworkLimitExceeded,
        IReadOnlyList<ProfileRules> Profiles);

    private sealed record ProfileRules(
        OwnedNetworkProfile Profile,
        IReadOnlyList<OwnedNetworkDomain> Rules);
}

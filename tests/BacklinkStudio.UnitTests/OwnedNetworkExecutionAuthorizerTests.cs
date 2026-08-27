using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using BacklinkStudio.Submission;

namespace BacklinkStudio.UnitTests;

public sealed class OwnedNetworkExecutionAuthorizerTests
{
    [Fact]
    public async Task AuthorizedExactHost_ReturnsPersistedProfile()
    {
        var fixture = Fixture(automationEnabled: true);
        var authorizer = new OwnedNetworkExecutionAuthorizer(
            new PolicyRepository(fixture.Policy),
            new NetworkRepository([fixture.Profile], [fixture.Rule]));

        var decision = await authorizer.AuthorizeSourceAsync(fixture.ProjectId,
            new OwnedNetworkSourceAuthorizationRequest("blog.example"), TestContext.Current.CancellationToken);

        Assert.True(decision.Allowed);
        Assert.Equal(fixture.Profile.Id, decision.Profile?.Id);
        Assert.Equal("normal_ownership_authorization", decision.Reason);
    }

    [Fact]
    public async Task HostWithoutPersistedRule_IsNotAuthorized()
    {
        var fixture = Fixture(automationEnabled: true);
        var authorizer = new OwnedNetworkExecutionAuthorizer(
            new PolicyRepository(fixture.Policy),
            new NetworkRepository([fixture.Profile], [fixture.Rule]));

        var decision = await authorizer.AuthorizeSourceAsync(fixture.ProjectId,
            new OwnedNetworkSourceAuthorizationRequest("other.example"), TestContext.Current.CancellationToken);

        Assert.False(decision.Allowed);
        Assert.Null(decision.Profile);
        Assert.Equal(OwnedNetworkExecutionAuthorizer.OwnershipNotAuthorizedReason, decision.Reason);
    }

    [Fact]
    public async Task DisabledProjectAutomation_CannotBeOverriddenByNetworkMatch()
    {
        var fixture = Fixture(automationEnabled: false);
        var authorizer = new OwnedNetworkExecutionAuthorizer(
            new PolicyRepository(fixture.Policy),
            new NetworkRepository([fixture.Profile], [fixture.Rule]));

        var decision = await authorizer.AuthorizeSourceAsync(fixture.ProjectId,
            new OwnedNetworkSourceAuthorizationRequest("blog.example"), TestContext.Current.CancellationToken);

        Assert.False(decision.Allowed);
        Assert.Equal("project_automation_not_authorized", decision.Reason);
    }

    private static AuthorizationFixture Fixture(bool automationEnabled)
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.CreateVersion7();
        var policy = new PolicyDefinition(projectId, now);
        policy.Update(automationEnabled, 0, 100, !automationEnabled, 100, 1_000, 100, now);
        var profile = new OwnedNetworkProfile(projectId, "Authorized network", null, OwnershipStatus.Owned,
            true, null, null, null, 8, 2, 0, true, now);
        var rule = new OwnedNetworkDomain(profile.Id, "blog.example",
            OwnedNetworkDomainMatchType.ExactHost, true, now);
        return new(projectId, policy, profile, rule);
    }

    private sealed record AuthorizationFixture(Guid ProjectId, PolicyDefinition Policy,
        OwnedNetworkProfile Profile, OwnedNetworkDomain Rule);

    private sealed class PolicyRepository(PolicyDefinition policy) : IPolicyRepository
    {
        public Task<PolicyDefinition?> GetAsync(Guid projectId, bool tracked,
            CancellationToken cancellationToken) =>
            Task.FromResult<PolicyDefinition?>(policy.ProjectId == projectId ? policy : null);

        public void Add(PolicyDefinition value) => throw new NotSupportedException();
        public Task<IReadOnlyList<BlocklistEntry>> ListBlocklistAsync(Guid projectId, PageCursor? cursor,
            int take, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BlocklistEntry>>([]);
        public Task<IReadOnlyList<BlocklistEntry>> ListEnabledBlocklistAsync(Guid projectId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BlocklistEntry>>([]);
        public Task<BlocklistEntry?> FindBlocklistAsync(Guid projectId, BlocklistMatchType matchType,
            string value, CancellationToken cancellationToken) => Task.FromResult<BlocklistEntry?>(null);
        public void Add(BlocklistEntry entry) => throw new NotSupportedException();
    }

    private sealed class NetworkRepository(
        IReadOnlyList<OwnedNetworkProfile> profiles,
        IReadOnlyList<OwnedNetworkDomain> rules) : IOwnedNetworkRepository
    {
        public void Add(OwnedNetworkProfile profile) => throw new NotSupportedException();
        public void AddDomain(OwnedNetworkDomain domain) => throw new NotSupportedException();
        public Task<OwnedNetworkProfile?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
            Task.FromResult(profiles.SingleOrDefault(value => value.Id == id));
        public Task<OwnedNetworkProfile?> FindByNameAsync(Guid projectId, string name,
            CancellationToken cancellationToken) => Task.FromResult<OwnedNetworkProfile?>(null);
        public Task<IReadOnlyList<OwnedNetworkProfile>> ListAsync(Guid projectId, PageCursor? cursor, int take,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OwnedNetworkProfile>>(
            profiles.Where(value => value.ProjectId == projectId).Take(take).ToArray());
        public Task<IReadOnlyList<OwnedNetworkDomain>> ListDomainsAsync(Guid ownedNetworkProfileId, bool tracked,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OwnedNetworkDomain>>(
            rules.Where(value => value.OwnedNetworkProfileId == ownedNetworkProfileId).ToArray());
        public void RemoveDomains(IEnumerable<OwnedNetworkDomain> domains) => throw new NotSupportedException();
    }
}

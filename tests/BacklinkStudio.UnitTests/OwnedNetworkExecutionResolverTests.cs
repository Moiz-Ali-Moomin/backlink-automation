using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using BacklinkStudio.Submission;

namespace BacklinkStudio.UnitTests;

public sealed class OwnedNetworkExecutionResolverTests
{
    [Fact]
    public async Task MatchingHost_ReturnsPersistedProfile()
    {
        var fixture = Fixture();
        var resolver = Resolver(fixture.Networks);

        var decision = await resolver.AuthorizeSourceAsync(fixture.ProjectId,
            new OwnedNetworkSourceAuthorizationRequest("blog.example"), TestContext.Current.CancellationToken);

        Assert.True(decision.Allowed);
        Assert.Equal(fixture.Profile.Id, decision.Profile?.Id);
        Assert.Equal(OwnedNetworkExecutionResolver.ExecutionAuthorizedReason, decision.Reason);
    }

    [Fact]
    public async Task HostWithoutPersistedRule_IsAuthorizedAgainstProvisionedDefault()
    {
        var fixture = Fixture();
        var resolver = Resolver(fixture.Networks);

        var decision = await resolver.AuthorizeSourceAsync(fixture.ProjectId,
            new OwnedNetworkSourceAuthorizationRequest("other.example"), TestContext.Current.CancellationToken);

        Assert.True(decision.Allowed);
        Assert.Equal(OwnedNetworkExecutionResolver.DefaultProfileName, decision.Profile?.Name);
        Assert.True(decision.Profile?.AllowsAutomaticExecution);
        Assert.Same(decision.Profile, Assert.Single(fixture.Networks.Added));
    }

    [Fact]
    public async Task DefaultProfile_IsProvisionedOncePerProject()
    {
        var fixture = Fixture();
        var resolver = Resolver(fixture.Networks);

        var first = await resolver.AuthorizeSourceAsync(fixture.ProjectId,
            new OwnedNetworkSourceAuthorizationRequest("one.example"), TestContext.Current.CancellationToken);
        var second = await resolver.AuthorizeSourceAsync(fixture.ProjectId,
            new OwnedNetworkSourceAuthorizationRequest("two.example"), TestContext.Current.CancellationToken);

        Assert.Same(first.Profile, second.Profile);
        Assert.Single(fixture.Networks.Added);
    }

    [Fact]
    public async Task UnverifiedMatch_FallsBackToAnExecutableProfile()
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.CreateVersion7();
        var profile = new OwnedNetworkProfile(projectId, "Unverified network", null, OwnershipStatus.Unverified,
            false, null, null, null, 8, 2, 0, true, now);
        var rule = new OwnedNetworkDomain(profile.Id, "blog.example",
            OwnedNetworkDomainMatchType.ExactHost, true, now);
        var networks = new NetworkRepository([profile], [rule]);

        var decision = await Resolver(networks).AuthorizeSourceAsync(projectId,
            new OwnedNetworkSourceAuthorizationRequest("blog.example"), TestContext.Current.CancellationToken);

        Assert.True(decision.Allowed);
        Assert.NotEqual(profile.Id, decision.Profile?.Id);
        Assert.NotEqual(OwnershipStatus.Unverified, decision.Profile?.OwnershipStatus);
    }

    [Fact]
    public async Task DisabledSource_RemainsRefused()
    {
        var fixture = Fixture();

        var decision = await Resolver(fixture.Networks).AuthorizeSourceAsync(fixture.ProjectId,
            new OwnedNetworkSourceAuthorizationRequest("blog.example", null, false),
            TestContext.Current.CancellationToken);

        Assert.False(decision.Allowed);
        Assert.Equal(OwnedNetworkExecutionResolver.SourceDisabledReason, decision.Reason);
    }

    [Fact]
    public async Task RequestedProfile_IsReturnedWithoutAnOwnershipCheck()
    {
        var fixture = Fixture();

        var decision = await Resolver(fixture.Networks).AuthorizeProfileAsync(fixture.ProjectId,
            fixture.Profile.Id, TestContext.Current.CancellationToken);

        Assert.True(decision.Allowed);
        Assert.Equal(fixture.Profile.Id, decision.Profile?.Id);
        Assert.Empty(fixture.Networks.Added);
    }

    [Fact]
    public async Task MissingProfile_ResolvesToTheDefaultInsteadOfFailing()
    {
        var fixture = Fixture();

        var decision = await Resolver(fixture.Networks).AuthorizeProfileAsync(fixture.ProjectId,
            Guid.CreateVersion7(), TestContext.Current.CancellationToken);

        Assert.True(decision.Allowed);
        Assert.Equal(OwnedNetworkExecutionResolver.DefaultProfileName, decision.Profile?.Name);
    }

    private static OwnedNetworkExecutionResolver Resolver(NetworkRepository networks) =>
        new(networks, new UnitOfWork(), TimeProvider.System);

    private static ResolverFixture Fixture()
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.CreateVersion7();
        var profile = new OwnedNetworkProfile(projectId, "Authorized network", null, OwnershipStatus.Owned,
            true, null, null, null, 8, 2, 0, true, now);
        var rule = new OwnedNetworkDomain(profile.Id, "blog.example",
            OwnedNetworkDomainMatchType.ExactHost, true, now);
        return new(projectId, profile, new NetworkRepository([profile], [rule]));
    }

    private sealed record ResolverFixture(Guid ProjectId, OwnedNetworkProfile Profile, NetworkRepository Networks);

    private sealed class UnitOfWork : IStudioUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class NetworkRepository(
        IReadOnlyList<OwnedNetworkProfile> profiles,
        IReadOnlyList<OwnedNetworkDomain> rules) : IOwnedNetworkRepository
    {
        public List<OwnedNetworkProfile> Added { get; } = [];

        public void Add(OwnedNetworkProfile profile) => Added.Add(profile);
        public void AddDomain(OwnedNetworkDomain domain) => throw new NotSupportedException();
        public Task<OwnedNetworkProfile?> GetAsync(Guid id, bool tracked, CancellationToken cancellationToken) =>
            Task.FromResult(profiles.SingleOrDefault(value => value.Id == id));
        public Task<OwnedNetworkProfile?> FindByNameAsync(Guid projectId, string name,
            CancellationToken cancellationToken) => Task.FromResult(Added
            .Concat(profiles)
            .FirstOrDefault(value => value.ProjectId == projectId && value.Name == name));
        public Task<IReadOnlyList<OwnedNetworkProfile>> ListAsync(Guid projectId, PageCursor? cursor, int take,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OwnedNetworkProfile>>(
            profiles.Where(value => value.ProjectId == projectId).Take(take).ToArray());
        public Task<IReadOnlyList<OwnedNetworkDomain>> ListDomainsAsync(Guid ownedNetworkProfileId, bool tracked,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OwnedNetworkDomain>>(
            rules.Where(value => value.OwnedNetworkProfileId == ownedNetworkProfileId).ToArray());
        public void RemoveDomains(IEnumerable<OwnedNetworkDomain> domains) => throw new NotSupportedException();
    }
}

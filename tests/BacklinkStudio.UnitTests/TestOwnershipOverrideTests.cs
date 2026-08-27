using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using BacklinkStudio.Submission;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BacklinkStudio.UnitTests;

public sealed class TestOwnershipOverrideTests
{
    [Fact]
    public async Task TestEnvironment_Disabled_BlocksUnverifiedSource()
    {
        var (profile, source) = UnverifiedFixture("wordpress-open");
        var authorizer = Authorizer("Test", false, "wordpress-open", Denied(profile));

        Assert.False((await AuthorizeAsync(authorizer, source)).Allowed);
    }

    [Fact]
    public async Task TestEnvironment_Enabled_AllowsExactAllowlistedHost()
    {
        var (profile, source) = UnverifiedFixture("wordpress-open");
        var decision = await AuthorizeAsync(Authorizer("Test", true, "WORDPRESS-OPEN.", Denied(profile)), source);

        Assert.True(decision.Allowed);
        Assert.True(decision.TestOwnershipOverrideApplied);
        Assert.Equal("test_ownership_override", decision.Reason);
    }

    [Fact]
    public async Task TestEnvironment_Enabled_BlocksHostOutsideAllowlist()
    {
        var (profile, source) = UnverifiedFixture("wordpress-moderated");
        var authorizer = Authorizer("Test", true, "wordpress-open", Denied(profile));

        Assert.False((await AuthorizeAsync(authorizer, source)).Allowed);
    }

    [Fact]
    public async Task Production_Disabled_PreservesNormalAuthorization()
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.CreateVersion7();
        var profile = Profile(projectId, OwnershipStatus.Owned, true, now);
        var source = Source(projectId, profile.Id, "wordpress-open", OwnershipStatus.Owned, true, now);
        var expected = new OwnedNetworkExecutionAuthorization(true, false,
            "normal_ownership_authorization", profile);

        var decision = await AuthorizeAsync(Authorizer("Production", false, "wordpress-open", expected), source);

        Assert.True(decision.Allowed);
        Assert.False(decision.TestOwnershipOverrideApplied);
        Assert.Equal("normal_ownership_authorization", decision.Reason);
    }

    [Fact]
    public void Production_Enabled_FailsDuringServiceConfiguration()
    {
        var configuration = Configuration(true, "wordpress-open");
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddBacklinkStudioSubmission(configuration, "Production"));

        Assert.Contains("permitted only in Development or Test", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Production", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wordpress-open.example.com")]
    [InlineData("evilwordpress-open")]
    [InlineData("wordpress-open.attacker.example")]
    public async Task ExactAllowlist_DoesNotMatchRelatedHosts(string sourceHost)
    {
        var (profile, source) = UnverifiedFixture(sourceHost);
        var authorizer = Authorizer("Test", true, "wordpress-open", Denied(profile));

        Assert.False((await AuthorizeAsync(authorizer, source)).Allowed);
    }

    [Fact]
    public async Task EmptyAllowlist_AuthorizesNothing()
    {
        var (profile, source) = UnverifiedFixture("wordpress-open");

        Assert.False((await AuthorizeAsync(
            Authorizer("Test", true, string.Empty, Denied(profile)), source)).Allowed);
    }

    [Fact]
    public async Task OverrideDecision_DoesNotMutatePersistedOwnership()
    {
        var (profile, source) = UnverifiedFixture("wordpress-open");

        var decision = await AuthorizeAsync(
            Authorizer("Development", true, "wordpress-open", Denied(profile)), source);

        Assert.True(decision.Allowed);
        Assert.Equal(OwnershipStatus.Unverified, profile.OwnershipStatus);
        Assert.False(profile.AutomationPermitted);
        Assert.Equal(OwnershipStatus.Unverified, source.OwnershipStatus);
        Assert.False(source.AutomationPermitted);
    }

    [Fact]
    public async Task CommentsClosed_RemainsTechnicallyBlockedWhenOwnershipOverrideApplies()
    {
        var (profile, source) = UnverifiedFixture("wordpress-closed");
        source.ApplyValidation(new(SourcePlatform.WordPress, CmsType.WordPress,
            OpportunityType.WordPressComment, null, TechnicalCompatibility.ManualActionRequired,
            SubmissionSourceValidationStatus.Invalid, "comments_closed", "comments_closed", false, false,
            true, false, false, false, 1, null, null, "Closed", "http://wordpress-closed/?p=1",
            null, 200, "text/html", 100, [], false, null, null, null, null, null, [], false, false,
            null), DateTimeOffset.UtcNow);

        var ownership = await AuthorizeAsync(
            Authorizer("Test", true, "wordpress-closed", Denied(profile)), source);

        Assert.True(ownership.Allowed);
        Assert.False(OwnedNetworkExecutionEligibility.IsTechnicallyExecutable(source));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("10.0.0.0/8")]
    [InlineData("wordpress-open:8080")]
    public void InvalidNonExactAllowlistEntries_FailConfiguration(string host)
    {
        Assert.Throws<InvalidOperationException>(() => Options("Test", true, host));
    }

    private static Task<OwnedNetworkExecutionAuthorization> AuthorizeAsync(
        IOwnedNetworkExecutionAuthorizer authorizer,
        SubmissionSource source) => authorizer.AuthorizeSourceAsync(source.ProjectId, source,
        TestContext.Current.CancellationToken);

    private static TestOwnershipExecutionAuthorizerDecorator Authorizer(
        string environment,
        bool enabled,
        string hosts,
        OwnedNetworkExecutionAuthorization decision) =>
        new TestOwnershipExecutionAuthorizerDecorator(new FixedAuthorizer(decision),
            Options(environment, enabled, hosts));

    private static TestOwnershipOverrideOptions Options(string environment, bool enabled, string hosts) =>
        TestOwnershipOverrideOptions.FromConfiguration(Configuration(enabled, hosts), environment);

    private static IConfiguration Configuration(bool enabled, string hosts) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [TestOwnershipOverrideOptions.EnabledEnvironmentVariable] = enabled.ToString(),
            [TestOwnershipOverrideOptions.AllowedHostsEnvironmentVariable] = hosts
        }).Build();

    private static OwnedNetworkExecutionAuthorization Denied(OwnedNetworkProfile profile) =>
        new(false, false, OwnedNetworkExecutionAuthorizer.OwnershipNotAuthorizedReason, profile);

    private static (OwnedNetworkProfile Profile, SubmissionSource Source) UnverifiedFixture(string host)
    {
        var now = DateTimeOffset.UtcNow;
        var projectId = Guid.CreateVersion7();
        var profile = Profile(projectId, OwnershipStatus.Unverified, false, now);
        var source = Source(projectId, profile.Id, host, OwnershipStatus.Unverified, false, now);
        return (profile, source);
    }

    private static OwnedNetworkProfile Profile(
        Guid projectId,
        OwnershipStatus ownershipStatus,
        bool automationPermitted,
        DateTimeOffset now) => new(projectId, "Test network", null, ownershipStatus, automationPermitted,
            null, null, null, 4, 1, 0, true, now);

    private static SubmissionSource Source(
        Guid projectId,
        Guid profileId,
        string host,
        OwnershipStatus ownershipStatus,
        bool automationPermitted,
        DateTimeOffset now) => new(projectId, profileId, $"http://{host}/?p=1", $"http://{host}/?p=1",
            host, host, ownershipStatus, automationPermitted, null, true, now);

    private sealed class FixedAuthorizer(OwnedNetworkExecutionAuthorization decision)
        : IOwnedNetworkExecutionAuthorizer
    {
        public Task<OwnedNetworkExecutionAuthorization> AuthorizeProfileAsync(Guid projectId,
            Guid ownedNetworkProfileId, CancellationToken cancellationToken) => Task.FromResult(decision);

        public Task<OwnedNetworkExecutionAuthorization> AuthorizeSourceAsync(Guid projectId,
            OwnedNetworkSourceAuthorizationRequest source, CancellationToken cancellationToken) =>
            Task.FromResult(decision);
    }
}

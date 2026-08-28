using System.Net;
using System.Net.Http.Json;

namespace BacklinkStudio.IntegrationTests;

/// <summary>
/// Example fast-path suite: exercises real routing/serialization/persistence
/// through the actual Api host, with authorization, policy scoring, and rate
/// limiting swapped for the pass-through fakes in FastPathWebApplicationFactory.
/// Use this for tests that are asserting behavior OTHER than those three gates.
/// If a test is specifically about ownership resolution, blocklist matching, or
/// throttling, exercise the real implementations instead (see
/// OwnedNetworkExecutionResolverTests, AnalysisRulesTests, and the
/// PostgresWorkflowTests rate-limiter coverage).
/// </summary>
public sealed class FastPathApiTests(FastPathWebApplicationFactory factory) : IClassFixture<FastPathWebApplicationFactory>
{
    [Fact]
    public async Task HealthLive_ReturnsOk()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // Example shape for an endpoint whose route/DTO you're testing without
    // wanting to seed a full owned-network + policy + rate-limit fixture:
    //
    // [Fact]
    // public async Task CreateProject_ReturnsAcceptedDto()
    // {
    //     var client = factory.CreateClient();
    //     var response = await client.PostAsJsonAsync("/api/projects",
    //         new { name = "Fast-path project", domain = "example.test" },
    //         TestContext.Current.CancellationToken);
    //
    //     Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    // }
}

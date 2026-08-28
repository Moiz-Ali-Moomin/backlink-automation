using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.IntegrationTests.TestDoubles;

// ---------------------------------------------------------------------------
// Pass-through test doubles for BacklinkStudio's execution-gating services.
//
// These live in the TEST assembly only and are wired exclusively into
// FastPathWebApplicationFactory (see FastPathWebApplicationFactory.cs).
// They must never be referenced from src/**, Program.cs, or any
// AddBacklinkStudio*() service-extension method — doing so would remove
// authorization, policy/blocklist enforcement, and rate limiting from a real
// host. Their only job here is to let fast-path integration tests assert
// behavior *downstream* of these gates (routing, persistence, DTO shape...)
// without paying for a live authorization/policy/rate-limit round trip.
// ---------------------------------------------------------------------------

/// <summary>
/// Always-allow <see cref="IOwnedNetworkExecutionAuthorizer"/> double. Returns an
/// allowed result backed by a synthetic <see cref="OwnedNetworkProfile"/> so tests
/// that don't care about ownership resolution don't need to seed one.
/// </summary>
internal sealed class AlwaysAllowAuthorizerFake : IOwnedNetworkExecutionAuthorizer
{
    public Task<OwnedNetworkExecutionAuthorization> AuthorizeProfileAsync(
        Guid projectId, Guid ownedNetworkProfileId, CancellationToken cancellationToken) =>
        Task.FromResult(new OwnedNetworkExecutionAuthorization(true,
            "Allowed by AlwaysAllowAuthorizerFake.", MockProfile(projectId)));

    public Task<OwnedNetworkExecutionAuthorization> AuthorizeSourceAsync(
        Guid projectId, OwnedNetworkSourceAuthorizationRequest source, CancellationToken cancellationToken) =>
        Task.FromResult(new OwnedNetworkExecutionAuthorization(true,
            "Allowed by AlwaysAllowAuthorizerFake.", MockProfile(projectId)));

    private static OwnedNetworkProfile MockProfile(Guid projectId) => new(
        projectId,
        name: "fast-path-test-network",
        description: "Synthetic profile from AlwaysAllowAuthorizerFake.",
        OwnershipStatus.Owned,
        automationPermitted: true,
        optionalNetworkTag: null,
        defaultIdentityPoolId: null,
        defaultTemplatePoolId: null,
        maxConcurrency: 10,
        perDomainConcurrency: 5,
        perDomainDelayMilliseconds: 0,
        enabled: true,
        TimeProvider.System.GetUtcNow());
}

/// <summary>
/// <see cref="IPolicyEvaluator"/> double that always returns
/// <see cref="PolicyDecision.Allowed"/>, skipping score-threshold and
/// blocklist evaluation entirely.
/// </summary>
internal sealed class BypassPolicyEvaluatorFake : IPolicyEvaluator
{
    public PolicyEvaluationResult Evaluate(
        PolicyDefinition policy, IReadOnlyCollection<BlocklistEntry> blocklist, PolicyEvaluationContext context) =>
        new(PolicyDecision.Allowed,
            ["Allowed by BypassPolicyEvaluatorFake — score thresholds and blocklists were not evaluated."]);
}

/// <summary>
/// <see cref="IDomainRateLimiter"/> double whose <see cref="WaitAsync"/> resolves
/// immediately, removing per-domain throttling delay from the test run.
/// </summary>
internal sealed class NoOpDomainRateLimiterFake : IDomainRateLimiter
{
    public Task WaitAsync(Guid projectId, Guid? campaignId, string domain,
        CancellationToken cancellationToken, int? minimumDelayMilliseconds = null) =>
        Task.CompletedTask;
}

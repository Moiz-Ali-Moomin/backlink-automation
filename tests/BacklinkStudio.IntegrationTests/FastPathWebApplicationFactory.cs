using BacklinkStudio.Application;
using BacklinkStudio.IntegrationTests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BacklinkStudio.IntegrationTests;

/// <summary>
/// Test host for the fast-path integration suite. Boots the real
/// BacklinkStudio.Api Program via WebApplicationFactory, then swaps
/// IOwnedNetworkExecutionAuthorizer, IPolicyEvaluator, and IDomainRateLimiter
/// for pass-through fakes so tests asserting other behavior (routing,
/// persistence, DTO shape...) don't pay for a live authorization lookup,
/// policy/blocklist scoring, or rate-limit delay.
///
/// Scope: this factory — and only this factory — carries the fakes. Every
/// other test host (BuildProvider() in PostgresWorkflowTests, the real
/// Program.cs entry point, Worker, Cli) keeps the production registrations
/// untouched. Nothing built on this factory should be pointed at a real
/// external target: the ownership check, blocklist enforcement, and rate
/// limiting it bypasses are exactly what make outbound execution safe.
/// </summary>
public sealed class FastPathWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Matches the "Test" environment gate that AddBacklinkStudioSubmission()
        // already enforces (see TestOwnershipOverrideTests) — this factory must
        // never run under "Production".
        builder.UseEnvironment("Test");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOwnedNetworkExecutionAuthorizer>();
            services.AddScoped<IOwnedNetworkExecutionAuthorizer, AlwaysAllowAuthorizerFake>();

            services.RemoveAll<IPolicyEvaluator>();
            services.AddScoped<IPolicyEvaluator, BypassPolicyEvaluatorFake>();

            services.RemoveAll<IDomainRateLimiter>();
            services.AddScoped<IDomainRateLimiter, NoOpDomainRateLimiterFake>();
        });
    }
}

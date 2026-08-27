using System.Net;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using BacklinkStudio.Opportunities;
using BacklinkStudio.Submission;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.UnitTests;

public sealed class SubmissionTests
{
    [Fact]
    public void OwnedNetwork_RequiresVerifiedOwnershipBeforeAutomation()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<DomainRuleException>(() => new OwnedNetworkProfile(
            Guid.CreateVersion7(), "network", null, OwnershipStatus.Unverified, true, null, null, null,
            100, 2, 500, true, now));

        var profile = new OwnedNetworkProfile(
            Guid.CreateVersion7(), "network", null, OwnershipStatus.Owned, true, null, null, null,
            100, 2, 500, true, now);
        Assert.True(profile.AllowsAutomaticExecution);
    }

    [Fact]
    public void OwnedNetworkDomain_MatchesDeterministicallyWithoutRegex()
    {
        var now = DateTimeOffset.UtcNow;
        var exact = new OwnedNetworkDomain(Guid.CreateVersion7(), "Example.COM.", OwnedNetworkDomainMatchType.ExactHost, true, now);
        var subdomain = new OwnedNetworkDomain(Guid.CreateVersion7(), "example.com", OwnedNetworkDomainMatchType.SubdomainOf, true, now);

        Assert.True(exact.Matches("example.com"));
        Assert.False(exact.Matches("blog.example.com"));
        Assert.True(subdomain.Matches("blog.example.com"));
        Assert.False(subdomain.Matches("example.com"));
        Assert.False(subdomain.Matches("notexample.com"));
    }

    [Fact]
    public void SubmissionSource_UnverifiedOwnershipCannotPermitAutomation()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Throws<DomainRuleException>(() => new SubmissionSource(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "https://outside.example/post", "https://outside.example/post",
            "outside.example", "outside.example", OwnershipStatus.Unverified, true, null, true, now));
    }

    [Fact]
    public void ExplicitApproval_AllowsAuthorizedManualCampaign_ButLimitsStillReject()
    {
        var now = DateTimeOffset.UtcNow;
        var policy = new PolicyDefinition(Guid.CreateVersion7(), now);
        var evaluator = new PolicyEvaluator();
        var approved = evaluator.Evaluate(policy, [], new(OpportunityType.OwnedProperty, "https://owned.example/page", "owned.example", 100, 0, CampaignApprovalMode.Manual, true, true, false, 0, 0, 0));
        var limited = evaluator.Evaluate(policy, [], new(OpportunityType.OwnedProperty, "https://owned.example/page", "owned.example", 100, 0, CampaignApprovalMode.Manual, true, true, false, 0, policy.DailyActionLimit, 0));
        Assert.Equal(PolicyDecision.Allowed, approved.Decision);
        Assert.Equal(PolicyDecision.Rejected, limited.Decision);
    }

    [Fact]
    public void SemiAutomaticCampaign_RequiresApprovalUntilProjectAutomationAllowsIt()
    {
        var now = DateTimeOffset.UtcNow;
        var policy = new PolicyDefinition(Guid.CreateVersion7(), now);
        var evaluator = new PolicyEvaluator();
        var pending = evaluator.Evaluate(policy, [], new(OpportunityType.OwnedProperty, "https://owned.example/page", "owned.example", 100, 0, CampaignApprovalMode.SemiAutomatic, true, false, false, 0, 0, 0));
        Assert.Equal(PolicyDecision.ApprovalRequired, pending.Decision);

        policy.Update(true, 60, 30, false, 10, 50, 1, now.AddSeconds(1));
        var automatic = evaluator.Evaluate(policy, [], new(OpportunityType.OwnedProperty, "https://owned.example/page", "owned.example", 100, 0, CampaignApprovalMode.SemiAutomatic, true, false, false, 0, 0, 0));
        Assert.Equal(PolicyDecision.Allowed, automatic.Decision);
    }

    [Fact]
    public void Opportunity_CannotApproveManualActionRequired()
    {
        var now = DateTimeOffset.UtcNow;
        var opportunity = new Opportunity(Guid.CreateVersion7(), Guid.CreateVersion7(), "https://source.example", "source.example", "test", now);
        opportunity.ApplyAnalysis(OpportunityType.ManualOutreach, 80, 5, AutomationStatus.ManualActionRequired, "manual", [], now);
        Assert.Throws<DomainRuleException>(() => opportunity.Approve("agent", now));
    }

    [Fact]
    public void SubmissionJob_PreservesRejectedAsDistinctFromFailed()
    {
        var now = DateTimeOffset.UtcNow;
        var submission = new SubmissionJob(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), now);
        submission.MarkProcessing(now);
        submission.MarkRejected(now.AddSeconds(1));
        Assert.Equal(SubmissionStatus.Rejected, submission.Status);
    }

    [Fact]
    public void Options_RejectNonLoopbackHttpAndDomainMismatch()
    {
        var validator = new SubmissionOptionsValidator();
        var insecure = new SubmissionOptions { Profiles = { ["bad"] = Profile("http://public.example/submit", "public.example") } };
        var mismatch = new SubmissionOptions { Profiles = { ["bad"] = Profile("https://owned.example/submit", "other.example") } };
        var embeddedQuery = new SubmissionOptions { Profiles = { ["bad"] = Profile("https://owned.example/submit?token=secret", "owned.example") } };
        Assert.False(validator.Validate(null, insecure).Succeeded);
        Assert.False(validator.Validate(null, mismatch).Succeeded);
        Assert.False(validator.Validate(null, embeddedQuery).Succeeded);
    }

    [Fact]
    public async Task Adapter_PostsOnlyToConfiguredEndpoint_WithStableIdempotencyKey()
    {
        var handler = new CapturingHandler();
        var options = Options.Create(new SubmissionOptions { AllowInsecureLoopbackHttpForTesting = true, Profiles = { ["local"] = Profile("http://127.0.0.1:43210/owned-submit", "127.0.0.1") } });
        var adapter = new OwnedEndpointSubmissionAdapter(new HttpClient(handler), options, new SubmissionAuthorizationResolver(options));
        var result = await adapter.SubmitAsync(new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "http://127.0.0.1/source", "https://target.example/", "Anchor", "local", "stable-key"), CancellationToken.None);
        Assert.True(result.Accepted);
        Assert.Equal("http://127.0.0.1:43210/owned-submit", handler.RequestUri?.AbsoluteUri);
        Assert.Equal("stable-key", handler.IdempotencyKey);
    }

    private static SubmissionProfileOptions Profile(string endpoint, string domain) => new() { Endpoint = endpoint, SourceDomain = domain, AllowedTypes = [OpportunityType.OwnedProperty] };

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? IdempotencyKey { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            IdempotencyKey = request.Headers.GetValues("Idempotency-Key").Single();
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.Add("X-Submission-Reference", "owned-42");
            return Task.FromResult(response);
        }
    }
}

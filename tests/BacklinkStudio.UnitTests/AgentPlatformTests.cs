using BacklinkStudio.Domain;
using BacklinkStudio.Infrastructure.Security;

namespace BacklinkStudio.UnitTests;

public sealed class AgentPlatformTests
{
    [Fact]
    public void AgentCredential_RequiresScopesFutureExpiryAndSingleRevocation()
    {
        var now = DateTimeOffset.UtcNow;
        var userId = Guid.CreateVersion7();
        Assert.Throws<DomainRuleException>(() => new AgentCredential(userId, "Agent", new string('a', 64), [1], [1], [], null, now));
        Assert.Throws<DomainRuleException>(() => new AgentCredential(userId, "Agent", new string('a', 64), [1], [1], ["projects:read"], now, now));

        var credential = new AgentCredential(userId, "Agent", new string('a', 64), [1], [1], ["projects:read"], now.AddDays(1), now);
        credential.Revoke(now.AddMinutes(1));
        Assert.False(credential.IsActive(now.AddMinutes(1)));
        Assert.Throws<DomainRuleException>(() => credential.Revoke(now.AddMinutes(2)));
    }

    [Fact]
    public void GeneratedApiKey_IsStrongAndVerifiable()
    {
        var hasher = new ApiKeyHasher();
        var material = new AgentApiKeyFactory(hasher).Create();
        Assert.StartsWith("bls_", material.Plaintext, StringComparison.Ordinal);
        Assert.True(material.Plaintext.Length >= 68);
        Assert.True(hasher.Verify(material.Plaintext, material.Hash, material.Salt));
        Assert.False(hasher.Verify(material.Plaintext + "x", material.Hash, material.Salt));
    }

    [Fact]
    public void CampaignLifecycle_ControlsPersistentWork()
    {
        var now = DateTimeOffset.UtcNow;
        var campaign = new Campaign(Guid.CreateVersion7(), "Agent campaign", CampaignApprovalMode.SemiAutomatic, "owned", "authorized", 10, now);
        campaign.Update("Updated", CampaignApprovalMode.Manual, 5, now.AddSeconds(1));
        campaign.Start(now.AddSeconds(2));
        campaign.Start(now.AddSeconds(3));
        Assert.Equal(CampaignStatus.Running, campaign.Status);
        campaign.Pause(now.AddSeconds(3));
        Assert.Equal(CampaignStatus.Paused, campaign.Status);
        Assert.Throws<DomainRuleException>(() => campaign.Start(now.AddSeconds(4)));
        campaign.Resume(now.AddSeconds(4));
        campaign.Stop(now.AddSeconds(5));
        Assert.Equal(CampaignStatus.Stopped, campaign.Status);

        var job = new PersistentJob(JobType.Submission, campaign.ProjectId, campaign.Id, "{}", 0, now, 3, "correlation", "idempotency", now);
        job.Cancel(now.AddSeconds(1));
        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.NotNull(job.CompletedAt);
    }
}

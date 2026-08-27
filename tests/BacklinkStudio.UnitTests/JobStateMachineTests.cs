using BacklinkStudio.Domain;

namespace BacklinkStudio.UnitTests;

public sealed class JobStateMachineTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Job_FollowsSuccessfulLifecycle()
    {
        var job = CreateJob();
        job.Claim("worker-1", Now, TimeSpan.FromMinutes(5));
        job.Start("worker-1", Now.AddSeconds(1), TimeSpan.FromMinutes(5));
        job.Succeed("worker-1", Now.AddSeconds(2));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(1, job.AttemptCount);
        Assert.NotNull(job.CompletedAt);
        Assert.Null(job.ClaimExpiresAt);
    }

    [Fact]
    public void Job_CanBeRecoveredAfterLeaseExpiry()
    {
        var job = CreateJob();
        job.Claim("dead-worker", Now, TimeSpan.FromSeconds(30));
        job.Start("dead-worker", Now, TimeSpan.FromSeconds(30));
        job.Claim("replacement", Now.AddSeconds(31), TimeSpan.FromMinutes(5));

        Assert.Equal(JobStatus.Claimed, job.Status);
        Assert.Equal("replacement", job.WorkerId);
        Assert.Equal(2, job.AttemptCount);
    }

    [Fact]
    public void Job_RejectsWrongWorkerAndInvalidTransition()
    {
        var job = CreateJob();
        Assert.Throws<DomainRuleException>(() => job.Start("worker", Now, TimeSpan.FromMinutes(1)));
        job.Claim("worker", Now, TimeSpan.FromMinutes(1));
        Assert.Throws<DomainRuleException>(() => job.Start("other", Now, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void RetryableFailure_StopsAtMaximumAttempts()
    {
        var job = CreateJob(maxAttempts: 1);
        job.Claim("worker", Now, TimeSpan.FromMinutes(1));
        job.Start("worker", Now, TimeSpan.FromMinutes(1));
        job.Fail("worker", "transient", true, JobFailureKind.Transient, Now, TimeSpan.FromSeconds(2));
        Assert.Equal(JobStatus.DeadLetter, job.Status);
    }

    [Fact]
    public void ActiveJob_PauseIsAcknowledgedAndCanResume()
    {
        var job = CreateJob();
        job.Claim("worker", Now, TimeSpan.FromMinutes(1));
        job.Start("worker", Now, TimeSpan.FromMinutes(1));
        job.RequestPause(Now.AddSeconds(1));

        Assert.NotNull(job.PauseRequestedAt);
        job.AcknowledgePause("worker", Now.AddSeconds(2));
        Assert.Equal(JobStatus.Paused, job.Status);
        Assert.Null(job.WorkerId);
        Assert.Null(job.PauseRequestedAt);

        job.Resume(Now.AddSeconds(3));
        Assert.Equal(JobStatus.RetryScheduled, job.Status);
    }

    [Fact]
    public void ActiveJob_CancellationAcknowledgementIsTerminal()
    {
        var job = CreateJob();
        job.Claim("worker", Now, TimeSpan.FromMinutes(1));
        job.Start("worker", Now, TimeSpan.FromMinutes(1));
        job.Cancel(Now.AddSeconds(1));

        job.AcknowledgeCancellation("worker", Now.AddSeconds(2));

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Equal(Now.AddSeconds(2), job.CompletedAt);
        Assert.Null(job.WorkerId);
        Assert.Null(job.PauseRequestedAt);
    }

    [Fact]
    public void DeadLetterJob_CanBeExplicitlyRedriven()
    {
        var job = CreateJob(maxAttempts: 1);
        job.Claim("worker", Now, TimeSpan.FromMinutes(1));
        job.Start("worker", Now, TimeSpan.FromMinutes(1));
        job.Fail("worker", "transient", true, JobFailureKind.Transient, Now, TimeSpan.Zero);

        job.Redrive(Now.AddSeconds(1));

        Assert.Equal(JobStatus.RetryScheduled, job.Status);
        Assert.Equal(0, job.AttemptCount);
        Assert.Null(job.CompletedAt);
    }

    [Fact]
    public void LeaseRenewal_RecordsWorkerHeartbeat()
    {
        var job = CreateJob();
        job.Claim("worker", Now, TimeSpan.FromSeconds(30));
        job.RenewLease("worker", Now.AddSeconds(10), TimeSpan.FromSeconds(30));

        Assert.Equal(Now.AddSeconds(10), job.LastHeartbeatAt);
        Assert.Equal(Now.AddSeconds(40), job.ClaimExpiresAt);
    }

    [Fact]
    public void ExpiredLease_CannotRenewOrCompleteAsStaleOwner()
    {
        var job = CreateJob();
        job.Claim("stale-worker", Now, TimeSpan.FromSeconds(30));
        job.Start("stale-worker", Now.AddSeconds(1), TimeSpan.FromSeconds(30));
        var afterExpiry = Now.AddSeconds(32);

        Assert.Throws<DomainRuleException>(() => job.RenewLease("stale-worker", afterExpiry, TimeSpan.FromSeconds(30)));
        Assert.Throws<DomainRuleException>(() => job.Succeed("stale-worker", afterExpiry));
        Assert.Equal(JobStatus.Running, job.Status);
    }

    private static PersistentJob CreateJob(int maxAttempts = 3) =>
        new(JobType.Analysis, Guid.CreateVersion7(Now), null, "{\"version\":1}", 0, Now, maxAttempts, "correlation", "idempotency", Now);
}

namespace BacklinkStudio.Domain;

public enum JobType
{
    Discovery,
    Analysis,
    SubmissionSourceImport,
    SubmissionSourceValidation,
    OwnedNetworkWorkflow,
    BacklinkWorkflow,
    OwnedNetworkCampaignExpansion,
    Submission,
    Verification,
    Monitoring,
    Report,
    Maintenance
}

public enum JobStatus
{
    Queued,
    Claimed,
    Running,
    Paused,
    Succeeded,
    Failed,
    RetryScheduled,
    Cancelled,
    DeadLetter
}

public enum JobFailureKind
{
    Transient,
    RateLimited,
    Timeout,
    DependencyUnavailable,
    InvalidInput,
    PolicyRejected,
    AuthorizationDenied,
    Unsupported,
    Unknown
}

public sealed class PersistentJob
{
    private PersistentJob() { }

    public PersistentJob(
        JobType type,
        Guid projectId,
        Guid? campaignId,
        string payload,
        int priority,
        DateTimeOffset availableAt,
        int maxAttempts,
        string correlationId,
        string idempotencyKey,
        DateTimeOffset now,
        string? domain = null)
    {
        if (priority is < -100 or > 100)
        {
            throw new DomainRuleException("Job priority must be between -100 and 100.");
        }

        if (maxAttempts is < 1 or > 20)
        {
            throw new DomainRuleException("Max attempts must be between 1 and 20.");
        }

        Id = Guid.CreateVersion7(now);
        Type = type;
        ProjectId = projectId;
        CampaignId = campaignId;
        Status = JobStatus.Queued;
        Payload = Guard.Required(payload, 64 * 1_024, nameof(Payload));
        Priority = priority;
        CreatedAt = now;
        AvailableAt = availableAt;
        MaxAttempts = maxAttempts;
        CorrelationId = Guard.Required(correlationId, 100, nameof(CorrelationId));
        IdempotencyKey = Guard.Required(idempotencyKey, 200, nameof(IdempotencyKey));
        Domain = Guard.Optional(domain?.Trim().ToLowerInvariant(), 253, nameof(Domain));
    }

    public Guid Id { get; private set; }
    public JobType Type { get; private set; }
    public Guid ProjectId { get; private set; }
    public Guid? CampaignId { get; private set; }
    public JobStatus Status { get; private set; }
    public int Priority { get; private set; }
    public string Payload { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset AvailableAt { get; private set; }
    public DateTimeOffset? ClaimedAt { get; private set; }
    public DateTimeOffset? ClaimExpiresAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? WorkerId { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public string? LastError { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string? Domain { get; private set; }
    public DateTimeOffset? LastHeartbeatAt { get; private set; }
    public DateTimeOffset? PauseRequestedAt { get; private set; }
    public DateTimeOffset? PausedAt { get; private set; }
    public JobFailureKind? LastFailureKind { get; private set; }
    public int RecoveryCount { get; private set; }

    public void Claim(string workerId, DateTimeOffset now, TimeSpan lease)
    {
        var available = (Status is JobStatus.Queued or JobStatus.RetryScheduled) && AvailableAt <= now;
        var recoverable = (Status is JobStatus.Claimed or JobStatus.Running) && ClaimExpiresAt <= now && PauseRequestedAt is null;
        if (!available && !recoverable)
        {
            throw new DomainRuleException($"Job in state {Status} cannot be claimed.");
        }

        if (AttemptCount >= MaxAttempts)
        {
            throw new DomainRuleException("Job has exhausted its attempts.");
        }

        if (recoverable)
        {
            RecoveryCount++;
            LastError = "Recovered after an expired worker claim.";
            LastFailureKind = JobFailureKind.Transient;
        }

        Status = JobStatus.Claimed;
        WorkerId = Guard.Required(workerId, 200, nameof(workerId));
        ClaimedAt = now;
        ClaimExpiresAt = now.Add(lease);
        LastHeartbeatAt = now;
        PausedAt = null;
        AttemptCount++;
    }

    public void Start(string workerId, DateTimeOffset now, TimeSpan lease)
    {
        RequireActiveLease(JobStatus.Claimed, workerId, now);
        Status = JobStatus.Running;
        StartedAt ??= now;
        ClaimExpiresAt = now.Add(lease);
        LastHeartbeatAt = now;
    }

    public void Succeed(string workerId, DateTimeOffset now)
    {
        RequireActiveLease(JobStatus.Running, workerId, now, allowPauseRequested: false);
        Status = JobStatus.Succeeded;
        CompletedAt = now;
        ClaimExpiresAt = null;
        PauseRequestedAt = null;
        LastError = null;
        LastFailureKind = null;
    }

    public void Fail(string workerId, string error, bool retryable, JobFailureKind failureKind, DateTimeOffset now, TimeSpan retryDelay)
    {
        RequireActiveLease(JobStatus.Running, workerId, now, allowPauseRequested: false);
        LastError = Guard.Required(error, 4_000, nameof(error));
        LastFailureKind = failureKind;
        WorkerId = null;
        ClaimExpiresAt = null;
        PauseRequestedAt = null;

        if (retryable && AttemptCount < MaxAttempts)
        {
            Status = JobStatus.RetryScheduled;
            AvailableAt = now.Add(retryDelay);
            return;
        }

        Status = retryable ? JobStatus.DeadLetter : JobStatus.Failed;
        CompletedAt = now;
    }

    public void Release(string workerId, DateTimeOffset now)
    {
        if (Status is not (JobStatus.Claimed or JobStatus.Running)
            || !string.Equals(WorkerId, workerId, StringComparison.Ordinal)
            || ClaimExpiresAt is null
            || ClaimExpiresAt <= now)
        {
            throw new DomainRuleException("Only the owning worker with an active lease can release a job.");
        }

        if (PauseRequestedAt is not null)
        {
            Status = JobStatus.Paused;
            PausedAt = now;
            PauseRequestedAt = null;
        }
        else
        {
            Status = JobStatus.RetryScheduled;
            AvailableAt = now;
        }
        WorkerId = null;
        ClaimExpiresAt = null;
        LastHeartbeatAt = now;
    }

    public void RenewLease(string workerId, DateTimeOffset now, TimeSpan lease)
    {
        if (Status is not (JobStatus.Claimed or JobStatus.Running)
            || !string.Equals(WorkerId, workerId, StringComparison.Ordinal)
            || ClaimExpiresAt is null
            || ClaimExpiresAt <= now)
        {
            throw new DomainRuleException("Only the owning worker with an active lease can renew a job.");
        }

        LastHeartbeatAt = now;
        ClaimExpiresAt = now.Add(lease);
    }

    public void RequestPause(DateTimeOffset now)
    {
        if (Status is JobStatus.Queued or JobStatus.RetryScheduled)
        {
            Status = JobStatus.Paused;
            PausedAt = now;
            PauseRequestedAt = null;
            return;
        }

        if (Status is JobStatus.Claimed or JobStatus.Running)
        {
            PauseRequestedAt ??= now;
            return;
        }

        if (Status == JobStatus.Paused)
        {
            return;
        }

        throw new DomainRuleException($"Job in state {Status} cannot be paused.");
    }

    public void AcknowledgePause(string workerId, DateTimeOffset now)
    {
        if (Status is not (JobStatus.Claimed or JobStatus.Running)
            || PauseRequestedAt is null
            || !string.Equals(WorkerId, workerId, StringComparison.Ordinal)
            || ClaimExpiresAt is null
            || ClaimExpiresAt <= now)
        {
            throw new DomainRuleException("The owning worker with an active lease can acknowledge only a requested pause.");
        }

        Status = JobStatus.Paused;
        PausedAt = now;
        PauseRequestedAt = null;
        WorkerId = null;
        ClaimExpiresAt = null;
        LastHeartbeatAt = now;
    }

    public void AcknowledgeCancellation(string workerId, DateTimeOffset now)
    {
        if (Status is not (JobStatus.Claimed or JobStatus.Running)
            || PauseRequestedAt is null
            || !string.Equals(WorkerId, workerId, StringComparison.Ordinal)
            || ClaimExpiresAt is null
            || ClaimExpiresAt <= now)
        {
            throw new DomainRuleException("The owning worker with an active lease can acknowledge only a requested cancellation.");
        }

        Status = JobStatus.Cancelled;
        CompletedAt = now;
        PausedAt = null;
        PauseRequestedAt = null;
        WorkerId = null;
        ClaimExpiresAt = null;
        LastHeartbeatAt = now;
    }

    public void Resume(DateTimeOffset now)
    {
        if (Status is JobStatus.Claimed or JobStatus.Running)
        {
            if (PauseRequestedAt is null)
            {
                throw new DomainRuleException("The active job has no pending pause request.");
            }

            PauseRequestedAt = null;
            return;
        }

        if (Status != JobStatus.Paused)
        {
            throw new DomainRuleException($"Job in state {Status} cannot be resumed.");
        }

        Status = JobStatus.RetryScheduled;
        AvailableAt = now;
        PauseRequestedAt = null;
        PausedAt = null;
    }

    public void Redrive(DateTimeOffset now)
    {
        if (Status is not (JobStatus.DeadLetter or JobStatus.Failed))
        {
            throw new DomainRuleException("Only failed or dead-letter jobs can be redriven.");
        }

        Status = JobStatus.RetryScheduled;
        AvailableAt = now;
        CompletedAt = null;
        WorkerId = null;
        ClaimExpiresAt = null;
        PauseRequestedAt = null;
        PausedAt = null;
        AttemptCount = 0;
    }

    public void Cancel(DateTimeOffset now)
    {
        if (Status is JobStatus.Queued or JobStatus.RetryScheduled or JobStatus.Paused)
        {
            Status = JobStatus.Cancelled;
            CompletedAt = now;
            WorkerId = null;
            ClaimExpiresAt = null;
            PauseRequestedAt = null;
            PausedAt = null;
            return;
        }

        if (Status is JobStatus.Claimed or JobStatus.Running)
        {
            PauseRequestedAt ??= now;
            return;
        }

        if (Status == JobStatus.Cancelled)
        {
            return;
        }

        throw new DomainRuleException($"Job in state {Status} cannot be cancelled.");
    }

    private void RequireActiveLease(JobStatus expected, string workerId, DateTimeOffset now, bool allowPauseRequested = true)
    {
        if (Status != expected
            || !string.Equals(WorkerId, workerId, StringComparison.Ordinal)
            || ClaimExpiresAt is null
            || ClaimExpiresAt <= now
            || (!allowPauseRequested && PauseRequestedAt is not null))
        {
            throw new DomainRuleException($"Job must be {expected} and owned by this worker with an active lease.");
        }
    }
}

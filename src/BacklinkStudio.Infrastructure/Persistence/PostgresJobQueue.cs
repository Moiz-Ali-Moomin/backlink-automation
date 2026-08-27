using System.Data;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BacklinkStudio.Infrastructure.Persistence;

public sealed class PostgresJobQueue(BacklinkStudioDbContext dbContext) : IJobQueue
{
    public Task<PersistentJob> EnqueueAsync(PersistentJob job, CancellationToken cancellationToken)
    {
        dbContext.Jobs.Add(job);
        return Task.FromResult(job);
    }

    public Task<PersistentJob?> FindByIdempotencyAsync(Guid projectId, JobType jobType, string idempotencyKey, CancellationToken cancellationToken) =>
        dbContext.Jobs.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Type == jobType && x.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<JobCorrelationSummary> GetCorrelationSummaryAsync(Guid projectId, JobType jobType,
        string correlationId, CancellationToken cancellationToken)
    {
        var result = await dbContext.Jobs.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Type == jobType && x.CorrelationId == correlationId)
            .GroupBy(_ => 1)
            .Select(group => new JobCorrelationSummary(
                group.Count(x => x.Status == JobStatus.Queued || x.Status == JobStatus.Claimed ||
                    x.Status == JobStatus.Running || x.Status == JobStatus.RetryScheduled || x.Status == JobStatus.Paused),
                group.Count(x => x.Status == JobStatus.Failed || x.Status == JobStatus.DeadLetter ||
                    x.Status == JobStatus.Cancelled)))
            .SingleOrDefaultAsync(cancellationToken);
        return result ?? new(0, 0);
    }

    public async Task<PersistentJob?> ClaimAsync(string workerId, TimeSpan lease, JobClaimLimits limits, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await using (var jit = connection.CreateCommand())
        {
            jit.Transaction = transaction;
            jit.CommandText = "SET LOCAL jit = off;";
            await jit.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var claimLock = connection.CreateCommand())
        {
            claimLock.Transaction = transaction;
            claimLock.CommandText = "SELECT pg_advisory_xact_lock(2026081804);";
            await claimLock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH paused_expired AS (
                UPDATE jobs AS paused_job
                SET status = CASE WHEN EXISTS (
                        SELECT 1 FROM campaigns AS stopped_campaign
                        WHERE stopped_campaign.id = paused_job.campaign_id AND stopped_campaign.status = 'Stopped'
                    ) THEN 'Cancelled' ELSE 'Paused' END,
                    worker_id = NULL,
                    claim_expires_at = NULL,
                    pause_requested_at = NULL,
                    paused_at = CASE WHEN EXISTS (
                        SELECT 1 FROM campaigns AS stopped_campaign
                        WHERE stopped_campaign.id = paused_job.campaign_id AND stopped_campaign.status = 'Stopped'
                    ) THEN NULL ELSE @now END,
                    completed_at = CASE WHEN EXISTS (
                        SELECT 1 FROM campaigns AS stopped_campaign
                        WHERE stopped_campaign.id = paused_job.campaign_id AND stopped_campaign.status = 'Stopped'
                    ) THEN @now ELSE completed_at END,
                    last_heartbeat_at = @now
                WHERE status IN ('Claimed', 'Running')
                  AND claim_expires_at <= @now
                  AND pause_requested_at IS NOT NULL
            ),
            exhausted AS (
                UPDATE jobs
                SET status = 'DeadLetter',
                    worker_id = NULL,
                    claim_expires_at = NULL,
                    completed_at = @now,
                    last_failure_kind = COALESCE(last_failure_kind, 'Transient'),
                    last_error = COALESCE(last_error, 'Job exhausted its attempts after an expired worker claim.')
                WHERE status IN ('Claimed', 'Running')
                  AND claim_expires_at <= @now
                  AND pause_requested_at IS NULL
                  AND attempt_count >= max_attempts
            ),
            active AS MATERIALIZED (
                SELECT project_id, campaign_id, domain
                FROM jobs
                WHERE status IN ('Claimed', 'Running')
                  AND claim_expires_at > @now
            ),
            caps AS MATERIALIZED (
                SELECT
                    (SELECT COUNT(*) FROM active) AS global_active,
                    ARRAY(SELECT project_id FROM active GROUP BY project_id HAVING COUNT(*) >= @project_concurrency) AS full_projects,
                    ARRAY(SELECT campaign_id FROM active WHERE campaign_id IS NOT NULL GROUP BY campaign_id HAVING COUNT(*) >= @campaign_concurrency) AS full_campaigns,
                    ARRAY(SELECT domain FROM active WHERE domain IS NOT NULL GROUP BY domain HAVING COUNT(*) >= @domain_concurrency) AS full_domains
            ),
            ready AS (
                SELECT job.id, job.priority, job.available_at, job.created_at
                FROM jobs AS job
                WHERE job.status IN ('Queued', 'RetryScheduled')
                  AND job.pause_requested_at IS NULL
                  AND job.available_at <= @now
                  AND job.attempt_count < job.max_attempts
                  AND (SELECT global_active FROM caps) < @global_concurrency
                  AND array_position((SELECT full_projects FROM caps), job.project_id) IS NULL
                  AND (job.campaign_id IS NULL OR array_position((SELECT full_campaigns FROM caps), job.campaign_id) IS NULL)
                  AND (job.domain IS NULL OR array_position((SELECT full_domains FROM caps), job.domain) IS NULL)
                  AND NOT EXISTS (
                    SELECT 1
                    FROM owned_network_campaign_configurations AS owned_config
                    WHERE owned_config.campaign_id = job.campaign_id
                      AND (
                        (SELECT COUNT(*) FROM active owned_active WHERE owned_active.campaign_id = job.campaign_id) >= LEAST(@campaign_concurrency, owned_config.global_concurrency)
                        OR (
                          job.domain IS NOT NULL
                          AND (SELECT COUNT(*) FROM active owned_domain_active WHERE owned_domain_active.campaign_id = job.campaign_id AND owned_domain_active.domain = job.domain) >= LEAST(@domain_concurrency, owned_config.per_domain_concurrency)
                        )
                      )
                  )
                  AND (
                    job.type <> 'Submission'
                    OR (
                        job.domain IS NOT NULL
                        AND EXISTS (
                            SELECT 1
                            FROM policy_definitions AS policy
                            JOIN campaigns AS campaign ON campaign.id = job.campaign_id
                            WHERE policy.project_id = job.project_id
                              AND policy.hourly_action_limit > (SELECT COUNT(*) FROM jobs completed WHERE completed.type = 'Submission' AND completed.status = 'Succeeded' AND completed.project_id = job.project_id AND completed.campaign_id IS NOT DISTINCT FROM job.campaign_id AND completed.completed_at >= @now - INTERVAL '1 hour')
                              AND policy.daily_action_limit > (SELECT COUNT(*) FROM jobs completed WHERE completed.type = 'Submission' AND completed.status = 'Succeeded' AND completed.project_id = job.project_id AND completed.campaign_id IS NOT DISTINCT FROM job.campaign_id AND completed.completed_at >= @now - INTERVAL '24 hours')
                              AND policy.per_domain_action_limit > (SELECT COUNT(*) FROM jobs completed WHERE completed.type = 'Submission' AND completed.status = 'Succeeded' AND completed.project_id = job.project_id AND completed.domain = job.domain AND completed.completed_at >= @now - INTERVAL '24 hours')
                              AND campaign.daily_action_limit > (SELECT COUNT(*) FROM jobs completed WHERE completed.type = 'Submission' AND completed.status = 'Succeeded' AND completed.campaign_id = job.campaign_id AND completed.completed_at >= @now - INTERVAL '24 hours')
                        )
                    )
                  )
                ORDER BY job.priority DESC, job.available_at, job.created_at
                FOR UPDATE OF job SKIP LOCKED
                LIMIT 1
            ),
            expired AS (
                SELECT job.id, job.priority, job.available_at, job.created_at
                FROM jobs AS job
                WHERE job.status IN ('Claimed', 'Running')
                  AND job.claim_expires_at <= @now
                  AND job.pause_requested_at IS NULL
                  AND job.attempt_count < job.max_attempts
                  AND (SELECT global_active FROM caps) < @global_concurrency
                  AND array_position((SELECT full_projects FROM caps), job.project_id) IS NULL
                  AND (job.campaign_id IS NULL OR array_position((SELECT full_campaigns FROM caps), job.campaign_id) IS NULL)
                  AND (job.domain IS NULL OR array_position((SELECT full_domains FROM caps), job.domain) IS NULL)
                  AND NOT EXISTS (
                    SELECT 1
                    FROM owned_network_campaign_configurations AS owned_config
                    WHERE owned_config.campaign_id = job.campaign_id
                      AND (
                        (SELECT COUNT(*) FROM active owned_active WHERE owned_active.campaign_id = job.campaign_id) >= LEAST(@campaign_concurrency, owned_config.global_concurrency)
                        OR (
                          job.domain IS NOT NULL
                          AND (SELECT COUNT(*) FROM active owned_domain_active WHERE owned_domain_active.campaign_id = job.campaign_id AND owned_domain_active.domain = job.domain) >= LEAST(@domain_concurrency, owned_config.per_domain_concurrency)
                        )
                      )
                  )
                  AND (
                    job.type <> 'Submission'
                    OR (
                        job.domain IS NOT NULL
                        AND EXISTS (
                            SELECT 1
                            FROM policy_definitions AS policy
                            JOIN campaigns AS campaign ON campaign.id = job.campaign_id
                            WHERE policy.project_id = job.project_id
                              AND policy.hourly_action_limit > (SELECT COUNT(*) FROM jobs completed WHERE completed.type = 'Submission' AND completed.status = 'Succeeded' AND completed.project_id = job.project_id AND completed.campaign_id IS NOT DISTINCT FROM job.campaign_id AND completed.completed_at >= @now - INTERVAL '1 hour')
                              AND policy.daily_action_limit > (SELECT COUNT(*) FROM jobs completed WHERE completed.type = 'Submission' AND completed.status = 'Succeeded' AND completed.project_id = job.project_id AND completed.campaign_id IS NOT DISTINCT FROM job.campaign_id AND completed.completed_at >= @now - INTERVAL '24 hours')
                              AND policy.per_domain_action_limit > (SELECT COUNT(*) FROM jobs completed WHERE completed.type = 'Submission' AND completed.status = 'Succeeded' AND completed.project_id = job.project_id AND completed.domain = job.domain AND completed.completed_at >= @now - INTERVAL '24 hours')
                              AND campaign.daily_action_limit > (SELECT COUNT(*) FROM jobs completed WHERE completed.type = 'Submission' AND completed.status = 'Succeeded' AND completed.campaign_id = job.campaign_id AND completed.completed_at >= @now - INTERVAL '24 hours')
                        )
                    )
                  )
                ORDER BY job.priority DESC, job.available_at, job.created_at
                FOR UPDATE OF job SKIP LOCKED
                LIMIT 1
            ),
            candidate AS (
                SELECT id
                FROM (SELECT * FROM ready UNION ALL SELECT * FROM expired) AS eligible
                ORDER BY priority DESC, available_at, created_at
                LIMIT 1
            )
            UPDATE jobs AS job
            SET status = 'Claimed',
                worker_id = @worker_id,
                claimed_at = @now,
                claim_expires_at = @claim_expires_at,
                last_heartbeat_at = @now,
                attempt_count = job.attempt_count + 1,
                recovery_count = job.recovery_count + CASE WHEN job.status IN ('Claimed', 'Running') THEN 1 ELSE 0 END,
                last_failure_kind = CASE WHEN job.status IN ('Claimed', 'Running') THEN 'Transient' ELSE job.last_failure_kind END,
                last_error = CASE WHEN job.status IN ('Claimed', 'Running') THEN 'Recovered after an expired worker claim.' ELSE job.last_error END
            FROM candidate
            WHERE job.id = candidate.id
            RETURNING job.id;
            """;
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("worker_id", workerId);
        command.Parameters.AddWithValue("claim_expires_at", now.Add(lease));
        command.Parameters.AddWithValue("global_concurrency", limits.GlobalConcurrency);
        command.Parameters.AddWithValue("project_concurrency", limits.PerProjectConcurrency);
        command.Parameters.AddWithValue("campaign_concurrency", limits.PerCampaignConcurrency);
        command.Parameters.AddWithValue("domain_concurrency", limits.PerDomainConcurrency);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not Guid jobId)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        await transaction.CommitAsync(cancellationToken);
        return await dbContext.Jobs.AsNoTracking().SingleAsync(x => x.Id == jobId, cancellationToken);
    }

    public async Task MarkRunningAsync(Guid jobId, string workerId, TimeSpan lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var job = await RequiredAsync(jobId, cancellationToken);
        job.Start(workerId, now, lease);
        await SaveAndClearAsync(cancellationToken);
    }

    public async Task MarkSucceededAsync(Guid jobId, string workerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var job = await RequiredAsync(jobId, cancellationToken);
        job.Succeed(workerId, now);
        await SaveAndClearAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid jobId, string workerId, string errorMessage, JobFailureDecision failure, DateTimeOffset now, TimeSpan retryDelay, CancellationToken cancellationToken)
    {
        var job = await RequiredAsync(jobId, cancellationToken);
        job.Fail(workerId, errorMessage, failure.Retryable, failure.Kind, now, retryDelay);
        await SaveAndClearAsync(cancellationToken);
    }

    public async Task<JobLeaseRenewal> RenewLeaseAsync(Guid jobId, string workerId, TimeSpan lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE jobs
            SET claim_expires_at = @claim_expires_at,
                last_heartbeat_at = @now
            WHERE id = @job_id
              AND worker_id = @worker_id
              AND status IN ('Claimed', 'Running')
              AND claim_expires_at > @now
            RETURNING pause_requested_at IS NOT NULL;
            """;
        command.Parameters.AddWithValue("job_id", jobId);
        command.Parameters.AddWithValue("worker_id", workerId);
        command.Parameters.AddWithValue("claim_expires_at", now.Add(lease));
        command.Parameters.AddWithValue("now", now);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null)
        {
            return JobLeaseRenewal.Lost;
        }

        return result is true ? JobLeaseRenewal.PauseRequested : JobLeaseRenewal.Renewed;
    }

    public async Task AcknowledgePauseAsync(Guid jobId, string workerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var job = await RequiredAsync(jobId, cancellationToken);
        var stoppedCampaign = job.CampaignId is { } campaignId && await dbContext.Campaigns.AsNoTracking()
            .AnyAsync(x => x.Id == campaignId && x.Status == CampaignStatus.Stopped, cancellationToken);
        if (stoppedCampaign) job.AcknowledgeCancellation(workerId, now);
        else job.AcknowledgePause(workerId, now);
        await SaveAndClearAsync(cancellationToken);
    }

    public async Task ReleaseAsync(Guid jobId, string workerId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var job = await RequiredAsync(jobId, cancellationToken);
        job.Release(workerId, now);
        await SaveAndClearAsync(cancellationToken);
    }

    public async Task<PersistentJob> PauseAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var job = await RequiredAsync(jobId, cancellationToken);
        job.RequestPause(now);
        await SaveAndClearAsync(cancellationToken);
        return job;
    }

    public async Task<PersistentJob> ResumeAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var job = await RequiredAsync(jobId, cancellationToken);
        job.Resume(now);
        await SaveAndClearAsync(cancellationToken);
        return job;
    }

    public async Task<PersistentJob> RedriveAsync(Guid jobId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var job = await RequiredAsync(jobId, cancellationToken);
        job.Redrive(now);
        await SaveAndClearAsync(cancellationToken);
        return job;
    }

    public Task<PersistentJob?> GetAsync(Guid id, CancellationToken cancellationToken) => dbContext.Jobs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PersistentJob>> ListAsync(Guid projectId, PageCursor? cursor, int take, CancellationToken cancellationToken)
    {
        var query = dbContext.Jobs.AsNoTracking().Where(x => x.ProjectId == projectId);
        if (cursor is not null)
        {
            query = query.Where(x => x.CreatedAt > cursor.Value.CreatedAt || (x.CreatedAt == cursor.Value.CreatedAt && x.Id.CompareTo(cursor.Value.Id) > 0));
        }
        return await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
    }

    private async Task<PersistentJob> RequiredAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.Jobs.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new ResourceNotFoundException("Job", id);

    private async Task SaveAndClearAsync(CancellationToken cancellationToken)
    {
        await dbContext.SaveChangesAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
    }
}

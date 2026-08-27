\set ON_ERROR_STOP on
\timing on

BEGIN;
SET LOCAL jit = off;

INSERT INTO projects (id, name, primary_domain, description, status, created_at, updated_at)
VALUES ('20000000-0000-7000-8000-000000000001', 'Job claim benchmark',
        'claim-benchmark.invalid', 'Rollback-only durable queue benchmark fixture', 'Active', now(), now());

\echo Generating :row_count ready durable jobs
INSERT INTO jobs
    (id, type, project_id, campaign_id, status, priority, payload, created_at, available_at,
     claimed_at, claim_expires_at, started_at, completed_at, worker_id, attempt_count, max_attempts,
     last_error, correlation_id, idempotency_key, domain, last_heartbeat_at, pause_requested_at,
     paused_at, last_failure_kind, recovery_count)
SELECT
    md5('claim-job-' || value::text)::uuid,
    'Analysis',
    '20000000-0000-7000-8000-000000000001'::uuid,
    NULL,
    'Queued',
    value % 10,
    '{"version":1}'::jsonb,
    '2026-01-01 00:00:00+00'::timestamptz + value * interval '1 microsecond',
    '2026-01-01 00:00:00+00'::timestamptz,
    NULL, NULL, NULL, NULL, NULL, 0, 3, NULL,
    'claim-' || value::text,
    'claim-' || value::text,
    NULL, NULL, NULL, NULL, NULL, 0
FROM generate_series(1, :row_count) AS generated(value);

ANALYZE jobs;

\echo Durable claim candidate plan for :row_count ready jobs
EXPLAIN (ANALYZE, BUFFERS, WAL, SETTINGS)
WITH active AS MATERIALIZED (
    SELECT project_id, campaign_id, domain
    FROM jobs
    WHERE status IN ('Claimed', 'Running')
      AND claim_expires_at > '2026-01-02 00:00:00+00'::timestamptz
),
caps AS MATERIALIZED (
    SELECT
        (SELECT COUNT(*) FROM active) AS global_active,
        ARRAY(SELECT project_id FROM active GROUP BY project_id HAVING COUNT(*) >= 100) AS full_projects,
        ARRAY(SELECT campaign_id FROM active WHERE campaign_id IS NOT NULL GROUP BY campaign_id HAVING COUNT(*) >= 100) AS full_campaigns,
        ARRAY(SELECT domain FROM active WHERE domain IS NOT NULL GROUP BY domain HAVING COUNT(*) >= 2) AS full_domains
),
ready AS (
    SELECT job.id, job.priority, job.available_at, job.created_at
    FROM jobs AS job
    WHERE job.status IN ('Queued', 'RetryScheduled')
      AND job.pause_requested_at IS NULL
      AND job.available_at <= '2026-01-02 00:00:00+00'::timestamptz
      AND job.attempt_count < job.max_attempts
      AND (SELECT global_active FROM caps) < 100
      AND array_position((SELECT full_projects FROM caps), job.project_id) IS NULL
      AND (job.campaign_id IS NULL OR array_position((SELECT full_campaigns FROM caps), job.campaign_id) IS NULL)
      AND (job.domain IS NULL OR array_position((SELECT full_domains FROM caps), job.domain) IS NULL)
    ORDER BY job.priority DESC, job.available_at, job.created_at
    FOR UPDATE OF job SKIP LOCKED
    LIMIT 1
),
expired AS (
    SELECT job.id, job.priority, job.available_at, job.created_at
    FROM jobs AS job
    WHERE job.status IN ('Claimed', 'Running')
      AND job.claim_expires_at <= '2026-01-02 00:00:00+00'::timestamptz
      AND job.pause_requested_at IS NULL
      AND job.attempt_count < job.max_attempts
      AND (SELECT global_active FROM caps) < 100
      AND array_position((SELECT full_projects FROM caps), job.project_id) IS NULL
      AND (job.campaign_id IS NULL OR array_position((SELECT full_campaigns FROM caps), job.campaign_id) IS NULL)
      AND (job.domain IS NULL OR array_position((SELECT full_domains FROM caps), job.domain) IS NULL)
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
SELECT id FROM candidate;

ROLLBACK;

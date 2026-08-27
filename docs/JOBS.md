# Persistent Jobs

PostgreSQL is authoritative for every long-running operation. Implemented `DiscoveryJob`, `AnalysisJob`, controlled `SubmissionJob`, `VerificationJob`, and `ReportJob` handlers run independently of REST or MCP sessions. Monitoring and maintenance types remain reserved.

## State machine

```text
Queued ─► Claimed ─► Running ─► Succeeded
  │          │          ├────► RetryScheduled ─► Claimed
  │          │          ├────► Failed
  │          │          ├────► DeadLetter
  │          │          └─ pause request ─► Paused ─► RetryScheduled
  │          └─ expired lease ─► Claimed (or Paused/DeadLetter)
  ├────────────────────────────► Paused
  └────────────────────────────► Cancelled
```

Only Domain methods accept transitions. Queued/retry jobs are eligible at `available_at`. A queued pause is immediate; an active pause sets `pause_requested_at`, is observed during lease renewal, cancels the handler, and is acknowledged by the owning worker. A failed/dead-letter job requires an explicit audited redrive before its attempt budget is reset.

## Atomic claims and distributed limits

Every claim runs in a PostgreSQL transaction under a fixed advisory transaction lock, then selects one eligible row with `FOR UPDATE SKIP LOCKED`. The same transaction:

- converts expired pause requests to `Paused`;
- converts expired jobs with exhausted attempts to `DeadLetter`;
- checks active global, project, campaign, and domain concurrency;
- checks stored hourly, daily, and per-domain policy limits for submission jobs;
- records worker ownership, lease expiry, heartbeat, attempts, and crash-recovery count.

The advisory lock intentionally serializes only the short claim decision, not job execution. Multiple workers execute in parallel after commit without duplicate ownership.

The claim hot path keeps that correctness boundary while avoiding work proportional to total queue depth. It materializes active leases and saturated project/campaign/domain keys once, then searches ready queued/retry work through the `available_at` claim index and expired leases through the `claim_expires_at` index. Each branch locks only its highest-ranked candidate before the common priority ordering chooses one result. Submission action-limit checks stay in the same transaction and the worker re-evaluates policy before external execution. PostgreSQL JIT is disabled only for the claim transaction because compiling this small, index-driven query costs more than executing it; the setting is not changed for other workloads.

## Bounded dispatch and shutdown

The worker uses a bounded `Channel<byte>` of dispatch permits. A consumer claims from PostgreSQL only after it receives a permit, so a job is never leased merely while waiting in an in-process buffer. Local concurrency and buffer sizes are validated at startup.

On SIGTERM the producer stops issuing permits, consumers stop claiming, active handlers receive cancellation, and owned claims are released to `RetryScheduled` or acknowledged as `Paused`. If persistence is unavailable, the renewable lease expires and another worker safely recovers the job. Handler effects remain idempotent through database uniqueness and deterministic upserts.

## Leases and worker heartbeats

Active jobs renew `claim_expires_at` before half of the lease interval and store `last_heartbeat_at`. A worker that no longer owns a row cancels local execution and does not write success or failure. Process-level `worker_heartbeats` store instance ID, host/process identifiers, configured capacity, active count, last heartbeat, and stop time for operational diagnosis.

An operational publisher reads queue/running/failure/dead-letter counts, current worker capacity, and due schedules with one bounded PostgreSQL aggregate at a configurable interval. Observable gauges export that snapshot through OpenTelemetry. Failure to publish telemetry is logged and retried; it cannot block claims or change durable job state. Worker utilization is calculated from active handlers and configured local capacity.

## Retry and dead-letter behavior

Failures are classified as transient, rate-limited, timeout, dependency-unavailable, invalid-input, policy-rejected, authorization-denied, unsupported, or unknown. Invalid input, authorization denial, domain-rule violations, and unsupported work are permanent failures. Retryable failures use capped exponential backoff with deterministic ±20% jitter. When `max_attempts` is reached, the job becomes `DeadLetter`; there is no infinite retry loop.

## Request and action rate limits

`domain_rate_limits` reserves the next HTTP request slot per project/campaign/domain in PostgreSQL. Analysis, sitemap, Serper, submission, and verification execution wait on this shared reservation before network I/O. Submission jobs include a normalized domain; claim selection applies hourly, daily, and per-domain completed-action limits, then the executor re-evaluates the current policy and named authorization profile.

The API and remote MCP hosts also enforce configurable fixed-window transport limits before authorization (`ApiRateLimit` and `McpRateLimit`). These protect ingress capacity; they do not replace project/campaign/domain policy limits enforced by the durable engine.

## Payload and idempotency

Payloads are versioned JSON objects and size-limited. Analysis payload v1 contains only `projectId`; discovery payload v1 contains only `discoveryRunId`; submission payload v1 contains only its version; verification payload v1 contains only `backlinkId`; report payload v1 contains only `reportId`. Submission endpoints, credentials, report paths, and filenames never enter job payloads. The stable submission ID becomes the downstream idempotency key. Enqueue and job-control operations use scoped idempotency records. Reusing the same key and input returns the original result; changing input with the same key is rejected.

Report generation marks its report row `Generating` before streaming. Temporary artifact writes are abandoned on cancellation; the durable job returns to the ordinary retry/recovery lifecycle. A retry may resume from `Generating` and atomically replace only the deterministic artifact for that report. Report completion is never inferred from a file alone: clients see `Completed` only after PostgreSQL stores the artifact name, content type, byte/row counts, and SHA-256 digest.

Campaign controls operate on the durable queue, not on an MCP or HTTP connection. `pause` immediately pauses queued/retry work and requests cooperative pause for claimed/running work. `resume` returns paused jobs to `retryScheduled`. `stop` permanently marks the campaign stopped, cancels queued work, and requests cooperative cancellation of active work. A worker acknowledges the same signal as `Cancelled` rather than `Paused` when the campaign is stopped. An uncertain in-flight WordPress attempt is persisted as reconciliation-required before cancellation completes. The executor refuses a new network action unless the campaign is currently running and repeats policy evaluation immediately before submission.

A campaign may be started again while already running to enqueue selections that received legitimate approval after an earlier preflight. Existing `submission_jobs` make this idempotent at the business level: selections already submitted or queued are skipped. Transport-level retries must still reuse the same idempotency key; a new key represents a deliberate later start after state changed.

## Owned source and campaign jobs

`SubmissionSourceImport` jobs stream staged 64 KiB chunks, batch 1,000 normalized rows, and rely on project URL uniqueness for idempotent deduplication. `SubmissionSourceValidation` uses a coordinator with 500-row keyset pages and one bounded durable child per source. `OwnedNetworkCampaignExpansion` persists its last source cursor and queues submissions in 500-row transactions; existing source IDs are fetched once per page, not once per row.

Owned submission jobs contain only version, campaign/source/pool IDs, target URL, placement enum, and verification delay. They never contain endpoints or credentials. Worker claim SQL combines host limits with the campaign's global/per-domain settings. PostgreSQL domain reservations enforce the campaign delay across worker replicas.

An attempt row is written before the external request. If recovery sees an unfinished attempt, it records `ReconciliationRequired` and schedules verification instead of blindly repeating an uncertain comment. Submitted, approved, or pending-moderation outcomes create a `PendingVerification` backlink. Advanced owned-network campaigns may queue delayed verification; the simple one-click/TXT workflow settles at `Submitted` without automatically scanning the source page. Only an explicit or scheduled Verification job with link evidence changes a backlink to `Verified`.

The crash-window regression test kills ownership after the attempt is durable but before final acknowledgement, reclaims the job under a different worker, and proves no second network send occurs. Recovery persists `ReconciliationRequired` and queues verification. Live direct-WordPress outage/recovery additionally proves an uncertain transport result remains durable and follows classified retry/reconciliation behavior rather than disappearing or being treated as a verified link.

Campaign lifecycle updates are set-based database operations, so pause/resume/stop do not truncate at a transport page limit even for million-job campaigns.

`OwnedNetworkWorkflow` powers the high-level CLI handoff. Each short job either observes import/validation completion, schedules one uniquely keyed delayed continuation, or idempotently creates and starts the campaign. It does not hold a worker thread while waiting and does not consume retry attempts as a polling mechanism.

`BacklinkWorkflow` is the one-click coordinator. Its payload contains only a version and workflow ID; inline identities/comments and all authorization state remain normalized relational records, never secret-bearing job JSON. START persists every normalized source as queued and returns before authorization. The durable coordinator calls the canonical `IOwnedNetworkExecutionAuthorizer` per source, marks unmatched sources `NotAuthorized`, links eligible sources to internal campaigns, fans out bounded validation jobs, and waits through durable continuations. One denied source never prevents other sources from progressing, and verification remains its own job type. HTTP 401/403 and permanent classifications are not retried; 429 is bounded by the workflow attempt limit and honors a valid `Retry-After` delay of at most one day.

## Persistent schedules

Schedules are durable producers of ordinary jobs, not another job executor. An independent scheduler process claims a due schedule with a renewable PostgreSQL lease, calls its typed Application service, and advances `next_run_at` only after durable job creation. Discovery, analysis, and verification therefore retain their existing worker state machines and retry rules.

```text
Active/due -> Claimed -> occurrence jobs persisted -> Active/next run
                                  |                  -> Completed (one-time)
                                  +-- failure -> retry availability -> Paused after 10 consecutive failures
Active <-> Paused
Any non-claimed state -> Deleted (soft)
Expired claim -> Claimed by another scheduler
```

Occurrence idempotency is keyed by schedule ID and scheduled UTC instant, plus backlink ID for bulk verification. Missed recurring occurrences coalesce into one recovery run, then calculate the next future occurrence. Schedule claims do not replace durable job claims; they only protect occurrence production.

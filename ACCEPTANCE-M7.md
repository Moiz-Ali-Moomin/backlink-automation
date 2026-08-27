# Milestone 7 Acceptance Tests

Milestone 7 is complete only when every item below passes on a clean PostgreSQL database.

## Persistent schedule model

- Migration `AddPersistentScheduling` creates project-owned schedules with explicit indexes on project pagination, due work, and claim expiry.
- Schedule definitions support `oneTime`, `interval`, `daily`, `weekly`, `monthly`, and standard five-field `cron` recurrence.
- All recurrence evaluation is UTC. Invalid or ambiguous timing combinations are rejected in Domain code and by a PostgreSQL check constraint.
- Supported actions are only `discovery`, `analysis`, and `verification`; no arbitrary job payload, URL request, SQL, shell, or browser action is accepted.
- Verification schedules accept either one stored project backlink or a bounded project/status selection of at most 500 backlinks per occurrence.

## Lifecycle and recovery

- Schedule states are `Active`, `Paused`, `Completed`, and soft-deleted `Deleted`.
- One-time schedules become `Completed` after their occurrence. Recurring schedules calculate and persist their next UTC occurrence.
- Missed recurring occurrences are coalesced into one recovery occurrence; the scheduler does not create an unbounded catch-up storm.
- Multiple scheduler processes atomically claim due rows with `FOR UPDATE SKIP LOCKED`.
- Claims have renewable leases. An expired claim is recoverable and increments `recovery_count`; a stale owner cannot complete it.
- A scheduler failure retains the original occurrence, records a bounded error, and delays retry. Ten consecutive failures pause the schedule.
- SIGTERM releases an owned active claim when PostgreSQL is reachable; otherwise the lease makes it recoverable.

## Durable actions

- A due discovery schedule calls the same `IDiscoveryService` used by REST/MCP and persists a normal DiscoveryJob.
- A due analysis schedule calls the same `IOpportunityService` and persists a normal AnalysisJob.
- A due verification schedule calls the same `IVerificationService` and persists bounded VerificationJobs for stored backlinks.
- Occurrence idempotency keys derive from schedule ID, scheduled UTC instant, and optional backlink ID. A crash after enqueue but before schedule completion does not duplicate jobs.
- Scheduler-originated enqueue and occurrence events are audited without action payloads or secrets.

## REST and MCP

- REST implements create, get, list, update, pause, resume, and soft delete under `/api/v1/schedules`.
- MCP implements `schedules_get`, `schedules_list`, `schedule_create`, `schedule_update`, `schedule_pause`, `schedule_resume`, and `schedule_delete`.
- Reads require `schedules:read`; writes require `schedules:write`; `admin` satisfies either.
- Every mutation requires an idempotency key and rejects reuse with different input.
- Lists use bounded cursor pagination.

## Hosting and deployment

- The scheduler runs independently of API, MCP, AI, and job workers by using the Worker host in scheduler-only mode.
- Compose starts `backlinkstudio-scheduler` with `Worker__Enabled=false` and `Scheduler__Enabled=true`.
- The scheduler container is non-root, read-only, has no published port, uses health checks, and depends on successful migration.
- API/MCP/worker/scheduler remain cross-platform .NET code with no Windows-only dependency.

## Automated verification

- Unit tests cover every recurrence, invalid cron/timing, missed-run coalescing, claim recovery, one-time completion, pause, and resume.
- PostgreSQL integration tests apply the complete migration chain, reject concurrent claims, recover an expired claim, run an occurrence, enqueue exactly one durable job, and persist audit history.
- Contract tests cover the REST routes, MCP catalog, scopes, and camel-case schedule enums.
- Architecture tests ensure Scheduling does not depend on Infrastructure or hosts and the scheduler host uses Application ports rather than `DbContext`.
- Full build, format verification, all tests with containers, Docker image build, clean Compose migration, API/MCP/scheduler health, and an end-to-end scheduled occurrence pass on Linux.

Milestone 7 does not implement reports, arbitrary custom actions, browser automation, local-time/DST recurrence, or Milestone 8 credential administration.

# BacklinkStudio Architecture

## Scope

Milestones 1 through 10 provide the production foundation, deterministic HTTP-first analysis, replaceable discovery providers, a production worker platform, controlled submission to explicitly configured owned/test endpoints, evidence-based backlink verification, persistent UTC scheduling, a vendor-neutral agent-control plane, durable reporting, and production hardening. Multiple workers and schedulers coordinate through PostgreSQL leases. There is no browser automation.

## Runtime topology

```text
REST clients ──► API ─┐
MCP clients ───► MCP ─┼─► Application ports ─► feature services ─► Infrastructure ─► PostgreSQL
CLI ──────────────────┘                                  ▲
                                                         │
PostgreSQL durable jobs ─► Worker ─► job executors ──────┘
```

Requests enqueue long work and return. A worker process owns execution; closing the client or gateway cannot stop a persisted job.

## Project boundaries

| Project | Responsibility | Allowed dependencies |
| --- | --- | --- |
| Domain | Entities, enums, invariants, job transitions | BCL only |
| Application | Use cases, DTOs, ports, authorization constants | Domain |
| Infrastructure | EF Core/Npgsql, repositories, hashing, job claim, audit | Application, Domain |
| Discovery | Provider adapters, audited discovery orchestration, candidate import | Application, Domain |
| Opportunities | Classification, scoring, policy rules, queries, and analysis orchestration | Application, Domain |
| Submission | Campaign orchestration, named authorization profiles, controlled adapters, submission worker | Application, Domain |
| Verification | Verification orchestration and persistent-job execution | Application, Domain |
| Scheduling | Recurrence calculation, schedule lifecycle, typed occurrence dispatch | Application, Domain |
| Reporting | Report orchestration, streaming JSON/CSV/XLSX/HTML renderers, report worker | Application, Domain |
| API/MCP/CLI/Worker | Composition and transport/hosting only | Application plus adapters |

Infrastructure does not own business rules. Transports do not access `DbContext`. Features interact with storage through Application ports.

## Implemented data model

```text
users 1──* agent_credentials
projects 1──* project_targets
projects 1──* candidate_sites 1──* candidate_pages
projects 1──* discovery_queries 1──1 discovery_runs 0..1──1 jobs
candidate_pages 1──0..1 opportunities
projects 1──* jobs
projects 1──* audit_events
idempotency_records enforce scoped mutation replay semantics
projects 1â”€â”€* reports 0..1â”€â”€1 jobs
```

`projects` contains `id`, name, primary domain, description, status, and UTC timestamps. `project_targets` contains original and normalized URLs, label, keywords, preferred anchor, category, priority, enabled, and created timestamp. Candidate sites are deduplicated by project/domain; candidate pages by project/normalized URL. Analysis classifies opportunities, persists bounded quality/risk scores with explainable reason rows, and records policy outcomes without treating an observed link as verification.

`jobs` contains type, project/campaign/domain keys, state, priority, JSON payload, availability and lifecycle timestamps, renewable claim lease, pause state, worker heartbeat, attempt/recovery counts, classified failure, correlation ID, and idempotency key. `worker_heartbeats` tracks live worker instances without becoming queue state. `domain_rate_limits` serializes request slots across processes. `audit_events` is append-only in both EF and PostgreSQL and stores actor/credential/operation/resource/request/result metadata plus a redacted summary. `idempotency_records` stores bounded mutation results keyed by operation scope and client request key.

`AgentCredentialService` is the administrative credential boundary shared by REST and MCP. It creates scoped identities, rotates and revokes credentials, validates the closed scope catalog and expiry, records an audit event, and commits the credential plus idempotency record atomically. Plaintext keys exist only in the first successful response. Durable replay data is deliberately redacted, and optimistic concurrency on revocation prevents two rotations of the same credential from both committing.

Key indexes cover project ownership, normalized URLs, domains, opportunities, `(status, available_at, priority, created_at)`, claim expiry, and audit timestamps. Uniqueness covers normalized URLs and idempotent mutation keys.

`reports` is the PostgreSQL source of truth for report kind, format, project/campaign scope, job linkage, lifecycle, row/byte counts, content type, SHA-256 digest, artifact name, timestamps, and bounded failure state. Artifact bytes are written through `IReportArtifactStore` to a server-controlled path; a client can never choose a path or filename. The Compose deployment shares a dedicated `reports-data` volume between the worker writer and read-only API reader.

Analysis adds `opportunity_score_reasons`, one `policy_definitions` row per project, and project-owned `blocklist_entries`. Candidate pages persist final URL, response metadata, title, canonical, CMS, robots directives, signals, JavaScript requirement, errors, and target-link presence. Opportunities store bounded scores and classification; every score component is retained as a separate reason row. An observed target link is not a verified backlink.

Discovery provider input is bounded and stored in PostgreSQL `discovery_queries`; durable job payloads contain only a version and discovery-run ID. `discovery_runs` retain provider/query metadata, lifecycle timestamps, accepted/duplicate/blocked/invalid/error counters, and bounded error details. The worker resolves the named provider, normalizes and deduplicates URLs, applies the project blocklist before persistence, and records an immutable audit event. Discovery does not automatically imply permission or enqueue submission.

## Controlled submission boundary

Campaigns reference a named server-side authorization profile and a non-secret authorization reference. Endpoints and optional bearer tokens exist only in host configuration; REST, MCP, campaign rows, and job payloads cannot supply them. Campaign creation verifies project/target/opportunity ownership and profile domain/type restrictions. The worker repeats those checks and applies the current blocklist, score thresholds, approval mode, project limits, campaign daily limit, and distributed domain rate limiter immediately before execution.

Campaign start performs a deterministic preflight over each selected opportunity and returns separate allowed, approval-required, rejected, and manual-action counts. A repeated start can enqueue opportunities approved after an earlier start; existing submissions remain deduplicated. Pause, resume, and stop mutate both the campaign and its durable jobs. The worker checks campaign state and current policy again immediately before any network action, so disconnecting an agent never weakens enforcement.

`campaigns`, `campaign_targets`, and `campaign_opportunities` record the approved plan. `submission_jobs` tracks submission state and `submission_attempts` retains one row per durable-job attempt. A stable submission ID is sent as the endpoint idempotency key for crash recovery. An accepted submission creates a `PendingVerification` backlink candidate; it never creates a verified backlink.

## Verification boundary

`backlinks` stores one candidate per normalized project/source/target tuple plus optional submission and campaign provenance. `verification_checks` is append-only in EF and PostgreSQL and retains every observation. A verification worker fetches only the stored source URL through the guarded public-network HTTP handler, parses bounded HTML with AngleSharp, resolves and normalizes anchor `href` values, and requires an exact normalized target match. HTTP success or submission success alone cannot produce `Verified`.

Found anchors update first/last seen timestamps and rel flags (`nofollow`, `ugc`, `sponsored`). A first clean absence becomes `Missing`; absence after prior evidence becomes `Lost`; transport, timeout, redirect, oversized-body, or transient server failures become `Error` without fabricating link loss. Verification jobs contain only version plus backlink ID and remain independent of REST/MCP sessions.

## Scheduling boundary

`schedules` stores one project-owned typed action, one UTC recurrence definition, `next_run_at`, retry availability, last occurrence/result counters, and a renewable scheduler claim. Supported actions are Discovery, Analysis, and Verification. Action JSON is generated only from validated Application records; it is not an arbitrary job payload or low-level execution surface.

Due rows are claimed transactionally with `FOR UPDATE SKIP LOCKED`. A scheduler occurrence invokes the same Application services as REST/MCP, so ordinary policy, idempotency, job, and audit behavior is reused. Occurrence keys contain schedule ID plus scheduled UTC instant and optional backlink ID. If a scheduler crashes after enqueueing but before advancing the schedule, recovery replays those keys and receives the existing jobs.

Recurrences are one-time, interval, daily, weekly, monthly, or standard five-field cron and are evaluated in UTC. Missed runs coalesce to one immediate recovery occurrence; the next occurrence is calculated after current time to prevent unbounded catch-up. Local timezone/DST rules are intentionally deferred.

## Reporting boundary

`report_generate` and `POST /api/v1/reports` create a `Report` row and versioned `ReportJob` atomically, then return report/job IDs without rendering in the request. The worker revalidates report/job/project/campaign linkage, streams aggregate/detail projections from PostgreSQL, writes a temporary server-named artifact, calculates SHA-256, atomically promotes it, and finally commits report completion metadata. A crash before metadata commit safely retries and overwrites only that report's deterministic artifact.

`CampaignPerformance` requires a campaign and reports campaign candidates/eligibility/approval plus submission, verification, and rel outcomes. `BacklinkInventory` reports project backlinks with an optional campaign filter. Summaries include attempt count, submission and verification rates, plus bounded top-100 domain/template/identity performance groups. Detail rows include source/target, anchor, opportunity/submission/verification states, rel, HTTP status, timestamps, and latest submission error. JSON, CSV, HTML, and XLSX render incrementally; XLSX provides summary/performance sheets, filterable detail sheets, and rollover at the spreadsheet row limit. CSV formula prefixes are neutralized, HTML is encoded and carries a restrictive CSP, and XLSX values are inline strings rather than formulas.

## Owned-network automation slice

`OwnedNetworkProfile` plus normalized child domain rules is the authorization root. `SubmissionSource` is the millions-capable catalog and has project URL uniqueness plus keyset access indexes for network, domain/host, platform/CMS, adapter, ownership, compatibility/validation, enabled state, validation time, and indexed prior submission/verification-state existence checks. Imports stage bounded PostgreSQL chunks and are parsed by a durable worker. Validation coordinators fan out bounded per-source jobs through the existing queue.

Owned campaigns extend, rather than replace, `Campaign`. `OwnedNetworkCampaignConfiguration` holds the source filter, deterministic content pools, concurrency/delay settings, durable expansion cursor, and queued count. A single expansion job reads 500-row source pages and atomically adds submission records/jobs. Claim SQL applies campaign-specific global/per-domain caps, and `domain_rate_limits` coordinates delay across replicas.

The named `OwnedWordPressCommentAdapter` resolves server-side strategy selection. Direct WordPress credentials stay in worker configuration behind `WordPressSiteProfile` references; the standard form strategy remains internal. `SubmissionAttempt` stores strategy, moderation/failure classifications, selected identity/template IDs, and resolved non-secret values. Accepted results create `PendingVerification` backlinks only. See [OWNED-NETWORKS.md](OWNED-NETWORKS.md), [SOURCE-IMPORT.md](SOURCE-IMPORT.md), and [WORDPRESS-AUTOMATION.md](WORDPRESS-AUTOMATION.md).

## Production operations boundary

Readiness is stricter than connectivity: the database must be reachable and have no pending EF Core migrations. The API, MCP host, worker, and scheduler therefore fail readiness when an application image is newer than its schema. Migrations remain an explicit one-shot deployment step.

OpenTelemetry exports host/runtime/HTTP traces and metrics over OTLP. A bounded PostgreSQL aggregate periodically publishes durable queue depth, current running/failed/dead-letter jobs, online workers, due schedules, and worker utilization. Telemetry is observational only; it never becomes queue state or an authorization input. The reference collector exposes Prometheus metrics on loopback and logs sampled traces for local operations; production deployments replace the debug exporter with their managed telemetry backend.

The M10 migration adds partial indexes for ready claims, expired leases, active project jobs, and completed submission limit queries. These indexes match the worker's bounded operational access paths without changing job semantics. Upgrade integration tests migrate a populated M9 schema in place and verify both retained data and the new indexes.

Backups use PostgreSQL custom archives plus SHA-256 sidecars. Restore is deliberately destructive only after an exact target-database confirmation, refuses PostgreSQL system databases, validates both checksum and archive structure, and restores in one transaction. Report artifacts remain a separate durable volume and must be captured in the same quiesced backup window as PostgreSQL metadata.

# BacklinkStudio

A vendor-neutral, cross-platform backend automation engine for backlink placement, verification, and reporting on **infrastructure you own or are explicitly authorized to use**. AI clients and operators orchestrate; durable server-side jobs execute.

The core design rule: **nothing about a run lives in the caller's session**. REST, MCP, and CLI requests validate input, enqueue a durable PostgreSQL job, and return an ID. Closing the client, dropping the MCP connection, or restarting the machine cannot stop, weaken, or duplicate work in flight.

---

## Authorization model (read this first)

BacklinkStudio automates comment/link placement only on sources that are **registered server-side** as owned, controlled, partner-controlled, or holding explicit permission. This is enforced in the worker, immediately before any network I/O — not in the transport layer where a client could shape it.

- Ownership states: `Unverified`, `Owned`, `Controlled`, `PartnerControlled`, `ExplicitPermission`.
- Execution requires **both** `OwnershipStatus != Unverified` **and** `AutomationPermitted = true`.
- Uploading a TXT/CSV list grants nothing. An import is *input only*. A pre-approved `OwnedNetworkProfile` authorizes source URLs that match its persisted domain rules; the upload itself never does.
- Authorization is resolved from persisted configuration by a single canonical authorizer. The caller is never asked for ownership status, automation flags, profile IDs, adapter selection, browser options, or credentials.
- An unauthorized source becomes a durable per-source `NotAuthorized` result. It does **not** abort the rest of a mixed batch, and it never prompts the caller to assert ownership.
- Submission endpoints and secrets live only in host configuration behind named authorization profiles. A campaign row, REST body, MCP argument, or job payload can never supply an endpoint or a credential.
- **A successful submission is not a backlink.** Only the Verification subsystem, on actual anchor evidence, may mark a backlink `Verified`.

Explicitly out of scope and prohibited by the engineering rules in [`AGENTS.md`](AGENTS.md): CAPTCHA/anti-bot bypass, stealth evasion, credential abuse, exploitation, unsolicited spam automation, and any generic MCP primitive (`http_request_any_url`, `submit_arbitrary_form`, `playwright_run_script`, `execute_shell`, `execute_sql`, …). MCP exposes named business operations only.

---

## Runtime topology

```text
REST clients ──► API ─┐
MCP clients ───► MCP ─┼─► Application ports ─► feature services ─► Infrastructure ─► PostgreSQL
CLI ──────────────────┘                                  ▲
                                                         │
PostgreSQL durable jobs ─► Worker ─► job executors ──────┘
                        └► Scheduler (UTC recurrence, FOR UPDATE SKIP LOCKED)
```

PostgreSQL is the single source of truth for durable state. In-process channels provide bounded local dispatch and are never the queue. Multiple workers and schedulers coordinate through renewable database leases with heartbeats, recovery, classified retries, and dead-lettering.

## Project boundaries

| Project | Responsibility | Allowed dependencies |
| --- | --- | --- |
| `Domain` | Entities, enums, invariants, job transitions | BCL only |
| `Application` | Use cases, DTOs, ports, authorization constants | Domain |
| `Infrastructure` | EF Core/Npgsql, repositories, hashing, job claim, audit | Application, Domain |
| `Discovery` | Provider adapters, audited discovery orchestration, candidate import | Application, Domain |
| `Opportunities` | Classification, scoring, policy rules, analysis orchestration | Application, Domain |
| `Submission` | Campaign orchestration, named authorization profiles, adapters | Application, Domain |
| `Verification` | Evidence-based verification and persistent-job execution | Application, Domain |
| `Scheduling` | Recurrence calculation, schedule lifecycle, typed dispatch | Application, Domain |
| `Reporting` | Report orchestration, streaming JSON/CSV/XLSX/HTML renderers | Application, Domain |
| `Api` / `Mcp` / `Cli` / `Worker` | Composition, transport, and hosting only | Application plus adapters |

Dependency direction is enforced by `tests/BacklinkStudio.ArchitectureTests` — transports never touch `DbContext`, and `Domain` references no framework. See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

---

## Pipeline

1. **Discovery** — replaceable providers (manual URLs, CSV import, competitor backlink import, Serper). Bounded provider input is persisted in `discovery_queries`; the durable payload carries only a version and run ID. The worker normalizes, deduplicates, applies the project blocklist *before* persistence, and writes an append-only audit event. Discovery implies no permission and enqueues no submission.
2. **Analysis & opportunities** — deterministic HTTP-first fetch and bounded AngleSharp parsing: final URL, response metadata, canonical, CMS detection, robots directives, JS requirement, and target-link presence. Classification persists bounded quality/risk scores with one explainable reason row per component. An observed link is an *observation*, never verification.
3. **Campaigns & submission** — campaigns reference a named server-side authorization profile. Start runs a deterministic preflight and returns separate **allowed / approval-required / rejected / manual-action** counts. The worker re-checks campaign state, blocklist, score thresholds, approval mode, project and daily limits, and a cross-process domain rate limiter immediately before execution. Every attempt is recorded before I/O, so a worker crash yields `ReconciliationRequired` plus verification — not a blind re-post.
4. **Owned-network execution** — `SubmissionSource` is a millions-capable catalog with keyset indexes and indexed prior submission/verification-state filters. Imports stage bounded PostgreSQL chunks parsed by a durable worker. Expansion reads 500-row pages and atomically creates submission records and jobs; claim SQL applies campaign global and per-domain caps. The named WordPress adapter walks a bounded **standard → tolerant fallback → controlled browser (Playwright)** strategy chain, all internal to the adapter.
5. **Verification** — fetches only the stored source URL through a guarded public-network handler, resolves and normalizes anchor `href` values, and requires an exact normalized target match. Found anchors update first/last seen plus `nofollow`/`ugc`/`sponsored` flags. First clean absence → `Missing`; absence after prior evidence → `Lost`; transport/timeout/oversize/transient failures → `Error` without fabricating link loss. `verification_checks` is append-only.
6. **Scheduling** — one typed action per schedule (Discovery, Analysis, Verification) with one-time, interval, daily, weekly, monthly, or five-field cron recurrence, all UTC. Due rows are claimed with `FOR UPDATE SKIP LOCKED`; occurrence keys make crash recovery replay-safe; missed runs coalesce into a single recovery occurrence.
7. **Reporting** — `CampaignPerformance` and `BacklinkInventory` render incrementally to JSON/CSV/HTML/XLSX. The worker streams projections from PostgreSQL, writes a server-named temp artifact, computes SHA-256, and atomically promotes it. Clients never choose a path or filename. CSV formula prefixes are neutralized, HTML carries a restrictive CSP, XLSX uses inline strings and rolls over at the row limit. A report's `verified` count derives only from stored `Verified` backlinks.

Cross-cutting: API-key credentials stored as one-way hashes with constant-time comparison, expiry, rotation, and revocation; a closed scope catalog; `Idempotency-Key` / `clientRequestKey` on every mutation with bounded replay records; append-only redacted audit events.

---

## Surfaces

| Surface | Entry point | Notes |
| --- | --- | --- |
| REST | `src/BacklinkStudio.Api` | `/api/v1`, camelCase, `X-Api-Key` or bearer, `Idempotency-Key` on mutations — [`docs/API.md`](docs/API.md) |
| MCP | `src/BacklinkStudio.Mcp` | `POST /mcp` (authenticated) and STDIO; protocol `2025-06-18` — [`docs/MCP.md`](docs/MCP.md) |
| CLI | `src/BacklinkStudio.Cli` | `project`, `network`, `sources`, `identity-pool`, `template-pool`, `campaign`, `backlinks`, `verification`, `report`, `job`, … |
| Worker | `src/BacklinkStudio.Worker` | Job executors, scheduler host, operational metrics publisher |

STDIO MCP has no network auth or rate limiter, so process access is an OS trust boundary — it defaults to **read-only** scopes (`AuthorizationScopes.DefaultStdio`). Elevating it requires explicit `McpStdio:Scopes`, and credential administration additionally requires the separate `McpStdio:AllowCredentialManagement` opt-in. Unknown scope names fail startup.

### The normal path is one call

Most work needs only `backlink_workflow_start` (sources, identities, comments, target URL, optional limits) followed by bounded polling of `backlink_workflow_get`. CLI parity:

```powershell
backlinkstudio backlinks start `
  --sources urls.txt `
  --identities identities.csv `
  --comments comments.txt `
  --target https://example.com/ `
  --concurrency 8 `
  --delay-ms 1000
```

The start contract exposes no ownership status, automation flag, profile ID, credential reference, adapter, endpoint, selector, or browser option. See [`docs/BACKLINK-CAMPAIGNS.md`](docs/BACKLINK-CAMPAIGNS.md) for the advanced sequence.

---

## Quickstart (Docker Compose)

Linux Docker is the reference production target. Containers run non-root, log to stdout, honor SIGTERM, and expose only required ports.

```bash
cp .env.example .env          # then replace every placeholder secret
docker compose build
docker compose up -d postgres redis
docker compose run --rm backlinkstudio-migrate
docker compose up -d backlinkstudio-api backlinkstudio-mcp backlinkstudio-worker backlinkstudio-scheduler
```

Migrations are always an explicit one-shot step. Readiness is stricter than connectivity: `/health/ready` fails when the database is reachable but has **pending migrations**, so a newer image never serves an older schema. `.env` is local-only; `.env.example` holds placeholders, never usable secrets.

## Local development

Requires the .NET SDK pinned in `global.json` (10.0.400, `net10.0`), PostgreSQL, and Docker for integration tests.

```bash
dotnet restore BacklinkStudio.sln
dotnet build   BacklinkStudio.sln --no-restore
dotnet test    BacklinkStudio.sln --no-build
dotnet format  BacklinkStudio.sln --verify-no-changes
```

Add a migration from the repository root, then apply it explicitly:

```bash
dotnet ef migrations add <DescriptiveName> \
  --project src/BacklinkStudio.Infrastructure \
  --startup-project src/BacklinkStudio.Api \
  --output-dir Persistence/Migrations

dotnet run --project src/BacklinkStudio.Api -- --migrate
```

`EnsureCreated` is never used, applied migrations are never silently rewritten, and destructive production migrations are never auto-applied.

## Connecting an AI client

[`setup.ps1`](setup.ps1) wires a Windows workstation to a remote BacklinkStudio MCP host over an SSH tunnel: it stores `BACKLINKSTUDIO_API_KEY` as a user environment variable, generates a syntax-checked reconnecting tunnel script, registers both Claude Code (`claude mcp add --transport http`) and Codex (`~/.codex/config.toml`), installs a logon-triggered Scheduled Task, and finishes with a live MCP `ping`. It requires SSH auth that already works unattended and deliberately **never generates or installs SSH keys**.

```powershell
.\setup.ps1 -VpsIp <host> -VpsUser <user> -LocalPort 18081 -RemotePort 8081 -ApiKey <bls_...>
```

## Testing and acceptance

| Suite | Scope |
| --- | --- |
| `tests/BacklinkStudio.UnitTests` | Domain rules, job state machine, adapters, authorization resolution, URL normalization |
| `tests/BacklinkStudio.ArchitectureTests` | Dependency direction and prohibited-reference rules |
| `tests/BacklinkStudio.ContractTests` | REST and MCP tool schema/contract stability, STDIO scope behavior |
| `tests/BacklinkStudio.IntegrationTests` | Testcontainers PostgreSQL workflows, in-place M9→M10 upgrade, failure injection |
| `tests/load` + `scripts/benchmark-*.sh` | Job-claim, source-import, and owned-network catalog benchmarks |

```bash
BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 BACKLINKSTUDIO_RUN_FAILURE_TESTS=1 \
  dotnet test tests/BacklinkStudio.IntegrationTests

BACKLINKSTUDIO_API_KEY="$BACKLINKSTUDIO_BOOTSTRAP_API_KEY" bash scripts/acceptance-m10.sh
bash -n scripts/*.sh
```

Milestone acceptance scripts (`acceptance-m1/m9/m10`, `acceptance-one-click.sh`, `acceptance-owned-network.sh`) stand up isolated stacks with local fixtures or a containerized WordPress network. External submission tests may target **only** local mocks or explicitly owned test environments. `BACKLINKSTUDIO_TEST_OWNERSHIP_OVERRIDE` is a test-harness switch — production startup fails if it is `true`. CI (`.github/workflows/ci.yml`) plus a security workflow enforce build, tests, formatting, and supply-chain checks with warnings-as-errors.

## Operations

- **Telemetry** — OpenTelemetry traces/metrics over OTLP, plus a bounded PostgreSQL aggregate publishing queue depth, running/failed/dead-letter counts, online workers, due schedules, and worker utilization. Telemetry is observational only: never queue state, never an authorization input. The reference collector's debug exporter is meant to be replaced by your managed backend.
- **Rate limiting** — fixed-window limits on REST and remote MCP in addition to scopes, plus a PostgreSQL-coordinated per-domain minimum interval shared across replicas.
- **Backups** — `scripts/postgres-backup.sh` writes custom archives with SHA-256 sidecars. `postgres-restore.sh` is destructive only after exact target-database confirmation, refuses system databases, and validates checksum and archive structure inside one transaction. Report artifacts are a separate durable volume and must be captured in the same quiesced window.

## Repository layout

```text
src/        13 projects: Domain, Application, Infrastructure, feature modules, Api, Mcp, Cli, Worker
tests/      unit, architecture, contract, integration, load
docs/       ARCHITECTURE, API, MCP, JOBS, SECURITY, DEPLOYMENT, DEVELOPMENT,
            OWNED-NETWORKS, SOURCE-IMPORT, WORDPRESS-AUTOMATION,
            IDENTITY-POOLS, TEMPLATE-POOLS, BACKLINK-CAMPAIGNS, acceptance records
scripts/    acceptance, benchmark, load, backup/restore
acceptance/ fixtures and containerized WordPress acceptance network
deploy/     deployment assets
docker/     image and collector configuration
urls-list/  local operator input files (sources, identities, comments) — not engine state
AGENTS.md   binding engineering, security, and authorization rules for this repository
```

Persistent state lives only in PostgreSQL. Text files, the registry, and process memory are never durable state — `urls-list/` is operator input that gets imported, nothing more.

## Documentation

| Document | Topic |
| --- | --- |
| [`AGENTS.md`](AGENTS.md) | Engineering instructions: architecture, conventions, security, prohibited patterns |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Boundaries, data model, and every enforcement point |
| [`docs/API.md`](docs/API.md) | REST contract, scopes, idempotency |
| [`docs/MCP.md`](docs/MCP.md) | MCP tools, scope filtering, STDIO authorization, agent rules |
| [`docs/JOBS.md`](docs/JOBS.md) | Job transitions, claiming, leases, retry classification |
| [`docs/OWNED-NETWORKS.md`](docs/OWNED-NETWORKS.md) · [`SOURCE-IMPORT.md`](docs/SOURCE-IMPORT.md) · [`WORDPRESS-AUTOMATION.md`](docs/WORDPRESS-AUTOMATION.md) | Owned-network authorization, catalog import, WordPress adapters |
| [`docs/IDENTITY-POOLS.md`](docs/IDENTITY-POOLS.md) · [`TEMPLATE-POOLS.md`](docs/TEMPLATE-POOLS.md) | Deterministic identity and content pools |
| [`docs/SECURITY.md`](docs/SECURITY.md) · [`DEPLOYMENT.md`](docs/DEPLOYMENT.md) · [`DEVELOPMENT.md`](docs/DEVELOPMENT.md) | Security policy, deployment, developer workflow |
| `ACCEPTANCE-M*.md`, [`docs/OWNED-NETWORK-RELEASE-CLOSURE.md`](docs/OWNED-NETWORK-RELEASE-CLOSURE.md) | Milestone acceptance evidence and release closure |

---

## Contributing

`AGENTS.md` is binding for humans and agents alike: implement one vertical slice at a time with tests, run the build/test/format gates after meaningful changes, review the diff for dependency-direction, security, migration, and cross-platform regressions, and update the contract docs in the same change. Add a dependency only when the framework cannot meet the need — pinned centrally in `Directory.Packages.props`, with the reason documented.

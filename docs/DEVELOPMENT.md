# Development

## Prerequisites

- .NET 10 SDK (the runtime alone is insufficient)
- Docker Desktop or Docker Engine with Compose v2 for PostgreSQL integration tests and the reference deployment
- PowerShell 7+ or a POSIX shell

## First start with Docker

```bash
cp .env.example .env
# Replace every placeholder in .env before continuing.
docker compose build
docker compose up -d postgres redis
docker compose run --rm backlinkstudio-migrate
docker compose up -d backlinkstudio-api backlinkstudio-mcp backlinkstudio-worker backlinkstudio-scheduler
docker compose ps
curl http://localhost:8080/health/ready
curl http://localhost:8081/health/ready
```

Use `X-Api-Key: <BACKLINKSTUDIO_BOOTSTRAP_API_KEY>` for authenticated calls. Stop with `docker compose down`; add `-v` only when intentionally deleting local database data.

## Native start

Start PostgreSQL, then set configuration with environment variables. Double underscores map nested .NET keys.

`BACKLINKSTUDIO_TEST_OWNERSHIP_OVERRIDE` and `BACKLINKSTUDIO_TEST_ALLOWED_HOSTS` no longer grant anything: the
ownership gate they used to unlock was removed with `OwnedNetworkExecutionAuthorizer`, and sources now resolve to an
owned-network profile without needing a pre-registered domain rule. Both settings are still parsed and validated at
startup, so enabling the override outside Development or Test continues to fail application startup and an invalid
non-exact allowlist entry is still rejected.

PowerShell:

```powershell
$env:ConnectionStrings__BacklinkStudio='Host=localhost;Port=5432;Database=backlinkstudio;Username=backlinkstudio;Password=local-password'
$env:BacklinkStudio__BootstrapApiKey='bls_replace-with-at-least-32-random-characters'
$env:Reporting__StoragePath=(Join-Path (Get-Location) 'data\reports')
dotnet run --project src/BacklinkStudio.Api -- --migrate
dotnet run --project src/BacklinkStudio.Api
dotnet run --project src/BacklinkStudio.Mcp
dotnet run --project src/BacklinkStudio.Worker
$env:Worker__Enabled='false'; $env:Scheduler__Enabled='true'; dotnet run --project src/BacklinkStudio.Worker
```

Linux/macOS:

```bash
export ConnectionStrings__BacklinkStudio='Host=localhost;Port=5432;Database=backlinkstudio;Username=backlinkstudio;Password=local-password'
export BacklinkStudio__BootstrapApiKey='bls_replace-with-at-least-32-random-characters'
export Reporting__StoragePath="$(pwd)/data/reports"
dotnet run --project src/BacklinkStudio.Api -- --migrate
dotnet run --project src/BacklinkStudio.Api
dotnet run --project src/BacklinkStudio.Mcp
dotnet run --project src/BacklinkStudio.Worker
Worker__Enabled=false Scheduler__Enabled=true dotnet run --project src/BacklinkStudio.Worker
```

Default ports are API `8080` and MCP `8081`. Start local MCP STDIO with `dotnet run --project src/BacklinkStudio.Mcp -- --stdio`.

## Build and test

```bash
dotnet restore BacklinkStudio.sln
dotnet build BacklinkStudio.sln --no-restore
dotnet test BacklinkStudio.sln --no-build
dotnet format BacklinkStudio.sln --verify-no-changes
```

Milestone 3 provider tests are in `DiscoveryProviderTests`; Milestone 4 lifecycle/retry tests are in `JobStateMachineTests` and `JobRetryPolicyTests`; Milestone 5 safety/state/adapter tests are in `SubmissionTests`; Milestone 6 parsing and backlink-state tests are in `BacklinkVerificationTests`; Milestone 7 recurrence/lifecycle tests are in `SchedulingTests`; Milestone 8 credential and campaign-control tests are in `AgentPlatformTests`; Milestone 9 renderer/state tests are in `ReportRendererTests`; and Milestone 10 schema-upgrade, outage-recovery, operational-metric, script-integrity, and supply-chain controls are covered by unit, integration, contract, and architecture tests. The controlled-automation workflow uses only loopback/deterministic fixtures and never posts to a public website.

Cron scheduling uses the MIT-licensed Cronos package because the .NET standard library has no cron parser. The dependency is isolated to `BacklinkStudio.Scheduling`, pinned centrally, and evaluates standard five-field expressions in UTC.

Reporting adds no third-party spreadsheet dependency. JSON uses `Utf8JsonWriter`; CSV/HTML use streaming BCL writers; XLSX is emitted as standards-based Open XML ZIP parts using `System.IO.Compression` and `System.Xml`. This keeps the dependency surface small while preserving bounded-memory streaming and multi-sheet rollover.

Integration tests use Testcontainers and skip with a clear reason when Docker is unavailable. Do not point tests at public websites.

Milestone 2 adds only `AngleSharp` for standards-based HTML parsing and `Microsoft.Extensions.Http` for typed `IHttpClientFactory` integration. Versions are pinned centrally. Browser automation is intentionally not installed.

Run the PostgreSQL-backed integration suite explicitly on a Docker-enabled host:

```bash
BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 dotnet test tests/BacklinkStudio.IntegrationTests
```

The database-outage injection test is intentionally opt-in because it stops and restarts its disposable PostgreSQL container:

```bash
BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 BACKLINKSTUDIO_RUN_FAILURE_TESTS=1 dotnet test tests/BacklinkStudio.IntegrationTests
```

Run shell syntax, dependency health, and production-like acceptance checks with:

```bash
bash -n scripts/*.sh
dotnet list BacklinkStudio.sln package --vulnerable --include-transitive
dotnet list BacklinkStudio.sln package --deprecated --include-transitive
BACKLINKSTUDIO_API_KEY="$BACKLINKSTUDIO_BOOTSTRAP_API_KEY" bash scripts/acceptance-m10.sh
```

Catalog access-path benchmarks are destructive in workload but rollback their fixture transaction. Run them only on an isolated migrated PostgreSQL database; the confirmation guard prevents accidental invocation. Execute each supported scale separately and retain the `EXPLAIN (ANALYZE, BUFFERS, WAL, SETTINGS)` artifacts:

```bash
export BACKLINKSTUDIO_BENCHMARK_CONFIRM=owned-network-catalog
export PGHOST=127.0.0.1 PGUSER=backlinkstudio PGDATABASE=backlinkstudio
bash scripts/benchmark-owned-network-catalog.sh 10000
bash scripts/benchmark-owned-network-catalog.sh 100000
bash scripts/benchmark-owned-network-catalog.sh 1000000
# Optional on adequately sized hosts:
bash scripts/benchmark-owned-network-catalog.sh 5000000
```

The M10 k6 test is a repeatable smoke baseline, not a universal SLA: by default it drives 10 iterations/second for 20 seconds, with three reads per iteration (roughly 30 HTTP requests/second), and requires greater than 99% checks, less than 1% HTTP failures, p95 below 750 ms, and p99 below 1.5 seconds. Record host size, worker count, database size, rate, duration, and output before comparing runs. k6 is containerized test tooling and is not linked into or shipped inside BacklinkStudio.

The final owned-network closure requires both container/failure flags and currently discovers 193 enabled tests. A lower count is a release failure. The measured 5M catalog, 500k claim, one-million-line import, real WordPress API/form, Linux/Windows soak, migration/restore, and security evidence is summarized in [OWNED-NETWORK-RELEASE-CLOSURE.md](OWNED-NETWORK-RELEASE-CLOSURE.md).

Gitleaks uses the repository `.gitleaks.toml`. Its allowlist contains only the exact deterministic CI placeholder and API-key hasher unit-test vector; do not add path-wide exclusions or generic token patterns.

`scripts/load-test.sh` uses Linux host networking by default. For an isolated Compose stack, set `K6_DOCKER_NETWORK=<compose-project>_edge` and `BASE_URL=http://backlinkstudio-api:8080`; `acceptance-m10.sh` does this automatically. Docker Desktop users may instead supply an explicitly reachable `BASE_URL` and network mode.

After the Compose services are healthy, verify the complete authenticated REST/MCP/worker workflow:

```bash
BACKLINKSTUDIO_API_KEY="$BACKLINKSTUDIO_BOOTSTRAP_API_KEY" bash scripts/acceptance-m1.sh
```

The historical script name is retained for compatibility; it now verifies the M2 policy, blocklist, analysis-status, and score-reason additions as well as the M1 workflow.

## Migrations

```bash
dotnet tool restore
dotnet ef migrations add <Name> --project src/BacklinkStudio.Infrastructure --startup-project src/BacklinkStudio.Api --output-dir Persistence/Migrations
dotnet run --project src/BacklinkStudio.Api -- --migrate
```

Never use `EnsureCreated`, edit an already-applied migration, or enable automatic destructive production migration.

Milestone 9 adds `20260820161547_AddReporting`. Milestone 10 adds `20260820174930_HardenProductionOperations`, replacing the generic claim-expiry index and adding partial indexes for active-project jobs, ready claims, expired claims, and completed submission-limit queries. Integration tests cover both clean migration and an in-place upgrade from a populated M9 database; do not edit either applied migration.

Local CLI reporting commands reuse `IReportService`:

```bash
dotnet run --project src/BacklinkStudio.Cli -- report generate --project <uuid> --kind BacklinkInventory --format Json --request-key local-report-1
dotnet run --project src/BacklinkStudio.Cli -- report list --project <uuid>
dotnet run --project src/BacklinkStudio.Cli -- report get --id <report-uuid>
```

To exercise multiple workers locally, scale the one worker service after migration:

```bash
docker compose up -d --scale backlinkstudio-worker=2 backlinkstudio-worker
```

All replicas must use the same global/project/campaign/domain limit configuration. Claims are serialized transactionally in PostgreSQL, so limits remain authoritative across replicas.

For an owned endpoint, configure the same non-secret profile metadata in API, MCP, and workers; place the optional token only in the worker's secret store:

```text
Submission__TimeoutSeconds=30
Submission__Profiles__owned-main__Endpoint=https://owned.example/backlinkstudio/submit
Submission__Profiles__owned-main__SourceDomain=owned.example
Submission__Profiles__owned-main__AllowedTypes__0=OwnedProperty
Submission__Profiles__owned-main__BearerToken=<worker-secret-store-value>
```

The endpoint receives `POST` JSON containing `sourceUrl`, `targetUrl`, `anchorText`, and `campaignId`, plus `Idempotency-Key`. Return `201` for submitted, `202` for pending moderation, and optionally `X-Submission-Reference`. Do not enable `Submission__AllowInsecureLoopbackHttpForTesting` outside local automated tests.

## Owned-network development checks

The source/import/domain tests cover deterministic ownership and streaming parsing. `WordPressSubmissionTests` covers hidden field/cookie replay, moderation parsing, and direct API credential isolation. `ControlledWordPressFlowTests` opens an actual loopback TCP server and exercises a WordPress-compatible GET/form POST/redirect flow without a mocked HTTP handler.

Run the container-gated PostgreSQL suite with the documented environment flags to validate clean migration, schema upgrade, atomic claims, multi-worker limits, report SQL translation, and failure recovery. Docker is required for those tests; a local run that skips them is not final release evidence. Use `acceptance/owned-sites.txt` only with controlled service names on the acceptance network.

Generate and inspect idempotent SQL without applying it:

```powershell
dotnet ef migrations has-pending-model-changes --project src/BacklinkStudio.Infrastructure --startup-project src/BacklinkStudio.Infrastructure
dotnet ef migrations script --idempotent --project src/BacklinkStudio.Infrastructure --startup-project src/BacklinkStudio.Infrastructure --output .tmp/backlinkstudio-migrations.sql
```

Catalog/source selection tests must use keyset pages. Benchmark fixtures may generate 10k/100k/1M rows, but network submission tests must target only deterministic controlled servers.

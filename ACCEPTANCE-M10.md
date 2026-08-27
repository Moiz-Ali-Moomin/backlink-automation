# Milestone 10 Acceptance

Milestone 10 is accepted only when a pinned .NET 10 build is clean, all automated tests pass (including PostgreSQL upgrade and failure injection), a fresh Compose deployment is healthy, telemetry is observable, backup/restore succeeds, the bounded load baseline passes, security scans succeed, and Linux/Windows release publications compile.

## 1. Configure an isolated acceptance stack

Run from the repository root on Linux or a Linux VPS with Docker Engine, Compose v2, `bash`, `curl`, and `jq`:

```bash
export COMPOSE_PROJECT_NAME=backlinkstudio_m10accept
export POSTGRES_PASSWORD='replace-with-a-random-acceptance-password'
export REDIS_PASSWORD='replace-with-a-different-random-password'
export BACKLINKSTUDIO_BOOTSTRAP_API_KEY='bls_replace_with_at_least_32_random_characters'
export BACKLINKSTUDIO_IMAGE_TAG=m10accept
export API_RATE_LIMIT_PERMITS=100000
export MCP_RATE_LIMIT_PERMITS=100000
```

The elevated transport limits apply only to this isolated load run; keep production limits sized to the deployment.

## 2. Build, migrate, and start

```bash
docker compose build
docker compose up -d postgres redis otel-collector
docker compose run --rm backlinkstudio-migrate
docker compose up -d backlinkstudio-api backlinkstudio-mcp backlinkstudio-worker backlinkstudio-scheduler
docker compose ps
curl --fail http://127.0.0.1:8080/health/ready
curl --fail http://127.0.0.1:8081/health/ready
curl --fail http://127.0.0.1:13133/
```

Both application readiness responses must contain `databaseReachable: true` and `schemaCurrent: true`. All long-running services must be healthy.

## 3. Run automated and operational acceptance

```bash
dotnet restore BacklinkStudio.sln
dotnet format BacklinkStudio.sln --verify-no-changes --no-restore
dotnet build BacklinkStudio.sln -c Release --no-restore
BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 BACKLINKSTUDIO_RUN_FAILURE_TESTS=1 dotnet test BacklinkStudio.sln -c Release --no-build
bash -n scripts/*.sh
docker compose config --quiet

export BACKLINKSTUDIO_API_KEY="$BACKLINKSTUDIO_BOOTSTRAP_API_KEY"
export LOAD_RATE=10
export LOAD_DURATION=20s
bash scripts/acceptance-m10.sh
```

The acceptance script verifies healthy/current schema, unauthenticated rejection, authenticated project creation, bounded k6 thresholds, exported durable operational metrics, rejection of a tampered backup, a valid checksummed PostgreSQL backup restored into a separate database, retained project data, and all M10 operational indexes. It refuses to overwrite a pre-existing restore-test database and deletes only the database it created.

### Claim-plan regression baseline

The claim candidate query must be checked separately with `EXPLAIN (ANALYZE, BUFFERS)` at 1,000, 20,000, and 80,000 ready jobs. Record planning/execution time, scanned rows, index usage, sort method, and subplan loops; do not add wall-clock thresholds to the ordinary test suite. The 2026-08-23 PostgreSQL 17 regression run produced:

Use `scripts/benchmark-job-claim.sh <row-count>` with `BACKLINKSTUDIO_BENCHMARK_CONFIRM=durable-job-claim`; the accepted sizes are 1,000, 20,000, 80,000, and 100,000. The transaction is always rolled back and the raw plan is retained under `.tmp/benchmarks` by default.

| Ready jobs | Previous execution | Optimized execution | Optimized candidate scan |
| ---: | ---: | ---: | --- |
| 1,000 | 36.711 ms | 1.341 ms | 1 ready row plus 5 expired rows |
| 20,000 | 707.886 ms | 0.861 ms | 1 ready row plus 5 expired rows |
| 80,000 | 2,714.532 ms | 0.804 ms | 1 ready row plus 5 expired rows |

The 2026-08-23 follow-up run using the committed rollback-only harness produced 1.078 ms at 1,000 jobs, 0.421 ms at 20,000, 0.435 ms at 80,000, and 0.459 ms at 100,000. At 100,000 the plan read one ready row through `ix_jobs_claim_ready`, checked the empty active/expired branches through the partial active indexes, touched seven shared buffers, and performed only bounded in-memory sorts. Raw plans are retained as release-validation artifacts rather than committed generated output.

The previous plan executed correlated concurrency subplans once per candidate (80,005 loops at the largest depth) and touched 6,448,548 shared buffers. The optimized plan uses the ready and expired claim indexes, computes active saturation once, performs only bounded top-N sorts over the branch candidates, and does not grow with the total ready queue in this baseline. Retain the raw plans as acceptance artifacts when rerunning on production-like hardware.

## 4. Supply-chain and release checks

```bash
dotnet list BacklinkStudio.sln package --vulnerable --include-transitive
dotnet list BacklinkStudio.sln package --deprecated --include-transitive
docker build -t backlinkstudio:security .

docker run --rm -v "$PWD:/workspace:ro" \
  ghcr.io/aquasecurity/trivy:0.74.0@sha256:62b1e65e8869bc4b4c6aa4fa2b21595256c7c2f6018a9d9ad61caf87187c1969 \
  fs --scanners vuln,misconfig,secret --severity HIGH,CRITICAL --exit-code 1 /workspace

docker run --rm -v /var/run/docker.sock:/var/run/docker.sock \
  ghcr.io/aquasecurity/trivy:0.74.0@sha256:62b1e65e8869bc4b4c6aa4fa2b21595256c7c2f6018a9d9ad61caf87187c1969 \
  image --ignore-unfixed --severity HIGH,CRITICAL --exit-code 1 backlinkstudio:security

dotnet publish src/BacklinkStudio.Api -c Release -r linux-x64 --self-contained false
dotnet publish src/BacklinkStudio.Mcp -c Release -r linux-x64 --self-contained false
dotnet publish src/BacklinkStudio.Worker -c Release -r linux-x64 --self-contained false
dotnet publish src/BacklinkStudio.Cli -c Release -r linux-x64 --self-contained false
dotnet publish src/BacklinkStudio.Api -c Release -r win-x64 --self-contained false
dotnet publish src/BacklinkStudio.Mcp -c Release -r win-x64 --self-contained false
dotnet publish src/BacklinkStudio.Worker -c Release -r win-x64 --self-contained false
dotnet publish src/BacklinkStudio.Cli -c Release -r win-x64 --self-contained false
```

The scheduled GitHub security workflow additionally runs CodeQL, dependency review, and uploads a CycloneDX image SBOM.

### Final owned-network release evidence (2026-08-23)

The closure rerun passed 193 enabled tests (116 unit, 27 integration, 33 contract, and 17 architecture) with zero failures and zero skips. Release build and formatting are clean. Four workers exercised both real standard-form and authenticated direct-API WordPress paths, including credential rejection, target outage/recovery, moderation approval/removal/recovery, anchor-based verification, and four-format report agreement. Native Windows and Linux/Docker each completed a 30-minute runtime soak. Five-million-source catalog and 500,000-job claim plans, 100k/1M streaming imports, clean/upgrade migrations, an operational backup/restore, final-image security scans, and both RID publications passed.

The complete measured evidence, production recommendations, intentional browser/manual boundary, and no-partial-status requirement table are recorded in [docs/OWNED-NETWORK-RELEASE-CLOSURE.md](docs/OWNED-NETWORK-RELEASE-CLOSURE.md). Git metadata was unavailable in the supplied workspace and was not initialized for this audit.

## 5. Clean up acceptance data

```bash
docker compose down -v
```

`-v` permanently deletes only the isolated `backlinkstudio_m10accept` Compose volumes. Do not run it against a production project name.

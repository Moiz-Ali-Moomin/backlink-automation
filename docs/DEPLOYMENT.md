# Deployment

Linux Docker is the reference target. Native `linux-x64`, `linux-arm64` where dependencies permit, and `win-x64` are supported through standard .NET publication.

## Compose

Copy `.env.example` to `.env`, replace every placeholder, then run:

```bash
docker compose build
docker compose up -d postgres redis
docker compose run --rm backlinkstudio-migrate
docker compose up -d backlinkstudio-api backlinkstudio-mcp backlinkstudio-worker backlinkstudio-scheduler otel-collector
docker compose ps
```

`SERPER_API_KEY` is optional. Set it only when Serper discovery is required. The worker needs outbound HTTPS/DNS for sitemap, Serper, and analysis jobs; API, MCP, PostgreSQL, and Redis do not execute discovery fetches. Native deployments use equivalent `DiscoveryHttp__*`, `Serper__Endpoint`, and secret-store-backed `Serper__ApiKey` settings.

PostgreSQL data is stored in the named `postgres-data` volume. Completed report artifacts are stored in `reports-data`; the worker mounts it read/write and the API mounts it read-only. Redis is ephemeral and is not a source of truth. `backlinkstudio-scheduler` uses the same cross-platform Worker host image in scheduler-only mode, publishes no port, and depends on the migration service.

Compose builds one shared `backlinkstudio-engine` image through the migration service, then starts API, MCP, and worker from that exact image after the one-shot migration succeeds. PostgreSQL and Redis use an internal backend network. API and MCP join the edge network for ingress; the worker joins it only for guarded analysis egress and publishes no port.

The bundled private-network PostgreSQL connection uses password authentication and explicitly disables unused GSS encryption negotiation. This is separate from TLS certificate validation. Deployments using an external PostgreSQL service should override `ConnectionStrings__BacklinkStudio` with the provider-required SSL/GSS settings, normally `SSL Mode=VerifyFull` with the appropriate trusted root.

API and MCP publish loopback ports `8080` and `8081` by default; isolated stacks may override `API_BIND_PORT` and `MCP_BIND_PORT` without changing the container ports. Put both behind HTTPS and network access controls in production. Do not publish PostgreSQL or Redis ports publicly.

The reference OpenTelemetry collector receives OTLP only on the internal network and publishes its health and Prometheus endpoints on loopback ports `13133` and `9464`. Its debug trace exporter is useful for a single-host reference deployment, but production deployments should send traces and metrics to an authenticated managed backend and apply suitable sampling/retention. Container JSON logs rotate at 10 MiB with five files.

## Native publication

```bash
dotnet publish src/BacklinkStudio.Api -c Release -r linux-x64 --self-contained false
dotnet publish src/BacklinkStudio.Api -c Release -r win-x64 --self-contained false
dotnet publish src/BacklinkStudio.Worker -c Release -r linux-x64 --self-contained false
dotnet publish src/BacklinkStudio.Worker -c Release -r win-x64 --self-contained false
```

Use a service manager (`systemd`, Windows Service wrapper, or orchestrator) to restart failed processes and deliver SIGTERM. Run migrations as a separate release step before starting new application instances.

## Worker configuration

`Worker__Concurrency` and `Worker__BufferSize` bound local execution and dispatch. `Worker__GlobalConcurrency`, `Worker__PerProjectConcurrency`, `Worker__PerCampaignConcurrency`, and `Worker__PerDomainConcurrency` are enforced transactionally during PostgreSQL claim selection across replicas. Claim leases are renewed according to `Worker__LeaseRenewalSeconds`; worker process health is persisted according to `Worker__HeartbeatIntervalSeconds`.

`JobPlatform__PerDomainRequestIntervalMilliseconds` reserves request slots in PostgreSQL across workers. `JobPlatform__MaximumRateLimitWaitSeconds` rejects excessive reservation backlog as a retryable rate-limit failure. Keep worker limit configuration identical across replicas.

Scale workers with `docker compose up -d --scale backlinkstudio-worker=N backlinkstudio-worker`. Workers publish no ports, stop acquisition on SIGTERM, cancel active handlers, persist pause/retry state where possible, and otherwise rely on lease expiry for recovery.

Scheduler settings are `Scheduler__PollIntervalMilliseconds`, `Scheduler__ClaimLeaseSeconds`, `Scheduler__LeaseRenewalSeconds`, and `Scheduler__FailureRetrySeconds`. More than one scheduler replica is safe because due rows use PostgreSQL row locks and renewable ownership leases. Keep scheduler configuration consistent across replicas. Schedule calendar and cron fields are evaluated in UTC.

`Reporting__StoragePath` configures the artifact root. Every API instance serving downloads and every report worker must see the same storage backend. Compose sets `/var/lib/backlinkstudio/reports` on the shared volume. The image pre-creates that directory with the non-root application UID so Docker's first-volume copy initializes writable ownership without a privileged init container. For native multi-host deployments, mount a shared durable filesystem at the same configured path and grant the service identity access; future object-storage implementations can replace `IReportArtifactStore` without changing report orchestration. Never expose the artifact directory directly through a web server.

## Configuration

Critical keys are `ConnectionStrings:BacklinkStudio` and the initial `BacklinkStudio:BootstrapApiKey`. Worker settings include poll interval, renewable claim lease, heartbeat, local/global/scope concurrency, retry backoff, and bounded buffer. `AnalysisHttp`, `DiscoveryHttp`, and `VerificationHttp` configure timeout (1-120 seconds), response bytes (16 KiB-10 MiB), redirects (0-10), and user agents; defaults are 20 seconds, 2 MiB, and five redirects. Configuration is validated at startup and secrets come from environment/orchestrator stores. Verification follows redirects explicitly, preserves normal TLS validation, and rejects loopback, private, link-local, and otherwise non-public destinations at every hop.

`BACKLINKSTUDIO_TEST_OWNERSHIP_OVERRIDE` must remain `false` in production. Startup fails when it is enabled outside
the Development or Test environment. `BACKLINKSTUDIO_TEST_ALLOWED_HOSTS` is an exact DNS-host allowlist used only by
the controlled test override; it is not a production ownership registration mechanism and is not exposed through REST
or MCP.

Use the bootstrap key only to create the first named `admin` credential through `POST /api/v1/agent-credentials` or `agent_credential_create`. Capture the returned key directly into the deployment secret store because it is displayed once. Create separate least-privilege credentials for Codex, Claude, Cursor, CI, and automation clients. Rotation returns a replacement once and revokes the prior credential atomically; update the client secret immediately. Keep a separate break-glass administrator before revoking an admin key. The configured bootstrap key remains an operational recovery credential until the deployment replaces or disables it at the secret-store level.

Controlled submission is disabled by default because no profiles are configured. A profile requires `Submission__Profiles__<key>__Endpoint`, `SourceDomain`, and one or more `AllowedTypes`; HTTPS and exact endpoint-host/domain equality are validated at startup. Configure matching non-secret metadata in API/MCP/worker, but inject `BearerToken` only into workers through the platform secret store. Never set `Submission__AllowInsecureLoopbackHttpForTesting` in production.

## Backup, restore, and upgrades

Run database backups from a PostgreSQL client container or native client with `PGHOST`, `PGUSER`, `PGPASSWORD`, and `PGDATABASE` supplied by the secret store:

```bash
bash scripts/postgres-backup.sh /secure/backups/backlinkstudio-$(date -u +%Y%m%dT%H%M%SZ).dump
```

The script creates a custom-format archive and SHA-256 sidecar atomically with owner-only permissions. Copy both files to encrypted off-host storage. Back up `reports-data` in the same maintenance window: pause report job acquisition, wait for active report jobs to finish, take the database backup, snapshot the artifact volume, then resume workers. Restoring only the database does not recreate artifact bytes.

Restore into a new empty database first. The restore script hashes the exact archive argument, rejects malformed sidecars and system databases, and requires an exact destructive confirmation. A sidecar cannot redirect verification to a different file:

```bash
export PGDATABASE=backlinkstudio_restore
export BACKLINKSTUDIO_RESTORE_CONFIRM=backlinkstudio_restore
bash scripts/postgres-restore.sh /secure/backups/backlinkstudio-20260820T120000Z.dump
```

Verify readiness, audit/report counts, artifact digest availability, and a representative authenticated read before switching traffic. Perform a restore drill at least quarterly and record archive size, checksum, restore duration, migration level, and verification results.

For upgrades: back up first, run the one-shot migration with the old application stopped or writes quiesced, inspect its successful exit, then start the new API/MCP/workers. `/health/ready` returns unhealthy/degraded when the database is unreachable or has pending migrations. Roll application binaries back only when they remain compatible with the migrated schema; schema rollback requires a reviewed migration and restored backup, never an ad-hoc database edit.

## Owned WordPress worker configuration

Only workers need WordPress credentials. Configure named values through the orchestrator secret store, for example `WordPressSubmission__Credentials__owned-blog__Username` and `WordPressSubmission__Credentials__owned-blog__ApplicationPassword`; store only `owned-blog` in a `WordPressSiteProfile`. Never place credential values in Compose YAML, `.env.example`, API/MCP arguments, or campaign data.

For controlled RFC1918 sites, set bounded exact `SubmissionSourceHttp__AllowedPrivateHosts__N` values on workers. The same guarded resolver revalidates DNS and redirects. Public-source behavior remains public-only. Production WordPress endpoints should use HTTPS. Plain HTTP additionally requires an exact `WordPressSubmission__AllowedInsecureControlledHttpHosts__N` entry; use it only for isolated operator-owned test networks. `WordPressSubmission__AllowInsecureLoopbackHttpForTesting=true` is exclusively for automated loopback tests and must be false in every deployed environment.

The worker image uses the pinned Microsoft Playwright .NET runtime image and includes headless Chromium without a desktop environment. It runs as non-root `pwuser`, explicitly enables Chromium's sandbox, and uses Playwright's reviewed Docker seccomp profile with user-namespace syscalls plus an init process, host IPC, and only the `SYS_CHROOT` capability required by Chromium's zygote sandbox. Configure `CONTROLLED_BROWSER_ENABLED`, `CONTROLLED_BROWSER_CONCURRENCY` (1-4), the bounded navigation/action timeouts, DOM byte limit, and `WORDPRESS_FALLBACK_MAX_STRATEGIES` (1-4). Public browser forms use an internal credentialless exact-host context after current server-side network authorization; enabling the runtime alone authorizes no host. Cross-host browser requests and redirects are aborted and revalidated by the same network policy.

The disposable three-site acceptance environment and its secret requirements are documented in [../acceptance/README.md](../acceptance/README.md).

Start with worker local concurrency 4-8 for a small deployment and 8-16 for a measured medium deployment, buffer at roughly twice concurrency, per-domain concurrency 1-2, and at least 1,000 ms domain delay. Increase only after observing database claim latency, remote site capacity, moderation behavior, and error rates. PostgreSQL remains authoritative; adding worker replicas does not bypass campaign/domain limits. Do not use 100+ global concurrency as an unmeasured default.

Measured starting points for small, medium, and million-source installations—including PostgreSQL CPU/RAM/storage, worker replicas, campaign concurrency, and connection-pool guidance—are recorded in [OWNED-NETWORK-RELEASE-CLOSURE.md](OWNED-NETWORK-RELEASE-CLOSURE.md). The five-million-row proof used 8.36 GB for the source relation and indexes alone, so production storage must also budget jobs, attempts, backlinks, WAL, vacuum, upgrades, reports, and backup generations.

Kubernetes manifests, object storage, provider-specific backup scheduling, retention policy enforcement, and true zero-downtime mixed-version orchestration are deployment-specific follow-on work; the Application and artifact-store boundaries permit them without changing domain logic.

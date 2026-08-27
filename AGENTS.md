# BacklinkStudio Engineering Instructions

These instructions apply to the entire repository. More specific `AGENTS.md` files may strengthen, but must not weaken, these rules.

## Product and architecture

- BacklinkStudio is a vendor-neutral, cross-platform backend automation engine. AI clients orchestrate; durable server-side jobs execute.
- Maintain dependency direction: `Domain` has no outward dependencies; `Application` depends only on `Domain`; Infrastructure and feature modules implement Application ports; hosts compose dependencies.
- REST, MCP, CLI, and workers call Application contracts. Do not put business rules, EF Core access, or low-level network operations in transports.
- PostgreSQL is the source of truth for durable state. In-process channels may provide bounded dispatch but are never the durable queue.
- Long operations return persistent job IDs. Never bind job lifetime to a request, agent, or MCP session.
- Implement only the active milestone. Do not create speculative frameworks or empty abstractions without a demonstrated boundary.

## Coding conventions

- Target `net10.0`; enable nullable reference types, implicit usings, analyzers, and warnings-as-errors in CI.
- Prefer small sealed classes, immutable records for transport DTOs, constructor injection, async APIs, and `CancellationToken` on I/O.
- Use UTC `DateTimeOffset`, `Guid.CreateVersion7()`, explicit result types, and centralized constants instead of magic strings.
- Do not use hidden global state, fire-and-forget work, broad exception swallowing, or domain logic in controllers/endpoints.
- All paths use `Path.Combine` and configuration. Core code must contain no hard-coded drive paths or OS-specific APIs.
- Add dependencies only when the standard library/framework cannot meet the need. Pin versions centrally in `Directory.Packages.props`, document the reason, verify licensing/maintenance/security, and avoid overlapping libraries.

## Security

- Never commit or log credentials, API keys, authorization headers, private keys, database passwords, or secret payloads.
- Store API keys only as one-way hashes; compare secret material in constant time. Keys must support expiry and revocation.
- Apply equivalent authentication and scope authorization to REST and remote MCP operations. Workers re-evaluate server-side rules in later automation milestones.
- Validate and bound every external input, list size, job payload, URL, and error response. Keep normal TLS validation enabled.
- MCP exposes named business operations only. Never add arbitrary HTTP, form submission, shell, SQL, filesystem, or browser-script tools.
- External submission tests may target only local mocks or explicitly owned test environments.
- Submission adapters resolve only named server-side authorization profiles. Never accept an endpoint or credential from a campaign, REST body, MCP argument, or durable job payload. A submission result is never a verified backlink.

## Owned network backlink automation

BacklinkStudio may automate backlink placement on websites that are owned, controlled, administered, or explicitly registered by the operator as part of an approved network.

Supported ownership states:

- `Unverified`
- `Owned`
- `Controlled`
- `PartnerControlled`
- `ExplicitPermission`

Automatic execution requires both `OwnershipStatus != Unverified` and `AutomationPermitted = true`.

A source imported from a generic TXT or CSV file must not automatically become trusted. A pre-approved `OwnedNetworkProfile` may authorize source URLs that match its persisted ownership rules; the upload itself grants nothing.

Normal `backlink_workflow_start` operations require only source URLs, identities, comments, target URL, and optional execution tuning.

Do not ask the caller for `OwnershipStatus`, `AutomationPermitted`, `OwnedNetworkProfileId`, `WordPressSiteProfileId`, adapter selection, Playwright configuration, or credentials for ordinary public comment forms.

Authorization is resolved automatically from persisted server-side configuration using the canonical execution authorizer.

Per-source authorization failures must become durable result states and must not abort an otherwise valid mixed batch. If a source is already authorized, proceed automatically through the bounded standard -> fallback -> browser strategy chain. If authorization is absent, return `NotAuthorized` without asking the caller questions.

Generic TXT/CSV upload remains input only and never grants execution authorization.

Low-level HTTP and browser behavior must remain internal to named adapters. MCP must not expose generic unrestricted primitives such as:

- `http_request_any_url`
- `submit_arbitrary_form`
- `playwright_run_script`
- `execute_javascript`
- `execute_shell`
- `execute_sql`
- `raw_network_request`

BacklinkStudio may automate WordPress comment placement, owned-property placement, and other backlink workflows on registered owned or controlled infrastructure.

A successful submission is not proof that a backlink exists. Only the Verification subsystem may mark a backlink `Verified`.

## Database and jobs

- Evolve PostgreSQL only through deterministic EF Core migrations. Review generated SQL and test every migration from a clean database.
- Never auto-apply destructive production migrations. Compose uses a one-shot migration process before service startup.
- Keep foreign keys, uniqueness, timestamps, and access-path indexes explicit. Use bounded/keyset pagination for large lists.
- Job transitions must follow `docs/JOBS.md`. Claiming is atomic, leases are recoverable, effects are idempotent, and retries are classified.
- Audit events are append-only. Do not update/delete them in application code or include raw secrets in summaries.

## Required workflow

1. Read this file and inspect the current implementation and working tree.
2. Run the existing tests before changing behavior.
3. State acceptance criteria and a short plan.
4. Implement one working vertical slice at a time with tests.
5. Build after meaningful changes, then run the full test and formatting checks.
6. Review the diff for dependency-direction, security, migration, and cross-platform regressions.
7. Update contracts and operational documentation with code changes.

## Commands

```bash
dotnet restore BacklinkStudio.sln
dotnet build BacklinkStudio.sln --no-restore
dotnet test BacklinkStudio.sln --no-build
dotnet format BacklinkStudio.sln --verify-no-changes
docker compose build
docker compose up -d postgres redis
docker compose run --rm backlinkstudio-migrate
docker compose up -d backlinkstudio-api backlinkstudio-mcp backlinkstudio-worker backlinkstudio-scheduler
docker compose down
bash -n scripts/*.sh
BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 BACKLINKSTUDIO_RUN_FAILURE_TESTS=1 dotnet test tests/BacklinkStudio.IntegrationTests
BACKLINKSTUDIO_API_KEY="$BACKLINKSTUDIO_BOOTSTRAP_API_KEY" bash scripts/acceptance-m10.sh
```

Create migrations from the repository root:

```bash
dotnet ef migrations add <DescriptiveName> --project src/BacklinkStudio.Infrastructure --startup-project src/BacklinkStudio.Api --output-dir Persistence/Migrations
```

Apply explicitly:

```bash
dotnet run --project src/BacklinkStudio.Api -- --migrate
```

Never use `EnsureCreated` for production or silently rewrite an applied migration.

## Docker

- Linux Docker is the reference production target. Application containers run as non-root, emit logs to stdout, expose only required ports, honor SIGTERM, and use read-only filesystems where practical.
- `.env` is local-only. `.env.example` contains placeholders, never usable secrets.
- Validate both `linux-x64` and `win-x64` publication before release. Keep OS-specific hosting glue isolated from core projects.

## Prohibited patterns

- Domain references to EF Core, ASP.NET Core, MCP, PostgreSQL, filesystems, Playwright, or UI frameworks.
- Direct `DbContext` use in REST/MCP/CLI/Worker code.
- Persistent state in text files, the Windows Registry, Access/MDB, or process memory.
- Unbounded task creation, unbounded result sets, arbitrary external execution, CAPTCHA/anti-bot bypass, credential abuse, stealth evasion, exploitation, or unsolicited spam automation.
- Treating submission success or HTTP 200 as proof of a verified backlink.

# Security

## Trust boundaries

REST and remote MCP are untrusted network boundaries. They authenticate API keys and authorize scopes before invoking Application services. Local MCP STDIO and local CLI rely on operating-system access and receive configured local-agent scopes; do not expose STDIO through an unauthenticated network relay.

Workers never infer permission from an AI request. Analysis independently evaluates stored blocklists and project policy. Submission workers independently re-evaluate the current profile, source domain/type, explicit approval, blocklist, score thresholds, project limits, campaign limit, and duplicate state immediately before every action.

## Submission network boundary

- REST/MCP accept only a profile key and non-secret authorization reference. They never accept an endpoint, bearer token, arbitrary form, arbitrary HTTP request, or browser script.
- Each profile fixes one endpoint, exact source domain, and allowed opportunity types in server configuration. HTTPS is mandatory. Plain HTTP is accepted only when the explicit test switch is enabled and the endpoint is loopback.
- Redirects are disabled, ordinary TLS validation remains enabled, timeouts are bounded, and optional tokens come only from secret-backed host configuration. Default HTTP request logging is removed.
- Workers re-resolve profiles and policy for every attempt. Stable idempotency keys make recovery safe. Tests use only an in-process loopback mock endpoint.
- Submission success creates a submission result, attempt history, and a `PendingVerification` candidate. It cannot create or imply a verified backlink.

## Verification network boundary

- REST/MCP accept only an existing backlink ID. Source and target URLs come from stored, project-scoped workflow data; there is no arbitrary URL fetch tool.
- Verification uses a dedicated typed client with ordinary TLS validation, manual bounded redirects, GET-only requests, response limits, no proxy inheritance, and the same DNS/address SSRF protections as analysis.
- Exact normalized anchor destination evidence is required. HTTP 200, submission success, page text, or a redirect is never enough to mark a backlink verified.
- Check rows are append-only in both EF and PostgreSQL, and the restricted backlink foreign key prevents parent deletion from cascading away verification evidence. Owned-network backlinks retain explicit submission-source and submission-attempt provenance; the migration backfills those links from existing submission jobs/attempts where possible. Missing is distinct from transient error, and a previously observed link becomes `Lost` only after a clean absence.

## API keys

The bootstrap key is supplied through configuration, must contain at least 32 characters, and is never written in plaintext. Storage contains a SHA-256 lookup digest plus a unique salt and PBKDF2-SHA512 verification hash. Comparisons use fixed-time equality. Credentials also contain scopes, expiry, revocation time, and last-used time. A disabled owning user invalidates all of that user's credentials.

Production secrets belong in an orchestrator secret store. Do not place them in `appsettings*.json`, Compose YAML, source, images, logs, audit summaries, or traces. Milestone 8 implements admin-only create, bounded list/get, rotate, and revoke through equivalent REST and MCP operations. Names and scopes are validated against the closed Application catalog; expiry is optional, must be future-dated, and is capped at five years.

Create and rotate generate 256 bits of random key material. Plaintext is returned only once and is never placed in the database, idempotency response, or audit event. Replaying the same mutation returns redacted metadata, so a lost response requires another rotation from a different active administrator. Rotation revokes the old key and adds its replacement in one transaction. Revocation is immediate, self-revocation is rejected, and optimistic concurrency prevents two rotations of the same key from both committing. Authentication racing a revocation fails closed instead of admitting the stale key or exposing a persistence error.

## Analysis network boundary

- Analysis uses `IHttpClientFactory` with a dedicated typed client, ordinary TLS validation, explicit time/redirect/body/header limits, and GET requests only.
- Redirects are processed manually and revalidated. The connection callback rejects loopback, private, link-local, carrier-grade NAT, metadata, benchmark, documentation, multicast, reserved, and IPv6 unique-local destinations after DNS resolution.
- Proxy use is disabled for the analyzer so a proxy cannot bypass destination checks. Default `HttpClient` request logging is removed so query strings and fetch exceptions are not emitted; bounded domain/result metrics remain available. Response bodies and secret headers are never logged or persisted.
- HTML tests use in-memory handlers or explicitly owned test environments; automated tests never fetch arbitrary public sites.

## Discovery network boundary

- Sitemap discovery uses the same guarded connection policy as analysis: HTTP(S) only, no proxy inheritance, manual bounded redirects, public-address enforcement after DNS resolution, and bounded response bodies.
- Sitemap XML prohibits DTD processing and external resolution. Sitemap indexes are capped at 100 documents and every output batch is bounded.
- Serper uses a configured HTTPS endpoint and an environment-provided API key. The key is never persisted in job/query/audit data, and default HTTP client request logging is removed.
- Manual, TXT, CSV, and competitor-backlink imports parse supplied content only; they do not fetch candidate URLs during discovery.

## Authorization scopes

The implemented surface applies project/candidate/opportunity reads and writes plus campaign, backlink, verification, schedules, `reports:read`, and `reports:write` policies. Credential administration requires `admin`; `admin` also satisfies every business scope. REST authorization policies and MCP tool filtering/call authorization consume the same Application scope constants. Local STDIO scopes remain explicitly configured and must not be exposed as an unauthenticated network service.

Campaign execution has two enforcement points. Start preflight classifies every selection as allowed, rejected, approval-required, or manual-action-required before creating a durable job. The worker then rechecks campaign state, approval mode, named authorization profile, blocklist, thresholds, duplicates, and limits before network I/O. Agent authorization can request an operation; it cannot override these server-side decisions.

## Scheduling boundary

- Schedule clients choose only typed Discovery, Analysis, or Verification actions. They cannot supply an arbitrary durable job payload, endpoint, credential, SQL, shell command, or browser script.
- Schedule action and timing input is bounded, project-scoped, and validated before persistence. A specific scheduled backlink must belong to the schedule project; bulk verification is capped at 500 backlinks per occurrence.
- All calendar and cron evaluation is UTC, avoiding platform-specific timezone identifiers and ambiguous DST execution.
- PostgreSQL leases prevent simultaneous occurrence production. Deterministic occurrence keys make recovery idempotent if a process stops after enqueueing work.
- Pause, update, and delete reject an actively claimed occurrence rather than racing it. Delete is soft and audit events remain append-only.

## Network and containers

Terminate public traffic with HTTPS at a trusted reverse proxy/load balancer. TLS validation is never disabled. Application containers run as a non-root user with read-only filesystems, all Linux capabilities dropped, bounded process counts, and a temporary `/tmp`. Only API and MCP ports are published; PostgreSQL and Redis are internal by default. Prometheus and collector-health ports bind to loopback.

## Reporting boundary

- Report requests choose only a project, optional campaign, closed report kind, and closed format. They cannot provide SQL, a query expression, a path, filename, template, executable content, or arbitrary data source.
- The worker creates temporary and final paths from the report UUID under the configured storage root. API downloads resolve by stored report ID/format only and never concatenate client text into a path.
- CSV prefixes formula-leading cells with an apostrophe; HTML encodes all data and embeds a restrictive offline CSP; XLSX writes user data as inline strings, never formulas or macros.
- MCP returns metadata only. Binary artifacts are served through the authenticated `reports:read` REST download endpoint with a digest-derived ETag.
- Compose grants the worker read/write access to `reports-data` and the API read-only access. Back up the artifact volume with PostgreSQL metadata; a missing artifact fails closed as unavailable rather than silently regenerating in the request.

## Input, output, and audit

- URLs must be canonicalizable HTTP(S), bounded in length, and contain no user information. Relative document links require an already validated HTTP(S) base.
- Pagination and import sizes are capped.
- Job payloads are typed and size-limited before persistence.
- Error responses do not expose connection strings, SQL, stack traces, or credentials.
- Audit events summarize identifiers and counts, never raw keys or authorization headers. EF rejects tracked updates/deletes and a PostgreSQL trigger rejects direct database mutations.
- MCP has no generic shell, SQL, arbitrary HTTP, form submission, or browser execution tool.

## Owned-network enforcement

Automatic source execution requires an enabled `OwnedNetworkProfile` whose ownership is not `Unverified`, `AutomationPermitted=true`, an enabled matching normalized domain rule, and an enabled source carrying the same profile. TXT/CSV hints cannot grant ownership. Validation and execution repeat this decision from current database state; disabling a profile/source or removing a rule blocks later I/O.

MCP exposes named business operations only. It does not expose `http_request_any_url`, `submit_arbitrary_form`, `playwright_run_script`, `execute_javascript`, `execute_shell`, `execute_sql`, or `raw_network_request`. WordPress low-level requests are internal to the named adapter. Direct credentials are resolved only from server-side references and are not serialized into transport DTOs, durable jobs, attempts, logs, traces, metrics, or audit summaries.

The public SSRF policy still permits only public resolved addresses with redirect revalidation and normal TLS. Controlled private infrastructure uses separate explicit bounded `SubmissionSourceHttp:AllowedPrivateHosts` and `VerificationHttp:AllowedPrivateHosts` configurations through the owned-network handler; neither weakens the public default. A site profile may retain an owned HTTP API URL as metadata, but network execution still fails closed unless that exact host is present in `WordPressSubmission:AllowedInsecureControlledHttpHosts` (or explicitly enabled for a loopback test). HTTPS remains the production default.

Default STDIO grants read scopes only, including owned-network/source/content/submission reads. It does not include `submissions:execute`, `campaigns:execute`, or admin credential management. Extended blocklists support domain, exact host, URL, URL prefix, source ID, network profile ID, and campaign ID and are rechecked immediately before placement.

## Worker coordination boundary

- Claim selection is serialized with a PostgreSQL transaction advisory lock and uses row locking; active global/project/campaign/domain counts are checked in the same transaction.
- Only the owning worker can start, renew, complete, release, or acknowledge a pause. A stale worker that loses its lease cannot persist success over a replacement worker.
- Active pause requests are observed through lease renewal. Expired paused claims become `paused`; expired exhausted claims become `deadLetter`.
- Retry classification treats invalid input, authorization denial, and unsupported operations as permanent. Transient dependency, timeout, and rate-limit failures use bounded exponential backoff with deterministic jitter.
- Per-domain request slots and worker heartbeats contain operational identifiers only. They never contain credentials, request bodies, or authorization headers.

Report suspected vulnerabilities privately. Do not include exploitable secrets or live targets in a public issue.

## Supply chain and operational data

- The SDK and runtime patch versions are pinned in `global.json` and `Dockerfile`; NuGet versions are centralized. Dependabot proposes weekly NuGet, Docker, and GitHub Actions updates.
- CI checks formatting, build/tests, Linux containers, Windows publications, vulnerable/deprecated NuGet packages, and shell/Compose validity. GitHub Actions are pinned to full commit SHAs.
- The security workflow runs CodeQL, pull-request dependency review, Trivy repository/image scanning for high/critical findings, secret/misconfiguration scanning, and produces a CycloneDX SBOM. Scan failures are release blockers and exceptions require documented risk ownership and expiry.
- Gitleaks extends its default rules through `.gitleaks.toml`. The only exceptions are exact deterministic non-secret values used as a CI placeholder and an API-key hashing test vector; no file, directory, credential family, or generic token pattern is excluded.
- Telemetry contains operational identifiers and bounded labels, never API keys, authorization headers, request bodies, full URLs, or job payloads. The reference collector is internal; secure its replacement backend with TLS, authentication, least privilege, retention, and tenant isolation.
- Backup archives contain production data and must be encrypted off host with access/audit controls. The checked-in scripts never accept credentials as command-line arguments, validate archive checksums, and require explicit target confirmation before destructive restore.

The final owned-network scan evidence, scanner versions/database date, and final image digest are recorded in [OWNED-NETWORK-RELEASE-CLOSURE.md](OWNED-NETWORK-RELEASE-CLOSURE.md).

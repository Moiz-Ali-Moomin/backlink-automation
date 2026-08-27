# Owned-network release closure

Validation date: 2026-08-23 UTC

## Final verdict

**100% - OWNED-NETWORK MILESTONE COMPLETE**

The release candidate satisfies the defined owned/controlled WordPress milestone. The final Release build has zero warnings and zero errors. All 193 enabled tests pass with no skips in the Docker-capable environment. Standard WordPress form submission and the authenticated WordPress REST API strategy were both exercised against separate real WordPress 6.8.2 containers. Four worker replicas executed controlled HTTP work, with durable completion events attributed to five worker identities across the submission, credential-rejection, temporary-outage, and recovery runs.

Git metadata was not present in the supplied workspace. Git was not initialized for the audit. Provenance status is therefore `GIT METADATA UNAVAILABLE`; this does not change runtime validation.

## Build and tests

| Gate | Result |
| --- | ---: |
| Release warnings | 0 |
| Release errors | 0 |
| Unit | 116 passed |
| Integration | 27 passed |
| Contract | 33 passed |
| Architecture | 17 passed |
| Total | 193 passed |
| Failed | 0 |
| Skipped | 0 |
| Format | clean |
| Pending EF model changes | none |
| Linux/Windows publication | API, MCP, Worker, CLI passed |

The enabled count increased from 187 to 193 because five WordPress security/strategy tests and one PostgreSQL crash-reconciliation test were added. No test was removed or disabled.

## Catalog and import scale

The catalog tests ran against isolated PostgreSQL 17 databases and rolled back their generated rows. Latencies are server-side `EXPLAIN (ANALYZE, BUFFERS)` execution times.

| Rows | Insert | Throughput | First keyset | Deep keyset | URL lookup | Total DB relation |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 10,000 | 0.560 s | 17,863 rows/s | 0.461 ms | 0.407 ms | 0.028 ms | 16 MB |
| 100,000 | 6.068 s | 16,480 rows/s | 0.654 ms | 0.433 ms | 0.029 ms | 162 MB |
| 1,000,000 | 64.012 s | 15,622 rows/s | 0.368 ms | 0.261 ms | 0.039 ms | 1,752 MB |
| 5,000,000 | 369.350 s | 13,537 rows/s | 1.079 ms | 0.671 ms | 0.395 ms | 8,358 MB |

At five million rows the table was 3,467 MB and indexes were 4,891 MB. Validation selection was 0.282 ms and a representative filtered source query was 0.097 ms. Hot paths used the intended project/keyset and project/normalized-URL indexes; no hot sequential scan, unbounded sort, or spill was observed. PostgreSQL peaked at approximately 753 MiB on the 8 GiB validation host; the host retained approximately 41 GiB free disk at the end of generation.

The durable streaming import measured:

| Accepted | Input lines | Duplicates | Invalid | Staging | Durable duration | Throughput | Peak API | Peak worker | Peak PostgreSQL |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 100,000 | 100,200 | 100 | 100 | 0.417 s | 13.049 s | 7,663.2 rows/s | included below | included below | included below |
| 1,000,000 | 1,000,200 | 100 | 100 | 1.261 s | 117.367 s | 8,520.3 rows/s | 157.8 MiB | 137.8 MiB | 277.7 MiB |

Memory stayed bounded while the one-million-line input was staged and processed in durable chunks. The resulting 1.1 million accepted-row database was 1,640 MB.

## Durable claim scale

| Queued jobs | Claim execution |
| ---: | ---: |
| 1,000 | 0.977 ms |
| 20,000 | 0.429 ms |
| 80,000 | 0.455 ms |
| 100,000 | 0.402 ms |
| 500,000 | 0.468 ms |

The 500,000-job plan had 2.757 ms planning time, used `ix_jobs_claim_ready`, read a bounded candidate, used 25 KiB in-memory sorts, and retained atomic claiming, advisory-lock correctness, leases, action limits, and distributed concurrency checks.

## Controlled WordPress acceptance

The final MCP-driven workflow imported four unique sources plus one duplicate, validated them, detected WordPress/comment capability, previewed deterministic identity/template resolution, created the campaign, returned durable identifiers, and allowed the client to disconnect. Workers continued independently.

- Standard form strategy: real GET, cookie/hidden-field replay, POST, WordPress redirect, published comment, and later anchor verification passed.
- Direct API strategy: a separate WordPress 6.8.2 container used a disposable Application Password resolved by a server-side credential reference. A real REST comment was created and persisted with the direct strategy. HTTP acceptance created only `PendingVerification`; a later page fetch found the normalized target anchor and set `Verified`.
- Invalid/revoked credential: real HTTP 401 was classified `AuthorizationDenied` and was not retried as transient.
- Temporary API outage: stopping the direct WordPress target produced an uncertain retryable attempt; after restart the durable job completed as submitted/duplicate without disappearing.
- Credential isolation: the disposable password was absent from logs, audit text, job payloads, report artifacts, and database text inspection.
- Multi-worker HTTP: four replicas were live. Submission completion audit events were attributed to five distinct worker identities across initial and recovery runs, and the primary three-source campaign was distributed across three workers. No race-created duplicate comment or configured limit violation occurred.
- Moderation lifecycle: pending moderation did not verify; approval produced `Verified`; a transient target outage produced `Error`, not `Lost`; clean removal produced `Lost`; restoration produced `Verified` again.

The final campaign report truth was three attempts: two submitted, one pending moderation, two verified, one missing, zero rejected/failed/duplicate/lost at report time, two `nofollow`, two `ugc`, zero follow, and zero sponsored. JSON, CSV, XLSX, and HTML agreed with PostgreSQL. Formula-injection, HTML encoding/CSP, and bounded XLSX/detail behavior remain covered by enabled tests.

## Browser-required sources

Status: **FULLY FUNCTIONAL BY DESIGN**.

No supported owned WordPress fixture requires a browser: the engine supports the documented authenticated API path and standard form/cookie/hidden-field path. `RequiresBrowser` currently denotes a validation result that the supported adapter cannot safely execute, including CAPTCHA-like/manual conditions. It terminates as `ManualActionRequired` before network execution. Tests prove an unowned source, disabled source, and outside-network redirect are rejected before browser/network I/O. A generic browser, arbitrary script execution, CAPTCHA bypass, anti-bot bypass, stealth, and fingerprint spoofing are intentionally absent.

## Soak and recovery

Linux/Docker ran for 1,800 seconds with four workers, 100 durable jobs, three scheduler runs, six database connections at the final sample, zero failed/dead-letter jobs, and zero fatal log matches. The soak exercised authenticated REST/MCP reads, imports, validation, campaign work, reporting, pause/resume, verification/rechecks, moderation transitions, and worker restart. Observed peaks were API 202.8 MiB, MCP 151.3 MiB, scheduler 137.3 MiB, an individual worker 187.4 MiB, and PostgreSQL about 74.1 MiB for this small live dataset.

Native Windows ran for 1,800 seconds using the published `win-x64` API, MCP, Worker, Scheduler, and CLI against an isolated PostgreSQL environment. It collected 100 samples per component, dispatched 30 durable jobs, exercised authentication/tool discovery/reads/polling/scheduler work, restarted the worker and scheduler, restarted the full stack, and confirmed state survival. A supplemental native shutdown drill sent `CTRL_BREAK` to API, MCP, Worker, and Scheduler in two complete stop/restart cycles: every host exited with code 0 and the same durable project remained readable after restart. Peak working sets were API 201,953,280 bytes, MCP 187,355,136, Worker 189,550,592, and Scheduler 181,903,360; maximum handles were 648, 642, 601, and 586. No process crashed or emitted stderr. Controlled WordPress network routing remained Linux-based; Windows proved the engine hosts, graceful lifecycle, and durable database workflow boundary.

Crash-window coverage verifies an attempt row exists before send. Recovery of an unfinished attempt changes it to `ReconciliationRequired`, schedules verification, and does not perform a second network send. The PostgreSQL regression test proves this transition. Live target outage/recovery proves uncertain outcomes remain durable and retryable; normal stable idempotency/external references and verification provide reconciliation evidence.

## Migration, restore, and security

Clean migration to all 16 migrations passed in multiple disposable environments. The explicit upgrade drill applied migrations 1-15, inserted pre-upgrade project data, applied `20260823115130_AddCampaignStateFiltersAndBacklinkProvenance`, and confirmed both data preservation and the new provenance column.

The operational restore drill restored a 162,906-byte custom PostgreSQL backup plus four report artifacts into a different database. API, MCP, Worker, and Scheduler started on the restore. One project, one network, four sources, one campaign, three submissions, three backlinks, one schedule, and four reports were readable; a 5,014-byte report downloaded and a new verification job succeeded after restore.

Security scans ran on 2026-08-23 UTC against the final image `sha256:64bfb70bf20fc764bdff9a665f00a0eb6975bb8699c0f950de3cf3808307c9f8` (87,499,293 bytes). NuGet reported no vulnerable package. The repository-pinned Trivy 0.74.0 downloaded a fresh vulnerability database during the final pass and reported no fixed HIGH/CRITICAL final-image vulnerability and no HIGH/CRITICAL filesystem vulnerability/misconfiguration/secret. Gitleaks 8.30.1 reported zero secrets after an exact-value allowlist for the documented CI placeholder and unit-test key vector; no path or generic rule is excluded. Syft 1.46.0 generated a CycloneDX SBOM. Grype 0.115.0 was version-recorded as the independent installed inventory scanner.

Negative startup tests against the final image failed fast with the intended validation messages for a missing database connection, invalid worker concurrency, inconsistent domain limits, invalid lease renewal, invalid scheduler polling, an incomplete named WordPress credential, an invalid WordPress timeout, and a malformed controlled-private-host entry. None reached durable work.

MCP exposes 84 named business tools to an administrator, with bounded scope-filtered discovery. Default STDIO remains the read-only `DefaultStdio` scope set and cannot execute submissions or administer credentials. Remote MCP and REST authentication, execution scope checks, worker ownership re-evaluation, credential isolation, redirect controls, and absence of arbitrary HTTP/browser/shell/SQL primitives are covered by the 33 contract and 17 architecture tests plus live acceptance.

## Production settings

Start conservatively and tune from claim latency, remote WordPress capacity, moderation, connection pressure, and error rates.

| Deployment | PostgreSQL | Workers | Local concurrency | Campaign global | Per-domain | Delay | Pool guidance |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| Small | 2-4 vCPU, 8 GiB RAM, 100 GiB SSD | 1-2 | 4-8 each | 8-16 | 1 | 1,000 ms | 20 connections per process maximum; keep total below DB headroom |
| Medium | 8 vCPU, 16-32 GiB RAM, 250 GiB NVMe | 2-4 | 8-16 each | 32-64 | 1-2 | 500-1,000 ms | 20-40 per service; budget the sum across replicas |
| Million-source | 16 vCPU, 64 GiB RAM, 500 GiB+ NVMe | 4-8 | 16 each initially | 64-128 | 1-2 | at least 500 ms | 100-200 DB connections total, preferably through a bounded pooler |

For five million sources, relation size alone was 8.36 GB; allow substantial additional space for jobs, attempts, reports, WAL, vacuum, upgrades, and two backup generations. A 100+ campaign concurrency value is not a default. Increase it only after controlled load evidence on the deployment's actual WordPress network.

## Known limitations and intentional non-goals

- Git history/provenance could not be audited because `.git` metadata was unavailable.
- Windows owned-WordPress HTTP routing was not used; the real standard/API and four-worker HTTP acceptance ran on Linux, while Windows validated native hosts and durable database work.
- Benchmark numbers characterize the stated 8 GiB VPS and are not universal SLAs.
- CAPTCHA bypass, anti-bot bypass, stealth/fingerprint evasion, exploitation, credential abuse, and automation against unapproved sources are intentional non-goals. Such sources end in `ManualActionRequired` or policy rejection.

## Requirement evidence

| Requirement | Status | Evidence |
| --- | --- | --- |
| Release build | PASS | Release build: 0 warnings, 0 errors |
| Enabled tests | PASS | 193 passed, 0 failed, 0 skipped |
| TXT import | PASS | Real durable import plus 100k/1M streaming benchmarks |
| CSV import | PASS | Enabled parser/import/contract coverage and REST/CLI/MCP parity |
| 1M catalog | PASS | 1,000,000-row rollback proof, indexed hot paths, 1,752 MB |
| 5M catalog | PASS | 5,000,000 rows, 13,537 rows/s, 8,358 MB, indexed plans |
| Scalable claims | PASS | 0.402 ms at 100k and 0.468 ms at 500k |
| Standard WordPress | PASS | Real form/cookie/hidden-field POST and verified anchor |
| Direct WordPress API | PASS | Separate real WordPress REST API with disposable Application Password |
| Browser-required workflow | NOT APPLICABLE WITH JUSTIFICATION | Supported API/form workflows need no browser; unsupported/manual conditions terminate safely before I/O |
| Multi-worker real HTTP | PASS | Four replicas; durable completions across five worker identities |
| Distributed limits | PASS | PostgreSQL claim/rate gates plus four-replica live run; no violations |
| Duplicate prevention | PASS | No race duplicate; stable business/idempotency constraints and tests |
| Crash reconciliation | PASS | New PostgreSQL regression plus live target outage/recovery |
| Moderation lifecycle | PASS | Pending, approved/Verified, transient Error, Lost, recovered Verified |
| Verification evidence | PASS | HTTP success stayed PendingVerification until anchor refetch |
| Scheduler | PASS | Linux and Windows scheduler runs plus restored schedule |
| Reports | PASS | JSON/CSV/XLSX/HTML agree with database truth |
| MCP | PASS | 84 named tools, scoped discovery/auth, least-privilege STDIO |
| REST | PASS | Authenticated parity used in soak, restore, import, and acceptance |
| CLI | PASS | Native Windows CLI and contract parity passed |
| Linux/Docker soak | PASS | 1,800 seconds, four workers, zero failed/dead-letter/fatal logs |
| Windows runtime soak | PASS | 1,800 seconds, all native hosts and CLI; two graceful CTRL_BREAK restart cycles exited 0 with state survival |
| Clean migration | PASS | All 16 migrations on disposable databases |
| Upgrade migration | PASS | Migration 15 with data to migration 16, data/schema verified |
| Backup/restore | PASS | Full stack on restored DB; report read and new job succeeded |
| Configuration fail-fast | PASS | Eight negative final-image startup cases exited on their intended validators |
| Dependency scan | PASS | No vulnerable NuGet package |
| Image/filesystem scan | PASS | No required-severity final-image/filesystem finding |
| Secret scan | PASS | Gitleaks and Trivy: zero retained secret findings |
| Documentation | PASS | Acceptance, deployment, jobs, security, and WordPress boundaries updated |

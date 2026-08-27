# Milestone 9 Acceptance

Milestone 9 adds durable, agent-controlled reporting without expanding submission or browser automation.

## Implemented

- `Report` domain lifecycle: `Queued -> Generating -> Completed|Failed`, with retry-safe recovery from `Generating`.
- PostgreSQL `reports` metadata, deterministic EF migration `20260820161547_AddReporting`, foreign keys, constraints, and list/worker indexes.
- Persistent versioned `ReportJob` payload containing only `reportId`; ordinary queue claims, leases, retries, crash recovery, audits, and idempotency remain authoritative.
- `CampaignPerformance` reports requiring a project-owned campaign and `BacklinkInventory` reports with an optional campaign filter.
- Summary fields: Candidates, Eligible, Approved, Queued, Processing, Submitted, Pending, Verified, Rejected, Failed, Lost, Follow, Nofollow, UGC, Sponsored.
- Streamed detail fields: Source URL, Target URL, Anchor, Opportunity Type, Submission Status, Verification Status, rel, HTTP Status, Submitted At, First Seen, Last Seen, Last Checked, Error.
- Streaming JSON, CSV, XLSX, and HTML renderers. XLSX rolls detail data across worksheets at its format limit.
- CSV formula-injection mitigation, HTML encoding/offline CSP, macro/formula-free XLSX inline strings, atomic server-named artifact promotion, SHA-256/length metadata.
- REST generate/list/get/download endpoints protected by `reports:write` and `reports:read`.
- MCP `report_generate`, `reports_list`, and `reports_get`; binary download intentionally remains REST-only.
- Local CLI `report generate`, `report list`, and `report get` commands reusing Application services.
- Compose `reports-data` volume: worker read/write, API read-only; non-root/read-only root filesystems remain intact.
- Structured report metrics and immutable enqueue/completion audit events with identifiers/counts only.

## Acceptance workflow

1. Authenticate with a credential containing `projects:write`, `reports:write`, `projects:read`, and `reports:read` (or `admin`).
2. Create a project/campaign or use existing verified backlinks.
3. Call `report_generate` or `POST /api/v1/reports` with a stable request key.
4. Receive `{ reportId, jobId, status: "queued" }`; disconnecting the client does not stop work.
5. Poll `jobs_get`/`GET /api/v1/jobs/{jobId}` until `succeeded`.
6. Read report metadata with `reports_get`/`GET /api/v1/reports/{reportId}` and confirm `completed`, row/byte counts, and SHA-256.
7. Download through `GET /api/v1/reports/{reportId}/download`; validate the requested content type and digest ETag.
8. Repeat the original mutation with the same key and body; confirm the same report/job IDs are returned and no duplicate rows/jobs are created.

## Required verification

```bash
dotnet restore BacklinkStudio.sln
dotnet build BacklinkStudio.sln -c Release --no-restore
dotnet test BacklinkStudio.sln -c Release --no-build
BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1 dotnet test tests/BacklinkStudio.IntegrationTests -c Release
dotnet format BacklinkStudio.sln --verify-no-changes
docker compose config --quiet
docker compose build
docker compose up -d postgres redis
docker compose run --rm backlinkstudio-migrate
docker compose up -d backlinkstudio-api backlinkstudio-mcp backlinkstudio-worker backlinkstudio-scheduler
```

The container workflow must start from an empty PostgreSQL volume, show all services healthy, generate and download all four formats, reject missing/incorrect report scopes, and retain the completed artifact after the API/MCP client disconnects.

## Deliberately remaining for Milestone 10

- Object-storage artifact adapter and retention/garbage-collection policy.
- Load/failure-injection testing for very large report sets and multi-host artifact storage.
- Backup/restore automation covering PostgreSQL plus report artifacts.
- Dependency/image scanning policy gates, database/query tuning, alert dashboards, Kubernetes manifests, and zero-downtime upgrade tests.

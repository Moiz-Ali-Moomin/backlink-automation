# REST API Contract

The base path is `/api/v1`. JSON properties and enum values use camel case. All protected requests accept an API key in `X-Api-Key` or `Authorization: Bearer`. Mutations, including policy and blocklist writes, require `Idempotency-Key`; clients must reuse it on retries.

## Implemented endpoints

| Method | Route | Scope | Result |
| --- | --- | --- | --- |
| GET | `/health/live` | public | process liveness |
| GET | `/health/ready` | public | PostgreSQL connectivity and current-schema readiness |
| GET/POST | `/api/v1/agent-credentials` | `admin` | bounded redacted page or display-once API key |
| GET | `/api/v1/agent-credentials/{id}` | `admin` | redacted credential metadata |
| POST | `/api/v1/agent-credentials/{id}/rotate` | `admin` | revoke and replace; display replacement key once |
| POST | `/api/v1/agent-credentials/{id}/revoke` | `admin` | revoke credential, except the calling credential |
| GET/POST | `/api/v1/projects` | read/write | page or created project |
| GET | `/api/v1/projects/{id}` | `projects:read` | project |
| GET/POST | `/api/v1/projects/{id}/targets` | read/write | page or created target |
| GET/PUT | `/api/v1/projects/{id}/policy` | project read/write | conservative policy or updated policy |
| GET/POST | `/api/v1/projects/{id}/blocklist` | project read/write | bounded page or normalized rule |
| GET | `/api/v1/projects/{id}/candidates` | `candidates:read` | page |
| GET | `/api/v1/candidates/{id}` | `candidates:read` | candidate and latest analysis state |
| POST | `/api/v1/projects/{id}/candidates/import` | `projects:write` | accepted/duplicate/invalid counts |
| POST | `/api/v1/discovery/jobs` | `projects:write` | `{ discoveryRunId, jobId, status }` |
| GET | `/api/v1/discovery/runs/{id}` | `projects:read` | auditable discovery run |
| GET | `/api/v1/discovery/runs?projectId=...` | `projects:read` | bounded run page |
| POST | `/api/v1/projects/{id}/analysis-jobs` | `projects:write` | `{ jobId, status }` |
| GET | `/api/v1/jobs/{id}` | `projects:read` | job |
| GET | `/api/v1/jobs?projectId=...` | `projects:read` | page |
| POST | `/api/v1/jobs/{id}/pause` | `projects:write` | durable pause/requested-pause state |
| POST | `/api/v1/jobs/{id}/resume` | `projects:write` | resumed job state |
| POST | `/api/v1/jobs/{id}/redrive` | `projects:write` | explicit failed/dead-letter retry |
| GET | `/api/v1/opportunities/{id}` | `opportunities:read` | opportunity |
| GET | `/api/v1/opportunities?projectId=...` | `opportunities:read` | filtered page |
| GET | `/api/v1/opportunities/summary?projectId=...` | `opportunities:read` | counts |
| POST | `/api/v1/opportunities/approve` | `opportunities:approve` | explicit approval count |
| GET/POST | `/api/v1/campaigns` | campaign read/write | bounded page or campaign |
| GET | `/api/v1/campaigns/{id}` | `campaigns:read` | campaign |
| PUT | `/api/v1/campaigns/{id}` | `campaigns:write` | update draft name, mode, and daily limit |
| POST | `/api/v1/campaigns/{id}/start` | `submissions:execute` | durable submission job IDs |
| POST | `/api/v1/campaigns/{id}/pause` | `campaigns:execute` | pause campaign and queued/active work |
| POST | `/api/v1/campaigns/{id}/resume` | `campaigns:execute` | resume campaign and paused work |
| POST | `/api/v1/campaigns/{id}/stop` | `campaigns:execute` | permanently stop and cancel queued work |
| GET | `/api/v1/campaigns/{id}/submissions` | `submissions:read` | bounded submission states |
| GET | `/api/v1/submissions/{id}/attempts` | `submissions:read` | maximum 100 attempts |
| POST | `/api/v1/backlink-workflows` | `submissions:execute` | one-click durable workflow/campaign/job IDs |
| GET | `/api/v1/backlink-workflows/{id}` | `submissions:read` | simple counters and keyset-paginated per-source states |
| POST | `/api/v1/verification/jobs` | `verification:execute` | `{ jobId, status }` for a stored backlink ID |
| GET | `/api/v1/backlinks/{id}` | `backlinks:read` | backlink and current evidence-derived state |
| GET | `/api/v1/backlinks?projectId=...&status=...` | `backlinks:read` | bounded backlink page |
| GET | `/api/v1/backlinks/{id}/verification-history` | `backlinks:read` | bounded append-only check page |
| GET/POST | `/api/v1/schedules` | schedules read/write | bounded page or created schedule |
| GET/PUT/DELETE | `/api/v1/schedules/{id}` | schedules read/write | schedule, replacement definition, or soft-deleted schedule |
| POST | `/api/v1/schedules/{id}/pause` | `schedules:write` | paused schedule |
| POST | `/api/v1/schedules/{id}/resume` | `schedules:write` | active schedule with recalculated next run |
| POST | `/api/v1/reports` | `reports:write` | `{ reportId, jobId, status }` |
| GET | `/api/v1/reports?projectId=...` | `reports:read` | bounded report metadata page |
| GET | `/api/v1/reports/{id}` | `reports:read` | report lifecycle and artifact metadata |
| GET | `/api/v1/reports/{id}/download` | `reports:read` | completed JSON/CSV/XLSX/HTML artifact |

Candidate responses include persisted analysis metadata and errors. Opportunity responses include classification, policy-derived automation status, bounded quality/risk scores, and the complete reason breakdown. An `existingTargetLink` candidate signal is not a verified backlink.

`POST /api/v1/discovery/jobs` requires `Idempotency-Key`. Its JSON body contains `projectId`, `provider`, optional `query`, optional inline `content`, optional `urls`, and `maximumResults`. Provider enum values are `manualUrl`, `txtImport`, `csvImport`, `sitemap`, `serper`, and `competitorBacklinkImport`. Import content is capped at 512 KiB, URL arrays at 5,000 items, general results at 5,000, and Serper results at 100. Long work continues in the worker after the HTTP request ends.

`limit` defaults to 50 and is capped at 100. Cursors are opaque and must be passed unchanged. Invalid input returns RFC 9457-style problem details without internal exception data. OpenAPI JSON is exposed at `/openapi/v1.json`.

Request and response schemas are generated from the executable endpoint contracts. The source definitions in `Api/Contracts` are canonical if this document and generated OpenAPI differ.

Readiness returns `status`, `databaseReachable`, `schemaCurrent`, and `checkedAt`. Pending EF Core migrations produce a degraded/unhealthy readiness response even when PostgreSQL accepts connections. Public API requests are protected by a configurable fixed-window `ApiRateLimit`; application policy and durable worker limits remain independently authoritative.

Opportunity approval, campaign creation, campaign start, and verification start require `Idempotency-Key`. Campaign creation accepts only an `authorizationProfileKey` naming server configuration plus a non-secret `authorizationReference`; it never accepts an endpoint or credential. A manual campaign requires every selected opportunity to have been explicitly approved. Start returns newly queued persistent job IDs and does not wait for execution. Submission states are not verified backlinks.

Credential create, rotate, and revoke require `Idempotency-Key` and `admin`. Create accepts a name, 1-50 values from the documented scope catalog, and an optional expiry no more than five years in the future. Create and rotate return the plaintext API key only in their first successful response. An idempotent replay returns the same credential metadata with `apiKey: null` and `displayed: false`; the secret cannot be recovered. Rotation revokes the prior credential atomically. Revocation rejects self-revocation so the caller cannot accidentally strand itself; self-rotation is allowed.

Campaign start applies the current project policy, blocklist, explicit approvals, named authorization profile, duplicate rules, and project/campaign/domain limits before any job is queued. Its result includes `jobsQueued`, `jobIds`, `approvalRequired`, `rejected`, `manualActionRequired`, and the durable campaign `status`. Calling start later with a new request key can queue newly approved selections; existing submission records prevent duplicates. Workers independently repeat the security-sensitive checks before network I/O.

Job-control mutations require `Idempotency-Key`. Pausing queued work is immediate. Pausing active work records a request; the owning worker observes it during lease renewal, cancels the handler, and acknowledges the durable `paused` state. Redrive is accepted only for `failed` or `deadLetter` jobs and resets the attempt budget explicitly; it never happens automatically.

Verification accepts only a stored `backlinkId`; clients cannot supply an arbitrary source URL. The worker records `Verified` only after finding an anchor whose normalized destination exactly matches the stored normalized target. Status values are `pendingVerification`, `verified`, `missing`, `lost`, and `error`. Every attempt creates a new history row.

Schedule mutations require `Idempotency-Key`. The body contains a typed `action` and `timing`. `actionType` is `discovery`, `analysis`, or `verification`; discovery uses the normal provider fields, while verification accepts either `backlinkId` or an optional backlink `status` plus `maximumBacklinks` (1-500). `recurrenceType` is `oneTime`, `interval`, `daily`, `weekly`, `monthly`, or `cron`. Timestamps and `timeOfDayUtc` are UTC; cron uses the standard five-field format. Timing fields unrelated to the selected recurrence must be omitted or null. Delete is soft so audit history and operational evidence remain.

Report generation requires `Idempotency-Key`. The body is `{ projectId, campaignId?, kind, format }`; `kind` is `campaignPerformance` or `backlinkInventory`, and `format` is `json`, `csv`, `xlsx`, or `html`. `campaignPerformance` requires a campaign belonging to the project; `backlinkInventory` may optionally filter to a campaign. The accepted response is asynchronous. Poll `/jobs/{jobId}`, then read `/reports/{reportId}`. Download returns `409` until the report is complete, `404` for an unknown report, and a digest-based ETag for a completed artifact.

Report pages follow the same opaque cursor and 100-item maximum as other lists. Report metadata includes lifecycle, format/kind, campaign/job linkage, row and byte counts, SHA-256, timestamps, and a bounded sanitized failure message. Artifact content is never returned by list/get endpoints.

## Owned-network REST resources

All mutations below require `Idempotency-Key`; all lists are bounded and cursor-based.

The normal placement path is `POST /api/v1/backlink-workflows` with `projectId`, either `sourceUrls` or `sourceImportId`, either inline `identities` or `identityPoolId`, either inline `comments` or `templatePoolId`, and one `targetUrl`. Concurrency, domain delay, attempt count, verification delay, and `clientRequestKey` are optional. Ownership, authorization flags, profiles, credentials, adapters, endpoints, post IDs, and browser configuration are deliberately absent. START normalizes, deduplicates, and persists every source as queued, then returns the durable workflow/job identifiers. The worker resolves each source through the canonical persisted project/network authorizer, classifies unknown hosts as `NotAuthorized`, and continues the rest of the batch. Authorized sources automatically follow the bounded standard -> fallback -> browser strategy chain. A submission that may have produced a backlink creates an independently scheduled verification job; only that verifier may advance the per-source result to `Verified`. A `PendingModeration` result remains distinct when the link is not yet visible.

| Method | Route | Scope | Result |
|---|---|---|---|
| GET/POST | `/api/v1/owned-networks` | `owned_networks:read/write` | profile page / profile |
| GET/PUT | `/api/v1/owned-networks/{id}` | `owned_networks:read/write` | profile with domain rules |
| POST | `/api/v1/submission-sources/import` | `submission_sources:write` | profile-free durable import/job IDs; per-source authorization is resolved internally |
| GET | `/api/v1/submission-source-imports/{id}` | `submission_sources:read` | import counters/status |
| GET | `/api/v1/submission-sources` | `submission_sources:read` | filtered keyset page |
| GET | `/api/v1/submission-sources/{id}` | `submission_sources:read` | detected source details |
| POST | `/api/v1/submission-sources/validate` | `submission_sources:write` | durable validation job |
| GET/POST/PUT | `/api/v1/identity-pools...` | `submission_identities:read/write` | pools and identities |
| GET/POST/PUT | `/api/v1/submission-templates...` | `submission_templates:read/write` | pools and templates |
| POST | `/api/v1/submissions/preview` | `submissions:read` | deterministic no-I/O preview |
| GET | `/api/v1/submissions/{id}` | `submissions:read` | durable submission state |
| GET | `/api/v1/submissions/{id}/attempts` | `submissions:read` | bounded immutable attempts |
| GET/POST/PUT | `/api/v1/wordpress-site-profiles...` | `admin` | server credential references only |

`GET /api/v1/submission-sources` accepts the catalog filters `ownedNetworkProfileId`, `domain`, `platform`, `cmsType`, `adapterName`, `ownershipStatus`, `automationPermitted`, `technicalCompatibility`, `validationStatus`, `enabled`, `tag`, `previousSubmissionStatus`, and `previousVerificationStatus`, plus the normal `limit` and opaque `cursor`.

`POST /api/v1/campaigns/owned-network` creates the extended campaign configuration. Its optional `previousSubmissionStatus` and `previousVerificationStatus` fields persist with the campaign and are applied by durable PostgreSQL expansion. Start returns immediately after queueing durable expansion. Preview/manual modes do not execute. Automatic mode requires current owned-network and project policy authorization. Import bodies are streamed; use REST or CLI rather than MCP's JSON content field for very large files.

Report summaries include attempt count, submission success rate, verification rate, and bounded top-100 performance groups by domain, template, and identity. CSV and HTML include a performance section. XLSX includes `Summary`, `Performance`, and filterable `Details` worksheets; detail worksheets roll over at the Open XML row limit.

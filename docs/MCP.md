# MCP Contract

BacklinkStudio supports authenticated remote JSON-RPC over `POST /mcp` and local newline-delimited JSON-RPC over STDIO. Protocol negotiation advertises `2025-06-18`. Remote requests require `X-Api-Key` or `Authorization: Bearer`.

Supported MCP methods are `initialize`, `notifications/initialized`, `ping`, `tools/list`, and `tools/call`. Tool results use MCP text content containing a JSON object. Long operations return a job ID immediately.

## Implemented tools

| Tool | Scope | Important input |
| --- | --- | --- |
| `system_health` | authenticated | none |
| `agent_credentials_list` / `agent_credentials_get` | `admin` | bounded redacted credential metadata |
| `agent_credential_create` | `admin` | name, scopes, optional expiry, clientRequestKey; key shown once |
| `agent_credential_rotate` / `agent_credential_revoke` | `admin` | credentialId, optional expiry, clientRequestKey |
| `projects_list` / `projects_get` | `projects:read` | cursor/limit or projectId |
| `project_create` | `projects:write` | name, primaryDomain, description?, clientRequestKey |
| `targets_list` | `projects:read` | projectId, cursor, limit |
| `target_add` | `projects:write` | projectId, url, metadata, clientRequestKey |
| `policy_get` / `policy_update` | project read/write | projectId, bounded thresholds/limits, write request key |
| `blocklist_list` / `blocklist_add` | project read/write | projectId, normalized domain/URL-prefix rule, write request key |
| `candidates_list` / `candidates_get` | `candidates:read` | project/page or candidateId |
| `candidates_import` | `projects:write` | projectId, urls (maximum 1,000), clientRequestKey |
| `discovery_start` | `projects:write` | projectId, provider, provider input, maximumResults, clientRequestKey |
| `discovery_runs_list` / `discovery_run_get` | `projects:read` | projectId/page or discoveryRunId |
| `opportunities_list` / `opportunities_get` | `opportunities:read` | projectId/filter/id |
| `opportunities_summary` | `opportunities:read` | projectId |
| `opportunities_approve` | `opportunities:approve` | projectId, 1-100 opportunityIds, clientRequestKey |
| `campaign_create` | `campaigns:write` | project/target/opportunities, mode, named profile, authorization reference, limit, request key |
| `campaigns_list` / `campaigns_get` / `campaigns_status` | `campaigns:read` | project/page or campaignId |
| `campaign_update` | `campaigns:write` | draft name, approvalMode, dailyActionLimit, clientRequestKey |
| `campaign_start` | `submissions:execute` | campaignId, clientRequestKey |
| `campaign_pause` / `campaign_resume` / `campaign_stop` | `campaigns:execute` | campaignId, clientRequestKey |
| `submissions_list` / `submission_get` / `submission_attempts` | `submissions:read` | campaign/page or submissionJobId |
| `verification_start` | `verification:execute` | stored backlinkId, clientRequestKey |
| `backlinks_list` / `backlinks_get` | `backlinks:read` | project/status/page or backlinkId |
| `verification_history` | `backlinks:read` | backlinkId, cursor, limit |
| `schedules_get` / `schedules_list` | `schedules:read` | scheduleId or projectId/page |
| `schedule_create` / `schedule_update` | `schedules:write` | typed action, UTC timing, clientRequestKey |
| `schedule_pause` / `schedule_resume` / `schedule_delete` | `schedules:write` | scheduleId, clientRequestKey |
| `reports_get` / `reports_list` | `reports:read` | reportId or projectId/page |
| `report_generate` | `reports:write` | projectId, campaignId?, kind, format, clientRequestKey |
| `analysis_start` | `projects:write` | projectId, clientRequestKey |
| `jobs_get` / `jobs_list` | `projects:read` | jobId or projectId/cursor/limit |
| `jobs_pause` / `jobs_resume` | `projects:write` | jobId, clientRequestKey |
| `jobs_redrive` | `projects:write` | failed/dead-letter jobId, clientRequestKey |
| `backlink_workflow_start` | `submissions:execute` | sources, identities, comments, targetUrl, optional execution limits, clientRequestKey |
| `backlink_workflow_get` | `submissions:read` | workflowId, cursor, limit |

MCP errors use JSON-RPC codes for invalid requests/methods/parameters. Tool-domain errors set `isError: true` and return a sanitized problem object; unexpected failures return JSON-RPC `-32603` without exception details. Tool payload enum values use camel case strings. Tool implementations contain parameter mapping only; Application services own behavior.

`system_health` returns `status`, `databaseReachable`, `schemaCurrent`, and `checkedAt`; a reachable database with pending migrations is not ready. Remote MCP requests use a configurable fixed-window `McpRateLimit` in addition to API-key scopes.

## STDIO authorization

STDIO has no network authentication or transport rate limiter, so access to the process remains an operating-system trust boundary. It does not imply administrator authority. With no `McpStdio` configuration, the process receives the read-only scopes in `AuthorizationScopes.DefaultStdio`; `tools/list` returns only tools permitted by those scopes, and credential administration, writes, approvals, and execution are unavailable.

Operators may explicitly replace that scope set through `McpStdio:Scopes`. Unknown scope names fail startup. Credential administration requires both the `admin` scope and the separate `McpStdio:AllowCredentialManagement` opt-in: `admin` without the opt-in fails startup, while the opt-in alone grants no additional scope. For example:

```bash
# Grant one non-administrative execution capability.
McpStdio__Scopes__0=campaigns:execute dotnet run --project src/BacklinkStudio.Mcp -- --stdio

# Explicitly grant administrator authority to this local process.
McpStdio__Scopes__0=admin \
McpStdio__AllowCredentialManagement=true \
dotnet run --project src/BacklinkStudio.Mcp -- --stdio
```

Treat elevated STDIO configuration like a privileged service account: restrict who can launch or attach to the process, and grant only the scopes required by the client.

## Agent rules

- Use authorized reads freely, but always paginate.
- Never claim a backlink exists unless its stored verification state is `Verified`.
- Do not bypass `ManualActionRequired`, approvals, policy rejection, or campaign limits.
- Poll `jobs_get`; do not repeat a mutation when work is merely pending. Reuse the same client request key for safe retries.
- Never expose credentials or request arbitrary network/shell/SQL/browser actions.
- Credential secrets are display-once. Store a newly created or rotated key immediately in an approved secret store; an idempotent replay intentionally returns only redacted metadata. Never place a key in prompts, logs, audit summaries, or tool arguments other than transport authentication.
- Treat analysis signals and `existingTargetLink` as observations only; only a stored `Verified` backlink may be reported as confirmed.
- Treat `discovery_start` as asynchronous: retain its job ID, poll `jobs_get`, then read the discovery run. Never retry with a new request key merely because the MCP session ended.
- A pause of active work is asynchronous. Poll `jobs_get` until the status is `paused`; do not repeat the tool with a new request key.
- Use `jobs_redrive` only after reviewing the classified failure and correcting its cause. Never redrive policy, authorization, invalid-input, or unsupported failures as a bypass.
- Never pass or request an arbitrary endpoint. `campaign_create` may name only a server-configured authorization profile. Treat `Submitted` and `PendingModeration` as submission outcomes, never as verified backlinks.
- Read the policy disposition counts returned by `campaign_start`. Do not bypass approval-required, rejected, or manual-action selections. After legitimate approval, use a new request key to start the same running campaign again; already queued submissions are deduplicated. A stopped campaign cannot resume.
- `verification_start` accepts a stored backlink ID and returns a durable job ID. Poll `jobs_get`, then read `backlinks_get` and `verification_history`; do not infer success from the job alone.
- Schedule tools accept only typed discovery, analysis, or verification actions. Cron and calendar fields are UTC. Do not create a replacement merely because an MCP session ended; schedules and their jobs persist independently. Reuse the same client request key when retrying mutations.
- `report_generate` is asynchronous. Retain both IDs, poll `jobs_get`, then use `reports_get`; do not repeat generation with a new client request key because an MCP session closed. MCP intentionally returns metadata only, so download a completed artifact through the scoped REST endpoint. A report's `verified` count derives only from stored `Verified` backlinks.

## Owned-network tools

The default operator/AI path needs only `backlink_workflow_start` followed by bounded polling of `backlink_workflow_get`. The start contract exposes no ownership status, automation flag, profile ID, credential reference, adapter, compatibility, post ID, endpoint, selector, JavaScript, or browser option. `clientRequestKey` is optional; when omitted the transport request ID supplies a scoped idempotency key. Server-side registered networks remain the authorization boundary: an unknown host becomes `NotAuthorized`, never owned, never triggers an ownership question, and does not stop other sources. Authorized sources automatically progress through bounded static detection, standard submission, tolerant fallback, controlled browser fallback, and independent verification.

The owned workflow exposes `owned_network_create/list/get/update`, `submission_sources_import/list/get/validate`, `submission_source_import_get`, `identity_pool_create/list/get/update`, `identity_create/update`, `template_pool_create/list/get/update`, `template_create/update`, `submission_preview`, `campaign_create/get/start/pause/resume/stop`, `submissions_list`, `submission_get`, `submission_attempts`, `backlinks_list`, `verification_start`, and `reports_generate`. Compatibility aliases for older plural campaign/report reads remain available.

Every mutation requires `clientRequestKey`. Import and campaign start return durable identifiers; the MCP client may disconnect immediately. Poll the import/job/campaign resources after reconnecting. `submission_preview` performs deterministic resolution only and never makes a network request.

`submission_sources_list` and owned-network `campaign_create` accept optional `previousSubmissionStatus` and `previousVerificationStatus` values. These are typed enum filters evaluated in PostgreSQL; MCP does not accept a query expression or regex.

Tool discovery is scope-filtered. Owned network, source, identity, template, and submission scopes are separate. `campaign_start` requires `submissions:execute`; lifecycle pause/resume/stop retains `campaigns:execute`. Default STDIO has read scopes only and cannot start a campaign or execute a submission. `WordPressSiteProfile` create/update/list requires `admin` and accepts a server credential *reference*, not the credential value.

The 2026-08-23 closure build contains 84 administrator-visible named tools. This count includes compatibility aliases; a client sees only the subset authorized by its current scopes. The count is release evidence, not a compatibility promise that clients should hard-code.

For the exact agent sequence, see [BACKLINK-CAMPAIGNS.md](BACKLINK-CAMPAIGNS.md).

# Milestone 8 Acceptance

Milestone 8 completes the vendor-neutral agent-control layer. It does not add reporting formats or arbitrary external execution.

## Acceptance criteria

- REST and remote MCP use the same API-key authentication and Application scope catalog.
- An administrator can create, list, inspect, rotate, and revoke agent-specific credentials through REST and MCP.
- Generated API keys have at least 256 bits of random material, are stored only as one-way hashes, and are displayed only in the first successful create/rotate response.
- Credential idempotency replays are redacted; audit and durable idempotency rows contain no plaintext key.
- Unknown scopes, expired/overlong expiry, self-revocation, revoked authentication, and unauthorized operations are rejected.
- Concurrent rotation of one credential cannot commit two replacements.
- MCP exposes bounded high-level business tools only, including candidate detail, campaign status/update/start/pause/resume/stop, schedules, jobs, and agent credentials.
- MCP tool discovery is scope-filtered, and direct unauthorized tool calls are rejected.
- Campaign approval modes are enforced during start preflight. Results distinguish allowed, rejected, approval-required, and manual-action-required selections.
- Campaign pause/resume/stop changes persistent jobs; stopped campaigns cannot resume.
- Submission workers independently re-evaluate campaign state, policy, authorization profile, blocklist, thresholds, duplicates, and limits before network execution.
- Important mutations require an HTTP `Idempotency-Key` or MCP `clientRequestKey` and reject key reuse with different input.
- Audit events remain append-only and contain actor/credential/request/result metadata without secrets.
- Unit, PostgreSQL integration, contract, and architecture suites pass; formatting and dependency checks are clean.
- Existing migrations apply to a clean PostgreSQL database; Milestone 8 requires no schema migration.
- Linux containers build and run as non-root, health endpoints pass, scoped REST/MCP behavior is verified live, and `linux-x64` plus `win-x64` publication succeeds.

## Agent workflow

1. Bootstrap an administrator and create a least-privilege agent credential; store its display-once key securely.
2. The agent reads projects, candidates, opportunities, campaigns, jobs, backlinks, and schedules only within its scopes and bounded pages.
3. The agent approves permitted opportunities and starts a campaign with a client request key.
4. The server returns persistent job IDs plus policy disposition counts; the connection may close without affecting execution.
5. The agent polls `jobs_get` and uses campaign pause/resume/stop controls without executing work itself.
6. The worker independently rechecks authorization policy before any configured owned/permitted submission.

Milestone 9 reporting is explicitly outside this milestone.

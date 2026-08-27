# Owned-Network Backlink Campaigns

An owned-network campaign binds a project, approved network, target URL, identity pool, template pool, PostgreSQL source filter, concurrency/delay limits, maximum attempts, verification delay, and mode. Modes are preview, manual approval, and automatic owned network. Only the automatic mode expands into submission jobs, and only while current project policy and network authorization permit it.

Source selection runs in PostgreSQL with keyset pages of 500. Filters cover network, domain, platform, CMS, compatibility, validation state, enabled state, tag, previous submission state, and previous backlink-verification state. Prior-state predicates use indexed correlated existence checks and therefore do not materialize the catalog. Each selected row becomes an existing durable PostgreSQL job; expansion never creates one in-process task per URL. The unique campaign/source/target/placement constraint prevents equivalent queued/running/submitted/moderation/verification work from being created twice.

Workers use bounded channels only as local dispatch. PostgreSQL jobs, leases, heartbeats, recovery, retries, dead letter, pause/resume/stop, and idempotency survive client/process/machine restarts. Claiming enforces configured operational limits plus campaign global and per-domain caps. A PostgreSQL rate reservation enforces minimum domain delay across replicas. Workers repeat ownership, network-domain matching, enabled state, project policy, extended blocklist, action limits, and duplicate checks immediately before I/O.

Every network execution first stores a `SubmissionAttempt` containing only non-secret resolved identity/template values and strategy metadata. If a worker dies with an unfinished attempt, recovery marks the submission `ReconciliationRequired` and queues verification instead of blindly posting the comment again.

Accepted or pending-moderation results create one backlink candidate in `PendingVerification`. Advanced campaign execution may also create a delayed verification job; the simple one-click/TXT path does not. The backlink stores explicit source and attempt provenance in addition to its durable submission-job association. Verification requires actual anchor evidence. Clean first absence becomes `Missing`, clean absence after evidence becomes `Lost`, and transport errors become `Error`. Use persistent verification schedules for hourly/daily/weekly/monthly/cron monitoring.

Advanced MCP agent sequence:

1. create/select a network;
2. import TXT/CSV and poll the durable import job;
3. start validation and poll durable jobs;
4. preview sample sources;
5. create and start the campaign;
6. disconnect;
7. reconnect later to read campaign/submissions/attempts/backlinks and queue reports.

The API, MCP session, or CLI process is never responsible for campaign lifetime.

For the normal workflow, call only `backlink_workflow_start` with sources, name/email identities, comments, and the target URL, then poll `backlink_workflow_get`. START persists normalized sources and returns immediately. The durable coordinator resolves each source through the single persisted project/network authorizer, creates workflow-scoped deterministic pools, internally groups authorized hosts into campaigns, validates and selects the standard/fallback/browser path, queues submission and verification work, and reports unauthorized sources as `NotAuthorized` without interrupting eligible sources. `Submitted` never implies `Verified`; only the automatically scheduled independent verifier can advance the result. CLI parity is `backlinkstudio backlinks start --sources urls.txt --identities identities.csv --comments comments.txt --target https://example.com/ --concurrency 8 --delay-ms 1000`.

The controlled WordPress acceptance starts two worker replicas by default. It replays the TXT-import and campaign-start requests to prove stable durable identifiers, then reuses each key with changed input to prove conflict rejection before allowing the workers to process the campaign.

The high-level CLI command stages the file and creates a durable `OwnedNetworkWorkflow` job:

```powershell
backlinkstudio backlinks run --sources owned-sites.txt --project <project-id> --network my-network --target https://target.example/ --identity-pool network-identities --template-pool comments --concurrency 100
```

Network and pool arguments accept either a UUID or a project-scoped name. The command returns workflow, import, and import-job IDs and exits. Durable workflow continuations wait for import, queue and wait for validation, then idempotently create/start the automatic campaign. A process restart cannot lose the transition, and retries reuse stable workflow keys.

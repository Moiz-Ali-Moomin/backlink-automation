# Owned Networks

`OwnedNetworkProfile` is the authorization boundary for source-catalog automation. A profile belongs to one project and contains normalized `OwnedNetworkDomain` rules. Supported ownership states are `Unverified`, `Owned`, `Controlled`, `PartnerControlled`, and `ExplicitPermission`. Automatic execution requires a non-`Unverified` state, `AutomationPermitted=true`, `Enabled=true`, and a currently matching enabled domain rule.

`IOwnedNetworkExecutionAuthorizer`, implemented by `OwnedNetworkExecutionAuthorizer`, is the single runtime decision point. It reads the current project policy, profile, and exact/subdomain rule from persistence for every source-stage check. Workflow, validation, campaign expansion, submission, preview, and browser verification consume that decision instead of reconstructing eligibility. The Development/Test exact-host override is a separate decorator and cannot override a disabled project automation policy.

Rules are deterministic and do not accept regular expressions:

- `ExactHost` matches one normalized host only.
- `ExactDomain` matches that normalized domain only.
- `SubdomainOf` matches the configured domain and its label-boundary subdomains.

Imported hints never grant ownership. TXT and CSV imports must select an existing profile; each URL is independently matched to its profile rules. An unmatched row is retained as `Unverified` with automation disabled so it can be audited, but workers reject it before network I/O. Updating or disabling a profile takes effect immediately because validation and submission workers repeat the current ownership check.

Profiles also supply default identity/template pools and bounded global concurrency, per-domain concurrency, and minimum per-domain delay. These settings are copied into a campaign for reproducibility, while the current authorization decision is still re-evaluated during execution.

The simple workflow persists every normalized, deduplicated URL as `Queued` before authorization. Its durable worker resolves authorization per source, links eligible sources to internally created campaigns, and marks unmatched sources `NotAuthorized` without failing or pausing the rest of the batch. Generic TXT/CSV import stores input as unverified and never grants execution authorization.

Private owned infrastructure uses the separate `SubmissionSourceHttp` controlled-network allowlist. The default public SSRF policy remains unchanged. Allow only exact private DNS names/IPs under operator control; never enable loopback HTTP outside automated tests.

Project blocklists can exclude a domain, exact host, exact URL, URL prefix, submission source ID, owned-network profile ID, or campaign ID. The worker checks these exclusions immediately before placement.

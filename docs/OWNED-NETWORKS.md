# Owned Networks

`OwnedNetworkProfile` groups source-catalog automation for one project and contains normalized `OwnedNetworkDomain` rules. Supported ownership states are `Unverified`, `Owned`, `Controlled`, `PartnerControlled`, and `ExplicitPermission`.

`IOwnedNetworkExecutionAuthorizer` is implemented by `OwnedNetworkExecutionResolver`, which **resolves** the profile a source is recorded against rather than gating execution on it. The previous `OwnedNetworkExecutionAuthorizer` — which required project automation policy, a non-`Unverified` profile, `AutomationPermitted=true`, and a matching domain rule before any source could run — has been removed. Resolution order for a source is:

1. the profile explicitly requested on the source or import, when it is enabled and not `Unverified`;
2. the best matching enabled domain rule (exact before subdomain, then oldest profile), when its profile is enabled and not `Unverified`;
3. the `Default execution network` profile, created once per project on first use with `Owned` ownership, automation permitted, and the domain-model maximum concurrency ceilings so it never lowers a caller's own limits.

Enabled/`Unverified` filtering in steps 1 and 2 is not an ownership gate — the domain model and the `ck_submission_sources_automation_ownership` / `ck_owned_network_profiles_automation_ownership` check constraints reject an unverified association, so an unusable profile falls through to step 3 instead of failing the source. A source the operator explicitly disabled is still refused, because that is a direct instruction. `AuthorizeProfileAsync` returns the requested profile whenever it exists, regardless of its ownership state, and falls back to the default profile only when the requested profile is missing.

Two workers that first touch the same brand-new project concurrently can both attempt to create the default profile; the unique `(project_id, name)` index makes one of them fail and retry, at which point it finds the persisted profile.

Rules are deterministic and do not accept regular expressions:

- `ExactHost` matches one normalized host only.
- `ExactDomain` matches that normalized domain only.
- `SubdomainOf` matches the configured domain and its label-boundary subdomains.

TXT and CSV imports may select a profile; each URL is independently resolved through the order above, so an unmatched row is attached to the default execution network instead of being retained as unexecutable. Updating or disabling a profile takes effect immediately because validation and submission workers repeat the current resolution.

Profiles also supply default identity/template pools and bounded global concurrency, per-domain concurrency, and minimum per-domain delay. These settings are copied into a campaign for reproducibility, while the current profile resolution is still re-evaluated during execution.

The simple workflow persists every normalized, deduplicated URL as `Queued`, then its durable worker resolves each source to a profile and links it to an internally created campaign. `NotAuthorized` is now reached only through a disabled source, a project blocklist match, or a transport-level `AuthorizationDenied` — never through a missing owned-network domain rule.

Private owned infrastructure uses the separate `SubmissionSourceHttp` controlled-network allowlist. The default public SSRF policy remains unchanged. Allow only exact private DNS names/IPs under operator control; never enable loopback HTTP outside automated tests.

Project blocklists can exclude a domain, exact host, exact URL, URL prefix, submission source ID, owned-network profile ID, or campaign ID. The worker checks these exclusions immediately before placement.

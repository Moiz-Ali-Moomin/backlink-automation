# Identity Pools

Identity pools hold bounded, non-secret owned-network identities. An identity records display name, email, optional website/organization, enabled state, weight, and usage count. BacklinkStudio does not create accounts or send email.

Selection modes are round-robin, deterministic random, and weighted deterministic random. Selection is reproducible from campaign ID, submission-source ID, and attempt number using a cryptographic hash; no process-global random state is used. The selected IDs and resolved display name/email/website are stored on each immutable attempt.

Email strategies are fixed configured values, alias-pool values, plus addressing, catch-all derivation, and pre-created synthetic identities. Plus/catch-all resolution is deterministic and produces only the non-secret address required for submission history. Mailbox credentials are never accepted by campaigns, REST, MCP, CLI, or job payloads.

Use `identity_pool_create/list/get/update` and `identity_create/update` over MCP, the `/api/v1/identity-pools` REST resources, or the equivalent `identity-pool` and `identity` CLI commands.

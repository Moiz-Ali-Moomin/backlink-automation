# Milestone 6 Acceptance Tests

Milestone 6 is accepted when all of the following pass:

1. A successful permitted submission creates one `PendingVerification` backlink candidate and does not set first-seen evidence.
2. `verification_start` and `POST /api/v1/verification/jobs` accept only a stored backlink ID, require `verification:execute`, support idempotency, and return a persistent job ID.
3. The worker claims and executes the verification job independently of REST/MCP sessions.
4. A real parsed anchor whose normalized destination exactly matches the target produces `Verified`, anchor text, canonical URL, HTTP status, rel tokens, and follow flags.
5. HTTP 200 without the matching anchor produces `Missing`, never `Verified`.
6. A clean absence after a prior verified observation produces `Lost`; transient/network failures produce `Error` instead.
7. Every run appends a `verification_checks` row; EF and PostgreSQL reject update/delete mutations.
8. Backlink and history reads are bounded and scope-protected in REST and MCP.
9. The clean PostgreSQL migration chain includes the restored Milestone 5 migration followed by `AddBacklinkVerification`.
10. Unit, integration, contract, architecture, formatting, Linux Docker, and Linux/Windows publish checks pass.

The test workflow uses only in-memory HTTP handlers, deterministic verifier doubles, and the owned loopback submission endpoint. It never posts to or verifies arbitrary public sites.

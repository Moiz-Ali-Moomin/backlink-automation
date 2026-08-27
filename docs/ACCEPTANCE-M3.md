# Milestone 3 Acceptance Tests

Milestone 3 is accepted when all previous tests remain green and this workflow succeeds against a clean migrated PostgreSQL database:

1. Create a project and add a domain blocklist rule.
2. Call REST or MCP `discovery_start` with a client request key and a manual, TXT, CSV, sitemap, Serper, or competitor-import request.
3. Confirm the call returns durable discovery-run and job IDs immediately.
4. Repeat the same request key and input; confirm the same IDs are returned and no duplicate query, run, or job is created.
5. Let the worker claim and execute the job after the client disconnects.
6. Confirm invalid, duplicate, and blocked URLs are counted and not persisted as candidates.
7. Confirm accepted normalized URLs are persisted once and the discovery run becomes `Succeeded` with timestamps and bounded error details.
8. Confirm enqueue/completion audit events contain summaries rather than raw import content or credentials.
9. Confirm unauthorized REST/MCP discovery calls are rejected and list calls remain bounded.
10. Confirm clean migration, Linux container build/start/health, `linux-x64` and `win-x64` publish, formatting, vulnerability scan, and all automated tests.

No step performs submission, CAPTCHA handling, browser automation, or arbitrary network execution.

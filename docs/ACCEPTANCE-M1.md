# Milestone 1 Acceptance Tests

Milestone 1 is accepted only when all applicable checks below pass. PostgreSQL/Docker checks are not waived when a workstation lacks Docker; they remain required on CI or another Docker-enabled host and must be reported as unverified locally.

| Area | Executable proof |
| --- | --- |
| Build | `dotnet build BacklinkStudio.sln -c Release` succeeds with warnings treated as errors |
| Unit | URL canonicalization subset, cursor bounds, API-key hashing, and job transition tests pass |
| Architecture | Domain/Application references and transport/worker source-boundary tests pass |
| Contracts | REST routes/docs and MCP JSON-RPC/tool catalog tests pass; no dangerous generic tools exist |
| Migration | All migrations apply in order to a clean PostgreSQL 17 container |
| Auth | Seeded plaintext key is absent from storage; missing/invalid API keys receive 401; disabled users and insufficient scopes are rejected |
| Workflow | Create project → add target → import candidates → enqueue analysis → claim/run worker handler → retrieve succeeded job/opportunity |
| Durability | Two workers cannot claim one active job; an expired claim is recoverable with incremented attempt count |
| Audit | Bootstrap, mutation, enqueue, and worker completion events persist without secret material; PostgreSQL rejects update/delete attempts |
| Health | API/MCP live checks are healthy; ready checks require PostgreSQL |
| Containers | Linux image builds, non-root services start, migration exits successfully, and Compose health checks pass |
| Cross-platform | Windows build and `win-x64` publish succeed; core projects contain no OS-specific dependencies |

Container tests run when `BACKLINKSTUDIO_RUN_CONTAINER_TESTS=1`. CI sets it on its Docker-enabled Linux runner.

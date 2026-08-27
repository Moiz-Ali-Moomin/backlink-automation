# Milestone 4 Acceptance Tests

Milestone 4 is accepted only when all of the following pass on a clean PostgreSQL database and Linux container runtime:

1. The complete solution restores, formats, builds with warnings as errors, and all unit, integration, contract, and architecture tests pass without skips in the authoritative Docker run.
2. The `AddProductionWorkerPlatform` migration applies after all five existing migrations and creates worker heartbeat and domain-rate reservation state without changing the existing Milestone 3 migration identity.
3. Concurrent workers claim distinct rows atomically; global and per-project claim limits cannot be exceeded by concurrent claim transactions.
4. A running worker renews its lease and process heartbeat. An expired lease is recovered once, increments recovery history, and cannot be completed by the stale owner.
5. Retryable failures receive bounded exponential backoff and terminate in `DeadLetter`; permanent failures terminate in `Failed` without retry.
6. Queued jobs pause immediately. Active jobs persist a pause request, are cancelled by their owning worker, become `Paused`, and resume through `RetryScheduled`.
7. Failed/dead-letter redrive is explicit, authenticated, scoped, idempotent, and audited through both REST and MCP business operations.
8. Analysis, sitemap, and Serper network operations use the PostgreSQL per-domain reservation service.
9. Two Compose worker replicas process durable jobs while API/MCP disconnects do not affect execution. Worker containers remain non-root, read-only, and without published ports.
10. SIGTERM stops new acquisition; active work releases safely or remains recoverable after lease expiry.

No submission adapter or real external submission is part of this milestone.

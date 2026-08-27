# Submission Source Import

TXT input is UTF-8 with one HTTP(S) URL per line. LF and CRLF are accepted. Blank lines and lines whose first non-space character is `#` are ignored. CSV requires a `url` header and may include `platform`, `cms`, `tag`, and `enabled`; ownership hints are intentionally ignored.

CLI example:

```powershell
backlinkstudio sources import acceptance\owned-sites.txt --project <project-id> --request-key import-2026-08-23
```

REST uses `POST /api/v1/submission-sources/import?projectId=...&format=Txt&fileName=owned-sites.txt` with the file as the request body and `Idempotency-Key`. MCP uses `submission_sources_import`; because MCP arguments are JSON, its bounded `content` field is intended for moderate inputs. Use REST or CLI streaming for large files. None of these normal import paths accepts a profile ID. The worker resolves each normalized host against persisted project/network policy through the canonical execution authorizer. A generic upload never grants authorization; unmatched sources remain `Unverified` with automation disabled and do not stop authorized rows in the same batch.

The request only stages bounded 64 KiB chunks in PostgreSQL and queues a durable import job. It returns `importId` and `jobId` immediately. Poll `submission_source_import_get`, the REST import resource, or `sources import-status`. The worker streams chunks, normalizes URLs, parses without loading the file, batches 1,000 rows, and uses PostgreSQL conflict handling against project-scoped normalized URL uniqueness. Counters distinguish total records, accepted rows, duplicates, invalid URLs, and errors. Reusing the same request key and body returns the same identifiers; changing the body with the same key is rejected.

After import, queue validation with `submission_sources_validate`, `POST /api/v1/submission-sources/validate`, or `sources validate`. Validation is durable and fans out in bounded 500-row pages. Lists always use opaque keyset cursors and never materialize the catalog.

For repeatable controlled throughput measurement against an isolated running stack, use `scripts/benchmark-source-import.sh 100000` with `BACKLINKSTUDIO_BENCHMARK_CONFIRM=owned-network-source-import`, `BACKLINKSTUDIO_API_KEY`, and optional `BASE_URL`. The harness generates only `.invalid` owned-network URLs, streams the TXT body through REST, polls the durable job, validates all counters, reports staging and end-to-end rows/second, and removes its temporary file. Accepted sizes are 10,000, 100,000, and 1,000,000 rows.

For a one-command CLI handoff, use `backlinkstudio backlinks run`; its durable workflow job owns the import → validation → campaign-start transitions after the CLI exits.

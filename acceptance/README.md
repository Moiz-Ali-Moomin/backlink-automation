# Owned-Network Acceptance

`owned-sites.txt` contains only service names in the controlled acceptance network. Do not replace them with arbitrary public websites.

Start the engine database first so the external `backlinkstudio_backend` network exists. Configure the worker's explicit controlled transport values before it starts:

```powershell
$env:OWNED_PRIVATE_HOST_0='wordpress-open'
$env:OWNED_PRIVATE_HOST_1='wordpress-moderated'
$env:OWNED_PRIVATE_HOST_2='wordpress-closed'
$env:OWNED_INSECURE_HTTP_HOST_0='wordpress-open'
$env:OWNED_INSECURE_HTTP_HOST_1='wordpress-moderated'
$env:OWNED_INSECURE_HTTP_HOST_2='wordpress-closed'
$env:BACKLINKSTUDIO_ACCEPTANCE_BACKEND_NETWORK='backlinkstudio_backend'
docker compose up -d postgres redis
docker compose -f acceptance/wordpress/docker-compose.yml up -d wordpress-db wordpress-open wordpress-moderated wordpress-closed
docker compose -f acceptance/wordpress/docker-compose.yml run --rm wordpress-bootstrap
docker compose run --rm backlinkstudio-migrate
docker compose up -d backlinkstudio-api backlinkstudio-mcp backlinkstudio-worker backlinkstudio-scheduler
```

The three instances cover immediate publication, moderation, and comments closed. The integration TCP fixture additionally exercises duplicate, invalid email/post, rate limit, login, temporary, and permanent responses deterministically. Configure an owned network with exact-host rules for all three names, then use the documented MCP sequence or `backlinkstudio backlinks run`.

On a disposable Docker host, the complete isolated MCP-driven scenario can be run with:

```bash
BACKLINKSTUDIO_ACCEPTANCE_CONFIRM=owned-network-wordpress \
COMPOSE_PROJECT_NAME=backlinkstudio_owned_acceptance \
bash scripts/run-owned-network-acceptance-stack.sh
```

The wrapper refuses to reuse a non-empty Compose project, generates ephemeral secrets without printing them, builds the production image, migrates a clean PostgreSQL database, starts API/MCP/scheduler plus two worker replicas and all three WordPress sites, runs `scripts/acceptance-owned-network.sh`, and removes only those uniquely named disposable containers and volumes on exit. Set `WORKER_REPLICAS` from 1 through 32 to override the default of two.

The MCP scenario also proves operation-specific idempotency: replaying an import or campaign start with the same `clientRequestKey` and payload returns the original durable identifiers, while reusing the key with changed input is rejected. The two worker replicas share PostgreSQL claims and domain-rate reservations; no client process remains connected while the jobs execute.

The BacklinkPro-style one-click scenario uses a bounded local Node fixture and an isolated Compose project:

```bash
BACKLINKSTUDIO_ACCEPTANCE_CONFIRM=backlinkpro-one-click \
COMPOSE_PROJECT_NAME=backlinkstudio_one_click_acceptance \
bash scripts/run-one-click-acceptance-stack.sh
```

Fixture provisioning registers one exact host before the acceptance boundary. The operator request then supplies only five source URLs, two name/email identities, three comments, one target URL, and START. From that boundary onward the script calls only `backlink_workflow_start` and `backlink_workflow_get`. It proves automatic standard, fallback, oversized JavaScript/browser, moderation, and internally blocked outcomes, verifies all three published placements independently, checks that the blocked alias never enters the trusted catalog, and confirms identity/comment values are absent from durable job payloads.

All passwords above are required environment variables and are local acceptance secrets. They are never committed to this directory. Tear down the fixture with `docker compose -f acceptance/wordpress/docker-compose.yml down`; add `--volumes` only when intentionally deleting the disposable acceptance databases.

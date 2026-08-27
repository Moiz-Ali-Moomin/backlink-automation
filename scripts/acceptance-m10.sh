#!/usr/bin/env bash
set -euo pipefail

: "${BACKLINKSTUDIO_API_KEY:?BACKLINKSTUDIO_API_KEY is required}"
: "${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required}"

base_url=${BASE_URL:-http://127.0.0.1:8080}
compose_project=${COMPOSE_PROJECT_NAME:-backlinkstudio_m10accept}
postgres_user=${POSTGRES_USER:-backlinkstudio}
postgres_database=${POSTGRES_DB:-backlinkstudio}
restore_database=backlinkstudio_restore_m10
repository_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
work=$(mktemp -d)
restore_created=0
cd -- "$repository_root"

cleanup() {
  if [[ "$restore_created" == "1" ]]; then
    docker exec "$(docker compose -p "$compose_project" ps -q postgres)" dropdb --if-exists -U "$postgres_user" "$restore_database" >/dev/null 2>&1 || true
  fi
  rm -rf -- "$work"
}
trap cleanup EXIT

for command in curl jq docker; do
  command -v "$command" >/dev/null || { echo "$command is required" >&2; exit 69; }
done

auth=(-H "Authorization: Bearer $BACKLINKSTUDIO_API_KEY")
health=$(curl --fail --silent --show-error "$base_url/health/ready")
jq -e '.status == "healthy" and .databaseReachable == true and .schemaCurrent == true' <<<"$health" >/dev/null

unauthorized=$(curl --silent --output /dev/null --write-out '%{http_code}' "$base_url/api/v1/projects?limit=1")
[[ "$unauthorized" == "401" ]]

project_json=$(curl --fail --silent --show-error -X POST "$base_url/api/v1/projects" "${auth[@]}" \
  -H 'Content-Type: application/json' -H "Idempotency-Key: m10-project-$(date +%s%N)" \
  --data '{"name":"Milestone 10 Acceptance","primaryDomain":"m10.example","description":"hardening acceptance"}')
project_id=$(jq -er '.id' <<<"$project_json")

BACKLINKSTUDIO_PROJECT_ID="$project_id" LOAD_RATE="${LOAD_RATE:-10}" LOAD_DURATION="${LOAD_DURATION:-20s}" \
  K6_DOCKER_NETWORK="${compose_project}_edge" BASE_URL=http://backlinkstudio-api:8080 \
  bash "$repository_root/scripts/load-test.sh"

metrics_ready=0
for _ in $(seq 1 30); do
  if curl --fail --silent http://127.0.0.1:9464/metrics >"$work/metrics" && grep -q 'backlinkstudio_jobs_queue_depth' "$work/metrics"; then
    metrics_ready=1
    break
  fi
  sleep 2
done
[[ "$metrics_ready" == "1" ]]

postgres_container=$(docker compose -p "$compose_project" ps -q postgres)
[[ -n "$postgres_container" ]]
network="${compose_project}_backend"
docker network inspect "$network" >/dev/null

existing_restore=$(docker exec "$postgres_container" psql -U "$postgres_user" -d postgres -Atc "SELECT 1 FROM pg_database WHERE datname = '$restore_database'")
if [[ -n "$existing_restore" ]]; then
  echo "Refusing to overwrite pre-existing acceptance restore database '$restore_database'." >&2
  exit 65
fi

docker run --rm --network "$network" \
  -e PGHOST=postgres -e PGUSER="$postgres_user" -e PGPASSWORD="$POSTGRES_PASSWORD" -e PGDATABASE="$postgres_database" \
  -v "$repository_root/scripts:/scripts:ro" -v "$work:/backup" \
  postgres:17-alpine /bin/sh /scripts/postgres-backup.sh /backup/m10.dump

cp -- "$work/m10.dump" "$work/tampered.dump"
cp -- "$work/m10.dump.sha256" "$work/tampered.dump.sha256"
printf '\0' >>"$work/tampered.dump"
if docker run --rm --network "$network" \
  -e PGHOST=postgres -e PGUSER="$postgres_user" -e PGPASSWORD="$POSTGRES_PASSWORD" -e PGDATABASE="$restore_database" \
  -e BACKLINKSTUDIO_RESTORE_CONFIRM="$restore_database" \
  -v "$repository_root/scripts:/scripts:ro" -v "$work:/backup:ro" \
  postgres:17-alpine /bin/sh /scripts/postgres-restore.sh /backup/tampered.dump; then
  echo "Tampered backup was not rejected." >&2
  exit 65
fi

docker exec "$postgres_container" createdb -U "$postgres_user" "$restore_database"
restore_created=1
docker run --rm --network "$network" \
  -e PGHOST=postgres -e PGUSER="$postgres_user" -e PGPASSWORD="$POSTGRES_PASSWORD" -e PGDATABASE="$restore_database" \
  -e BACKLINKSTUDIO_RESTORE_CONFIRM="$restore_database" \
  -v "$repository_root/scripts:/scripts:ro" -v "$work:/backup:ro" \
  postgres:17-alpine /bin/sh /scripts/postgres-restore.sh /backup/m10.dump

restored_projects=$(docker exec "$postgres_container" psql -U "$postgres_user" -d "$restore_database" -Atc 'SELECT COUNT(*) FROM projects')
[[ "$restored_projects" -ge 1 ]]

index_count=$(docker exec "$postgres_container" psql -U "$postgres_user" -d "$postgres_database" -Atc "SELECT COUNT(*) FROM pg_indexes WHERE indexname LIKE 'ix_jobs_%' AND indexname IN ('ix_jobs_active_project','ix_jobs_claim_ready','ix_jobs_expired_claims','ix_jobs_submission_campaign_completed','ix_jobs_submission_project_campaign_completed','ix_jobs_submission_project_domain_completed')")
[[ "$index_count" == "6" ]]

echo "M10 acceptance passed: project=$project_id load_rate=${LOAD_RATE:-10}/s backup_restore=verified operational_indexes=$index_count telemetry=verified"

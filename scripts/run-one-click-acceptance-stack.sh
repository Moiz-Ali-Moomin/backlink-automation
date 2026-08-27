#!/usr/bin/env bash
set -euo pipefail

: "${BACKLINKSTUDIO_ACCEPTANCE_CONFIRM:?Set BACKLINKSTUDIO_ACCEPTANCE_CONFIRM=backlinkpro-one-click}"
[[ "$BACKLINKSTUDIO_ACCEPTANCE_CONFIRM" == "backlinkpro-one-click" ]] || {
  echo "Invalid acceptance confirmation value." >&2
  exit 64
}

for command in curl docker jq node; do
  command -v "$command" >/dev/null || { echo "$command is required" >&2; exit 69; }
done

repository_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
compose_project=${COMPOSE_PROJECT_NAME:-backlinkstudio_one_click_acceptance}
fixture_port=${ONE_CLICK_FIXTURE_PORT:-18080}
api_port=${ONE_CLICK_API_PORT:-18081}
mcp_port=${ONE_CLICK_MCP_PORT:-18082}
cd -- "$repository_root"

random_secret() {
  printf '%s%s' "$(tr -d '-' </proc/sys/kernel/random/uuid)" "$(tr -d '-' </proc/sys/kernel/random/uuid)"
}

export POSTGRES_DB=backlinkstudio_one_click_acceptance
export POSTGRES_USER=backlinkstudio_acceptance
export POSTGRES_PASSWORD=${POSTGRES_PASSWORD:-$(random_secret)}
export REDIS_PASSWORD=${REDIS_PASSWORD:-$(random_secret)}
export BACKLINKSTUDIO_BOOTSTRAP_API_KEY=${BACKLINKSTUDIO_BOOTSTRAP_API_KEY:-$(random_secret)}
export BACKLINKSTUDIO_BOOTSTRAP_NAME="One-click Acceptance Administrator"
export BACKLINKSTUDIO_IMAGE_TAG=${BACKLINKSTUDIO_IMAGE_TAG:-one-click-acceptance}
export WORKER_CONCURRENCY=${WORKER_CONCURRENCY:-4}
export WORKER_BUFFER_SIZE=${WORKER_BUFFER_SIZE:-8}
export WORKER_GLOBAL_CONCURRENCY=${WORKER_GLOBAL_CONCURRENCY:-8}
export WORKER_PROJECT_CONCURRENCY=${WORKER_PROJECT_CONCURRENCY:-8}
export WORKER_CAMPAIGN_CONCURRENCY=${WORKER_CAMPAIGN_CONCURRENCY:-4}
export WORKER_DOMAIN_CONCURRENCY=${WORKER_DOMAIN_CONCURRENCY:-1}
export DOMAIN_REQUEST_INTERVAL_MS=${DOMAIN_REQUEST_INTERVAL_MS:-25}
export ONE_CLICK_FIXTURE_PORT=$fixture_port
export API_BIND_PORT=$api_port
export MCP_BIND_PORT=$mcp_port
export OTEL_METRICS_BIND_PORT=${ONE_CLICK_OTEL_METRICS_PORT:-19464}
export OTEL_HEALTH_BIND_PORT=${ONE_CLICK_OTEL_HEALTH_PORT:-19133}

if [[ -n $(docker compose -p "$compose_project" ps -aq) ]]; then
  echo "Refusing to reuse a non-empty Compose acceptance project." >&2
  exit 65
fi

fixture_log=$(mktemp)
result_file=$(mktemp)
node acceptance/one-click/fixture.js 2>"$fixture_log" &
fixture_pid=$!

cleanup() {
  local exit_code=$?
  kill "$fixture_pid" >/dev/null 2>&1 || true
  wait "$fixture_pid" >/dev/null 2>&1 || true
  if [[ "$exit_code" != "0" ]]; then
    echo "One-click acceptance failed; sanitized service status and recent logs follow." >&2
    docker compose -p "$compose_project" ps >&2 || true
    docker compose -p "$compose_project" logs --tail 120 backlinkstudio-api backlinkstudio-mcp backlinkstudio-worker backlinkstudio-scheduler >&2 || true
    tail -40 "$fixture_log" >&2 || true
  fi
  docker compose -p "$compose_project" down --volumes --remove-orphans >/dev/null 2>&1 || true
  rm -f -- "$fixture_log" "$result_file"
  return "$exit_code"
}
trap cleanup EXIT

for _ in $(seq 1 30); do
  curl --fail --silent "http://127.0.0.1:$fixture_port/__health" >/dev/null && break
  sleep 1
done
curl --fail --silent "http://127.0.0.1:$fixture_port/__health" >/dev/null

main_compose=(docker compose -p "$compose_project" -f docker-compose.yml \
  -f acceptance/one-click/backlinkstudio-compose.override.yml)
"${main_compose[@]}" config --quiet
if [[ ${SKIP_DOCKER_BUILD:-0} != "1" ]]; then
  "${main_compose[@]}" build
fi
"${main_compose[@]}" up -d postgres redis otel-collector
"${main_compose[@]}" run --rm backlinkstudio-migrate
"${main_compose[@]}" up -d backlinkstudio-api backlinkstudio-mcp backlinkstudio-worker backlinkstudio-scheduler

for _ in $(seq 1 120); do
  if curl --fail --silent "http://127.0.0.1:$api_port/health/ready" >/dev/null && \
     curl --fail --silent "http://127.0.0.1:$mcp_port/health/ready" >/dev/null; then
    break
  fi
  sleep 1
done
curl --fail --silent "http://127.0.0.1:$api_port/health/ready" >/dev/null
curl --fail --silent "http://127.0.0.1:$mcp_port/health/ready" >/dev/null

ONE_CLICK_ACCEPTANCE_RESULT_FILE="$result_file" \
BACKLINKSTUDIO_API_KEY="$BACKLINKSTUDIO_BOOTSTRAP_API_KEY" MCP_URL="http://127.0.0.1:$mcp_port/mcp" \
  bash scripts/acceptance-one-click.sh >/dev/null

project_id=$(jq -er '.projectId' "$result_file")
workflow_id=$(jq -er '.workflowId' "$result_file")
campaign_id=$(jq -er '.campaignId' "$result_file")

strategies=$("${main_compose[@]}" exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc \
  "SELECT strategy || ':' || count(*) FROM submission_attempts WHERE campaign_id = '$campaign_id' GROUP BY strategy ORDER BY strategy")
[[ "$strategies" == *"StandardComment:"* && "$strategies" == *"FallbackComment:"* && "$strategies" == *"ControlledBrowser:"* ]] || {
  echo "Expected automatic standard/fallback/browser attempts, observed: $strategies" >&2
  exit 70
}

verified_by_strategy=$("${main_compose[@]}" exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc \
  "SELECT a.strategy || ':' || count(*) FROM backlinks b JOIN submission_attempts a ON a.id = b.submission_attempt_id WHERE b.campaign_id = '$campaign_id' AND b.status = 'Verified' GROUP BY a.strategy ORDER BY a.strategy")
[[ "$verified_by_strategy" == *"StandardComment:"* && "$verified_by_strategy" == *"FallbackComment:"* && "$verified_by_strategy" == *"ControlledBrowser:"* ]] || {
  echo "Expected independently verified backlinks for all three successful strategies, observed: $verified_by_strategy" >&2
  exit 70
}

verification_jobs=$("${main_compose[@]}" exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc \
  "SELECT count(*) FROM jobs WHERE correlation_id = '$workflow_id' AND type = 'Verification'")
[[ "$verification_jobs" == "4" ]] || { echo "Expected four automatic verification jobs, observed: $verification_jobs" >&2; exit 70; }

blocked_catalog=$("${main_compose[@]}" exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc \
  "SELECT count(*) FROM submission_sources WHERE project_id = '$project_id' AND host = 'blocked-one-click.fixture'")
[[ "$blocked_catalog" == "0" ]] || { echo "Unauthorized host was persisted as a trusted source." >&2; exit 70; }

payload_leaks=$("${main_compose[@]}" exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc \
  "SELECT count(*) FROM jobs WHERE correlation_id = '$workflow_id' AND (payload::text ILIKE '%john@example.com%' OR payload::text ILIKE '%Great article%')")
[[ "$payload_leaks" == "0" ]] || { echo "Inline identity or comment leaked into a durable job payload." >&2; exit 70; }

fixture_stats=$(curl --fail --silent "http://127.0.0.1:$fixture_port/__stats")
jq -e '.placements.standard == "https://target.example/one-click" and .placements.fallback == "https://target.example/one-click" and .placements["oversized-js"] == "https://target.example/one-click"' <<<"$fixture_stats" >/dev/null

"${main_compose[@]}" ps
echo "one-click acceptance: workflow=$workflow_id standard/fallback/browser submissions and independent verification proved" >&2

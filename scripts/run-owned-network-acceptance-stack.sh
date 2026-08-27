#!/usr/bin/env bash
set -euo pipefail

: "${BACKLINKSTUDIO_ACCEPTANCE_CONFIRM:?Set BACKLINKSTUDIO_ACCEPTANCE_CONFIRM=owned-network-wordpress}"
[[ "$BACKLINKSTUDIO_ACCEPTANCE_CONFIRM" == "owned-network-wordpress" ]] || {
  echo "Invalid acceptance confirmation value." >&2
  exit 64
}

for command in curl docker jq unzip; do
  command -v "$command" >/dev/null || { echo "$command is required" >&2; exit 69; }
done

repository_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
compose_project=${COMPOSE_PROJECT_NAME:-backlinkstudio_owned_acceptance}
wordpress_project=${WORDPRESS_COMPOSE_PROJECT_NAME:-${compose_project}_wordpress}
worker_replicas=${WORKER_REPLICAS:-2}
cd -- "$repository_root"

[[ "$worker_replicas" =~ ^[1-9][0-9]*$ ]] && (( worker_replicas <= 32 )) || {
  echo "WORKER_REPLICAS must be an integer from 1 through 32." >&2
  exit 64
}

random_secret() {
  printf '%s%s' "$(tr -d '-' </proc/sys/kernel/random/uuid)" "$(tr -d '-' </proc/sys/kernel/random/uuid)"
}

export POSTGRES_DB=backlinkstudio_owned_acceptance
export POSTGRES_USER=backlinkstudio_acceptance
export POSTGRES_PASSWORD=${POSTGRES_PASSWORD:-$(random_secret)}
export REDIS_PASSWORD=${REDIS_PASSWORD:-$(random_secret)}
export BACKLINKSTUDIO_BOOTSTRAP_API_KEY=${BACKLINKSTUDIO_BOOTSTRAP_API_KEY:-$(random_secret)}
export BACKLINKSTUDIO_BOOTSTRAP_NAME="Owned Network Acceptance Administrator"
export BACKLINKSTUDIO_IMAGE_TAG=${BACKLINKSTUDIO_IMAGE_TAG:-owned-acceptance}
export WORDPRESS_TEST_DB_PASSWORD=${WORDPRESS_TEST_DB_PASSWORD:-$(random_secret)}
export WORDPRESS_TEST_DB_ROOT_PASSWORD=${WORDPRESS_TEST_DB_ROOT_PASSWORD:-$(random_secret)}
export WORDPRESS_TEST_ADMIN_PASSWORD=${WORDPRESS_TEST_ADMIN_PASSWORD:-$(random_secret)}
export OWNED_PRIVATE_HOST_0=wordpress-open
export OWNED_PRIVATE_HOST_1=wordpress-moderated
export OWNED_PRIVATE_HOST_2=wordpress-closed
export OWNED_INSECURE_HTTP_HOST_0=wordpress-open
export OWNED_INSECURE_HTTP_HOST_1=wordpress-moderated
export OWNED_INSECURE_HTTP_HOST_2=wordpress-closed
if [[ ${TEST_OWNERSHIP_OVERRIDE_ACCEPTANCE:-0} == "1" ]]; then
  export BACKLINKSTUDIO_ENVIRONMENT=Test
  export BACKLINKSTUDIO_TEST_OWNERSHIP_OVERRIDE=true
  export BACKLINKSTUDIO_TEST_ALLOWED_HOSTS=wordpress-open,wordpress-moderated,wordpress-direct,wordpress-closed
fi
export BACKLINKSTUDIO_ACCEPTANCE_BACKEND_NETWORK=${compose_project}_backend
export WORKER_CONCURRENCY=${WORKER_CONCURRENCY:-8}
export WORKER_BUFFER_SIZE=${WORKER_BUFFER_SIZE:-16}
export WORKER_GLOBAL_CONCURRENCY=${WORKER_GLOBAL_CONCURRENCY:-8}
export WORKER_PROJECT_CONCURRENCY=${WORKER_PROJECT_CONCURRENCY:-8}
export WORKER_CAMPAIGN_CONCURRENCY=${WORKER_CAMPAIGN_CONCURRENCY:-8}
export WORKER_DOMAIN_CONCURRENCY=${WORKER_DOMAIN_CONCURRENCY:-1}
export DOMAIN_REQUEST_INTERVAL_MS=${DOMAIN_REQUEST_INTERVAL_MS:-100}

if [[ -n $(docker compose -p "$compose_project" ps -aq) || -n $(docker compose -p "$wordpress_project" -f acceptance/wordpress/docker-compose.yml ps -aq) ]]; then
  echo "Refusing to reuse a non-empty Compose acceptance project." >&2
  exit 65
fi

cleanup() {
  local exit_code=$?
  if [[ "$exit_code" != "0" ]]; then
    echo "Acceptance failed; sanitized service status and recent logs follow." >&2
    docker compose -p "$compose_project" ps >&2 || true
    docker compose -p "$compose_project" logs --tail 120 backlinkstudio-api backlinkstudio-mcp backlinkstudio-worker backlinkstudio-scheduler >&2 || true
  fi
  docker compose -p "$wordpress_project" -f acceptance/wordpress/docker-compose.yml down --volumes --remove-orphans >/dev/null 2>&1 || true
  docker compose -p "$compose_project" down --volumes --remove-orphans >/dev/null 2>&1 || true
  return "$exit_code"
}
trap cleanup EXIT

docker compose -p "$compose_project" config --quiet
docker compose -p "$wordpress_project" -f acceptance/wordpress/docker-compose.yml config --quiet
if [[ ${SKIP_DOCKER_BUILD:-0} != "1" ]]; then
  docker compose -p "$compose_project" build
fi
docker compose -p "$compose_project" up -d postgres redis otel-collector
docker compose -p "$wordpress_project" -f acceptance/wordpress/docker-compose.yml up -d wordpress-db wordpress-open wordpress-moderated wordpress-closed wordpress-direct
docker compose -p "$wordpress_project" -f acceptance/wordpress/docker-compose.yml run --rm wordpress-bootstrap
export WORDPRESS_DIRECT_APPLICATION_PASSWORD
WORDPRESS_DIRECT_APPLICATION_PASSWORD=$(docker compose -p "$wordpress_project" \
  -f acceptance/wordpress/docker-compose.yml run --rm --no-deps wordpress-bootstrap \
  sh -c 'cat /credentials/direct-api-password')
[[ -n "$WORDPRESS_DIRECT_APPLICATION_PASSWORD" ]] || {
  echo "Disposable direct-API credential was not generated." >&2
  exit 70
}
main_compose=(docker compose -p "$compose_project" -f docker-compose.yml \
  -f acceptance/wordpress/backlinkstudio-compose.override.yml)
wordpress_compose=(docker compose -p "$wordpress_project" -f acceptance/wordpress/docker-compose.yml)
"${main_compose[@]}" config --quiet
docker compose -p "$compose_project" run --rm backlinkstudio-migrate
"${main_compose[@]}" up -d --scale backlinkstudio-worker="$worker_replicas" \
  backlinkstudio-api backlinkstudio-mcp backlinkstudio-worker backlinkstudio-scheduler

running_workers=$("${main_compose[@]}" ps --status running -q backlinkstudio-worker | wc -l | tr -d '[:space:]')
[[ "$running_workers" == "$worker_replicas" ]] || {
  echo "Expected $worker_replicas running worker replicas, found $running_workers." >&2
  docker compose -p "$compose_project" ps >&2
  exit 70
}
echo "acceptance: $running_workers worker replicas are running" >&2

ready=0
for _ in $(seq 1 120); do
  if curl --fail --silent http://127.0.0.1:8080/health/ready >/dev/null && \
     curl --fail --silent http://127.0.0.1:8081/health/ready >/dev/null; then
    ready=1
    break
  fi
  sleep 1
done
[[ "$ready" == "1" ]] || {
  docker compose -p "$compose_project" ps >&2
  exit 70
}

acceptance_result=$(mktemp)
export OWNED_ACCEPTANCE_RESULT_FILE="$acceptance_result"
BACKLINKSTUDIO_API_KEY="$BACKLINKSTUDIO_BOOTSTRAP_API_KEY" \
  BASE_URL=http://127.0.0.1:8080 MCP_URL=http://127.0.0.1:8081/mcp \
  bash scripts/acceptance-owned-network.sh

if [[ ${TEST_OWNERSHIP_OVERRIDE_ACCEPTANCE:-0} == "1" ]]; then
  project_id=$(jq -er '.projectId' "$acceptance_result")
  network_id=$(jq -er '.ownedNetworkId' "$acceptance_result")
  persisted_network=$(docker compose -p "$compose_project" exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc \
    "SELECT ownership_status || ':' || automation_permitted FROM owned_network_profiles WHERE id = '$network_id'")
  persisted_sources=$(docker compose -p "$compose_project" exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc \
    "SELECT count(*) FROM submission_sources WHERE project_id = '$project_id' AND ownership_status = 'Unverified' AND automation_permitted = false")
  override_audits=$(docker compose -p "$compose_project" exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc \
    "SELECT count(*) FROM audit_events WHERE project_id = '$project_id' AND operation = 'submission.test_ownership_override_applied' AND input_summary LIKE '%testOwnershipOverrideApplied=true%reason=test_ownership_override%'")
  [[ "$persisted_network" == "Unverified:false" && "$persisted_sources" == "5" && "$override_audits" == "3" ]] || {
    echo "Test ownership override persistence/audit proof failed: network=$persisted_network sources=$persisted_sources audits=$override_audits" >&2
    exit 70
  }
  echo "acceptance: test override audited; persisted network and source ownership remained Unverified/false" >&2
fi

if [[ -n ${ACCEPTANCE_BACKUP_PATH:-} ]]; then
  backup_path=$(realpath -m "$ACCEPTANCE_BACKUP_PATH")
  [[ "$backup_path" == "$repository_root/evidence/"* ]] || {
    echo "ACCEPTANCE_BACKUP_PATH must be within $repository_root/evidence." >&2
    exit 64
  }
  mkdir -p "$backup_path/reports"
  docker compose -p "$compose_project" exec -T postgres sh -eu -c \
    'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc' > "$backup_path/acceptance.dump"
  backup_worker=$("${main_compose[@]}" ps -q backlinkstudio-worker | head -1)
  docker cp "$backup_worker:/var/lib/backlinkstudio/reports/." "$backup_path/reports" >/dev/null
  sha256sum "$backup_path/acceptance.dump" > "$backup_path/acceptance.dump.sha256"
  printf 'backupBytes=%s\nreportFiles=%s\n' "$(stat -c %s "$backup_path/acceptance.dump")" \
    "$(find "$backup_path/reports" -type f | wc -l)" > "$backup_path/backup-summary.txt"
  echo "acceptance: database and report artifacts backed up for restore drill" >&2
fi

if [[ ${ACCEPTANCE_DIRECT_API_FAILURES:-0} == "1" ]]; then
  mcp_call() {
    local tool=$1 arguments=$2 response combined status request
    request=$(jq -cn --arg tool "$tool" --argjson arguments "$arguments" \
      '{jsonrpc:"2.0",id:1,method:"tools/call",params:{name:$tool,arguments:$arguments}}')
    for _ in $(seq 1 60); do
      combined=$(curl --silent --show-error -w $'\n%{http_code}' -X POST http://127.0.0.1:8081/mcp \
        -H "Authorization: Bearer $BACKLINKSTUDIO_BOOTSTRAP_API_KEY" -H 'Content-Type: application/json' --data "$request")
      status=${combined##*$'\n'}
      response=${combined%$'\n'*}
      [[ "$status" != '429' ]] && break
      sleep 2
    done
    [[ "$status" =~ ^2 ]] || { echo "MCP $tool returned HTTP $status." >&2; return 1; }
    jq -e '.error == null and .result.isError == false' <<<"$response" >/dev/null || {
      jq -c . <<<"$response" >&2
      return 1
    }
    jq -cer '.result.content[0].text | fromjson' <<<"$response"
  }
  pg_value() {
    docker compose -p "$compose_project" exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc "$1"
  }
  project_id=$(jq -er '.projectId' "$acceptance_result")
  network_id=$(jq -er '.ownedNetworkId' "$acceptance_result")
  identity_pool_id=$(pg_value "SELECT id FROM submission_identity_pools WHERE project_id = '$project_id' ORDER BY created_at LIMIT 1")
  template_pool_id=$(pg_value "SELECT id FROM submission_template_pools WHERE project_id = '$project_id' ORDER BY created_at LIMIT 1")
  direct_run_key="direct-negative-$(date +%s%N)"

  create_direct_campaign() {
    local name=$1 key=$2 campaign start campaign_id submissions active
    campaign=$(mcp_call campaign_create "$(jq -cn --arg projectId "$project_id" --arg networkId "$network_id" \
      --arg identityPoolId "$identity_pool_id" --arg templatePoolId "$template_pool_id" --arg name "$name" --arg key "$key-campaign" \
      '{projectId:$projectId,name:$name,ownedNetworkId:$networkId,targetUrl:"https://target.example/owned-network-acceptance",identityPoolId:$identityPoolId,templatePoolId:$templatePoolId,globalConcurrency:2,perDomainConcurrency:1,perDomainDelayMilliseconds:100,maximumAttempts:3,verificationDelaySeconds:1,mode:"automaticOwnedNetwork",domain:"wordpress-direct",technicalCompatibility:"compatible",validationStatus:"valid",tag:"acceptance",dailyActionLimit:1000,clientRequestKey:$key}')")
    campaign_id=$(jq -er '.id' <<<"$campaign")
    start=$(mcp_call campaign_start "$(jq -cn --arg campaignId "$campaign_id" --arg key "$key-start" '{campaignId:$campaignId,clientRequestKey:$key}')")
    [[ $(jq '.jobIds | length' <<<"$start") -ge 1 ]]
    for _ in $(seq 1 180); do
      submissions=$(mcp_call submissions_list "$(jq -cn --arg campaignId "$campaign_id" '{campaignId:$campaignId,limit:10}')")
      active=$(jq '[.items[] | select(.status == "queued" or .status == "processing")] | length' <<<"$submissions")
      [[ $(jq '.items | length' <<<"$submissions") == 1 && "$active" == 0 ]] && break
      sleep 1
    done
    jq -cer '.items[0]' <<<"$submissions"
  }

  valid_direct_password=$WORDPRESS_DIRECT_APPLICATION_PASSWORD
  export WORDPRESS_DIRECT_APPLICATION_PASSWORD="invalid-disposable-credential-$RANDOM"
  "${main_compose[@]}" up -d --force-recreate --scale backlinkstudio-worker="$worker_replicas" backlinkstudio-worker >/dev/null
  sleep 5
  rejected_submission=$(create_direct_campaign "Direct API rejected credential" "$direct_run_key-auth")
  [[ $(jq -r '.status' <<<"$rejected_submission") == 'failed' ]]
  rejected_attempts=$(mcp_call submission_attempts "$(jq -cn --arg submissionJobId "$(jq -er '.id' <<<"$rejected_submission")" '{submissionJobId:$submissionJobId}')")
  jq -e 'length == 1 and .[0].strategy == "DirectApi" and .[0].httpStatus == 401 and .[0].failureKind == "authorizationDenied"' <<<"$rejected_attempts" >/dev/null
  echo "acceptance: real WordPress API invalid credential classified AuthorizationDenied" >&2

  export WORDPRESS_DIRECT_APPLICATION_PASSWORD=$valid_direct_password
  "${main_compose[@]}" up -d --force-recreate --scale backlinkstudio-worker="$worker_replicas" backlinkstudio-worker >/dev/null
  sleep 5
  "${wordpress_compose[@]}" stop wordpress-direct >/dev/null
  temporary_campaign=$(mcp_call campaign_create "$(jq -cn --arg projectId "$project_id" --arg networkId "$network_id" \
    --arg identityPoolId "$identity_pool_id" --arg templatePoolId "$template_pool_id" --arg key "$direct_run_key-temporary-campaign" \
    '{projectId:$projectId,name:"Direct API temporary outage",ownedNetworkId:$networkId,targetUrl:"https://target.example/owned-network-acceptance",identityPoolId:$identityPoolId,templatePoolId:$templatePoolId,globalConcurrency:2,perDomainConcurrency:1,perDomainDelayMilliseconds:100,maximumAttempts:10,verificationDelaySeconds:1,mode:"automaticOwnedNetwork",domain:"wordpress-direct",technicalCompatibility:"compatible",validationStatus:"valid",tag:"acceptance",dailyActionLimit:1000,clientRequestKey:$key}')")
  temporary_campaign_id=$(jq -er '.id' <<<"$temporary_campaign")
  mcp_call campaign_start "$(jq -cn --arg campaignId "$temporary_campaign_id" --arg key "$direct_run_key-temporary-start" '{campaignId:$campaignId,clientRequestKey:$key}')" >/dev/null
  temporary_submission_id=''
  temporary_uncertain=0
  for _ in $(seq 1 300); do
    temporary_submission_id=$(pg_value "SELECT id FROM submission_jobs WHERE campaign_id = '$temporary_campaign_id' ORDER BY created_at LIMIT 1")
    if [[ -n "$temporary_submission_id" ]]; then
      temporary_uncertain=$(pg_value "SELECT count(*) FROM submission_attempts WHERE submission_job_id = '$temporary_submission_id' AND failure_kind = 'Uncertain'")
      [[ "$temporary_uncertain" -ge 1 ]] && break
    fi
    sleep 0.2
  done
  [[ -n "$temporary_submission_id" ]]
  [[ "$temporary_uncertain" -ge 1 ]]
  temporary_job_id=$(pg_value "SELECT persistent_job_id FROM submission_jobs WHERE id = '$temporary_submission_id'")
  temporary_retry_state=$(pg_value "SELECT status || '|' || attempt_count || '|' || coalesce(last_failure_kind,'') FROM jobs WHERE id = '$temporary_job_id'")
  [[ "$temporary_retry_state" == RetryScheduled\|* || "$temporary_retry_state" == Claimed\|* || "$temporary_retry_state" == Running\|* ]]
  "${wordpress_compose[@]}" start wordpress-direct >/dev/null
  sleep 10
  for _ in $(seq 1 180); do
    temporary_job_status=$(pg_value "SELECT status FROM jobs WHERE id = '$temporary_job_id'")
    [[ "$temporary_job_status" == 'Succeeded' ]] && break
    [[ "$temporary_job_status" == 'Failed' || "$temporary_job_status" == 'DeadLetter' ]] && break
    sleep 1
  done
  [[ "$temporary_job_status" == 'Succeeded' ]]
  temporary_attempts=$(mcp_call submission_attempts "$(jq -cn --arg submissionJobId "$temporary_submission_id" '{submissionJobId:$submissionJobId}')")
  jq -e 'length >= 2 and .[0].failureKind == "uncertain" and (.[-1].result == "submitted" or .[-1].result == "duplicate")' <<<"$temporary_attempts" >/dev/null
  echo "acceptance: real WordPress API temporary outage retried safely after controlled recovery" >&2
fi

distribution=$(docker compose -p "$compose_project" exec -T postgres \
  psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc \
  "SELECT actor_id || '|' || count(*) FROM audit_events WHERE operation = 'submission.wordpress_complete' GROUP BY actor_id ORDER BY actor_id")
distributed_workers=$(wc -l <<<"$distribution" | tr -d '[:space:]')
if [[ ${TEST_OWNERSHIP_OVERRIDE_ACCEPTANCE:-0} == "1" ]]; then
  [[ "$distributed_workers" -ge 1 ]] || {
    echo "Expected controlled override submissions to be attributed to a worker." >&2
    exit 70
  }
else
  [[ "$distributed_workers" -ge 2 ]] || {
    echo "Expected real WordPress submissions to be distributed across at least two workers; observed $distributed_workers." >&2
    printf '%s\n' "$distribution" >&2
    exit 70
  }
fi
echo "acceptance: real HTTP submissions were durably attributed to $distributed_workers worker(s)" >&2
printf '%s\n' "$distribution" >&2

secret_pattern=$(mktemp)
chmod 600 "$secret_pattern"
printf '%s' "$WORDPRESS_DIRECT_APPLICATION_PASSWORD" > "$secret_pattern"
if docker compose -p "$compose_project" logs --no-color | grep -F -f "$secret_pattern" >/dev/null || \
   docker compose -p "$compose_project" exec -T postgres pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
     --data-only --no-owner --no-privileges | grep -F -f "$secret_pattern" >/dev/null; then
  rm -f -- "$secret_pattern" "$acceptance_result"
  echo "The disposable WordPress credential appeared in logs or persisted database content." >&2
  exit 70
fi

if [[ ${ACCEPTANCE_MODERATION_LIFECYCLE:-0} == "1" ]]; then
  project_id=$(jq -er '.projectId' "$acceptance_result")
  moderated_backlink_id=$(docker compose -p "$compose_project" exec -T postgres \
    psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc \
    "SELECT b.id FROM backlinks b JOIN submission_sources s ON s.id = b.submission_source_id WHERE b.project_id = '$project_id' AND s.host = 'wordpress-moderated' ORDER BY b.created_at LIMIT 1")
  [[ -n "$moderated_backlink_id" ]] || { echo "Moderated acceptance backlink was not found." >&2; exit 70; }

  wait_verification() {
    local backlink_id=$1 expected=$2 key=$3 response job_id status=''
    response=$(curl --fail --silent --show-error -X POST http://127.0.0.1:8080/api/v1/verification/jobs \
      -H "Authorization: Bearer $BACKLINKSTUDIO_BOOTSTRAP_API_KEY" -H 'Content-Type: application/json' \
      -H "Idempotency-Key: $key" --data "{\"backlinkId\":\"$backlink_id\"}")
    job_id=$(jq -er '.jobId' <<<"$response")
    for _ in $(seq 1 120); do
      status=$(curl --fail --silent --show-error -H "Authorization: Bearer $BACKLINKSTUDIO_BOOTSTRAP_API_KEY" \
        "http://127.0.0.1:8080/api/v1/jobs/$job_id" | jq -r '.status')
      [[ "$status" == "succeeded" ]] && break
      [[ "$status" == "failed" || "$status" == "deadLetter" ]] && break
      sleep 1
    done
    [[ "$status" == "succeeded" ]] || { echo "Verification job $job_id ended as $status." >&2; exit 70; }
    status=$(curl --fail --silent --show-error -H "Authorization: Bearer $BACKLINKSTUDIO_BOOTSTRAP_API_KEY" \
      "http://127.0.0.1:8080/api/v1/backlinks/$backlink_id" | jq -r '.status')
    [[ "$status" == "$expected" ]] || { echo "Expected backlink $backlink_id to be $expected, observed $status." >&2; exit 70; }
    echo "acceptance: moderation lifecycle backlink=$backlink_id status=$status" >&2
  }

  "${wordpress_compose[@]}" exec -T wordpress-db sh -eu -c \
    'mariadb --user=root --password="$MARIADB_ROOT_PASSWORD" "$MARIADB_DATABASE" -e "UPDATE moderated_comments SET comment_approved = '\''1'\'' WHERE comment_approved = '\''0'\'';"'
  wait_verification "$moderated_backlink_id" verified "moderation-approved-$moderated_backlink_id"

  "${wordpress_compose[@]}" stop wordpress-moderated >/dev/null
  wait_verification "$moderated_backlink_id" error "moderation-transient-$moderated_backlink_id"
  "${wordpress_compose[@]}" start wordpress-moderated >/dev/null
  sleep 10
  wait_verification "$moderated_backlink_id" verified "moderation-recovered-$moderated_backlink_id"

  "${wordpress_compose[@]}" exec -T wordpress-db sh -eu -c \
    'mariadb --user=root --password="$MARIADB_ROOT_PASSWORD" "$MARIADB_DATABASE" -e "UPDATE moderated_comments SET comment_approved = '\''spam'\'' WHERE comment_approved = '\''1'\'';"'
  wait_verification "$moderated_backlink_id" lost "moderation-lost-$moderated_backlink_id"
  "${wordpress_compose[@]}" exec -T wordpress-db sh -eu -c \
    'mariadb --user=root --password="$MARIADB_ROOT_PASSWORD" "$MARIADB_DATABASE" -e "UPDATE moderated_comments SET comment_approved = '\''1'\'' WHERE comment_approved = '\''spam'\'';"'
  wait_verification "$moderated_backlink_id" verified "moderation-restored-$moderated_backlink_id"
  echo "acceptance: pending -> approved -> transient error -> recovered -> lost -> recovered lifecycle passed" >&2
fi

soak_seconds=${ACCEPTANCE_SOAK_SECONDS:-0}
if [[ "$soak_seconds" =~ ^[0-9]+$ ]] && (( soak_seconds > 0 )); then
  project_id=$(jq -er '.projectId' "$acceptance_result")
  campaign_id=$(jq -er '.campaignId' "$acceptance_result")
  mkdir -p "$repository_root/.tmp/closure-evidence"
  soak_log="$repository_root/.tmp/closure-evidence/linux-soak.txt"
  : > "$soak_log"
  starts_at=$(date -u +'%Y-%m-%dT%H:%M:%SZ')
  schedule=$(curl --fail --silent --show-error -X POST http://127.0.0.1:8080/api/v1/schedules \
    -H "Authorization: Bearer $BACKLINKSTUDIO_BOOTSTRAP_API_KEY" -H 'Content-Type: application/json' \
    -H "Idempotency-Key: closure-soak-schedule-$campaign_id" \
    --data "$(jq -cn --arg projectId "$project_id" --arg startsAt "$starts_at" \
      '{projectId:$projectId,name:"Closure verification soak",action:{actionType:"verification",verification:{status:"verified",maximumBacklinks:100}},timing:{recurrenceType:"interval",startsAt:$startsAt,intervalMinutes:1}}')")
  schedule_id=$(jq -er '.id' <<<"$schedule")
  echo "linux-soak:start=$starts_at;seconds=$soak_seconds;workers=$worker_replicas;schedule=$schedule_id" | tee -a "$soak_log" >&2
  start_epoch=$(date +%s)
  restarted=0
  paused=0
  while (( $(date +%s) - start_epoch < soak_seconds )); do
    now_epoch=$(date +%s)
    elapsed=$((now_epoch - start_epoch))
    curl --fail --silent http://127.0.0.1:8080/health/ready >/dev/null
    curl --fail --silent http://127.0.0.1:8081/health/ready >/dev/null
    curl --fail --silent -H "Authorization: Bearer $BACKLINKSTUDIO_BOOTSTRAP_API_KEY" \
      "http://127.0.0.1:8080/api/v1/projects/$project_id" >/dev/null
    curl --fail --silent -X POST http://127.0.0.1:8081/mcp \
      -H "Authorization: Bearer $BACKLINKSTUDIO_BOOTSTRAP_API_KEY" -H 'Content-Type: application/json' \
      --data '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}' | jq -e '.result.tools | length > 0' >/dev/null
    docker stats --no-stream --format '{{.Name}}|{{.CPUPerc}}|{{.MemUsage}}|{{.NetIO}}|{{.BlockIO}}' \
      $("${main_compose[@]}" ps -q backlinkstudio-api backlinkstudio-mcp backlinkstudio-worker backlinkstudio-scheduler postgres) \
      | sed "s/^/elapsed=$elapsed|/" >> "$soak_log"
    if (( paused == 0 && elapsed >= soak_seconds / 3 )); then
      curl --fail --silent -X POST "http://127.0.0.1:8080/api/v1/campaigns/$campaign_id/pause" \
        -H "Authorization: Bearer $BACKLINKSTUDIO_BOOTSTRAP_API_KEY" -H "Idempotency-Key: closure-soak-pause-$campaign_id" >/dev/null
      curl --fail --silent -X POST "http://127.0.0.1:8080/api/v1/campaigns/$campaign_id/resume" \
        -H "Authorization: Bearer $BACKLINKSTUDIO_BOOTSTRAP_API_KEY" -H "Idempotency-Key: closure-soak-resume-$campaign_id" >/dev/null
      echo "linux-soak:pause-resume elapsed=$elapsed" | tee -a "$soak_log" >&2
      paused=1
    fi
    if (( restarted == 0 && elapsed >= soak_seconds / 2 )); then
      worker_container=$("${main_compose[@]}" ps -q backlinkstudio-worker | head -1)
      docker stop --time 20 "$worker_container" >/dev/null
      docker start "$worker_container" >/dev/null
      echo "linux-soak:worker-restart elapsed=$elapsed container=$worker_container" | tee -a "$soak_log" >&2
      restarted=1
    fi
    sleep 30
  done
  summary=$(docker compose -p "$compose_project" exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atc \
    "SELECT 'jobs=' || count(*) || ';failed=' || count(*) FILTER (WHERE status IN ('Failed','DeadLetter')) || ';recoveries=' || coalesce(sum(recovery_count),0) FROM jobs; SELECT 'scheduleRuns=' || coalesce(last_job_count,0) || ';lastRun=' || coalesce(last_run_at::text,'') FROM schedules WHERE id = '$schedule_id'; SELECT 'connections=' || count(*) FROM pg_stat_activity WHERE datname = current_database();")
  printf '%s\n' "$summary" | tee -a "$soak_log" >&2
  grep -q 'failed=0' <<<"$summary"
  grep -Eq 'lastRun=[^;[:space:]]' <<<"$summary"
  echo "linux-soak:passed duration=$soak_seconds" | tee -a "$soak_log" >&2
fi
rm -f -- "$secret_pattern" "$acceptance_result"
echo "acceptance: disposable WordPress credential absent from logs and database content" >&2

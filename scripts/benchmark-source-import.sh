#!/usr/bin/env bash
set -euo pipefail

: "${BACKLINKSTUDIO_BENCHMARK_CONFIRM:?Set BACKLINKSTUDIO_BENCHMARK_CONFIRM=owned-network-source-import}"
[[ "$BACKLINKSTUDIO_BENCHMARK_CONFIRM" == "owned-network-source-import" ]] || {
  echo "Benchmark confirmation value is invalid." >&2
  exit 64
}
: "${BACKLINKSTUDIO_API_KEY:?BACKLINKSTUDIO_API_KEY is required}"

row_count=${1:-10000}
[[ "$row_count" =~ ^(10000|100000|1000000)$ ]] || {
  echo "Row count must be 10000, 100000, or 1000000." >&2
  exit 64
}

for command in awk curl date jq seq; do
  command -v "$command" >/dev/null || { echo "$command is required" >&2; exit 69; }
done

base_url=${BASE_URL:-http://127.0.0.1:8080}
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT
auth=(-H "Authorization: Bearer $BACKLINKSTUDIO_API_KEY")
run_key="source-import-benchmark-$(date +%s%N)-$row_count"

project=$(curl --fail --silent --show-error -X POST "$base_url/api/v1/projects" "${auth[@]}" \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $run_key-project" \
  --data '{"name":"Owned source import benchmark","primaryDomain":"benchmark.invalid","description":"Disposable controlled import benchmark"}')
project_id=$(jq -er '.id' <<<"$project")

network=$(curl --fail --silent --show-error -X POST "$base_url/api/v1/owned-networks" "${auth[@]}" \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $run_key-network" \
  --data "$(jq -cn --arg projectId "$project_id" '{projectId:$projectId,name:"Owned import benchmark",description:"Disposable controlled import benchmark",ownershipStatus:"controlled",automationPermitted:true,domains:[{domain:"benchmark.invalid",matchType:"subdomainOf",enabled:true}],maxConcurrency:100,perDomainConcurrency:2,perDomainDelayMilliseconds:0,enabled:true}')")
network_id=$(jq -er '.id' <<<"$network")

seq 1 "$row_count" | awk '{ print "https://site" ($1 % 1000) ".benchmark.invalid/post/" $1 }' >"$work/sources.txt"
expected_duplicates=0
expected_invalid=0
if [[ ${BACKLINKSTUDIO_IMPORT_NOISY_ROWS:-0} == "1" ]]; then
  seq 1 100 | awk '{ print "https://site" ($1 % 1000) ".benchmark.invalid/post/" $1 }' >>"$work/sources.txt"
  seq 1 100 | awk '{ print "invalid-owned-source-" $1 }' >>"$work/sources.txt"
  expected_duplicates=100
  expected_invalid=100
fi
input_lines=$(wc -l <"$work/sources.txt" | tr -d ' ')
byte_count=$(wc -c <"$work/sources.txt" | tr -d ' ')
started_ns=$(date +%s%N)
accepted=$(curl --fail --silent --show-error -X POST \
  "$base_url/api/v1/submission-sources/import?projectId=$project_id&networkId=$network_id&format=Txt&fileName=benchmark.txt&tag=benchmark" \
  "${auth[@]}" -H "Idempotency-Key: $run_key-import" -H 'Content-Type: text/plain; charset=utf-8' \
  --data-binary "@$work/sources.txt")
staged_ns=$(date +%s%N)
import_id=$(jq -er '.importId' <<<"$accepted")
job_id=$(jq -er '.jobId' <<<"$accepted")

job_status=queued
for _ in $(seq 1 600); do
  job=$(curl --fail --silent --show-error "$base_url/api/v1/jobs/$job_id" "${auth[@]}")
  job_status=$(jq -r '.status' <<<"$job")
  case "$job_status" in
    succeeded) break ;;
    failed|deadLetter|cancelled)
      jq -c '{status,lastFailureKind,lastError,attemptCount,recoveryCount}' <<<"$job" >&2
      exit 1
      ;;
  esac
  sleep 1
done
[[ "$job_status" == "succeeded" ]] || { echo "Import did not finish within 600 seconds." >&2; exit 1; }
completed_ns=$(date +%s%N)

result=$(curl --fail --silent --show-error "$base_url/api/v1/submission-source-imports/$import_id" "${auth[@]}")
jq -e --argjson expected "$row_count" --argjson duplicates "$expected_duplicates" --argjson invalid "$expected_invalid" \
  '.status == "completed" and .accepted == $expected and .duplicates == $duplicates and .invalid == $invalid and .errors == 0' <<<"$result" >/dev/null

staging_seconds=$(awk -v start="$started_ns" -v end="$staged_ns" 'BEGIN { printf "%.3f", (end-start)/1000000000 }')
total_seconds=$(awk -v start="$started_ns" -v end="$completed_ns" 'BEGIN { printf "%.3f", (end-start)/1000000000 }')
throughput=$(awk -v rows="$row_count" -v start="$started_ns" -v end="$completed_ns" 'BEGIN { printf "%.1f", rows/((end-start)/1000000000) }')

jq -n --argjson rows "$row_count" --argjson inputLines "$input_lines" --argjson bytes "$byte_count" \
  --argjson duplicates "$expected_duplicates" --argjson invalid "$expected_invalid" --arg stagingSeconds "$staging_seconds" \
  --arg totalSeconds "$total_seconds" --arg rowsPerSecond "$throughput" --arg importId "$import_id" --arg jobId "$job_id" \
  '{rows:$rows,inputLines:$inputLines,bytes:$bytes,duplicates:$duplicates,invalid:$invalid,stagingSeconds:($stagingSeconds|tonumber),totalSeconds:($totalSeconds|tonumber),rowsPerSecond:($rowsPerSecond|tonumber),importId:$importId,jobId:$jobId,status:"passed"}'

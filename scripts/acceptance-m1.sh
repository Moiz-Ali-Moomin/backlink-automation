#!/usr/bin/env bash
set -euo pipefail

api_url="${BACKLINKSTUDIO_API_URL:-http://127.0.0.1:8080}"
mcp_url="${BACKLINKSTUDIO_MCP_URL:-http://127.0.0.1:8081/mcp}"
api_key="${BACKLINKSTUDIO_API_KEY:?BACKLINKSTUDIO_API_KEY must be set}"

assert_loopback() {
  case "$1" in
    http://127.0.0.1:*|http://localhost:*) ;;
    *) echo "Acceptance endpoints must be loopback unless BACKLINKSTUDIO_ALLOW_REMOTE_ACCEPTANCE=1." >&2; exit 2 ;;
  esac
}

if [[ "${BACKLINKSTUDIO_ALLOW_REMOTE_ACCEPTANCE:-0}" != "1" ]]; then
  assert_loopback "$api_url"
  assert_loopback "$mcp_url"
fi

temporary_response="$(mktemp)"
trap 'rm -f "$temporary_response"' EXIT

assert_status() {
  local expected="$1"
  shift
  local actual
  actual="$(curl --silent --show-error --output "$temporary_response" --write-out '%{http_code}' "$@")"
  if [[ "$actual" != "$expected" ]]; then
    echo "Expected HTTP $expected, received $actual: $(cat "$temporary_response")" >&2
    exit 1
  fi
}

mcp_request() {
  curl --fail --silent --show-error \
    --header "X-Api-Key: $api_key" \
    --header 'Content-Type: application/json' \
    --data-binary @- \
    "$mcp_url"
}

mcp_tool() {
  local name="$1"
  local arguments="$2"
  jq --compact-output --null-input \
    --arg name "$name" \
    --argjson arguments "$arguments" \
    '{jsonrpc:"2.0",id:1,method:"tools/call",params:{name:$name,arguments:$arguments}}' | mcp_request
}

tool_value() {
  jq --exit-status --raw-output '.result.content[0].text | fromjson'
}

assert_status 200 "$api_url/health/live"
assert_status 200 "$api_url/health/ready"
assert_status 401 "$api_url/api/v1/projects"
assert_status 401 --header 'X-Api-Key: invalid-key-material' "$api_url/api/v1/projects"

assert_status 201 \
  --request POST \
  --header "X-Api-Key: $api_key" \
  --header 'Idempotency-Key: acceptance-rest-project-v1' \
  --header 'Content-Type: application/json' \
  --data '{"name":"REST Contract Acceptance","primaryDomain":"rest.example","description":"Milestone 2 REST acceptance"}' \
  "$api_url/api/v1/projects"

initialize_response="$(jq --compact-output --null-input '{jsonrpc:"2.0",id:1,method:"initialize",params:{protocolVersion:"2025-06-18",capabilities:{},clientInfo:{name:"m2-acceptance",version:"1"}}}' | mcp_request)"
jq --exit-status '.result.protocolVersion == "2025-06-18"' <<<"$initialize_response" >/dev/null

tools_response="$(jq --compact-output --null-input '{jsonrpc:"2.0",id:2,method:"tools/list",params:{}}' | mcp_request)"
jq --exit-status '[.result.tools[].name] | index("project_create") != null and index("analysis_start") != null and index("jobs_get") != null and index("policy_get") != null and index("blocklist_add") != null' <<<"$tools_response" >/dev/null

project_arguments="$(jq --compact-output --null-input '{name:"MCP Acceptance Project",primaryDomain:"example.com",description:"Milestone 2 end-to-end acceptance",clientRequestKey:"acceptance-mcp-project-v1"}')"
project="$(mcp_tool project_create "$project_arguments" | tool_value)"
project_id="$(jq --exit-status --raw-output '.id' <<<"$project")"

target_arguments="$(jq --compact-output --null-input --arg projectId "$project_id" '{projectId:$projectId,url:"https://example.com/product#details",label:"Product",keywords:["widgets"],preferredAnchor:"Example widgets",category:"product",priority:80,clientRequestKey:"acceptance-target-v1"}')"
target="$(mcp_tool target_add "$target_arguments" | tool_value)"
jq --exit-status '.normalizedUrl == "https://example.com/product"' <<<"$target" >/dev/null

policy_arguments="$(jq --compact-output --null-input --arg projectId "$project_id" '{projectId:$projectId}')"
policy="$(mcp_tool policy_get "$policy_arguments" | tool_value)"
jq --exit-status '.automationEnabled == false and .minimumQualityScore == 60 and .maximumRiskScore == 30 and .manualReviewRequired == true' <<<"$policy" >/dev/null

blocklist_arguments="$(jq --compact-output --null-input --arg projectId "$project_id" '{projectId:$projectId,matchType:"domain",value:"blocked.example",reason:"Acceptance safety rule",clientRequestKey:"acceptance-blocklist-v1"}')"
mcp_tool blocklist_add "$blocklist_arguments" | tool_value | jq --exit-status '.value == "blocked.example"' >/dev/null

candidate_arguments="$(jq --compact-output --null-input --arg projectId "$project_id" '{projectId:$projectId,urls:["https://partner.example/resources","https://partner.example/resources/","https://blocked.example/list","not-a-url"],clientRequestKey:"acceptance-candidates-v1"}')"
candidate_result="$(mcp_tool candidates_import "$candidate_arguments" | tool_value)"
jq --exit-status '.accepted == 2 and .duplicates == 1 and .invalid == 1' <<<"$candidate_result" >/dev/null

analysis_arguments="$(jq --compact-output --null-input --arg projectId "$project_id" '{projectId:$projectId,clientRequestKey:"acceptance-analysis-v1"}')"
analysis_result="$(mcp_tool analysis_start "$analysis_arguments" | tool_value)"
job_id="$(jq --exit-status --raw-output '.jobId' <<<"$analysis_result")"

job_status=""
for _ in $(seq 1 30); do
  job_arguments="$(jq --compact-output --null-input --arg jobId "$job_id" '{jobId:$jobId}')"
  job="$(mcp_tool jobs_get "$job_arguments" | tool_value)"
  job_status="$(jq --raw-output '.status' <<<"$job")"
  if [[ "$job_status" == "succeeded" || "$job_status" == "Succeeded" || "$job_status" == "3" ]]; then
    break
  fi
  if [[ "$job_status" == "failed" || "$job_status" == "Failed" || "$job_status" == "4" || "$job_status" == "deadLetter" || "$job_status" == "DeadLetter" || "$job_status" == "7" ]]; then
    echo "Analysis job entered terminal failure state: $job_status" >&2
    exit 1
  fi
  sleep 1
done

if [[ "$job_status" != "succeeded" && "$job_status" != "Succeeded" && "$job_status" != "3" ]]; then
  echo "Analysis job did not complete within 30 seconds; last status: $job_status" >&2
  exit 1
fi

opportunity_arguments="$(jq --compact-output --null-input --arg projectId "$project_id" '{projectId:$projectId,limit:10}')"
opportunities="$(mcp_tool opportunities_list "$opportunity_arguments" | tool_value)"
jq --exit-status '(.items | length == 1) and (.items[0].scoreReasons | length > 0)' <<<"$opportunities" >/dev/null

candidate_list_arguments="$(jq --compact-output --null-input --arg projectId "$project_id" '{projectId:$projectId,limit:10}')"
candidates="$(mcp_tool candidates_list "$candidate_list_arguments" | tool_value)"
jq --exit-status '[.items[].analysisStatus] | index("blocked") != null and (index("error") != null or index("analyzed") != null)' <<<"$candidates" >/dev/null

printf 'M2 acceptance passed: project=%s job=%s status=%s opportunities=%s\n' \
  "$project_id" \
  "$job_id" \
  "$job_status" \
  "$(jq --raw-output '.items | length' <<<"$opportunities")"

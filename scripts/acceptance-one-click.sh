#!/usr/bin/env bash
set -euo pipefail

: "${BACKLINKSTUDIO_API_KEY:?BACKLINKSTUDIO_API_KEY is required}"

mcp_url=${MCP_URL:-http://127.0.0.1:8081/mcp}
fixture_port=${ONE_CLICK_FIXTURE_PORT:-18080}
result_file=${ONE_CLICK_ACCEPTANCE_RESULT_FILE:-}
auth=(-H "Authorization: Bearer $BACKLINKSTUDIO_API_KEY")

for command in curl jq; do
  command -v "$command" >/dev/null || { echo "$command is required" >&2; exit 69; }
done

mcp_tool() {
  local tool=$1
  local arguments=$2
  local request response
  request=$(jq -cn --arg tool "$tool" --argjson arguments "$arguments" \
    '{jsonrpc:"2.0",id:1,method:"tools/call",params:{name:$tool,arguments:$arguments}}')
  response=$(curl --fail --silent --show-error -X POST "$mcp_url" "${auth[@]}" \
    -H 'Content-Type: application/json' --data "$request")
  jq -e '.error == null and .result.isError == false' <<<"$response" >/dev/null || {
    jq -c . <<<"$response" >&2
    return 1
  }
  jq -cer '.result.content[0].text | fromjson' <<<"$response"
}

run_key="one-click-$(date +%s%N)"

# Controlled fixture provisioning happens before the acceptance boundary. It represents an
# already-approved network and is deliberately not part of the normal operator workflow.
project=$(mcp_tool project_create "$(jq -cn --arg key "$run_key-project" \
  '{name:"One-click controlled acceptance",primaryDomain:"target.example",description:"Disposable one-click fixture",clientRequestKey:$key}')")
project_id=$(jq -er '.id' <<<"$project")
mcp_tool policy_update "$(jq -cn --arg projectId "$project_id" --arg key "$run_key-policy" \
  '{projectId:$projectId,automationEnabled:true,minimumQualityScore:0,maximumRiskScore:100,manualReviewRequired:false,hourlyActionLimit:1000,dailyActionLimit:1000,perDomainActionLimit:1000,clientRequestKey:$key}')" >/dev/null
mcp_tool owned_network_create "$(jq -cn --arg projectId "$project_id" --arg key "$run_key-network" \
  '{projectId:$projectId,name:"Pre-authorized one-click fixture",ownershipStatus:"controlled",automationPermitted:true,domains:[{domain:"one-click.fixture",matchType:"exactHost",enabled:true}],maxConcurrency:8,perDomainConcurrency:1,perDomainDelayMilliseconds:25,enabled:true,clientRequestKey:$key}')" >/dev/null

sources=$(jq -cn --arg port "$fixture_port" '[
  "http://one-click.fixture:" + $port + "/standard",
  "http://one-click.fixture:" + $port + "/fallback",
  "http://one-click.fixture:" + $port + "/oversized-js",
  "http://one-click.fixture:" + $port + "/moderated",
  "http://one-click.fixture:" + $port + "/closed",
  "http://blocked-one-click.fixture:" + $port + "/standard"
]')

# Acceptance boundary: the normal user supplies only sources, identities, comments, target,
# optional limits, and START. From this point the only MCP operations are start and status get.
start_arguments=$(jq -cn --arg projectId "$project_id" --argjson sources "$sources" --arg key "$run_key-start" \
  '{projectId:$projectId,sourceUrls:$sources,identities:[{name:"John",email:"john@example.com"},{name:"Mike",email:"mike@example.com"}],comments:["Great article.","Thanks for sharing this.","Interesting read."],targetUrl:"https://target.example/one-click",globalConcurrency:4,perDomainConcurrency:1,perDomainDelayMilliseconds:25,maximumAttempts:2,verificationDelaySeconds:1,clientRequestKey:$key}')
start=$(mcp_tool backlink_workflow_start "$start_arguments")
workflow_id=$(jq -er '.workflowId' <<<"$start")
job_id=$(jq -er '.jobId' <<<"$start")
campaign_id=$(jq -er '.campaignId' <<<"$start")

status=''
for _ in $(seq 1 240); do
  status=$(mcp_tool backlink_workflow_get "$(jq -cn --arg workflowId "$workflow_id" \
    '{workflowId:$workflowId,limit:10}')")
  if jq -e '.total == 6 and .verified == 3 and .pendingModeration == 1 and .commentsClosed == 1 and .notAuthorized == 1 and .queued == 0 and .checking == 0 and .submitting == 0' <<<"$status" >/dev/null; then
    break
  fi
  sleep 1
done

jq -e '.status == "completedWithFailures" and .total == 6 and .submitted == 0 and .verified == 3 and .pendingModeration == 1 and .commentsClosed == 1 and .notAuthorized == 1 and .failed == 0 and .rejected == 0 and .unsupported == 0 and (.sources.items | length) == 6' <<<"$status" >/dev/null
jq -e '[.sources.items[] | select(.sourceUrl | endswith("/closed"))] | length == 1 and .[0].status == "commentsClosed"' <<<"$status" >/dev/null
jq -e '[.sources.items[] | select(.sourceUrl | contains("blocked-one-click.fixture"))] | length == 1 and .[0].status == "notAuthorized"' <<<"$status" >/dev/null

result=$(jq -cn --arg projectId "$project_id" --arg workflowId "$workflow_id" --arg campaignId "$campaign_id" \
  --arg jobId "$job_id" --argjson status "$status" \
  '{projectId:$projectId,workflowId:$workflowId,campaignId:$campaignId,jobId:$jobId,status:$status,operatorInputs:["sources","identities","comments","targetUrl"],operations:["backlink_workflow_start","backlink_workflow_get"]}')
if [[ -n "$result_file" ]]; then
  printf '%s\n' "$result" >"$result_file"
fi
printf '%s\n' "$result"
echo "one-click acceptance: five authorized sources reached 3 independently verified, 1 moderated, and 1 comments-closed result; the unauthorized control returned NotAuthorized" >&2

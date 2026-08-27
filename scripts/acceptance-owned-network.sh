#!/usr/bin/env bash
set -euo pipefail

: "${BACKLINKSTUDIO_API_KEY:?BACKLINKSTUDIO_API_KEY is required}"

api_url=${BASE_URL:-http://127.0.0.1:8080}
mcp_url=${MCP_URL:-http://127.0.0.1:8081/mcp}
repository_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
source_file=${OWNED_SOURCES_FILE:-$repository_root/acceptance/owned-sites.txt}
override_mode=${TEST_OWNERSHIP_OVERRIDE_ACCEPTANCE:-0}
target_url=${OWNED_TARGET_URL:-https://target.example/owned-network-acceptance}
work=$(mktemp -d)
trap 'rm -rf -- "$work"' EXIT

for command in curl jq; do
  command -v "$command" >/dev/null || { echo "$command is required" >&2; exit 69; }
done
[[ -f "$source_file" ]] || { echo "Owned source fixture not found: $source_file" >&2; exit 66; }

auth=(-H "Authorization: Bearer $BACKLINKSTUDIO_API_KEY")

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

mcp_tool_error() {
  local tool=$1
  local arguments=$2
  local request response
  request=$(jq -cn --arg tool "$tool" --argjson arguments "$arguments" \
    '{jsonrpc:"2.0",id:1,method:"tools/call",params:{name:$tool,arguments:$arguments}}')
  response=$(curl --fail --silent --show-error -X POST "$mcp_url" "${auth[@]}" \
    -H 'Content-Type: application/json' --data "$request")
  jq -e '.error == null and .result.isError == true' <<<"$response" >/dev/null || {
    jq -c . <<<"$response" >&2
    return 1
  }
  jq -cer '.result.content[0].text | fromjson' <<<"$response"
}

wait_job() {
  local project_id=$1
  local job_id=$2
  local description=$3
  local state payload
  for _ in $(seq 1 180); do
    payload=$(mcp_tool jobs_get "$(jq -cn --arg jobId "$job_id" '{jobId:$jobId}')")
    state=$(jq -r '.status' <<<"$payload")
    case "$state" in
      succeeded) return 0 ;;
      failed|deadLetter|cancelled)
        echo "$description failed with durable state $state (project=$project_id job=$job_id)" >&2
        jq -c '{status,lastFailureKind,lastError,attemptCount,recoveryCount}' <<<"$payload" >&2
        return 1
        ;;
    esac
    sleep 1
  done
  echo "$description did not finish within 180 seconds (job=$job_id)" >&2
  return 1
}

health=$(mcp_tool system_health '{}')
jq -e '.status == "healthy" and .databaseReachable == true and .schemaCurrent == true' <<<"$health" >/dev/null
echo "acceptance: MCP health and authentication passed" >&2

run_key="owned-acceptance-$(date +%s%N)"
project=$(mcp_tool project_create "$(jq -cn --arg key "$run_key-project" \
  '{name:"Owned Network Acceptance",primaryDomain:"target.example",description:"Disposable controlled WordPress acceptance",clientRequestKey:$key}')")
project_id=$(jq -er '.id' <<<"$project")
echo "acceptance: project created" >&2

mcp_tool policy_update "$(jq -cn --arg projectId "$project_id" --arg key "$run_key-policy" \
  '{projectId:$projectId,automationEnabled:true,minimumQualityScore:0,maximumRiskScore:100,manualReviewRequired:false,hourlyActionLimit:1000,dailyActionLimit:1000,perDomainActionLimit:1000,clientRequestKey:$key}')" >/dev/null

identity_pool=$(mcp_tool identity_pool_create "$(jq -cn --arg projectId "$project_id" --arg key "$run_key-identities" \
  '{projectId:$projectId,name:"Acceptance identities",selectionStrategy:"deterministicRandom",emailStrategy:"fixed",enabled:true,clientRequestKey:$key}')")
identity_pool_id=$(jq -er '.id' <<<"$identity_pool")
mcp_tool identity_create "$(jq -cn --arg poolId "$identity_pool_id" --arg key "$run_key-identity" \
  '{identityPoolId:$poolId,displayName:"BacklinkStudio Acceptance",email:"acceptance@example.invalid",organization:"BacklinkStudio",enabled:true,weight:1,clientRequestKey:$key}')" >/dev/null

template_pool=$(mcp_tool template_pool_create "$(jq -cn --arg projectId "$project_id" --arg key "$run_key-templates" \
  '{projectId:$projectId,name:"Acceptance comments",templateType:"wordPressComment",selectionStrategy:"deterministicRandom",placementMethod:"websiteField",enabled:true,clientRequestKey:$key}')")
template_pool_id=$(jq -er '.id' <<<"$template_pool")
mcp_tool template_create "$(jq -cn --arg poolId "$template_pool_id" --arg key "$run_key-template" \
  '{templatePoolId:$poolId,name:"Controlled acceptance",body:"Controlled acceptance comment from {{display_name}} on {{source_domain}}.",prefixVariants:["Automated owned-network check."],suffixVariants:["Reference: {{target_domain}}."],anchorVariants:[],targetUrlVariants:[],enabled:true,weight:1,clientRequestKey:$key}')" >/dev/null

network=$(mcp_tool owned_network_create "$(jq -cn --arg projectId "$project_id" --arg identityPoolId "$identity_pool_id" \
  --arg templatePoolId "$template_pool_id" --arg key "$run_key-network" \
  --arg ownershipStatus "$([[ "$override_mode" == "1" ]] && printf unverified || printf controlled)" \
  --argjson automationPermitted "$([[ "$override_mode" == "1" ]] && printf false || printf true)" \
  --argjson includeUnlisted "$([[ "$override_mode" == "1" ]] && printf true || printf false)" \
  '{projectId:$projectId,name:"Controlled WordPress network",description:"Disposable Docker acceptance sites",ownershipStatus:$ownershipStatus,automationPermitted:$automationPermitted,domains:([{domain:"wordpress-open",matchType:"exactHost",enabled:true},{domain:"wordpress-moderated",matchType:"exactHost",enabled:true},{domain:"wordpress-closed",matchType:"exactHost",enabled:true},{domain:"wordpress-direct",matchType:"exactHost",enabled:true}] + (if $includeUnlisted then [{domain:"wordpress-unlisted",matchType:"exactHost",enabled:true}] else [] end)),optionalNetworkTag:"acceptance",defaultIdentityPoolId:$identityPoolId,defaultTemplatePoolId:$templatePoolId,maxConcurrency:8,perDomainConcurrency:1,perDomainDelayMilliseconds:100,enabled:true,clientRequestKey:$key}')")
network_id=$(jq -er '.id' <<<"$network")
direct_profile=$(mcp_tool wordpress_site_profile_create "$(jq -cn --arg networkId "$network_id" --arg key "$run_key-direct-profile" \
  '{ownedNetworkId:$networkId,domain:"wordpress-direct",apiBaseUrl:"http://wordpress-direct/",credentialReference:"acceptance-direct",submissionMode:"directApi",enabled:true,clientRequestKey:$key}')")
jq -e '.domain == "wordpress-direct" and .credentialReference == "acceptance-direct" and .submissionMode == "directApi" and .enabled == true' <<<"$direct_profile" >/dev/null
echo "acceptance: policy, identity/template pools, and owned network created" >&2

source_content=$(<"$source_file")
expected_sources=4
expected_wordpress_sources=3
if [[ "$override_mode" == "1" ]]; then
  source_content+=$'\nhttp://wordpress-unlisted/?p=1'
  expected_sources=5
  expected_wordpress_sources=4
fi
import=$(mcp_tool submission_sources_import "$(jq -cn --arg projectId "$project_id" --arg networkId "$network_id" \
  --arg content "$source_content" --arg key "$run_key-import" \
  '{projectId:$projectId,ownedNetworkId:$networkId,format:"txt",fileName:"owned-sites.txt",tag:"acceptance",content:$content,clientRequestKey:$key}')")
import_id=$(jq -er '.importId' <<<"$import")
import_job_id=$(jq -er '.jobId' <<<"$import")
import_replay=$(mcp_tool submission_sources_import "$(jq -cn --arg projectId "$project_id" --arg networkId "$network_id" \
  --arg content "$source_content" --arg key "$run_key-import" \
  '{projectId:$projectId,ownedNetworkId:$networkId,format:"txt",fileName:"owned-sites.txt",tag:"acceptance",content:$content,clientRequestKey:$key}')")
jq -e --arg importId "$import_id" --arg jobId "$import_job_id" \
  '.importId == $importId and .jobId == $jobId' <<<"$import_replay" >/dev/null
import_conflict=$(mcp_tool_error submission_sources_import "$(jq -cn --arg projectId "$project_id" --arg networkId "$network_id" \
  --arg content "$source_content" --arg key "$run_key-import" \
  '{projectId:$projectId,ownedNetworkId:$networkId,format:"txt",fileName:"changed-owned-sites.txt",tag:"acceptance",content:($content + "\nhttps://wordpress-open/?p=999"),clientRequestKey:$key}')")
jq -e '.code == "invalid_request" and (.message | contains("idempotency key was already used with different input"))' <<<"$import_conflict" >/dev/null
wait_job "$project_id" "$import_job_id" "source import"
import_result=$(mcp_tool submission_source_import_get "$(jq -cn --arg importId "$import_id" '{importId:$importId}')")
jq -e --argjson expected "$expected_sources" '.status == "completed" and .accepted == $expected and .duplicates == 1 and .invalid == 0' <<<"$import_result" >/dev/null
echo "acceptance: durable TXT import normalized and deduplicated" >&2
echo "acceptance: source-import idempotency replay and conflict passed" >&2

validation=$(mcp_tool submission_sources_validate "$(jq -cn --arg projectId "$project_id" --arg networkId "$network_id" --arg key "$run_key-validation" \
  '{projectId:$projectId,ownedNetworkId:$networkId,maximumSources:100,clientRequestKey:$key}')")
validation_job_id=$(jq -er '.jobId' <<<"$validation")
wait_job "$project_id" "$validation_job_id" "source validation scheduling"

sources=''
for _ in $(seq 1 180); do
  sources=$(mcp_tool submission_sources_list "$(jq -cn --arg projectId "$project_id" --arg networkId "$network_id" \
    '{projectId:$projectId,ownedNetworkId:$networkId,limit:10}')")
  if [[ $(jq '[.items[] | select(.validationStatus == "valid")] | length' <<<"$sources") == "$expected_sources" ]]; then break; fi
  sleep 1
done
echo "acceptance: validated source observations" >&2
jq -c '[.items[] | {host,validationStatus,cmsType,supportsWordPressComment,adapterName,technicalCompatibility,validationReason}]' <<<"$sources" >&2
jq -e --argjson expected "$expected_sources" --arg status "$([[ "$override_mode" == "1" ]] && printf unverified || printf controlled)" \
  --argjson permitted "$([[ "$override_mode" == "1" ]] && printf false || printf true)" \
  '.items | length == $expected and all(.[]; .ownershipStatus == $status and .automationPermitted == $permitted)' <<<"$sources" >/dev/null
jq -e --argjson expected "$expected_wordpress_sources" '[.items[] | select(.cmsType == "wordPress" and .supportsWordPressComment == true and .adapterName == "OwnedWordPressCommentAdapter")] | length == $expected' <<<"$sources" >/dev/null
open_source_id=$(jq -er '.items[] | select(.host == "wordpress-open") | .id' <<<"$sources")
direct_source_id=$(jq -er '.items[] | select(.host == "wordpress-direct") | .id' <<<"$sources")
closed_source_id=$(jq -er '.items[] | select(.host == "wordpress-closed") | .id' <<<"$sources")
echo "acceptance: source validation and WordPress capability detection passed" >&2

preview=$(mcp_tool submission_preview "$(jq -cn --arg projectId "$project_id" --arg sourceId "$open_source_id" \
  --arg identityPoolId "$identity_pool_id" --arg templatePoolId "$template_pool_id" --arg targetUrl "$target_url" \
  '{projectId:$projectId,submissionSourceId:$sourceId,identityPoolId:$identityPoolId,templatePoolId:$templatePoolId,targetUrl:$targetUrl,attemptNumber:1}')")
jq -e '.ownershipPermitted == true and .expectedExecutionStrategy == "StandardComment" and .placementMethod == "websiteField" and (.resolvedComment | length > 0)' <<<"$preview" >/dev/null
echo "acceptance: deterministic submission preview passed" >&2
direct_preview=$(mcp_tool submission_preview "$(jq -cn --arg projectId "$project_id" --arg sourceId "$direct_source_id" \
  --arg identityPoolId "$identity_pool_id" --arg templatePoolId "$template_pool_id" --arg targetUrl "$target_url" \
  '{projectId:$projectId,submissionSourceId:$sourceId,identityPoolId:$identityPoolId,templatePoolId:$templatePoolId,targetUrl:$targetUrl,attemptNumber:1}')")
jq -e '.ownershipPermitted == true and .expectedExecutionStrategy == "DirectApi"' <<<"$direct_preview" >/dev/null
echo "acceptance: direct authenticated WordPress API preview passed" >&2
if [[ "$override_mode" == "1" ]]; then
  closed_preview=$(mcp_tool submission_preview "$(jq -cn --arg projectId "$project_id" --arg sourceId "$closed_source_id" \
    --arg identityPoolId "$identity_pool_id" --arg templatePoolId "$template_pool_id" --arg targetUrl "$target_url" \
    '{projectId:$projectId,submissionSourceId:$sourceId,identityPoolId:$identityPoolId,templatePoolId:$templatePoolId,targetUrl:$targetUrl,attemptNumber:1}')")
  jq -e '.ownershipPermitted == true and .technicalCompatibility != "compatible" and .expectedExecutionStrategy == "ManualActionRequired"' <<<"$closed_preview" >/dev/null
  unlisted_source_id=$(jq -er '.items[] | select(.host == "wordpress-unlisted") | .id' <<<"$sources")
  unlisted_preview=$(mcp_tool submission_preview "$(jq -cn --arg projectId "$project_id" --arg sourceId "$unlisted_source_id" \
    --arg identityPoolId "$identity_pool_id" --arg templatePoolId "$template_pool_id" --arg targetUrl "$target_url" \
    '{projectId:$projectId,submissionSourceId:$sourceId,identityPoolId:$identityPoolId,templatePoolId:$templatePoolId,targetUrl:$targetUrl,attemptNumber:1}')")
  jq -e '.ownershipPermitted == false' <<<"$unlisted_preview" >/dev/null
  echo "acceptance: closed source kept its compatibility block and non-allowlisted host kept its ownership block" >&2
fi

campaign=$(mcp_tool campaign_create "$(jq -cn --arg projectId "$project_id" --arg networkId "$network_id" \
  --arg identityPoolId "$identity_pool_id" --arg templatePoolId "$template_pool_id" --arg targetUrl "$target_url" --arg key "$run_key-campaign" \
  '{projectId:$projectId,name:"Owned WordPress acceptance",ownedNetworkId:$networkId,targetUrl:$targetUrl,identityPoolId:$identityPoolId,templatePoolId:$templatePoolId,globalConcurrency:8,perDomainConcurrency:1,perDomainDelayMilliseconds:100,maximumAttempts:2,verificationDelaySeconds:1,mode:"automaticOwnedNetwork",technicalCompatibility:"compatible",validationStatus:"valid",tag:"acceptance",dailyActionLimit:1000,clientRequestKey:$key}')")
campaign_id=$(jq -er '.id' <<<"$campaign")
start=$(mcp_tool campaign_start "$(jq -cn --arg campaignId "$campaign_id" --arg key "$run_key-start" '{campaignId:$campaignId,clientRequestKey:$key}')")
jq -e '.campaignId != null and (.jobIds | length) >= 1' <<<"$start" >/dev/null
start_replay=$(mcp_tool campaign_start "$(jq -cn --arg campaignId "$campaign_id" --arg key "$run_key-start" '{campaignId:$campaignId,clientRequestKey:$key}')")
jq -e --arg campaignId "$campaign_id" --argjson expectedJobIds "$(jq '.jobIds' <<<"$start")" \
  '.campaignId == $campaignId and .jobIds == $expectedJobIds' <<<"$start_replay" >/dev/null
start_conflict=$(mcp_tool_error campaign_start "$(jq -cn --arg key "$run_key-start" \
  '{campaignId:"00000000-0000-0000-0000-000000000002",clientRequestKey:$key}')")
jq -e '.code == "invalid_request" and (.message | contains("idempotency key was already used with different input"))' <<<"$start_conflict" >/dev/null
echo "acceptance: campaign-start idempotency replay and conflict passed" >&2

for _ in $(seq 1 180); do
  campaign=$(mcp_tool campaign_get "$(jq -cn --arg campaignId "$campaign_id" '{campaignId:$campaignId}')")
  [[ $(jq -r '.ownedNetwork.expansionCompleted' <<<"$campaign") == "true" ]] && break
  sleep 1
done
jq -e '.ownedNetwork.expansionCompleted == true and .ownedNetwork.sourcesQueued == 3' <<<"$campaign" >/dev/null
echo "acceptance: campaign expanded into durable submission jobs" >&2

submissions=''
for _ in $(seq 1 180); do
  submissions=$(mcp_tool submissions_list "$(jq -cn --arg campaignId "$campaign_id" '{campaignId:$campaignId,limit:10}')")
  active=$(jq '[.items[] | select(.status == "queued" or .status == "processing")] | length' <<<"$submissions")
  [[ $(jq '.items | length' <<<"$submissions") == "3" && "$active" == "0" ]] && break
  sleep 1
done
echo "acceptance: durable submission observations" >&2
jq -c '[.items[] | {id,submissionSourceId,status,targetUrl,placementType}]' <<<"$submissions" >&2
while IFS= read -r observed_submission_id; do
  observed_attempts=$(mcp_tool submission_attempts "$(jq -cn --arg submissionJobId "$observed_submission_id" '{submissionJobId:$submissionJobId}')")
  jq -c --arg submissionJobId "$observed_submission_id" '{submissionJobId:$submissionJobId,attempts:[.[] | {attemptNumber,result,httpStatus,strategy,moderationStatus,failureKind,error}]}' <<<"$observed_attempts" >&2
done < <(jq -r '.items[].id' <<<"$submissions")
jq -e '[.items[] | select(.status == "submitted")] | length == 2' <<<"$submissions" >/dev/null
jq -e '[.items[] | select(.status == "pendingModeration")] | length == 1' <<<"$submissions" >/dev/null
echo "acceptance: published and moderation submission states persisted" >&2

backlinks=''
for _ in $(seq 1 180); do
  backlinks=$(mcp_tool backlinks_list "$(jq -cn --arg projectId "$project_id" '{projectId:$projectId,limit:10}')")
  [[ $(jq '[.items[] | select(.status == "verified")] | length' <<<"$backlinks") -ge 1 ]] && break
  sleep 1
done
jq -e '.items | length == 3' <<<"$backlinks" >/dev/null
jq -e '[.items[] | select(.status == "verified" and .targetUrl == $target)] | length == 2' --arg target "$target_url" <<<"$backlinks" >/dev/null
jq -e '[.items[] | select(.submissionSourceId != null and .submissionAttemptId != null)] | length == 3' <<<"$backlinks" >/dev/null
echo "acceptance: verification found real target-link evidence" >&2

report=$(mcp_tool reports_generate "$(jq -cn --arg projectId "$project_id" --arg campaignId "$campaign_id" --arg key "$run_key-report" \
  '{projectId:$projectId,campaignId:$campaignId,kind:"campaignPerformance",format:"json",clientRequestKey:$key}')")
report_id=$(jq -er '.reportId' <<<"$report")
report_job_id=$(jq -er '.jobId' <<<"$report")
wait_job "$project_id" "$report_job_id" "report generation"
report_metadata=$(mcp_tool reports_get "$(jq -cn --arg reportId "$report_id" '{reportId:$reportId}')")
jq -e '.status == "completed" and .rowCount == 3 and (.sha256 | length == 64)' <<<"$report_metadata" >/dev/null
curl --fail --silent --show-error "$api_url/api/v1/reports/$report_id/download" "${auth[@]}" -o "$work/report.json"
jq -e '.summary.verified == 2 and .summary.attemptCount == 3 and .summary.submissionSuccessRate == 100 and .summary.verificationRate > 66 and .summary.verificationRate < 67 and (.summary.domainPerformance | length) == 3 and (.summary.templatePerformance | length) == 1 and (.summary.identityPerformance | length) == 1 and (.details | length) == 3' "$work/report.json" >/dev/null
echo "acceptance: durable report generated and downloaded" >&2

for report_format in csv xlsx html; do
  extra_report=$(mcp_tool reports_generate "$(jq -cn --arg projectId "$project_id" --arg campaignId "$campaign_id" \
    --arg format "$report_format" --arg key "$run_key-report-$report_format" \
    '{projectId:$projectId,campaignId:$campaignId,kind:"campaignPerformance",format:$format,clientRequestKey:$key}')")
  extra_report_id=$(jq -er '.reportId' <<<"$extra_report")
  wait_job "$project_id" "$(jq -er '.jobId' <<<"$extra_report")" "$report_format report generation"
  extra_metadata=$(mcp_tool reports_get "$(jq -cn --arg reportId "$extra_report_id" '{reportId:$reportId}')")
  jq -e '.status == "completed" and .rowCount == 3 and (.sha256 | length == 64)' <<<"$extra_metadata" >/dev/null
  curl --fail --silent --show-error "$api_url/api/v1/reports/$extra_report_id/download" "${auth[@]}" \
    -o "$work/report.$report_format"
done

csv_summary=$(sed -n '2p' "$work/report.csv" | tr -d '\r')
IFS=',' read -r -a csv_values <<<"$csv_summary"
[[ ${csv_values[7]//\"/} == 2 && ${csv_values[20]//\"/} == 3 && ${csv_values[18]//\"/} == 1 ]] || {
  echo "CSV report summary does not agree with database-backed JSON totals." >&2
  exit 70
}

mapfile -t xlsx_values < <(unzip -p "$work/report.xlsx" xl/worksheets/sheet1.xml |
  sed 's#<t xml:space="preserve">#\n#g' | tail -n +2 | sed 's#</t>.*##')
[[ ${xlsx_values[30]} == 2 && ${xlsx_values[43]} == 3 && ${xlsx_values[41]} == 1 ]] || {
  echo "XLSX report summary does not agree with database-backed JSON totals." >&2
  exit 70
}
unzip -p "$work/report.xlsx" xl/worksheets/sheet3.xml | grep -F 'ref="A1:W4"' >/dev/null

mapfile -t html_values < <(grep -o '<td>[^<]*</td>' "$work/report.html" | sed 's#</\?td>##g')
[[ ${html_values[7]} == 2 && ${html_values[20]} == 3 && ${html_values[18]} == 1 ]] || {
  echo "HTML report summary does not agree with database-backed JSON totals." >&2
  exit 70
}
grep -F "Content-Security-Policy" "$work/report.html" >/dev/null
echo "acceptance: JSON, CSV, XLSX, and HTML totals agree with database-backed summary" >&2

submitted_id=$(jq -er --arg sourceId "$open_source_id" '.items[] | select(.submissionSourceId == $sourceId and .status == "submitted") | .id' <<<"$submissions")
attempts=$(mcp_tool submission_attempts "$(jq -cn --arg submissionJobId "$submitted_id" '{submissionJobId:$submissionJobId}')")
jq -e 'length == 1 and .[0].strategy == "StandardComment" and .[0].result == "submitted"' <<<"$attempts" >/dev/null
direct_submission_id=$(jq -er --arg sourceId "$direct_source_id" '.items[] | select(.submissionSourceId == $sourceId) | .id' <<<"$submissions")
direct_attempts=$(mcp_tool submission_attempts "$(jq -cn --arg submissionJobId "$direct_submission_id" '{submissionJobId:$submissionJobId}')")
jq -e 'length == 1 and .[0].strategy == "DirectApi" and .[0].result == "submitted" and (.[0].externalReference | length) > 0' <<<"$direct_attempts" >/dev/null
echo "acceptance: real authenticated WordPress REST API placement persisted without premature verification" >&2

result=$(jq -n --arg projectId "$project_id" --arg networkId "$network_id" --arg importId "$import_id" \
  --arg validationJobId "$validation_job_id" --arg campaignId "$campaign_id" --arg reportId "$report_id" \
  --argjson imported "$(jq '.accepted' <<<"$import_result")" --argjson duplicates "$(jq '.duplicates' <<<"$import_result")" \
  --argjson sources "$(jq '.items | length' <<<"$sources")" --argjson submissions "$(jq '.items | length' <<<"$submissions")" \
  --argjson verified "$(jq '[.items[] | select(.status == "verified")] | length' <<<"$backlinks")" \
  '{status:"passed",projectId:$projectId,ownedNetworkId:$networkId,importId:$importId,validationJobId:$validationJobId,campaignId:$campaignId,reportId:$reportId,imported:$imported,duplicates:$duplicates,sources:$sources,submissions:$submissions,verified:$verified}')
if [[ -n ${OWNED_ACCEPTANCE_RESULT_FILE:-} ]]; then
  printf '%s\n' "$result" > "$OWNED_ACCEPTANCE_RESULT_FILE"
fi
printf '%s\n' "$result"

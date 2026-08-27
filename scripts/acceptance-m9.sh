#!/usr/bin/env bash
set -euo pipefail

api_url="${BACKLINKSTUDIO_API_URL:-http://127.0.0.1:8080}"
mcp_url="${BACKLINKSTUDIO_MCP_URL:-http://127.0.0.1:8081/mcp}"
api_key="${BACKLINKSTUDIO_API_KEY:?BACKLINKSTUDIO_API_KEY must be set}"
work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT

status() {
  curl --silent --show-error --output "$work/response" --write-out '%{http_code}' "$@"
}

[[ "$(status "$api_url/health/ready")" == "200" ]]
[[ "$(status "$api_url/api/v1/reports?projectId=00000000-0000-0000-0000-000000000000")" == "401" ]]

project="$(curl --fail --silent --show-error \
  --header "X-Api-Key: $api_key" \
  --header 'Idempotency-Key: m9-accept-project-v1' \
  --header 'Content-Type: application/json' \
  --data '{"name":"Milestone 9 Reporting","primaryDomain":"reports.example","description":"M9 acceptance"}' \
  "$api_url/api/v1/projects")"
project_id="$(jq --exit-status --raw-output '.id' <<<"$project")"

reader="$(curl --fail --silent --show-error \
  --header "X-Api-Key: $api_key" \
  --header 'Idempotency-Key: m9-accept-reader-v1' \
  --header 'Content-Type: application/json' \
  --data '{"name":"M9 report reader","scopes":["projects:read","reports:read"],"expiresAt":null}' \
  "$api_url/api/v1/agent-credentials")"
reader_key="$(jq --exit-status --raw-output '.apiKey' <<<"$reader")"
[[ "$reader_key" == bls_* ]]
[[ "$(status --request POST --header "X-Api-Key: $reader_key" --header 'Idempotency-Key: forbidden-report' --header 'Content-Type: application/json' --data "{\"projectId\":\"$project_id\",\"campaignId\":null,\"kind\":\"backlinkInventory\",\"format\":\"json\"}" "$api_url/api/v1/reports")" == "403" ]]

mcp_request() {
  curl --fail --silent --show-error --header "X-Api-Key: $api_key" --header 'Content-Type: application/json' --data-binary @- "$mcp_url"
}

tools="$(jq --compact-output --null-input '{jsonrpc:"2.0",id:1,method:"tools/list",params:{}}' | mcp_request)"
jq --exit-status '[.result.tools[].name] | index("report_generate") != null and index("reports_list") != null and index("reports_get") != null' <<<"$tools" >/dev/null

mcp_generate() {
  local request_key="$1"
  jq --compact-output --null-input --arg projectId "$project_id" --arg key "$request_key" \
    '{jsonrpc:"2.0",id:2,method:"tools/call",params:{name:"report_generate",arguments:{projectId:$projectId,kind:"backlinkInventory",format:"json",clientRequestKey:$key}}}' \
    | mcp_request | jq --exit-status --compact-output '.result.content[0].text | fromjson'
}

json_report="$(mcp_generate m9-accept-json-v1)"
json_report_replay="$(mcp_generate m9-accept-json-v1)"
[[ "$(jq -r '.reportId' <<<"$json_report")" == "$(jq -r '.reportId' <<<"$json_report_replay")" ]]
[[ "$(jq -r '.jobId' <<<"$json_report")" == "$(jq -r '.jobId' <<<"$json_report_replay")" ]]

printf '%s\n' "$json_report" > "$work/reports.ndjson"
for format in csv xlsx html; do
  curl --fail --silent --show-error \
    --header "X-Api-Key: $api_key" \
    --header "Idempotency-Key: m9-accept-$format-v1" \
    --header 'Content-Type: application/json' \
    --data "{\"projectId\":\"$project_id\",\"campaignId\":null,\"kind\":\"backlinkInventory\",\"format\":\"$format\"}" \
    "$api_url/api/v1/reports" >> "$work/reports.ndjson"
  printf '\n' >> "$work/reports.ndjson"
done

while IFS= read -r accepted; do
  [[ -n "$accepted" ]] || continue
  job_id="$(jq --exit-status --raw-output '.jobId' <<<"$accepted")"
  report_id="$(jq --exit-status --raw-output '.reportId' <<<"$accepted")"
  job_status=""
  for _ in $(seq 1 60); do
    job_status="$(curl --fail --silent --show-error --header "X-Api-Key: $api_key" "$api_url/api/v1/jobs/$job_id" | jq --raw-output '.status')"
    [[ "$job_status" == "succeeded" ]] && break
    [[ "$job_status" == "failed" || "$job_status" == "deadLetter" || "$job_status" == "cancelled" ]] && { echo "report job failed: $job_id $job_status" >&2; exit 1; }
    sleep 1
  done
  [[ "$job_status" == "succeeded" ]]
  metadata="$(curl --fail --silent --show-error --header "X-Api-Key: $reader_key" "$api_url/api/v1/reports/$report_id")"
  jq --exit-status '.status == "completed" and .rowCount == 0 and .byteLength > 0 and (.sha256 | length) == 64' <<<"$metadata" >/dev/null
  format="$(jq --raw-output '.format' <<<"$metadata")"
  curl --fail --silent --show-error --dump-header "$work/$format.headers" --header "X-Api-Key: $reader_key" --output "$work/report.$format" "$api_url/api/v1/reports/$report_id/download"
  grep -qi '^etag: "[0-9a-f]\{64\}"' "$work/$format.headers"
done < "$work/reports.ndjson"

jq --exit-status '.summary.verified == 0 and (.details | length) == 0' "$work/report.json" >/dev/null
grep -q '"Candidates"' "$work/report.csv"
grep -q 'Content-Security-Policy' "$work/report.html"
unzip -t "$work/report.xlsx" >/dev/null
unzip -l "$work/report.xlsx" | grep -q 'xl/worksheets/sheet2.xml'

listing="$(curl --fail --silent --show-error --header "X-Api-Key: $reader_key" "$api_url/api/v1/reports?projectId=$project_id&limit=2")"
jq --exit-status '(.items | length) == 2 and .nextCursor != null' <<<"$listing" >/dev/null

printf 'M9 acceptance passed: project=%s reports=4 formats=json,csv,xlsx,html\n' "$project_id"

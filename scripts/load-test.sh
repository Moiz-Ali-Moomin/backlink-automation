#!/usr/bin/env bash
set -euo pipefail

: "${BACKLINKSTUDIO_API_KEY:?BACKLINKSTUDIO_API_KEY is required}"
: "${BACKLINKSTUDIO_PROJECT_ID:?BACKLINKSTUDIO_PROJECT_ID is required}"

repository_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
network=${K6_DOCKER_NETWORK:-host}
docker run --rm \
  --network "$network" \
  -e BASE_URL="${BASE_URL:-http://127.0.0.1:8080}" \
  -e BACKLINKSTUDIO_API_KEY \
  -e BACKLINKSTUDIO_PROJECT_ID \
  -e LOAD_RATE="${LOAD_RATE:-10}" \
  -e LOAD_DURATION="${LOAD_DURATION:-20s}" \
  -v "$repository_root/tests/load:/scripts:ro" \
  grafana/k6:2.1.0 run /scripts/milestone10.js

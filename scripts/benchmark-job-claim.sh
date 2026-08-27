#!/usr/bin/env bash
set -euo pipefail

: "${BACKLINKSTUDIO_BENCHMARK_CONFIRM:?Set BACKLINKSTUDIO_BENCHMARK_CONFIRM=durable-job-claim to run the rollback-only database benchmark}"
[[ "$BACKLINKSTUDIO_BENCHMARK_CONFIRM" == "durable-job-claim" ]] || { echo "Benchmark confirmation value is invalid." >&2; exit 64; }

row_count=${1:-1000}
[[ "$row_count" =~ ^(1000|20000|80000|100000|500000)$ ]] || { echo "Row count must be 1000, 20000, 80000, 100000, or 500000." >&2; exit 64; }
: "${PGHOST:?PGHOST is required}"
: "${PGUSER:?PGUSER is required}"
: "${PGDATABASE:?PGDATABASE is required}"

repository_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
artifact_directory=${BACKLINKSTUDIO_BENCHMARK_ARTIFACTS:-"$repository_root/.tmp/benchmarks"}
mkdir -p -- "$artifact_directory"
artifact="$artifact_directory/job-claim-${row_count}.txt"

psql --no-psqlrc --set ON_ERROR_STOP=1 --set row_count="$row_count" \
  --file "$repository_root/scripts/benchmark-job-claim.sql" | tee "$artifact"

echo "Durable job-claim benchmark completed and rolled back: rows=$row_count artifact=$artifact"

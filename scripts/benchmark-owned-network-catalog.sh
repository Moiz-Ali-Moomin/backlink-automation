#!/usr/bin/env bash
set -euo pipefail

: "${BACKLINKSTUDIO_BENCHMARK_CONFIRM:?Set BACKLINKSTUDIO_BENCHMARK_CONFIRM=owned-network-catalog to run the rollback-only database benchmark}"
[[ "$BACKLINKSTUDIO_BENCHMARK_CONFIRM" == "owned-network-catalog" ]] || { echo "Benchmark confirmation value is invalid." >&2; exit 64; }

row_count=${1:-10000}
[[ "$row_count" =~ ^(10000|100000|1000000|5000000)$ ]] || { echo "Row count must be 10000, 100000, 1000000, or 5000000." >&2; exit 64; }
: "${PGHOST:?PGHOST is required}"
: "${PGUSER:?PGUSER is required}"
: "${PGDATABASE:?PGDATABASE is required}"

repository_root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
artifact_directory=${BACKLINKSTUDIO_BENCHMARK_ARTIFACTS:-"$repository_root/.tmp/benchmarks"}
mkdir -p -- "$artifact_directory"
artifact="$artifact_directory/owned-network-catalog-${row_count}.txt"

psql --no-psqlrc --set ON_ERROR_STOP=1 --set row_count="$row_count" \
  --file "$repository_root/scripts/benchmark-owned-network-catalog.sql" | tee "$artifact"

echo "Owned-network catalog benchmark completed and rolled back: rows=$row_count artifact=$artifact"

#!/bin/sh
set -eu

if [ "$#" -ne 1 ]; then
  echo "Usage: postgres-restore.sh /path/to/backup.dump" >&2
  exit 64
fi

: "${PGDATABASE:?PGDATABASE must name the restore target}"
: "${PGUSER:?PGUSER must name the restore role}"

case "$PGDATABASE" in
  postgres|template0|template1)
    echo "Refusing to restore into PostgreSQL system database '$PGDATABASE'." >&2
    exit 64
    ;;
esac

if [ "${BACKLINKSTUDIO_RESTORE_CONFIRM:-}" != "$PGDATABASE" ]; then
  echo "Set BACKLINKSTUDIO_RESTORE_CONFIRM exactly to '$PGDATABASE' to authorize destructive restore." >&2
  exit 64
fi

archive=$1
archive_directory=$(cd -- "$(dirname -- "$archive")" && pwd -P)
archive_name=$(basename -- "$archive")
archive="$archive_directory/$archive_name"
checksum="$archive.sha256"

if [ ! -f "$archive" ] || [ ! -f "$checksum" ]; then
  echo "Both the backup and its .sha256 sidecar are required." >&2
  exit 66
fi

expected_checksum=$(awk 'NR == 1 { print $1; exit }' "$checksum")
if [ "${#expected_checksum}" -ne 64 ]; then
  echo "Backup checksum sidecar is malformed." >&2
  exit 65
fi
case "$expected_checksum" in
  *[!0-9A-Fa-f]*)
    echo "Backup checksum sidecar is malformed." >&2
    exit 65
    ;;
esac
actual_checksum=$(sha256sum -- "$archive" | awk '{ print $1 }')
if [ "$actual_checksum" != "$expected_checksum" ]; then
  echo "Backup checksum verification failed." >&2
  exit 65
fi
pg_restore --list "$archive" >/dev/null
pg_restore --clean --if-exists --no-owner --no-acl --exit-on-error --single-transaction --dbname="$PGDATABASE" "$archive"
echo "Restore completed into database: $PGDATABASE"

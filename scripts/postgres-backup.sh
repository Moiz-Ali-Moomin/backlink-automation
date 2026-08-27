#!/bin/sh
set -eu

if [ "$#" -ne 1 ]; then
  echo "Usage: postgres-backup.sh /absolute/or/relative/path.dump" >&2
  exit 64
fi

: "${PGDATABASE:?PGDATABASE must name the database to back up}"
: "${PGUSER:?PGUSER must name the backup role}"

archive=$1
archive_directory=$(dirname -- "$archive")
archive_name=$(basename -- "$archive")
mkdir -p -- "$archive_directory"
archive_directory=$(cd -- "$archive_directory" && pwd -P)
archive="$archive_directory/$archive_name"
umask 077
temporary_archive=$(mktemp "$archive_directory/.${archive_name}.partial.XXXXXX")
temporary_checksum=$(mktemp "$archive_directory/.${archive_name}.sha256.partial.XXXXXX")

cleanup() {
  rm -f -- "$temporary_archive" "$temporary_checksum"
}
trap cleanup EXIT

pg_dump --format=custom --compress=6 --no-owner --no-acl --file="$temporary_archive" "$PGDATABASE"
pg_restore --list "$temporary_archive" >/dev/null
chmod 600 "$temporary_archive"
mv -f -- "$temporary_archive" "$archive"
(
  cd -- "$archive_directory"
  sha256sum -- "$archive_name"
) >"$temporary_checksum"
chmod 600 "$temporary_checksum"
mv -f -- "$temporary_checksum" "$archive.sha256"

trap - EXIT
echo "Backup completed: $archive"

#!/bin/sh
set -eu

install_site() {
  path="$1"
  url="$2"
  title="$3"
  moderation="$4"
  comment_status="$5"
  table_prefix="$6"

  export WORDPRESS_TABLE_PREFIX="$table_prefix"

  while [ ! -f "$path/wp-config.php" ]; do sleep 2; done
  until wp --allow-root --path="$path" db check >/dev/null 2>&1; do sleep 2; done
  if ! wp --allow-root --path="$path" core is-installed >/dev/null 2>&1; then
    wp --allow-root --path="$path" core install \
      --url="$url" \
      --title="$title" \
      --admin_user=acceptance-operator \
      --admin_password="$WORDPRESS_TEST_ADMIN_PASSWORD" \
      --admin_email=acceptance@example.invalid \
      --skip-email
  fi
  wp --allow-root --path="$path" option update default_comment_status open >/dev/null
  wp --allow-root --path="$path" option update comment_moderation "$moderation" >/dev/null
  wp --allow-root --path="$path" option update comment_previously_approved 0 >/dev/null
  wp --allow-root --path="$path" post update 1 --comment_status="$comment_status" >/dev/null
}

install_site /sites/open http://wordpress-open "Open comments" 0 open wp_
install_site /sites/moderated http://wordpress-moderated "Moderated comments" 1 open moderated_
install_site /sites/closed http://wordpress-closed "Closed comments" 0 closed closed_
install_site /sites/direct http://wordpress-direct "Direct API comments" 0 open direct_

# This disposable Application Password exists only in the isolated acceptance
# volume. The wrapper reads it into the worker's server-side configuration and
# never prints it or passes it through an API, MCP argument, job, or campaign.
umask 077
export WORDPRESS_TABLE_PREFIX=direct_
wp --allow-root --path=/sites/direct user application-password create acceptance-operator \
  "BacklinkStudio closure" --app-id=2d7c31a0-747c-4a9a-90ac-b902b3721751 --porcelain \
  > /credentials/direct-api-password

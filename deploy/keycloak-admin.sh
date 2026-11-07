#!/bin/sh
# One-time Keycloak setup on the host, after its first start:
#   1. points the master realm (the admin console's) at http://localhost:8080, so the console signs in through the
#      SSH tunnel; the public hostname never serves the master realm;
#   2. creates a permanent master-realm admin;
#   3. signs in as that admin and deletes the temporary bootstrap admin.
#
# Usage, as root on the host: deploy/keycloak-admin.sh <new admin username>
# The new admin's password is read from the terminal, so it stays out of shell history. See docs/deployment.md.
# ENV_FILE overrides where the settings are read from (default: .env next to docker-compose.prod.yml).
set -eu

if [ "$#" -ne 1 ]; then
  echo "Usage: $0 <new admin username>" >&2
  exit 2
fi
new_admin="$1"

repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
env_file="${ENV_FILE:-$repo_dir/.env}"

compose() {
  docker compose -f "$repo_dir/docker-compose.prod.yml" --env-file "$env_file" "$@"
}

if [ -t 0 ]; then
  printf 'Password for %s (12+ characters): ' "$new_admin" >&2
  stty -echo
  read -r password
  stty echo
  printf '\n' >&2
else
  read -r password
fi
if [ "${#password}" -lt 12 ]; then
  echo "Use at least 12 characters." >&2
  exit 1
fi

# Runs inside the Keycloak container, which already has the bootstrap admin's credentials in its environment. The new
# password arrives on stdin.
# shellcheck disable=SC2016 # The variables are the container's, so they must expand there, not here.
printf '%s\n' "$password" | compose exec -T -e NEW_ADMIN="$new_admin" keycloak sh -eu -c '
  read -r new_password
  kcadm() { /opt/keycloak/bin/kcadm.sh "$@" --config /tmp/kcadm.config; }

  kcadm config credentials --server http://localhost:8080 --realm master \
    --user "$KC_BOOTSTRAP_ADMIN_USERNAME" --password "$KC_BOOTSTRAP_ADMIN_PASSWORD"

  kcadm update realms/master -s attributes.frontendUrl=http://localhost:8080
  echo "Master realm: admin console signs in at http://localhost:8080 (through the SSH tunnel)."

  kcadm create users -r master -s username="$NEW_ADMIN" -s enabled=true
  kcadm set-password -r master --username "$NEW_ADMIN" --new-password "$new_password"
  kcadm add-roles -r master --uusername "$NEW_ADMIN" --rolename admin
  echo "Created permanent admin $NEW_ADMIN."

  kcadm config credentials --server http://localhost:8080 --realm master \
    --user "$NEW_ADMIN" --password "$new_password"
  temp_id=$(kcadm get users -r master -q username="$KC_BOOTSTRAP_ADMIN_USERNAME" -q exact=true --fields id --format csv --noquotes)
  if [ -n "$temp_id" ]; then
    kcadm delete "users/$temp_id" -r master
    echo "Deleted the temporary admin $KC_BOOTSTRAP_ADMIN_USERNAME."
  fi
  rm -f /tmp/kcadm.config
'

echo "Done. You can remove KC_BOOTSTRAP_ADMIN_PASSWORD from $env_file: Keycloak only reads it when its database is new."

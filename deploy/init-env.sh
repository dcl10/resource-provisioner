#!/bin/sh
# Creates the host's .env (root-only) from deploy/production.env.example, on first boot: fills in the two hostnames and
# generates the database passwords, AUTH_SECRET and Keycloak's temporary admin password. An existing .env is left alone.
#
# Usage: deploy/init-env.sh <app host> <auth host>
set -eu

if [ "$#" -ne 2 ]; then
  echo "Usage: $0 <app host> <auth host>" >&2
  exit 2
fi
app_host="$1"
auth_host="$2"

repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
env_file="$repo_dir/.env"

if [ -e "$env_file" ]; then
  echo "$env_file already exists; leaving it alone."
  exit 0
fi

# Letters and digits only, so the values are safe inside connection strings.
random_secret() {
  openssl rand -base64 48 | tr -dc 'A-Za-z0-9' | cut -c1-40
}

umask 077
sed \
  -e "s|^APP_HOST=.*|APP_HOST=$app_host|" \
  -e "s|^AUTH_HOST=.*|AUTH_HOST=$auth_host|" \
  -e "s|^POSTGRES_APP_PASSWORD=.*|POSTGRES_APP_PASSWORD=$(random_secret)|" \
  -e "s|^POSTGRES_KEYCLOAK_PASSWORD=.*|POSTGRES_KEYCLOAK_PASSWORD=$(random_secret)|" \
  -e "s|^AUTH_SECRET=.*|AUTH_SECRET=$(random_secret)|" \
  -e "s|^KC_BOOTSTRAP_ADMIN_PASSWORD=.*|KC_BOOTSTRAP_ADMIN_PASSWORD=$(random_secret)|" \
  "$repo_dir/deploy/production.env.example" > "$env_file"
chmod 600 "$env_file"

echo "Created $env_file. Fill in the rest before deploying: see docs/deployment.md."

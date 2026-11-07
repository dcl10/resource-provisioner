#!/bin/sh
# Deploys the app on the host: checks out a branch or tag, rebuilds the images and restarts what changed. Run it as
# root on the host: deploy/deploy.sh [branch or tag], default main. See docs/deployment.md.
set -eu

# Everything is in main(), which the shell reads in full before running: this script updates its own checkout, and
# sh would otherwise go on reading the new version part-way through.
main() {
  ref="${1:-main}"
  repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
  cd "$repo_dir"

  if [ ! -f .env ]; then
    echo "No .env in $repo_dir: create it with deploy/init-env.sh first." >&2
    exit 1
  fi

  git fetch --prune --tags origin
  if git rev-parse --verify --quiet "origin/$ref" >/dev/null; then
    target="origin/$ref"
  else
    target="$ref"
  fi
  git checkout --quiet --detach "$target"
  echo "Deploying $(git rev-parse --short HEAD) ($ref)"

  docker compose -f docker-compose.prod.yml up -d --build --remove-orphans
  docker image prune -f >/dev/null
  docker compose -f docker-compose.prod.yml ps
}

main "$@"
exit

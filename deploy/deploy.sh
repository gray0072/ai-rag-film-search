#!/usr/bin/env bash
# Deploys a version of the demo environment, or rolls back to an earlier one (deploy/README.md):
#   ./deploy.sh v0.20.0
# Steps: back up the database, pull the images, apply migrations, start the new containers, wait for /health, record
# the tag in .env. Run on the server in the folder with compose.yaml and .env.
set -euo pipefail
cd "$(dirname "$0")"

tag="${1:?usage: ./deploy.sh <image tag>}"
compose() { docker compose --env-file .env "$@"; }

running="$(sed -n 's/^FILMSEARCH_TAG=//p' .env)"
echo "Deploying ${tag} (configured: ${running:-none})"

# The shell variable wins over .env for this run; .env is updated only after a successful deploy.
export FILMSEARCH_TAG="${tag}"

# SKIP_PULL=1: images built on this machine (docker compose -f compose.yaml -f compose.build.yaml build).
if [ "${SKIP_PULL:-}" != "1" ]; then
  compose pull --quiet
fi
compose up -d --wait postgres

# A dump before the migrations: the way back if a migration is not backward compatible.
./backup.sh

# Migrations run while the old API still serves requests, so they must be backward compatible (expand, then contract
# in a later release). If they fail, nothing else is restarted.
compose run --rm migrate

compose up -d --remove-orphans

echo "Waiting for the API…"
for _ in $(seq 1 60); do
  # Degraded (HTTP 200): the store works, an AI provider is not reachable; the deploy goes on with a warning.
  health="$(compose exec -T web wget -qO- http://api:8080/health 2>/dev/null || true)"
  if [ "${health}" = "Healthy" ] || [ "${health}" = "Degraded" ]; then
    if [ "${health}" = "Degraded" ]; then
      echo "Warning: the API is Degraded (an AI provider is not reachable). Check: docker compose logs api" >&2
    fi
    sed -i "s/^FILMSEARCH_TAG=.*/FILMSEARCH_TAG=${tag}/" .env
    echo "$(date -u +%Y-%m-%dT%H:%M:%SZ) ${running:-none} -> ${tag}" >> deploy-history.log
    echo "Deployed ${tag}."
    exit 0
  fi
  sleep 5
done

echo "The API did not become healthy within 5 minutes. Logs: docker compose logs api worker" >&2
echo "Roll back with: ./deploy.sh ${running}" >&2
exit 1

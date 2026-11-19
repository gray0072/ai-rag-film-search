#!/usr/bin/env bash
# Restores a dump of backup.sh and starts the given image version on it: the rollback across a migration that the old
# version cannot run on (deploy/README.md "Rollback"). Data written after the dump is lost.
#   ./restore.sh backups/filmsearch-20261119T120000Z.dump v0.19.0
set -euo pipefail
cd "$(dirname "$0")"

dump="${1:?usage: ./restore.sh <dump file> <image tag>}"
tag="${2:?usage: ./restore.sh <dump file> <image tag>}"
compose() { docker compose --env-file .env "$@"; }

[ -f "${dump}" ] || { echo "No such dump: ${dump}" >&2; exit 1; }

compose stop api worker
compose exec -T postgres psql -U filmsearch -d postgres -v ON_ERROR_STOP=1 \
  -c "DROP DATABASE IF EXISTS filmsearch WITH (FORCE)" \
  -c "CREATE DATABASE filmsearch OWNER filmsearch"
compose exec -T postgres pg_restore -U filmsearch -d filmsearch --no-owner --exit-on-error < "${dump}"
echo "Restored ${dump}."

# The dump holds the schema of its time: deploy.sh applies only migrations that are missing, then starts the version.
./deploy.sh "${tag}"

#!/usr/bin/env bash
# Dumps the database to backups/filmsearch-<UTC time>.dump (custom format) and keeps the last 10 dumps.
# deploy.sh runs it before every migration; restore a dump with restore.sh.
set -euo pipefail
cd "$(dirname "$0")"

compose() { docker compose --env-file .env "$@"; }

if [ -z "$(compose ps --status running -q postgres)" ]; then
  echo "PostgreSQL is not running: no backup (first deploy?)."
  exit 0
fi

mkdir -p backups
file="backups/filmsearch-$(date -u +%Y%m%dT%H%M%SZ).dump"
compose exec -T postgres pg_dump -U filmsearch -d filmsearch --format=custom > "${file}"
echo "Backup: ${file} ($(du -h "${file}" | cut -f1))"

ls -1t backups/*.dump | tail -n +11 | xargs -r rm --

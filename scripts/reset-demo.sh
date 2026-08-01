#!/usr/bin/env bash
set -euo pipefail

# One-command reset of the Demo environment: drop the demo database volume entirely, bring
# up a fresh Postgres container, then reseed deterministically (same fictional "Apex
# Insurance Group" data every time — see PerformanceManagement.DemoSeeder).
#
# Never touches the real Development Postgres container/volume — a different container
# name and a different volume entirely (see docker-compose.yml).
#
# Usage:
#   ./scripts/reset-demo.sh

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
COMPOSE_FILE="$ROOT_DIR/docker-compose.yml"

# docker-compose.yml pins `name: pms`, so the volume below is always named
# `pms_pms-demo-pgdata` regardless of which directory this repo is checked out into —
# no more guessing it from the checkout directory's name (a real prior bug: the guess and
# the actual Compose-assigned name silently diverged once the checkout path changed,
# e.g. between a local clone and /opt/pms-demo/repo on the VPS, so this "reset" could
# leave the real volume — and its password — untouched).
#
# If a repo-root .env exists (see .env.example), pass it explicitly so this script's
# behavior always matches deploy/bootstrap-server.sh's; local dev without a .env keeps
# using docker-compose.yml's checked-in fallback password, same as before.
ENV_FILE_ARGS=()
if [ -f "$ROOT_DIR/.env" ]; then
  ENV_FILE_ARGS=(--env-file "$ROOT_DIR/.env")
fi

echo "1) Stopping the demo Postgres container (if running)..."
docker compose "${ENV_FILE_ARGS[@]}" -f "$COMPOSE_FILE" stop postgres-demo 2>/dev/null || true
docker compose "${ENV_FILE_ARGS[@]}" -f "$COMPOSE_FILE" rm -f postgres-demo 2>/dev/null || true

echo "2) Removing the demo database volume (full fresh database, not just a table wipe)..."
docker volume rm pms_pms-demo-pgdata 2>/dev/null || true

echo "3) Starting a fresh demo Postgres container (--wait blocks until its healthcheck passes)..."
docker compose "${ENV_FILE_ARGS[@]}" -f "$COMPOSE_FILE" up -d --wait postgres-demo

echo "4) Applying migrations and seeding deterministic demo data..."
dotnet run --project "$ROOT_DIR/src/PerformanceManagement.DemoSeeder"

echo
echo "Demo database reset and reseeded."

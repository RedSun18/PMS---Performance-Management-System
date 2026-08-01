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

echo "1) Stopping the demo Postgres container (if running)..."
docker compose -f "$COMPOSE_FILE" stop postgres-demo 2>/dev/null || true
docker compose -f "$COMPOSE_FILE" rm -f postgres-demo 2>/dev/null || true

echo "2) Removing the demo database volume (full fresh database, not just a table wipe)..."
docker volume rm "$(basename "$ROOT_DIR" | tr '[:upper:]' '[:lower:]' | tr -d ' ')_pms-demo-pgdata" 2>/dev/null || \
  docker volume ls -q | grep -E 'pms-demo-pgdata$' | xargs -r docker volume rm

echo "3) Starting a fresh demo Postgres container..."
docker compose -f "$COMPOSE_FILE" up -d postgres-demo

echo "4) Waiting for Postgres to accept connections..."
until docker exec pms-demo-postgres pg_isready -U pms_demo -d pms_demo >/dev/null 2>&1; do
  sleep 1
done

echo "5) Applying migrations and seeding deterministic demo data..."
dotnet run --project "$ROOT_DIR/src/PerformanceManagement.DemoSeeder"

echo
echo "Demo database reset and reseeded."

#!/usr/bin/env bash
set -euo pipefail

# Start local development stack for the Performance Management app.
# Usage:
#   ./scripts/start-dev.sh        # actually runs the steps
#   ./scripts/start-dev.sh --dry-run   # prints commands without running

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DRY_RUN=false
if [ "${1:-}" = "--dry-run" ]; then
  DRY_RUN=true
fi

echo "Root: $ROOT_DIR"

run() {
  if [ "$DRY_RUN" = true ]; then
    echo "+ $*"
  else
    echo "Running: $*"
    eval "$@"
  fi
}

echo "1) Ensure the Development Postgres container is running (docker compose up -d postgres)"
run "docker compose -f \"$ROOT_DIR/docker-compose.yml\" up -d postgres"

echo "2) Restore dotnet global tools (if needed)"
run "dotnet tool restore"

echo "3) Run the importer to load References/Database (idempotent)"
run "dotnet run --project \"$ROOT_DIR/src/PerformanceManagement.Importer\" -- --data \"$ROOT_DIR/References/Database\" --connection \"Host=localhost;Port=5445;Database=pms;Username=pms;Password=pms_dev\""

echo "4) Start the web app (will apply migrations and seed core data)"
run "PM_CONNECTION=\"Host=localhost;Port=5445;Database=pms;Username=pms;Password=pms_dev\" dotnet run --project \"$ROOT_DIR/src/PerformanceManagement.Web\""

echo "Done."

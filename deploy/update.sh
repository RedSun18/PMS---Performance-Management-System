#!/usr/bin/env bash
set -euo pipefail

# The "update script": pulls the latest main and republishes. Used both for manual updates
# and as the target of the GitHub Actions deploy workflow (.github/workflows/deploy.yml).
#
# Run FROM the persistent server-side checkout (see deploy/RUNBOOK.md "Server layout"),
# e.g. /opt/pms-demo/repo.
#
# Usage: sudo deploy/update.sh [ref]   (ref defaults to origin/main)

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REF="${1:-origin/main}"

cd "$REPO_DIR"
echo "==> Fetching"
git fetch origin
echo "==> Resetting to $REF"
git reset --hard "$REF"

exec "$REPO_DIR/deploy/publish.sh"

#!/usr/bin/env bash
set -euo pipefail

# Nightly backup of the Demo Postgres database ONLY (container pms-demo-postgres / db
# pms_demo, per docker-compose.yml). This script never touches Development — there is no
# Development Postgres container running on this VPS at all (see deploy/RUNBOOK.md).
#
# Run by pms-demo-backup.timer (deploy/systemd/pms-demo-backup.timer); safe to run manually.
# Usage: sudo deploy/backup-demo-db.sh

CONTAINER="pms-demo-postgres"
DB_USER="pms_demo"
DB_NAME="pms_demo"
BACKUP_DIR="/var/backups/pms-demo"
RETENTION_DAYS=30

mkdir -p "$BACKUP_DIR"
STAMP="$(date -u +%Y%m%d-%H%M%S)"
OUT_FILE="$BACKUP_DIR/pms_demo-$STAMP.sql.gz"

echo "==> Dumping $DB_NAME from $CONTAINER"
docker exec "$CONTAINER" pg_dump -U "$DB_USER" "$DB_NAME" | gzip > "$OUT_FILE"

SIZE="$(du -h "$OUT_FILE" | cut -f1)"
echo "==> Wrote $OUT_FILE ($SIZE)"

echo "==> Pruning backups older than $RETENTION_DAYS days"
find "$BACKUP_DIR" -name 'pms_demo-*.sql.gz' -mtime "+$RETENTION_DAYS" -print -delete

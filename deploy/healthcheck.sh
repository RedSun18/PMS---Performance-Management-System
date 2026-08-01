#!/usr/bin/env bash
# Verifies every component of the live deployment actually works — PostgreSQL, the app
# (systemd + Kestrel + migrations, implicitly: Program.cs runs migrations before /health is
# even reachable, so a failed migration shows up here as a failed Kestrel check), Nginx, and
# all four public HTTPS endpoints. Read-only — never modifies anything, safe to run any time,
# not just right after a deploy.
#
# Called automatically by publish.sh after every deploy. Run it standalone for diagnostics:
#   sudo deploy/healthcheck.sh
#
# Deliberately not `set -e`: a single failed check should not stop the rest from running —
# the point is to report EVERY problem in one pass, not just the first one.
set -uo pipefail

FAILED=0

check() {
  local name="$1"; shift
  if "$@" >/dev/null 2>&1; then
    printf '  OK    %s\n' "$name"
  else
    printf '  FAIL  %s\n' "$name"
    FAILED=1
  fi
}

check_url() {
  local name="$1" url="$2"
  if curl -fsS -o /dev/null --max-time 10 "$url"; then
    printf '  OK    %s (%s)\n' "$name" "$url"
  else
    printf '  FAIL  %s (%s)\n' "$name" "$url"
    FAILED=1
  fi
}

echo "== PostgreSQL (Demo) =="
check "postgres-demo container running" bash -c \
  '[ "$(docker inspect -f "{{.State.Running}}" pms-demo-postgres 2>/dev/null)" = "true" ]'
check "postgres-demo accepting connections" \
  docker exec pms-demo-postgres pg_isready -U pms_demo -d pms_demo

echo "== systemd =="
check "pms-demo.service active"        systemctl is-active --quiet pms-demo
check "nginx active"                   systemctl is-active --quiet nginx
check "pms-demo-backup.timer active"   systemctl is-active --quiet pms-demo-backup.timer

echo "== Application (Kestrel, local — also covers migrations: Program.cs runs them"
echo "   before /health is reachable at all, so a migration failure fails this check) =="
check_url "Kestrel /health" "http://127.0.0.1:8090/health"

echo "== Public HTTPS endpoints (Nginx + Let's Encrypt + Cloudflare) =="
check_url "PMS Demo"    "https://pms.aryanb.dev/health"
check_url "Portfolio"   "https://aryanb.dev/"
check_url "Docs"        "https://docs.aryanb.dev/"
check_url "RenewalFlow" "https://renewalflow.aryanb.dev/"

echo
if [ "$FAILED" -eq 1 ]; then
  echo "One or more checks FAILED — see above."
  echo "Start with: journalctl -u pms-demo -n 100 --no-pager   /   journalctl -u nginx -n 50 --no-pager"
  echo "If only the public HTTPS checks failed while everything above them passed, this is"
  echo "usually DNS/Cloudflare/certificates, not the app — see deploy/RUNBOOK.md 'Cloudflare"
  echo "DNS check + HTTPS via Let's Encrypt'."
  exit 1
fi
echo "All checks passed."

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

# Diagnostic only (never sets FAILED) — same reasoning as bootstrap-server.sh's Chromium apt
# packages: PDF export is one optional feature, not core app health, so a problem here must
# never fail the whole deploy. This exists because PDF export failures produce nothing more
# specific than a generic 500 (Error.cshtml deliberately shows no exception detail — see
# Pages/Error.cshtml.cs), and there's no interactive SSH access to this box (the GitHub
# Actions deploy key runs update.sh only), so `ldd` against the actual downloaded Chromium
# binary is the only way to see whether required shared libraries are missing without direct
# server access — its output lands in the routine GitHub Actions deploy log.
echo "== PDF export (PuppeteerSharp/headless Chromium) =="
CHROME_BIN="$(find /var/www/pms-demo -path '*/puppeteer/*' -type f \( -name chrome -o -name headless_shell \) -perm -u+x 2>/dev/null | head -1)"
if [ -z "$CHROME_BIN" ]; then
  echo "  FAIL  No downloaded Chromium binary found under /var/www/pms-demo/.cache/puppeteer"
  echo "        -- PuppeteerSharp's BrowserFetcher.DownloadAsync() has not completed successfully."
  # Reproduces the exact download BrowserFetcher performs, but with curl instead of .NET's
  # HttpClient, so the real network-level failure (DNS/TLS/timeout/HTTP status) is visible —
  # PuppeteerSharp's own exception only says "Failed to download", no lower-level detail.
  CHROME_URL="https://storage.googleapis.com/chrome-for-testing-public/130.0.6723.69/linux64/chrome-linux64.zip"
  echo "  -- Reproducing the download directly (curl -v against the exact URL PuppeteerSharp uses):"
  curl -v --max-time 25 -o /dev/null "$CHROME_URL" 2>&1 | tail -25 | sed 's/^/        /'
  echo "  -- DNS resolution for storage.googleapis.com:"
  getent hosts storage.googleapis.com 2>&1 | sed 's/^/        /' || echo "        (getent failed / not found)"
else
  echo "  OK    Chromium binary found: $CHROME_BIN"
  MISSING="$(ldd "$CHROME_BIN" 2>&1 | grep 'not found' || true)"
  if [ -n "$MISSING" ]; then
    echo "  FAIL  Missing shared libraries required to launch Chromium:"
    echo "$MISSING" | sed 's/^/        /'
  else
    echo "  OK    All shared libraries resolved (ldd reports none missing)"
  fi
fi
echo "  -- Recent PDF/Chromium-related lines from journalctl -u pms-demo (if any):"
journalctl -u pms-demo --no-pager -n 500 2>/dev/null | grep -iE "puppeteer|chromium|headless_shell|pdf" | tail -40 | sed 's/^/        /' || true

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

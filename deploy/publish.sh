#!/usr/bin/env bash
set -euo pipefail

# Publishes the PMS Demo app from the current git checkout, using a capistrano-style
# releases/ + current symlink layout so wwwroot/uploads (branding logos) survives every
# redeploy and a bad release can be rolled back in one command.
#
# Run FROM the persistent server-side git checkout (see deploy/RUNBOOK.md "Server layout"),
# e.g. /opt/pms-demo/repo. Never run this against anything but the Demo project — there is
# no Production/Development path through this script.
#
# Usage: sudo deploy/publish.sh

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_ROOT="/var/www/pms-demo"
RELEASES_DIR="$APP_ROOT/releases"
SHARED_DIR="$APP_ROOT/shared"
CURRENT_LINK="$APP_ROOT/current"
KEEP_RELEASES=5
# Kept in sync by hand with deploy/systemd/pms-demo.service's ASPNETCORE_URLS and
# deploy/nginx/pms.aryanb.dev.conf's proxy_pass — all three must agree on this port.
HEALTH_URL="http://127.0.0.1:8090/health"
SERVICE_NAME="pms-demo"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "!! 'dotnet' is not on PATH. Install the .NET 8 SDK/runtime first, then re-run." >&2
  exit 1
fi

TS="$(date -u +%Y%m%d%H%M%S)"
RELEASE_DIR="$RELEASES_DIR/$TS"
GIT_COMMIT="$(git -C "$REPO_DIR" rev-parse --short HEAD)"
APP_VERSION="$(git -C "$REPO_DIR" describe --tags --always 2>/dev/null || echo "$GIT_COMMIT")"
DEPLOY_TIMESTAMP="$(date -u +"%Y-%m-%dT%H:%M:%SZ")"

echo "==> Publishing $GIT_COMMIT to $RELEASE_DIR"
mkdir -p "$RELEASES_DIR" "$SHARED_DIR/uploads"

dotnet publish "$REPO_DIR/src/PerformanceManagement.Web" \
  -c Release -o "$RELEASE_DIR" --no-self-contained

# --- Persistent uploads: first-ever release seeds shared/uploads from whatever the publish
# step produced (e.g. an empty wwwroot/uploads tree); every release after that gets its
# wwwroot/uploads replaced by a symlink into the one persistent shared/uploads directory, so
# nothing published here ever shadows or wipes real uploaded files.
if [ -d "$RELEASE_DIR/wwwroot/uploads" ] && [ -z "$(ls -A "$SHARED_DIR/uploads" 2>/dev/null)" ]; then
  cp -a "$RELEASE_DIR/wwwroot/uploads/." "$SHARED_DIR/uploads/"
fi
rm -rf "$RELEASE_DIR/wwwroot/uploads"
ln -s "$SHARED_DIR/uploads" "$RELEASE_DIR/wwwroot/uploads"

# --- Bake deploy metadata into the release (read by RUNBOOK's verification steps / a future
# admin status page — see plan item on the admin dashboard).
cat > "$RELEASE_DIR/deploy-info.json" <<EOF
{
  "commit": "$GIT_COMMIT",
  "version": "$APP_VERSION",
  "deployedAt": "$DEPLOY_TIMESTAMP",
  "environment": "Demo"
}
EOF

PREVIOUS_RELEASE="$(readlink -f "$CURRENT_LINK" 2>/dev/null || true)"

echo "==> Flipping current -> $RELEASE_DIR"
ln -sfn "$RELEASE_DIR" "$APP_ROOT/current.tmp"
mv -Tf "$APP_ROOT/current.tmp" "$CURRENT_LINK"

echo "==> Restarting $SERVICE_NAME"
systemctl restart "$SERVICE_NAME"

echo "==> Health-checking $HEALTH_URL"
ok=false
for i in $(seq 1 20); do
  if curl -fsS "$HEALTH_URL" >/dev/null 2>&1; then
    ok=true
    break
  fi
  sleep 1.5
done

if [ "$ok" != true ]; then
  echo "!! Health check failed after deploy. Rolling back."
  if [ -n "$PREVIOUS_RELEASE" ] && [ -d "$PREVIOUS_RELEASE" ]; then
    ln -sfn "$PREVIOUS_RELEASE" "$APP_ROOT/current.tmp"
    mv -Tf "$APP_ROOT/current.tmp" "$CURRENT_LINK"
    systemctl restart "$SERVICE_NAME"
    echo "!! Rolled back to $PREVIOUS_RELEASE. Deploy of $GIT_COMMIT FAILED."
  else
    echo "!! No previous release to roll back to — $SERVICE_NAME is down. Investigate immediately (journalctl -u $SERVICE_NAME)."
  fi
  exit 1
fi

echo "==> Deploying static sites"
"$REPO_DIR/deploy/publish-static-sites.sh" "$GIT_COMMIT" "$APP_VERSION" "$DEPLOY_TIMESTAMP"

echo "==> Pruning old releases (keeping last $KEEP_RELEASES)"
ls -1dt "$RELEASES_DIR"/*/ 2>/dev/null | tail -n +$((KEEP_RELEASES + 1)) | xargs -r rm -rf

echo "==> Deploy OK: $GIT_COMMIT ($APP_VERSION) live at $DEPLOY_TIMESTAMP"

# The rollback gate above only proves THIS release's app process is healthy — it says
# nothing about Postgres, Nginx, HTTPS, or the static sites. Run the full verification pass
# now that the release itself is confirmed good. Deliberately NOT part of the rollback
# decision above: a DNS/certificate problem is an infra issue to fix on its own terms, not a
# reason to revert an otherwise-working app release.
echo "==> Verifying the full deployment"
if ! "$REPO_DIR/deploy/healthcheck.sh"; then
  echo "!! Deploy published successfully and the app itself is healthy, but the full"
  echo "!! verification pass found a problem — see above."
  exit 1
fi

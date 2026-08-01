#!/usr/bin/env bash
set -euo pipefail

# Publishes the three static sites (aryanb.dev, docs.aryanb.dev, renewalflow.aryanb.dev)
# using the same releases/ + current symlink pattern as publish.sh, so a bad push can be
# rolled back the same way. Called by publish.sh — not normally run standalone.
#
# Usage: deploy/publish-static-sites.sh <git-commit> <app-version> <deploy-timestamp>

GIT_COMMIT="${1:?git commit required}"
APP_VERSION="${2:?app version required}"
DEPLOY_TIMESTAMP="${3:?deploy timestamp required}"

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
TS="$(date -u +%Y%m%d%H%M%S)"
KEEP_RELEASES=5

deploy_site() {
  local domain="$1"
  local src="$REPO_DIR/deploy/sites/$domain"
  local app_root="/var/www/$domain"
  local release_dir="$app_root/releases/$TS"

  echo "----> $domain"
  mkdir -p "$release_dir" "$app_root/shared"
  cp -a "$src/." "$release_dir/"

  # Placeholder substitution (footer deploy metadata — see index.html {{...}} tokens).
  find "$release_dir" -name '*.html' -print0 | xargs -0 -r sed -i \
    -e "s/{{DEPLOY_TIMESTAMP}}/$DEPLOY_TIMESTAMP/g" \
    -e "s/{{GIT_COMMIT}}/$GIT_COMMIT/g" \
    -e "s/{{APP_VERSION}}/$APP_VERSION/g" \
    -e "s/{{ENVIRONMENT}}/Demo/g"

  # Optional Cloudflare Web Analytics token — never committed, read from a server-local file
  # if present (see deploy/RUNBOOK.md "Analytics"). Left as the literal placeholder (inside
  # an HTML comment, so inert) when no token is configured.
  if [ -f "$app_root/shared/cf-analytics-token" ]; then
    local token
    token="$(cat "$app_root/shared/cf-analytics-token")"
    find "$release_dir" -name '*.html' -print0 | xargs -0 -r sed -i "s/__CF_BEACON_TOKEN__/$token/g"
  fi

  ln -sfn "$release_dir" "$app_root/current.tmp"
  mv -Tf "$app_root/current.tmp" "$app_root/current"

  ls -1dt "$app_root"/releases/*/ 2>/dev/null | tail -n +$((KEEP_RELEASES + 1)) | xargs -r rm -rf
}

deploy_site "aryanb.dev"
# Resume PDF is deliberately NOT stored in git (a personal document, and this repo is
# public) — drop it once at /var/www/aryanb.dev/shared/resume.pdf on the server and every
# future release picks it up automatically. Absent that file, the Download Resume button
# 404s, which is expected until it's uploaded (see deploy/sites/aryanb.dev/RESUME_PLACEHOLDER.md).
if [ -f "/var/www/aryanb.dev/shared/resume.pdf" ]; then
  cp "/var/www/aryanb.dev/shared/resume.pdf" "/var/www/aryanb.dev/current/resume.pdf"
fi

deploy_site "renewalflow.aryanb.dev"

deploy_site "docs.aryanb.dev"
# The doc bundle itself (guides, pptx, screenshots, sample PDF) is generated content that
# lives in the main repo, not in deploy/sites/ — copy it alongside the hand-authored index.
cp -a "$REPO_DIR/docs/demo/." "/var/www/docs.aryanb.dev/current/"
# Re-apply footer substitution to index.html in case the docs/demo copy overwrote it (it
# ships its own README.md but no index.html, so this is defensive, not currently needed).
sed -i \
  -e "s/{{DEPLOY_TIMESTAMP}}/$DEPLOY_TIMESTAMP/g" \
  -e "s/{{GIT_COMMIT}}/$GIT_COMMIT/g" \
  -e "s/{{APP_VERSION}}/$APP_VERSION/g" \
  -e "s/{{ENVIRONMENT}}/Demo/g" \
  "/var/www/docs.aryanb.dev/current/index.html"

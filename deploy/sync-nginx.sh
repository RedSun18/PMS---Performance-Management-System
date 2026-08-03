#!/usr/bin/env bash
set -euo pipefail

# Re-syncs Nginx config (snippets + the 4 per-domain site configs) from this repo to the
# live server, validates with `nginx -t`, and reloads unconditionally.
#
# WHY THIS EXISTS: bootstrap-server.sh used to be the ONLY thing that ever wrote Nginx
# config to disk. deploy/update.sh and deploy/publish.sh — the routine, repeated deploy
# path — never touched it. That meant a config fix committed to the repo (e.g. a corrected
# www redirect, a fixed `root` path) would sit in git forever without ever reaching the
# live server unless someone remembered to re-run the FULL bootstrap script — which nobody
# does for a routine content/code update. This is exactly how aryanb.dev ended up serving
# docs.aryanb.dev's content in production: the origin's nginx config had drifted from the
# repo, and nothing in the normal deploy path would ever have corrected it.
#
# WHY UNCONDITIONAL RELOAD (not "only if changed"): an earlier version of this script only
# reloaded when a cmp against the on-disk file showed a difference. That silently broke the
# very first time a *snippet* changed on its own (e.g. a CSP fix in security-headers-static.conf
# with no per-domain conf edit) — the new content was copied to disk, but since nothing else
# differed, CHANGED stayed 0 and nginx kept serving the OLD config from memory. The next
# deploy's cmp then saw disk already matching the repo and skipped the reload *again*, so the
# fix silently never went live even though every file on disk was correct. `nginx -t` is cheap
# and `systemctl reload nginx` is graceful (no dropped connections), so there is no real cost
# to just always doing both — it removes an entire class of "file matches repo but nginx
# hasn't actually loaded it" bugs instead of trying to track that state correctly.
#
# Idempotent, safe to run every time. A domain still missing a certificate is requested via
# deploy/ensure-certs.sh first (self-healing — see that script's comment for the exact
# incident this closes: aryanb.dev's cert failed once during bootstrap and nothing in the
# routine deploy path ever retried it), so a domain's final config only fails to install here
# if certbot itself is still failing (e.g. a genuine DNS/Cloudflare issue), not because
# nobody remembered to re-run the full bootstrap script.
#
# Called by both bootstrap-server.sh and publish.sh (every deploy).

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

mkdir -p /etc/nginx/snippets
cp "$REPO_DIR/deploy/nginx/acme-challenge.conf" /etc/nginx/snippets/acme-challenge.conf
cp "$REPO_DIR/deploy/nginx/cloudflare-realip.conf" /etc/nginx/snippets/cloudflare-realip.conf
cp "$REPO_DIR/deploy/nginx/security-headers-static.conf" /etc/nginx/snippets/security-headers-static.conf

mkdir -p /etc/letsencrypt
if [ ! -f /etc/letsencrypt/options-ssl-nginx.conf ]; then
  cp "$REPO_DIR/deploy/nginx/options-ssl-nginx.conf" /etc/letsencrypt/options-ssl-nginx.conf
fi
if [ ! -f /etc/letsencrypt/ssl-dhparam.pem ]; then
  echo "-- Generating a 2048-bit DH parameter file (one-time, can take up to ~a minute)..."
  openssl dhparam -out /etc/letsencrypt/ssl-dhparam.pem 2048
fi

chmod +x "$REPO_DIR/deploy/ensure-certs.sh"
"$REPO_DIR/deploy/ensure-certs.sh"

# Installed unconditionally (no certificate dependency — see default-catchall.conf) so a
# domain missing its certificate can never silently inherit another real domain's content
# via Nginx's default-server fallback, only a closed connection for itself.
cp "$REPO_DIR/deploy/nginx/default-catchall.conf" "/etc/nginx/sites-available/default-catchall.conf"
ln -sf "/etc/nginx/sites-available/default-catchall.conf" "/etc/nginx/sites-enabled/default-catchall.conf"

for f in pms.aryanb.dev aryanb.dev docs.aryanb.dev renewalflow.aryanb.dev; do
  if [ -d "/etc/letsencrypt/live/$f" ]; then
    cp "$REPO_DIR/deploy/nginx/$f.conf" "/etc/nginx/sites-available/$f.conf"
    # Unconditional even when the file content is unchanged: self-heals a missing or
    # wrong symlink in sites-enabled too, not just wrong file content.
    ln -sf "/etc/nginx/sites-available/$f.conf" "/etc/nginx/sites-enabled/$f.conf"
  else
    echo "-- Skipping $f: no certificate yet (run deploy/bootstrap-server.sh first)."
  fi
done

# deploy/ensure-certs.sh may have created a standalone www.aryanb.dev.conf ACME stub while
# aryanb.dev's certificate was still missing (each name in that request gets its own stub —
# see its comment). Once the real aryanb.dev.conf is installed above, it already handles
# www.aryanb.dev itself (server_name aryanb.dev www.aryanb.dev in its own :80 block), so a
# leftover stub file becomes a duplicate server_name Nginx has to arbitrarily pick between
# ("conflicting server name ... ignored") rather than a real problem — but it's still cruft
# that should be cleaned up, not left in place.
if [ -d "/etc/letsencrypt/live/aryanb.dev" ] && [ -f /etc/nginx/sites-available/www.aryanb.dev.conf ]; then
  rm -f /etc/nginx/sites-enabled/www.aryanb.dev.conf /etc/nginx/sites-available/www.aryanb.dev.conf
  echo "-- Removed the now-redundant www.aryanb.dev.conf ACME stub"
fi
rm -f /etc/nginx/sites-enabled/default

echo "-- Validating and reloading Nginx"
nginx -t
systemctl reload nginx

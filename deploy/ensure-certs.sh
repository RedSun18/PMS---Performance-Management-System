#!/usr/bin/env bash
set -euo pipefail

# Ensures every domain has a Let's Encrypt certificate, requesting any that are missing.
# Idempotent — a domain that already has a cert is left untouched; --expand on the request
# itself makes re-running safe even if the name list changed.
#
# WHY THIS EXISTS: aryanb.dev's certificate request failed during the original bootstrap run
# (bootstrap-server.sh's request_cert() logs a warning and continues on failure, by design,
# rather than aborting the whole script over one domain). Nothing in the routine deploy path
# (deploy/update.sh -> deploy/publish.sh) ever retried it, because only bootstrap-server.sh
# ever called certbot at all. The practical effect: /etc/letsencrypt/live/aryanb.dev never
# existed, so deploy/sync-nginx.sh correctly refused to install a config referencing a
# nonexistent cert (the right call — installing it would break `nginx -t`), which meant
# aryanb.dev never had ANY real Nginx config, and requests for it silently fell through to
# whatever domain's config nginx picked as the default for unmatched SNI on :443 —
# docs.aryanb.dev, as it happened. This is why aryanb.dev appeared to "serve
# docs.aryanb.dev's content": there was no server block for it at all, not a wrong one.
#
# Called by both bootstrap-server.sh and deploy/sync-nginx.sh (so a missing cert can now
# self-heal on any routine deploy, not just a full bootstrap run) — see deploy/RUNBOOK.md
# "HTTPS via Let's Encrypt" for the Cloudflare HTTP-01 fallback if this keeps failing.

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CERTBOT_EMAIL="hello@aryanb.dev"

mkdir -p /var/www/certbot/.well-known/acme-challenge /etc/nginx/snippets
cp "$REPO_DIR/deploy/nginx/acme-challenge.conf" /etc/nginx/snippets/acme-challenge.conf

# Any domain still missing a cert needs SOME server block on :80 to answer the HTTP-01
# challenge — a domain that already has its final TLS config installed already has one (its
# :80 block redirects to :443 but still serves /.well-known/acme-challenge/ first, see
# deploy/nginx/*.conf). Only domains with neither a cert nor any config yet need this stub.
reload_needed=0
for d in pms.aryanb.dev aryanb.dev www.aryanb.dev docs.aryanb.dev renewalflow.aryanb.dev; do
  case "$d" in
    www.aryanb.dev) primary=aryanb.dev ;;
    *) primary="$d" ;;
  esac
  if [ ! -d "/etc/letsencrypt/live/$primary" ] && [ ! -f "/etc/nginx/sites-available/$d.conf" ]; then
    cat > "/etc/nginx/sites-available/$d.conf" <<EOF
server {
    listen 80;
    listen [::]:80;
    server_name $d;
    include /etc/nginx/snippets/acme-challenge.conf;
    location / { return 200 "bootstrap: $d\n"; add_header Content-Type text/plain; }
}
EOF
    ln -sf "/etc/nginx/sites-available/$d.conf" "/etc/nginx/sites-enabled/$d.conf"
    reload_needed=1
  fi
done
rm -f /etc/nginx/sites-enabled/default
if [ "$reload_needed" -eq 1 ]; then
  nginx -t && systemctl reload nginx
fi

request_cert() {
  local primary="$1"; shift
  if [ -d "/etc/letsencrypt/live/$primary" ]; then
    return 0
  fi
  echo "-- No certificate for $primary yet — requesting: $*"
  certbot certonly --webroot -w /var/www/certbot "$@" \
    --non-interactive --agree-tos --expand -m "$CERTBOT_EMAIL" || \
    echo "!! Certbot failed for: $* — see message above. If behind Cloudflare, see" \
         "deploy/RUNBOOK.md 'HTTPS via Let's Encrypt' for the DNS-only fallback, then re-run."
}

request_cert pms.aryanb.dev -d pms.aryanb.dev
request_cert aryanb.dev -d aryanb.dev -d www.aryanb.dev
request_cert docs.aryanb.dev -d docs.aryanb.dev
request_cert renewalflow.aryanb.dev -d renewalflow.aryanb.dev

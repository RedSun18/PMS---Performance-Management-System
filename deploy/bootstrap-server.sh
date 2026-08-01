#!/usr/bin/env bash
set -euo pipefail

# One-time VPS setup for the PMS Demo + static sites deployment. Idempotent (safe to
# re-run), but designed to run once on a fresh Ubuntu 24.04 box per deploy/RUNBOOK.md.
#
# Does NOT touch SSH password/root-login settings — that's a separate, deliberately manual
# step in deploy/RUNBOOK.md ("SSH hardening") because flipping it non-interactively risks
# permanently locking you out of the box if key auth isn't already confirmed working.
#
# Run FROM the repo checkout on the server, e.g.:
#   git clone <your-repo-url> /opt/pms-demo/repo
#   cd /opt/pms-demo/repo && sudo deploy/bootstrap-server.sh
#
# Domains are hardcoded (this deployment is specific to aryanb.dev) — this is
# infrastructure-as-code for one deployment, not a generic template.

if [ "$(id -u)" -ne 0 ]; then
  echo "Run as root (sudo deploy/bootstrap-server.sh)." >&2
  exit 1
fi

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOMAINS=(pms.aryanb.dev aryanb.dev www.aryanb.dev docs.aryanb.dev renewalflow.aryanb.dev)
CERTBOT_EMAIL="hello@aryanb.dev"

echo "############################################################"
echo "# 1) System packages"
echo "############################################################"
apt-get update
apt-get install -y \
  curl git ufw fail2ban unattended-upgrades apt-listchanges \
  nginx certbot python3-certbot-nginx \
  docker-compose-plugin \
  libnss3 libatk-bridge2.0-0 libcups2 libxcomposite1 libxdamage1 libxrandr2 libgbm1 \
  libpango-1.0-0 libasound2t64

echo "############################################################"
echo "# 2) Dedicated service user + directory layout"
echo "############################################################"
if ! id -u pms-demo >/dev/null 2>&1; then
  useradd --system --create-home --home-dir /var/www/pms-demo --shell /usr/sbin/nologin pms-demo
fi

mkdir -p /var/www/pms-demo/releases /var/www/pms-demo/shared/uploads
mkdir -p /var/www/aryanb.dev/releases /var/www/aryanb.dev/shared
mkdir -p /var/www/docs.aryanb.dev/releases /var/www/docs.aryanb.dev/shared
mkdir -p /var/www/renewalflow.aryanb.dev/releases /var/www/renewalflow.aryanb.dev/shared
mkdir -p /var/www/certbot/.well-known/acme-challenge
mkdir -p /var/backups/pms-demo
chown -R pms-demo:pms-demo /var/www/pms-demo

if [ ! -f /var/www/pms-demo/shared/pms-demo.env ]; then
  echo "!! /var/www/pms-demo/shared/pms-demo.env does not exist yet — create it from"
  echo "   deploy/postgres-demo.env.example (PM_CONNECTION) before starting pms-demo.service."
fi

echo "############################################################"
echo "# 3) UFW — allow only SSH/HTTP/HTTPS"
echo "############################################################"
ufw default deny incoming
ufw default allow outgoing
ufw allow 22/tcp
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable
ufw status verbose

echo "############################################################"
echo "# 4) Fail2Ban (sshd jail)"
echo "############################################################"
cp "$REPO_DIR/deploy/fail2ban/jail.local" /etc/fail2ban/jail.local
systemctl enable --now fail2ban
systemctl restart fail2ban

echo "############################################################"
echo "# 5) unattended-upgrades (security patches only, no auto-reboot)"
echo "############################################################"
cp "$REPO_DIR/deploy/unattended-upgrades/50unattended-upgrades" /etc/apt/apt.conf.d/50unattended-upgrades
cp "$REPO_DIR/deploy/unattended-upgrades/20auto-upgrades" /etc/apt/apt.conf.d/20auto-upgrades
systemctl enable --now unattended-upgrades

echo "############################################################"
echo "# 6) journald log retention for the app"
echo "############################################################"
mkdir -p /etc/systemd/journald.conf.d
cp "$REPO_DIR/deploy/systemd/journald-pms-demo.conf" /etc/systemd/journald.conf.d/pms-demo.conf
systemctl restart systemd-journald

echo "############################################################"
echo "# 7) Nginx log rotation"
echo "############################################################"
cp "$REPO_DIR/deploy/logrotate/nginx-pms" /etc/logrotate.d/nginx-pms

echo "############################################################"
echo "# 8) Demo Postgres via Docker Compose"
echo "############################################################"
if [ ! -f "$REPO_DIR/deploy/postgres-demo.env" ]; then
  echo "!! $REPO_DIR/deploy/postgres-demo.env does not exist yet."
  echo "   Copy deploy/postgres-demo.env.example -> deploy/postgres-demo.env and set a"
  echo "   strong POSTGRES_DEMO_PASSWORD before continuing. Skipping DB startup for now."
else
  (cd "$REPO_DIR" && docker compose --env-file deploy/postgres-demo.env up -d postgres-demo)
fi

echo "############################################################"
echo "# 9) Nginx — temporary HTTP-only bootstrap config for ACME"
echo "############################################################"
mkdir -p /etc/nginx/snippets
cp "$REPO_DIR/deploy/nginx/acme-challenge.conf" /etc/nginx/snippets/acme-challenge.conf
cp "$REPO_DIR/deploy/nginx/cloudflare-realip.conf" /etc/nginx/snippets/cloudflare-realip.conf
cp "$REPO_DIR/deploy/nginx/security-headers-static.conf" /etc/nginx/snippets/security-headers-static.conf

for d in "${DOMAINS[@]}"; do
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
done
rm -f /etc/nginx/sites-enabled/default
nginx -t && systemctl reload nginx

echo "############################################################"
echo "# 10) Let's Encrypt certificates (HTTP-01 via shared webroot)"
echo "############################################################"
echo "If this fails behind Cloudflare, see deploy/RUNBOOK.md 'HTTPS via Let's Encrypt' for"
echo "the DNS-only fallback before retrying."
# --expand makes each of these safe to re-run: if a cert already covers exactly these names,
# Certbot just reports "not yet due for renewal" and does nothing; if the name list has
# changed since it was issued (e.g. adding www.aryanb.dev to an existing aryanb.dev-only
# cert on a server that already ran an older version of this script), it reissues to cover
# the full set instead of erroring. aryanb.dev and www.aryanb.dev deliberately share ONE
# certificate (aryanb.dev.conf serves both from the same cert) rather than getting separate
# ones.
request_cert() {
  certbot certonly --webroot -w /var/www/certbot "$@" \
    --non-interactive --agree-tos --expand -m "$CERTBOT_EMAIL" || \
    echo "!! Certbot failed for: $* — see message above, fix, and re-run this step manually."
}
request_cert -d pms.aryanb.dev
request_cert -d aryanb.dev -d www.aryanb.dev
request_cert -d docs.aryanb.dev
request_cert -d renewalflow.aryanb.dev

echo "############################################################"
echo "# 11) Certbot's Nginx SSL support files"
echo "############################################################"
# The Nginx configs below (deploy/nginx/*.conf) reference /etc/letsencrypt/options-ssl-nginx.conf
# and /etc/letsencrypt/ssl-dhparam.pem. Those two files are normally created as a SIDE EFFECT
# of Certbot's nginx *plugin* running (`certbot --nginx` / `certbot install --nginx`) — but
# step 10 deliberately uses `certonly --webroot` instead, specifically so Certbot never
# rewrites our git-tracked Nginx configs itself. That means the plugin's prepare() step that
# would normally drop these two files never runs, and `nginx -t` fails on a fresh box
# ("cannot load certificate ... ssl-dhparam.pem: No such file or directory") regardless of
# whether Certbot came from the Ubuntu apt package or Snap — this is not a snap-vs-apt
# packaging difference, it's specifically a webroot-vs-nginx-plugin one.
#
# Fix: provide both files ourselves. options-ssl-nginx.conf is Certbot's own long-standing
# publicly-documented baseline (TLSv1.2/1.3, modern cipher list) — deploy/nginx/options-ssl-nginx.conf
# is a checked-in copy of it, installed only if nothing is there yet (never overwrites a
# real Certbot-managed copy, e.g. if `--nginx` was ever used on this box for something else).
# ssl-dhparam.pem has no meaningful "checked-in" version (it's meant to be host-specific, and
# hand-embedding a long DH parameter blob in git is exactly the kind of thing a single typo
# would silently make cryptographically weak rather than obviously broken) — generated once
# with openssl instead; skipped on re-runs since it's slow (up to ~a minute) and never needs
# to change.
mkdir -p /etc/letsencrypt
if [ ! -f /etc/letsencrypt/options-ssl-nginx.conf ]; then
  cp "$REPO_DIR/deploy/nginx/options-ssl-nginx.conf" /etc/letsencrypt/options-ssl-nginx.conf
fi
if [ ! -f /etc/letsencrypt/ssl-dhparam.pem ]; then
  echo "-- Generating a 2048-bit DH parameter file (one-time, can take up to ~a minute)..."
  openssl dhparam -out /etc/letsencrypt/ssl-dhparam.pem 2048
fi

echo "############################################################"
echo "# 12) Nginx — final TLS-enabled site configs"
echo "############################################################"
for f in pms.aryanb.dev aryanb.dev docs.aryanb.dev renewalflow.aryanb.dev; do
  if [ -d "/etc/letsencrypt/live/$f" ]; then
    cp "$REPO_DIR/deploy/nginx/$f.conf" "/etc/nginx/sites-available/$f.conf"
  else
    echo "-- Skipping final config for $f: no certificate yet."
  fi
done
# The www.aryanb.dev bootstrap stub is superseded by aryanb.dev.conf (which handles the
# www -> apex redirect itself), so remove it once the real cert/config are in place.
rm -f /etc/nginx/sites-enabled/www.aryanb.dev.conf /etc/nginx/sites-available/www.aryanb.dev.conf
nginx -t && systemctl reload nginx

echo "############################################################"
echo "# 13) Certbot auto-renewal deploy-hook (reload Nginx after renewal)"
echo "############################################################"
mkdir -p /etc/letsencrypt/renewal-hooks/deploy
cat > /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh <<'EOF'
#!/bin/sh
systemctl reload nginx
EOF
chmod +x /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
systemctl enable --now certbot.timer 2>/dev/null || true

echo "############################################################"
echo "# 14) systemd units: app + nightly backup"
echo "############################################################"
cp "$REPO_DIR/deploy/systemd/pms-demo.service" /etc/systemd/system/pms-demo.service
cp "$REPO_DIR/deploy/systemd/pms-demo-backup.service" /etc/systemd/system/pms-demo-backup.service
cp "$REPO_DIR/deploy/systemd/pms-demo-backup.timer" /etc/systemd/system/pms-demo-backup.timer
chmod +x "$REPO_DIR/deploy/backup-demo-db.sh" "$REPO_DIR/deploy/publish.sh" "$REPO_DIR/deploy/update.sh" "$REPO_DIR/deploy/publish-static-sites.sh"
systemctl daemon-reload
systemctl enable --now pms-demo-backup.timer

echo "############################################################"
echo "Bootstrap complete."
echo "Next: create deploy/postgres-demo.env and /var/www/pms-demo/shared/pms-demo.env"
echo "(if not already done), then run: sudo deploy/publish.sh"
echo "############################################################"

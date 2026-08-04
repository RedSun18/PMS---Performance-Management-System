#!/usr/bin/env bash
set -euo pipefail

# One-time VPS setup for the PMS Demo + static sites deployment. Idempotent (safe to
# re-run), but designed to run once on a fresh Ubuntu 24.04 box per deploy/RUNBOOK.md.
#
# Does NOT touch SSH password/root-login settings — that's a separate, deliberately manual
# step in deploy/RUNBOOK.md ("SSH hardening") because flipping it non-interactively risks
# permanently locking you out of the box if key auth isn't already confirmed working.
#
# Run FROM /opt/pms-demo/repo — this exact path, not a suggestion:
#   git clone <your-repo-url> /opt/pms-demo/repo
#   cd /opt/pms-demo/repo
#   cp .env.example .env && $EDITOR .env   # set POSTGRES_DEMO_PASSWORD — see .env.example
#   sudo deploy/bootstrap-server.sh
#
# .env (repo root) is the ONE file you create/edit by hand for the database. Every other
# deployment file — Docker Compose, the app's ConnectionStrings__Pm, Nginx, certificates,
# systemd — is generated or derived from it automatically by this script. Do not hand-maintain a
# second copy of the password anywhere; that duplication is exactly what caused a previous
# version of this deployment to fail with "password authentication failed for user
# pms_demo" (see deploy/RUNBOOK.md "Recovering a server...").
#
# Domains are hardcoded (this deployment is specific to aryanb.dev) — this is
# infrastructure-as-code for one deployment, not a generic template.

if [ "$(id -u)" -ne 0 ]; then
  echo "Run as root (sudo deploy/bootstrap-server.sh)." >&2
  exit 1
fi

# Without these, a fresh Ubuntu Server install can hang this script indefinitely with no
# error: `apt-get install -y` only answers apt's OWN yes/no prompts, not package-level
# debconf questions (apt-listchanges' "how do you want to see changelogs?" being a real
# example in this exact package list) — those need DEBIAN_FRONTEND=noninteractive. Separately,
# Ubuntu Server ships `needrestart` by default, which pops an interactive whiptail dialog
# ("which services should be restarted?") after installing/upgrading packages unless told
# not to — NEEDRESTART_MODE=a answers "restart them all automatically" instead of waiting for
# a keypress that will never come over a non-interactive SSH/CI session.
export DEBIAN_FRONTEND=noninteractive
export NEEDRESTART_MODE=a

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REQUIRED_REPO_DIR="/opt/pms-demo/repo"
ENV_FILE="$REPO_DIR/.env"

# This path isn't just a convention — deploy/systemd/pms-demo-backup.service and
# .github/workflows/deploy.yml both hardcode /opt/pms-demo/repo (a single fixed path is
# simpler and more auditable than templating it into a systemd unit and a CI workflow every
# time this script runs). Enforced here, once, so a checkout anywhere else fails loudly at
# bootstrap time instead of silently breaking the nightly backup timer and CI deploys later.
if [ "$REPO_DIR" != "$REQUIRED_REPO_DIR" ]; then
  cat >&2 <<EOF
!! This repo is checked out at $REPO_DIR, but deployment tooling requires exactly
!! $REQUIRED_REPO_DIR (deploy/systemd/pms-demo-backup.service and the GitHub Actions
!! deploy workflow both hardcode that path).

Move (or re-clone) it there and re-run:

  sudo mkdir -p $(dirname "$REQUIRED_REPO_DIR")
  sudo mv "$REPO_DIR" "$REQUIRED_REPO_DIR"
  cd "$REQUIRED_REPO_DIR"
  sudo deploy/bootstrap-server.sh
EOF
  exit 1
fi

echo "############################################################"
echo "# 0) Database secret (.env) — fail fast if it's missing"
echo "############################################################"
if [ ! -f "$ENV_FILE" ]; then
  cat >&2 <<EOF
!! $ENV_FILE does not exist.

This is the one file you create by hand before running this script — everything else
(the Docker Compose password, the app's ConnectionStrings__Pm, and so on) is derived from it
automatically. Create it with a strong, randomly-generated password, then re-run:

  cp "$REPO_DIR/.env.example" "$ENV_FILE"
  sed -i "s#change-me-to-a-long-random-value#\$(openssl rand -base64 24)#" "$ENV_FILE"
  sudo deploy/bootstrap-server.sh
EOF
  exit 1
fi

set -a
# shellcheck disable=SC1090,SC1091
. "$ENV_FILE"
set +a

# Defensive: a .env edited on/from Windows (a pasted password, a GUI editor defaulting to
# CRLF) can leave a trailing carriage return baked into the sourced value — invisible when
# printed, but a guaranteed auth failure since it becomes part of the actual password. Strip
# it rather than let that surface as another confusing "password authentication failed".
POSTGRES_DEMO_PASSWORD="${POSTGRES_DEMO_PASSWORD%$'\r'}"

if [ -z "${POSTGRES_DEMO_PASSWORD:-}" ] || [ "$POSTGRES_DEMO_PASSWORD" = "change-me-to-a-long-random-value" ]; then
  echo "!! POSTGRES_DEMO_PASSWORD in $ENV_FILE is empty or still the placeholder value." >&2
  echo "!! Set a real password (e.g. \$(openssl rand -base64 24)) and re-run." >&2
  exit 1
fi

echo "############################################################"
echo "# 1) System packages"
echo "############################################################"
apt-get update
apt-get install -y \
  curl git ufw fail2ban unattended-upgrades apt-listchanges \
  nginx certbot python3-certbot-nginx postgresql-client

# Isolated from the core packages above deliberately — Ubuntu's 24.04 "time_t" transition
# renamed a number of these libs (libasound2 -> libasound2t64 being one), and a single
# renamed/missing package name in one `apt-get install` line aborts the ENTIRE line under
# `set -e`, which previously took nginx/certbot/fail2ban/UFW down with it too (the same class
# of bug fixed for docker-compose-plugin below). A future Ubuntu point release renaming one of
# these must not be able to block the rest of the deployment — it should only cost PDF export.
# Also (re-)run by deploy/publish.sh on every routine deploy — see deploy/ensure-pdf-deps.sh
# for why a one-time bootstrap-only install isn't enough.
chmod +x "$REPO_DIR/deploy/ensure-pdf-deps.sh"
"$REPO_DIR/deploy/ensure-pdf-deps.sh"

echo "############################################################"
echo "# 2) Docker Engine + Compose plugin"
echo "############################################################"
# Split out from step 1 deliberately: docker-compose-plugin is distributed via Docker's OWN
# apt repository (download.docker.com), not Ubuntu's default archive. If Docker on this box
# was installed via Ubuntu's docker.io package (or any route that never added Docker's repo),
# a plain `apt-get install docker-compose-plugin` fails with "Unable to locate package" — and
# because it used to sit in the SAME apt-get install line as nginx/certbot/fail2ban/UFW/the
# Chromium libraries, that one bad package name took the entire line down with it, silently
# leaving every other package uninstalled too. Isolating it here means a Compose install
# problem can't block anything else, and it's handled with a real fallback instead of failing.
if ! command -v docker >/dev/null 2>&1; then
  echo "!! Docker is not installed. Install Docker Engine first:" >&2
  echo "!!   https://docs.docker.com/engine/install/ubuntu/" >&2
  echo "!! then re-run: sudo deploy/bootstrap-server.sh" >&2
  exit 1
fi

# Explicit rather than assumed: standard Docker installs (docker.io or Docker's own apt repo)
# enable+start the daemon by default, but that's packaging behavior, not a guarantee — and
# `Wants=docker.service` in pms-demo.service only pulls the daemon in when THAT unit starts,
# which doesn't help step 9 below (Postgres via Compose) if the daemon isn't already up right
# now. `enable --now` is a no-op if it's already enabled and running.
systemctl enable --now docker
if ! docker info >/dev/null 2>&1; then
  echo "!! Docker is installed but the daemon isn't responding (docker info failed)." >&2
  echo "!! Check: systemctl status docker" >&2
  exit 1
fi

if ! docker compose version >/dev/null 2>&1; then
  echo "-- 'docker compose' (v2 plugin) not found; installing docker-compose-plugin"
  if ! apt-get install -y docker-compose-plugin; then
    echo "-- Not available from the currently configured apt sources."
    echo "-- Adding Docker's official apt repository and retrying..."
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc
    # shellcheck disable=SC1091
    . /etc/os-release
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" \
      > /etc/apt/sources.list.d/docker.list
    apt-get update
    apt-get install -y docker-compose-plugin
  fi
fi
docker compose version

echo "############################################################"
echo "# 3) Dedicated service user + directory layout"
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

echo "############################################################"
echo "# 4) UFW — allow only SSH/HTTP/HTTPS"
echo "############################################################"
ufw default deny incoming
ufw default allow outgoing
ufw allow 22/tcp
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable
ufw status verbose

echo "############################################################"
echo "# 5) Fail2Ban (sshd jail)"
echo "############################################################"
cp "$REPO_DIR/deploy/fail2ban/jail.local" /etc/fail2ban/jail.local
systemctl enable --now fail2ban
systemctl restart fail2ban

echo "############################################################"
echo "# 6) unattended-upgrades (security patches only, no auto-reboot)"
echo "############################################################"
cp "$REPO_DIR/deploy/unattended-upgrades/50unattended-upgrades" /etc/apt/apt.conf.d/50unattended-upgrades
cp "$REPO_DIR/deploy/unattended-upgrades/20auto-upgrades" /etc/apt/apt.conf.d/20auto-upgrades
systemctl enable --now unattended-upgrades

echo "############################################################"
echo "# 7) journald log retention for the app"
echo "############################################################"
mkdir -p /etc/systemd/journald.conf.d
cp "$REPO_DIR/deploy/systemd/journald-pms-demo.conf" /etc/systemd/journald.conf.d/pms-demo.conf
systemctl restart systemd-journald

echo "############################################################"
echo "# 8) Nginx log rotation"
echo "############################################################"
cp "$REPO_DIR/deploy/logrotate/nginx-pms" /etc/logrotate.d/nginx-pms

echo "############################################################"
echo "# 9) Demo Postgres via Docker Compose"
echo "############################################################"
# --env-file is explicit here for clarity/defensiveness, but docker-compose.yml's directory
# (repo root) also holds .env under its own default name, so Compose would pick up the same
# value even from a bare `docker compose ...` with no flags — see docker-compose.yml's
# comment on postgres-demo. `--wait` blocks until the service's own healthcheck (pg_isready)
# passes, instead of just "the container process started". 120s (not Compose's default) gives
# real headroom for the FIRST run specifically, where this also has to pull the postgres:16-
# alpine image over the network before the container can even start.
(cd "$REPO_DIR" && docker compose --env-file "$ENV_FILE" up -d --wait --wait-timeout 120 postgres-demo)

echo "-- Verifying the .env password actually authenticates..."
# MUST connect the same way the real app does: a TCP connection to 127.0.0.1:5446 (the
# published port), exactly like the app's own Host=localhost;Port=5446. `docker exec ...
# psql` (no -h) was tried here originally and is a NO-OP that always reports success — it
# connects via the container's internal Unix socket / loopback, and the official postgres
# image's own pg_hba.conf trusts BOTH unconditionally ("local all all trust" and "host all
# all 127.0.0.1/32 trust" are baked in regardless of POSTGRES_PASSWORD), so it never actually
# checks the password at all. Confirmed by direct testing: a wrong password via `docker exec`
# succeeds; the same wrong password over the real TCP path correctly fails. Requires
# `postgresql-client` (installed in step 1) for a host-native `psql`.
if ! PGPASSWORD="$POSTGRES_DEMO_PASSWORD" psql -h 127.0.0.1 -p 5446 -U pms_demo -d pms_demo \
    -c 'SELECT 1' >/dev/null 2>&1; then
  cat >&2 <<EOF

!! Postgres is running but rejected the password in $ENV_FILE for user "pms_demo".

This means the pms-demo-pgdata Docker volume was already initialized with a DIFFERENT
password on a previous run. PostgreSQL only applies POSTGRES_PASSWORD the very first time it
initializes an EMPTY data directory — editing .env (or re-running this script) afterward
does not change the password already stored inside an existing volume; the container has to
be recreated from a wiped volume for a new password to actually take effect.

Fix — wipe the Demo volume only (this never touches the real Development database/volume)
and re-run:

  cd "$REPO_DIR"
  docker compose stop postgres-demo
  docker compose rm -f postgres-demo
  docker volume rm pms_pms-demo-pgdata
  sudo deploy/bootstrap-server.sh
EOF
  exit 1
fi
echo "-- Password verified."

echo "-- Writing /var/www/pms-demo/shared/pms-demo.env (derived from .env — do not hand-edit)"
# MUST be ConnectionStrings__Pm, not PM_CONNECTION. Proved by actually running the published
# app against a real Postgres instance: Program.cs resolves the connection string as
# `builder.Configuration.GetConnectionString("Pm") ?? Environment.GetEnvironmentVariable
# ("PM_CONNECTION") ?? <dev default>` — and appsettings.Demo.json ships its OWN checked-in
# ConnectionStrings:Pm (a local-dev convenience, pointing at the default docker-compose
# password, for `dotnet run` without exporting anything). Since GetConnectionString("Pm")
# reads the FULL config chain including appsettings.Demo.json, it is NEVER null there — so
# the `?? PM_CONNECTION` fallback this file used to rely on was silently never reached, and
# the real app would always connect with appsettings.Demo.json's hardcoded default password,
# not this one, regardless of what's written here. `ConnectionStrings__Pm` (double
# underscore — ASP.NET Core's standard nested-key env var convention) is a higher-priority
# config source than any appsettings.*.json file in the default host builder, so THIS
# correctly overrides it. Confirmed by actually publishing and running the app: with
# PM_CONNECTION set, it silently connected to the wrong database and never touched this
# password at all; with ConnectionStrings__Pm set, migrations ran and /health reflected the
# intended database.
cat > /var/www/pms-demo/shared/pms-demo.env <<EOF
# AUTO-GENERATED by deploy/bootstrap-server.sh from $ENV_FILE's POSTGRES_DEMO_PASSWORD.
# Do not hand-edit this file — it will be overwritten on the next bootstrap run. To change
# the password, edit .env instead (and see deploy/RUNBOOK.md "Changing the database
# password" — you'll also need to wipe the Postgres volume for the change to take effect).
ConnectionStrings__Pm=Host=localhost;Port=5446;Database=pms_demo;Username=pms_demo;Password=$POSTGRES_DEMO_PASSWORD
EOF
chown pms-demo:pms-demo /var/www/pms-demo/shared/pms-demo.env
chmod 600 /var/www/pms-demo/shared/pms-demo.env

echo "############################################################"
echo "# 10) Nginx, Let's Encrypt certificates, and final site configs"
echo "############################################################"
# Explicit rather than relying on the nginx apt package's default-enabled postinst behavior
# (true today, but not a contract) — nginx must already be active for anything below to work,
# and `enable` guarantees it comes back on its own after a reboot too.
systemctl enable --now nginx
rm -f /etc/nginx/sites-enabled/default
# deploy/sync-nginx.sh handles everything from here: installing ACME-challenge stubs and
# requesting any missing certificates (deploy/ensure-certs.sh), generating ssl-dhparam.pem,
# and installing the final TLS-enabled config for every domain that now has a cert. It's the
# SAME script deploy/publish.sh calls on every routine deploy — see its own comment for why
# that matters (a Let's Encrypt or Nginx-config problem here should never require re-running
# this whole bootstrap script again; deploy/update.sh alone can now self-heal it).
echo "If certificate requests fail behind Cloudflare, see deploy/RUNBOOK.md 'HTTPS via"
echo "Let's Encrypt' for the DNS-only fallback, then re-run this step."
chmod +x "$REPO_DIR/deploy/ensure-certs.sh" "$REPO_DIR/deploy/sync-nginx.sh"
"$REPO_DIR/deploy/sync-nginx.sh"

echo "############################################################"
echo "# 11) Certbot auto-renewal deploy-hook (reload Nginx after renewal)"
echo "############################################################"
mkdir -p /etc/letsencrypt/renewal-hooks/deploy
cat > /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh <<'EOF'
#!/bin/sh
systemctl reload nginx
EOF
chmod +x /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
systemctl enable --now certbot.timer 2>/dev/null || true

echo "############################################################"
echo "# 12) systemd units: app + nightly backup"
echo "############################################################"
cp "$REPO_DIR/deploy/systemd/pms-demo.service" /etc/systemd/system/pms-demo.service
cp "$REPO_DIR/deploy/systemd/pms-demo-backup.service" /etc/systemd/system/pms-demo-backup.service
cp "$REPO_DIR/deploy/systemd/pms-demo-backup.timer" /etc/systemd/system/pms-demo-backup.timer
chmod +x "$REPO_DIR/deploy/backup-demo-db.sh" "$REPO_DIR/deploy/publish.sh" "$REPO_DIR/deploy/update.sh" \
  "$REPO_DIR/deploy/publish-static-sites.sh" "$REPO_DIR/deploy/healthcheck.sh" "$REPO_DIR/deploy/sync-nginx.sh" \
  "$REPO_DIR/deploy/ensure-certs.sh" "$REPO_DIR/deploy/ensure-pdf-deps.sh"
systemctl daemon-reload
# `enable` (no --now): registers pms-demo to start on every future boot, without starting it
# now — there's no release under /var/www/pms-demo/current yet at this point in a fresh
# bootstrap, so starting it here would just crash-loop pointlessly. deploy/publish.sh's
# `systemctl restart pms-demo` performs the actual first start once a release exists. Without
# this `enable`, the app would never come back on its own after a reboot (a kernel security
# update via unattended-upgrades, for instance) — it would only ever have been started by
# publish.sh's one-time `restart`, silently, until someone happened to notice it was down.
systemctl enable pms-demo
systemctl enable --now pms-demo-backup.timer

echo "############################################################"
echo "Bootstrap complete. Next: sudo deploy/publish.sh"
echo "############################################################"

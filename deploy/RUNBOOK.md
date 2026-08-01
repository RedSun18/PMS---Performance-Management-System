# Deployment Runbook — PMS Demo + aryanb.dev

Ordered, copy-pasteable steps to take this repo from "nothing deployed" to the four live
domains. Everything here targets the **Demo** environment only (fictional Apex Corporation
data) plus three static sites — nothing here ever touches the Development database. Run
each numbered section on the VPS (via SSH) unless marked otherwise.

Domains: `pms.aryanb.dev` (app) · `aryanb.dev` (portfolio) · `docs.aryanb.dev` (docs) ·
`renewalflow.aryanb.dev` (coming soon).

---

## 0. Server layout (reference)

```
/opt/pms-demo/repo/            git checkout — the source of every script/config below
/var/www/pms-demo/
  releases/<timestamp>/        one dotnet-publish output per deploy
  current -> releases/...      symlink the systemd unit and Nginx point at
  shared/uploads/               persistent branding-logo uploads (survives every redeploy)
  shared/pms-demo.env           PM_CONNECTION secret (gitignored, never in git)
/var/www/aryanb.dev/           same releases/current/shared pattern (shared/resume.pdf)
/var/www/docs.aryanb.dev/      same pattern
/var/www/renewalflow.aryanb.dev/   same pattern
/var/backups/pms-demo/         nightly pg_dump output
```

## 1. Clone the repo onto the server

```bash
sudo mkdir -p /opt/pms-demo
sudo git clone <your-repo-url> /opt/pms-demo/repo
cd /opt/pms-demo/repo
```

## 2. Create secrets (never committed)

```bash
cp deploy/postgres-demo.env.example deploy/postgres-demo.env
# edit deploy/postgres-demo.env: set POSTGRES_DEMO_PASSWORD to `openssl rand -base64 24`

sudo mkdir -p /var/www/pms-demo/shared
sudo tee /var/www/pms-demo/shared/pms-demo.env >/dev/null <<EOF
PM_CONNECTION=Host=localhost;Port=5446;Database=pms_demo;Username=pms_demo;Password=<same password as above>
EOF
sudo chown pms-demo:pms-demo /var/www/pms-demo/shared/pms-demo.env   # after step 3 creates the user
sudo chmod 600 /var/www/pms-demo/shared/pms-demo.env
```

## 3. Bootstrap the server (one-time)

```bash
sudo deploy/bootstrap-server.sh
```

This installs system packages (including the Chromium shared libraries PuppeteerSharp's PDF
export needs), creates the `pms-demo` service user and directory layout, configures
**UFW** (only 22/80/443 open), **Fail2Ban** (sshd jail), **unattended-upgrades** (security
patches, no auto-reboot), journald/Nginx log retention, brings up the Demo Postgres
container via Docker Compose, and gets Let's Encrypt certificates. Re-run it any time — it's
idempotent. It does **not** touch SSH password/root-login settings (see §6).

Watch the output for step 8 ("Demo Postgres via Docker Compose") and step 10 (Let's
Encrypt) — both print a clear message if a prerequisite (the `.env` file, DNS) isn't ready
yet, and skip rather than fail the whole run.

## 4. Cloudflare DNS check + HTTPS via Let's Encrypt

Confirm in the Cloudflare dashboard, before running bootstrap's certificate step (or before
retrying it):
- All four A/AAAA records point at the VPS and are **proxied** (orange cloud).
- SSL/TLS mode is **Full** (not Flexible, not yet Full Strict — flip to Full Strict only
  after confirming HTTPS works end-to-end in §9).

Certbot's HTTP-01 challenge (`certbot certonly --webroot`, step 10 of bootstrap) needs plain
HTTP on port 80 for `/.well-known/acme-challenge/` to reach this server through Cloudflare.
This normally works fine proxied. **If it fails** (common cause: an "Always Use HTTPS" page
rule or redirect rule catching the challenge path):
1. In Cloudflare DNS, temporarily set the affected record(s) to **DNS only** (grey cloud).
2. Re-run: `sudo certbot certonly --webroot -w /var/www/certbot -d <domain> --non-interactive --agree-tos -m hello@aryanb.dev`
3. Once issued, re-enable the proxy (orange cloud) for that record.
4. Re-run `sudo deploy/bootstrap-server.sh` (or just its Nginx-final-config step) to switch
   that domain's Nginx config from the bootstrap stub to the real TLS config.

Renewal is automatic (`certbot.timer`, enabled by bootstrap) with an Nginx-reload deploy-hook
already wired up — no cron job to maintain.

## 5. First deploy

```bash
sudo deploy/publish.sh
```

Publishes the app to `/var/www/pms-demo/releases/<ts>`, flips `current`, restarts
`pms-demo.service`, health-checks `/health`, and deploys all three static sites. If the
health check fails it automatically rolls back and exits non-zero — check
`journalctl -u pms-demo -n 100` for why.

## 6. Verify

```bash
curl -I https://pms.aryanb.dev/health
curl -I https://aryanb.dev
curl -I https://docs.aryanb.dev
curl -I https://renewalflow.aryanb.dev
```

All should return `200`/`301→200` with a valid certificate. Log into
`https://pms.aryanb.dev` as `admin` / `Admin@123` (see `docs/DEMO.md` for the full demo
credential list) and click around — Dashboard, Reports (PDF/Excel export), switch to
Arabic. I (Claude) will also run a browser-based verification pass against the public URLs
once this step is done — see the main conversation for that report.

## 7. SSH hardening (manual — read before running)

**Do this in a second terminal you keep open until you've confirmed key-based login works.**
Getting this wrong locks you out of the box entirely.

```bash
# In a NEW terminal, first confirm key-based login works without a password prompt:
ssh -o PreferredAuthentications=publickey -o PasswordAuthentication=no <user>@<vps-ip>
# If that logs you in cleanly, proceed. If it doesn't, fix your key setup first and stop here.
```

Then, on the server:

```bash
sudo sed -i \
  -e 's/^#\?PasswordAuthentication.*/PasswordAuthentication no/' \
  -e 's/^#\?PermitRootLogin.*/PermitRootLogin no/' \
  -e 's/^#\?ChallengeResponseAuthentication.*/ChallengeResponseAuthentication no/' \
  /etc/ssh/sshd_config
sudo sshd -t   # validates the config BEFORE restarting — do not skip this
sudo systemctl restart sshd
```

Immediately test from a **third**, fresh terminal (leave the second one connected) that key
login still works and password login is now refused. Only close the second terminal once
that's confirmed.

## 8. GitHub Actions CD setup

Generate a deploy-only key pair (no passphrase, since it runs unattended in CI):

```bash
ssh-keygen -t ed25519 -f ~/pms-deploy-key -C "github-actions-deploy" -N ""
```

On the server, restrict this key to only running `update.sh` — it can never open an
interactive shell even if the private key leaks:

```bash
echo 'command="sudo /opt/pms-demo/repo/deploy/update.sh origin/main",no-port-forwarding,no-X11-forwarding,no-agent-forwarding,no-pty '"$(cat ~/pms-deploy-key.pub)" \
  | sudo tee -a /root/.ssh/authorized_keys
```

(Using `root` as the deploy user because `update.sh`/`publish.sh` need `systemctl restart`
and to write under `/var/www/*`; the forced `command=` is what actually limits blast radius,
not the account. If you'd rather not grant a key any root access at all, create a dedicated
`deployer` user with a narrowly-scoped sudoers rule for just those two scripts instead.)

In GitHub: **Settings > Secrets and variables > Actions**, add:
- `DEPLOY_HOST` — VPS IP or hostname
- `DEPLOY_USER` — `root` (or your `deployer` user)
- `DEPLOY_SSH_KEY` — contents of `~/pms-deploy-key` (the private key)
- `DEPLOY_PORT` — your SSH port (omit if 22)

Delete `~/pms-deploy-key*` from your local machine once it's in GitHub Secrets. Every push
to `main` that passes `dotnet test` now auto-deploys via `.github/workflows/deploy.yml`.

## 9. Configure real SMTP (in-app, no secrets in git)

Log into `https://pms.aryanb.dev` as `admin` → **Settings > Email** → enter your SMTP
host/port/username/password/sender address → **Save** → **Send Test Email**. This is the
app's existing, database-backed settings flow (`SettingsService`/`EmailService`) — nothing
to configure on the server, and the password is encrypted at rest (ASP.NET Core Data
Protection). Demo continues to log every dispatch to `EmailLog` regardless.

## 10. Resume PDF

```bash
scp ~/Aryan-Bhandary-Resume.pdf <user>@<vps-ip>:/tmp/resume.pdf
ssh <user>@<vps-ip> 'sudo mkdir -p /var/www/aryanb.dev/shared && sudo mv /tmp/resume.pdf /var/www/aryanb.dev/shared/resume.pdf'
sudo /opt/pms-demo/repo/deploy/publish-static-sites.sh "$(git -C /opt/pms-demo/repo rev-parse --short HEAD)" "$(git -C /opt/pms-demo/repo describe --tags --always)" "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
```

Kept out of git deliberately (personal document, public repo) — see
`deploy/sites/aryanb.dev/RESUME_PLACEHOLDER.md`.

## 11. Analytics (optional — Cloudflare Web Analytics)

No token is hardcoded anywhere in this repo. To enable it: Cloudflare dashboard > Web
Analytics > add site > copy the beacon token, then:

```bash
echo -n "<token>" | sudo tee /var/www/aryanb.dev/shared/cf-analytics-token
```

The next `publish-static-sites.sh` run (any future deploy) will substitute it into the
commented-out beacon script in each site's `<head>`. Repeat for `docs.aryanb.dev`/
`renewalflow.aryanb.dev` if desired (same file name under each site's `shared/`).

## 12. Monitoring (optional, not installed by anything here)

Nothing in this deployment installs uptime monitoring — documented only, per your request:

- **Uptime Kuma** (self-hosted, free): run it as its own Docker container on this VPS (or a
  separate small box) and point it at `https://pms.aryanb.dev/health` and the three static
  domains' `/`. Simplest if you don't want a third-party dependency.
- **Better Stack** (hosted, free tier available): add the same four URLs as HTTP monitors
  from their dashboard — no server-side install at all, notifies you externally if the VPS
  itself goes down (which a self-hosted Uptime Kuma on the same box obviously can't).

Either integrates with the existing `/health` endpoint (`Program.cs` — unauthenticated,
checks real DB connectivity) with zero app changes.

## 13. Backups

Nightly `pg_dump` of `pms_demo` runs automatically via `pms-demo-backup.timer` (installed by
bootstrap), 30-day retention, output in `/var/backups/pms-demo/`. Test a restore
periodically:

```bash
gunzip -c /var/backups/pms-demo/pms_demo-<stamp>.sql.gz | docker exec -i pms-demo-postgres psql -U pms_demo -d pms_demo
```

(Into a scratch database, not the live one, if you're validating rather than actually
restoring — create one with `docker exec pms-demo-postgres createdb -U pms_demo pms_demo_restore_test` first.)

## 14. Rollback

```bash
# List releases, newest first:
ls -1dt /var/www/pms-demo/releases/*/
# Point `current` at a previous one and restart:
sudo ln -sfn /var/www/pms-demo/releases/<older-timestamp> /var/www/pms-demo/current
sudo systemctl restart pms-demo
```

`publish.sh` does this automatically on a failed health check — manual rollback is only for
a release that passed its health check but has some other issue you notice later.

## 15. Common troubleshooting

- **App won't start**: `journalctl -u pms-demo -n 200 --no-pager`. Most likely cause: the
  `PM_CONNECTION` in `/var/www/pms-demo/shared/pms-demo.env` doesn't match
  `deploy/postgres-demo.env`'s password, or the `postgres-demo` container isn't up
  (`docker ps`).
- **PDF export fails / times out**: confirm the Chromium shared libraries from bootstrap
  step 1 actually installed (`dpkg -l | grep libnss3`); check for a first-run Chromium
  download failure in `journalctl -u pms-demo` (needs outbound internet on first run only).
- **SMTP test email fails**: check the exact error shown by Send Test Email — almost always
  a wrong port/SSL combination (587+STARTTLS vs 465+implicit-TLS) or an app-password
  requirement (Gmail, etc.), not something on this server.
- **Cloudflare 521/522**: `pms-demo.service` (or Nginx) isn't running —
  `systemctl status pms-demo nginx`.
- **Certbot renewal fails silently**: `sudo certbot renew --dry-run` to test without
  actually renewing; check `/var/log/letsencrypt/letsencrypt.log`.

# Deployment Runbook — PMS Demo + aryanb.dev

Ordered, copy-pasteable steps to take this repo from "nothing deployed" to the four live
domains. Everything here targets the **Demo** environment only (fictional Apex Corporation
data) plus three static sites — nothing here ever touches the Development database. Run
each numbered section on the VPS (via SSH) unless marked otherwise.

Domains: `pms.aryanb.dev` (app) · `aryanb.dev` (portfolio) · `docs.aryanb.dev` (docs) ·
`renewalflow.aryanb.dev` (coming soon).

---

## Recovering a server stuck at "SSL configuration" (missing options-ssl-nginx.conf / ssl-dhparam.pem)

**If you already ran an older version of `bootstrap-server.sh`** and Nginx is failing to
start/reload with something like:

```
nginx: [emerg] cannot load certificate "/etc/letsencrypt/live/.../fullchain.pem" ...
# or
open() "/etc/letsencrypt/options-ssl-nginx.conf" failed (No such file or directory)
open() "/etc/letsencrypt/ssl-dhparam.pem" failed (No such file or directory)
```

— this is a fixed bug, not something to hand-edit around. **Root cause**: those two files
are normally created as a side effect of Certbot's *nginx plugin* running (`certbot --nginx`).
This deployment deliberately uses `certbot certonly --webroot` instead (so Certbot never
rewrites the git-tracked Nginx configs), which means that side effect never fires — on
**any** Ubuntu 24.04 box, whether Certbot came from the apt package (`certbot 2.9.0`, as
you have) or Snap. It is not a packaging difference; it's a webroot-vs-nginx-plugin one.
Separately, Ubuntu 24.04's apt `nginx` package is `1.24.0`, which predates the `http2 on;`
directive (added in 1.25.1) — the old templates used that newer syntax and would have failed
`nginx -t` with "unknown directive" on this exact setup even once the SSL files existed.

**Fix — do not hand-edit anything under `/etc/nginx/` or `/etc/letsencrypt/`.** Pull the
corrected repo and re-run the bootstrap script; it's idempotent and will not re-request
certificates you already have:

```bash
cd /opt/pms-demo/repo
sudo git pull
sudo deploy/bootstrap-server.sh
```

What this does on a server that's already partway deployed, step by step:
- Step 11 (certificate issuance) sees `/etc/letsencrypt/live/<domain>` already exists for
  each domain and does nothing, **except** `aryanb.dev`: the corrected script now requests
  that certificate with `www.aryanb.dev` included (`--expand`), so it reissues once to add
  that name — one extra, harmless certificate operation, not a rate-limit concern.
- Step 12 creates `/etc/letsencrypt/options-ssl-nginx.conf` (copied from
  `deploy/nginx/options-ssl-nginx.conf`, only if that path is empty — it will be, in your
  case) and generates `/etc/letsencrypt/ssl-dhparam.pem` via `openssl dhparam` (takes up to
  ~a minute; skipped on any future re-run since it already exists).
- Step 13 reinstalls the (now `http2`-syntax-fixed) final Nginx configs from
  `deploy/nginx/*.conf` and reloads.

Nginx briefly serves the plain-HTTP bootstrap stub for each domain partway through this
run (step 10, unconditionally re-applied) before step 13 puts the real HTTPS config back —
a few seconds of flapping is expected and not a sign anything went wrong. Verify afterward:

```bash
sudo nginx -t
curl -I https://pms.aryanb.dev/health
curl -I https://aryanb.dev
curl -I https://www.aryanb.dev   # should now present a valid cert too, not a mismatch
```

If you hadn't reached `sudo deploy/publish.sh` yet before hitting this error, continue from
§5 below now that Nginx is healthy.

---

## Recovering from "password authentication failed for user \"pms_demo\""

**If `pms-demo.service` keeps crash-looping** (`journalctl -u pms-demo` shows a Postgres
`28P01`/"password authentication failed" exception) — this was caused by three compounding
bugs, all fixed and all confirmed by actually publishing and running the app against a real
Postgres instance, not just by reading the code:

1. **The database password had two separate manually-maintained copies** —
   `deploy/postgres-demo.env` (read by Docker Compose) and
   `/var/www/pms-demo/shared/pms-demo.env` (read by the app) — that could independently
   drift out of sync, or be edited without realizing `docker compose` needed the
   `--env-file` flag pointed at exactly the right path to pick up a value at all (a bare
   `docker compose ...` with no flags silently fell back to the checked-in
   `pms_demo_dev` default). **Fixed**: there is now exactly ONE file, `.env` at the repo
   root — Docker Compose's own auto-discovered default env file, so every invocation
   (flagged or not) reads the same value — and `bootstrap-server.sh` generates
   `/var/www/pms-demo/shared/pms-demo.env` FROM it automatically. You never enter the
   password a second time anywhere.
2. **PostgreSQL only applies `POSTGRES_PASSWORD` the first time it initializes an empty
   data directory.** If the `pms-demo-pgdata` volume was ever brought up once with the
   wrong (default or mismatched) password, no amount of fixing the `.env` file or
   restarting the container changes the password already stored inside that volume — the
   volume itself has to be removed. "Destroying and recreating the database" only works if
   the actual named Docker volume is removed, not just the container; a plain
   `docker compose down`/`rm` leaves the volume (and its old password) in place. The volume
   name itself was also non-deterministic before this fix — it was derived from the
   checkout directory's name, so a `docker volume rm <guessed-name>` run from the wrong
   directory (or copy-pasted from local-dev instructions) could silently target a volume
   that doesn't exist while the real one, still holding the stale password, was untouched.
   **Fixed**: `docker-compose.yml` now pins `name: pms`, so the volume is always
   `pms_pms-demo-pgdata`, and `bootstrap-server.sh` actively verifies the `.env` password
   against the running container right after starting it — failing loudly with the exact
   fix below instead of letting the app crash-loop three layers downstream.
3. **The verification in (2) was itself broken, and separately, the app was never actually
   reading the generated password at all.**
   - The verification originally ran as `docker exec <container> psql ...` (no explicit
     host). That connects over the container's internal Unix socket, which the official
     postgres image's own `pg_hba.conf` trusts unconditionally (`local all all trust`),
     regardless of password — so the check always reported success even against a
     completely wrong password. Confirmed by testing with a deliberately wrong password: it
     "passed." **Fixed**: verification now runs as a real TCP connection
     (`psql -h 127.0.0.1 -p 5446 ...`, requiring `postgresql-client`, now installed in
     step 1) — the same connection type the app itself uses, which Postgres's default
     `pg_hba.conf` genuinely password-checks.
   - Separately — and this is the one that would have broken *every* real deployment
     regardless of the above two fixes — `appsettings.Demo.json` ships its own checked-in
     `ConnectionStrings:Pm` (a convenience default for local `dotnet run` testing).
     `Program.cs` resolves the connection string as
     `builder.Configuration.GetConnectionString("Pm") ?? Environment.GetEnvironmentVariable
     ("PM_CONNECTION") ?? <dev default>`, and `GetConnectionString("Pm")` reads
     `appsettings.Demo.json` — meaning it was **never null**, so the `PM_CONNECTION`
     env var this file used to write was silently never consulted at all. The real app
     would always connect using appsettings.Demo.json's hardcoded default password, no
     matter what was generated here. Confirmed by actually publishing and running the app:
     with `PM_CONNECTION` set, it silently connected to the wrong database; with
     `ConnectionStrings__Pm` set (the standard ASP.NET Core env-var override for a nested
     config key, which *does* outrank an appsettings.*.json value), it correctly connected,
     ran every migration, and seeded the admin account. **Fixed**: this file now writes
     `ConnectionStrings__Pm`, not `PM_CONNECTION`.

**Fix**:

```bash
cd /opt/pms-demo/repo
sudo git pull

# Confirm .env has the password you actually want (see .env.example) — this is now the
# ONLY file that needs it.
cat .env

# Wipe the Demo volume so it reinitializes with that password (never touches Development):
docker compose stop postgres-demo
docker compose rm -f postgres-demo
docker volume rm pms_pms-demo-pgdata

# Re-run bootstrap: recreates Postgres, verifies the password actually works, regenerates
# /var/www/pms-demo/shared/pms-demo.env from .env automatically, and (idempotently) redoes
# everything else.
sudo deploy/bootstrap-server.sh
sudo deploy/publish.sh
```

If `bootstrap-server.sh` itself now reports the password mismatch (step 9, "Verifying the
.env password actually authenticates"), it already prints these exact commands — that
message is the authoritative version of this fix going forward.

---

## 0. Server layout (reference)

```
/opt/pms-demo/repo/            git checkout — the source of every script/config below
  .env                          the ONE database secret (gitignored, see .env.example) —
                                 everything below is derived from this automatically
/var/www/pms-demo/
  releases/<timestamp>/        one dotnet-publish output per deploy
  current -> releases/...      symlink the systemd unit and Nginx point at
  shared/uploads/               persistent branding-logo uploads (survives every redeploy)
  shared/pms-demo.env           ConnectionStrings__Pm — AUTO-GENERATED from .env by
                                 bootstrap-server.sh, never hand-edited (gitignored either way)
/var/www/aryanb.dev/           same releases/current/shared pattern (shared/resume.pdf)
/var/www/docs.aryanb.dev/      same pattern
/var/www/renewalflow.aryanb.dev/   same pattern
/var/backups/pms-demo/         nightly pg_dump output
```

## 1. Clone the repo onto the server

`/opt/pms-demo/repo` is not a suggestion — `deploy/systemd/pms-demo-backup.service` and
`.github/workflows/deploy.yml` both hardcode this exact path, and `bootstrap-server.sh`
refuses to continue (with a clear message) if it's checked out anywhere else.

```bash
sudo mkdir -p /opt/pms-demo
sudo git clone <your-repo-url> /opt/pms-demo/repo
cd /opt/pms-demo/repo
```

## 2. Create the database secret (never committed)

This is the **only** manual configuration step in the entire deployment — one file, one
value. Everything else (Docker Compose's password, the app's `ConnectionStrings__Pm`, Nginx,
certificates, systemd) is generated or derived automatically from it by
`bootstrap-server.sh`.

```bash
cp .env.example .env
sed -i "s#change-me-to-a-long-random-value#$(openssl rand -base64 24)#" .env
```

## 3. Bootstrap the server (one-time)

```bash
sudo deploy/bootstrap-server.sh
```

Fails immediately with an actionable message if the repo isn't at `/opt/pms-demo/repo`, or
if `.env` is missing or still has the placeholder password — the exact path and the database
password are the only two things you're expected to get right by hand. Otherwise this installs system packages, ensures Docker + the Compose plugin are available
(adding Docker's official apt repository itself if needed — see the script's step 2 comment
for why this can't just live in the main package list), creates the `pms-demo` service user
and directory layout, configures **UFW** (only 22/80/443 open), **Fail2Ban** (sshd jail),
**unattended-upgrades** (security patches, no auto-reboot), journald/Nginx log retention,
brings up the Demo Postgres container via Docker Compose — verifying `.env`'s password
actually authenticates before continuing, and only then generating
`/var/www/pms-demo/shared/pms-demo.env` from it — and gets Let's Encrypt certificates.
Re-run it any time — it's idempotent. It does **not** touch SSH password/root-login settings
(see §7).

If the password verification fails (a stale volume from an old password — see "Recovering
from 'password authentication failed'" above), the script stops there with the exact fix
printed, before touching Nginx/certificates/systemd at all.

## 4. Cloudflare DNS check + HTTPS via Let's Encrypt

Confirm in the Cloudflare dashboard, before running bootstrap's certificate step (or before
retrying it):
- All four A/AAAA records point at the VPS and are **proxied** (orange cloud).
- SSL/TLS mode is **Full** (not Flexible, not yet Full Strict — flip to Full Strict only
  after confirming HTTPS works end-to-end in §9).

Certbot's HTTP-01 challenge (`certbot certonly --webroot`, step 11 of bootstrap) needs plain
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
`pms-demo.service`, health-checks `/health` (rolling back and exiting non-zero
automatically if that fails — check `journalctl -u pms-demo -n 100` for why), then deploys
all three static sites, then runs `deploy/healthcheck.sh` — a full pass over PostgreSQL,
every systemd unit, Kestrel, and all four public HTTPS endpoints. Any failure in that final
pass exits non-zero with a clear per-check report (it does NOT roll back the app release —
by that point the release itself already proved healthy; a failure here is almost always
DNS/certificates/Nginx, a separate problem from the release).

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

Start here regardless of symptom:

```bash
sudo deploy/healthcheck.sh
```

Runs every check from a fresh deploy (PostgreSQL, systemd, Kestrel, Nginx/HTTPS, all four
public sites) in one pass and tells you exactly which layer is broken, without needing to
publish anything.

- **App won't start / "password authentication failed for user pms_demo"**:
  `journalctl -u pms-demo -n 200 --no-pager`. See "Recovering from 'password authentication
  failed'" near the top of this document — almost always a stale `pms-demo-pgdata` volume
  from before `.env` had its current password. `ConnectionStrings__Pm` in
  `/var/www/pms-demo/shared/pms-demo.env` is auto-generated from `.env` on every bootstrap
  run, so those two can no longer drift apart; if the container itself isn't up at all,
  check `docker ps` and `docker compose logs postgres-demo`.
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

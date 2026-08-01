# Changelog

Human-curated summary of notable changes. For an automatic per-deploy log (timestamp + commit
+ commit subject, appended by `.github/workflows/deploy.yml` on every push to `main`), see
[`RELEASE_NOTES.md`](RELEASE_NOTES.md).

## [Unreleased]

## v1.0.0 — Production Deployment (Phase 17)

First public deployment. The Demo environment ("Apex Corporation" fictional data) is now
live at `pms.aryanb.dev`, alongside a portfolio homepage (`aryanb.dev`), documentation site
(`docs.aryanb.dev`), and a placeholder for RenewalFlow (`renewalflow.aryanb.dev`).

- Production infrastructure: Nginx reverse proxy + TLS (Let's Encrypt/Certbot), systemd
  service, Docker Compose Postgres for the Demo database, nightly backups, UFW, Fail2Ban,
  unattended-upgrades, journald/Nginx log retention — see `deploy/RUNBOOK.md`.
- CI/CD: GitHub Actions now builds, tests, and deploys `main` to the VPS automatically
  (`.github/workflows/deploy.yml`).
- The Development environment (real AIC data) was not touched by, and is not reachable
  from, any of this — see `docs/DEMO.md` and the "Environments" table in `README.md`.

Everything before this point (authentication, employee/department/KPI/competency
management, the PM Form workflow, manager/HR reviews, workflow administration, dashboard,
reports, PDF/Excel export, English/Arabic localization, email notifications, audit logging)
was completed in earlier phases — see `git log` for the full history.

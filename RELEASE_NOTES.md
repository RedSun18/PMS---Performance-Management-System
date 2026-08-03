# Release Notes

Auto-appended by `.github/workflows/deploy.yml` on every push to `main` that passes CI — one
entry per deploy (date, short commit hash, commit subject), newest first. This is an
operational log, not a curated summary — see [`CHANGELOG.md`](CHANGELOG.md) for that.

## 2026-08-03 — `5ca8a79`

- Always reload Nginx in sync-nginx.sh instead of tracking a CHANGED flag

## 2026-08-03 — `1e04521`

- Fix Nginx snippets never triggering a reload when only they change

## 2026-08-03 — `1f1aa02`

- Wire up RenewalFlow waitlist link, docs presentation download, and fix inline-script CSP

## 2026-08-03 — `51e6cda`

- Clean up leftover www.aryanb.dev.conf ACME stub after the real cert is issued

## 2026-08-03 — `10b2bd6`

- Fix aryanb.dev never getting a certificate: self-heal missing certs on every deploy

## 2026-08-03 — `774e034`

- Live QA pass: fix notification localhost links, PM Form leave-warning, dept auto-sync, Employee Master auto-numbering, Reference Master layout

## 2026-08-03 — `825a39e`

- Fix aryanb.dev serving docs.aryanb.dev's content: Nginx config never re-synced on deploy

## 2026-08-03 — `8e943f4`

- Finish docs.aryanb.dev redesign: wire in real guides and real screenshots

## 2026-08-03 — `3c9f15c`

- RC1: fix .gitignore excluding .github/workflows/, redesign portfolio and RenewalFlow sites

## 2026-08-01 — `23fb9b1`

- Initial production deployment (Phase 17): PMS Demo live at pms.aryanb.dev, plus
  aryanb.dev, docs.aryanb.dev, and renewalflow.aryanb.dev.

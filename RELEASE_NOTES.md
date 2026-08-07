# Release Notes

Auto-appended by `.github/workflows/deploy.yml` on every push to `main` that passes CI — one
entry per deploy (date, short commit hash, commit subject), newest first. This is an
operational log, not a curated summary — see [`CHANGELOG.md`](CHANGELOG.md) for that.

## 2026-08-07 — `e6f584e`

- Redesign unsaved-changes tracking: fix root cause, not another patch on it

## 2026-08-05 — `36bf281`

- Rename job title on portfolio: Assistant Programmer -> Developer

## 2026-08-05 — `ea038ee`

- Add .claude/ to .gitignore

## 2026-08-04 — `196e51a`

- Self-heal missing Chromium shared-library deps on every deploy

## 2026-08-04 — `cb993d9`

- Temporarily re-add full exception capture to PDF diagnostics

## 2026-08-04 — `15b977b`

- Fix PDF export 500: Chromium cache dir must be writable by the app's own user

## 2026-08-03 — `74f0033`

- Capture full exception block (incl. inner exception) in PDF diagnostics

## 2026-08-03 — `b6d20fa`

- Deepen PDF export diagnostics: reproduce the Chrome download failure directly

## 2026-08-03 — `a7172d7`

- Add PDF export (Chromium) diagnostics to healthcheck.sh

## 2026-08-03 — `367e559`

- UI/UX consistency pass: standardize buttons, fix branded 404/403 pages, empty states

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

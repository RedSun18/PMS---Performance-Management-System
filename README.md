# AIC Performance Management (standalone Mac rebuild)

A standalone replacement for Al Ahleia Insurance Company's legacy WebForms/DevExpress/Informix
PM Form system. Built with C#/.NET 8, ASP.NET Core Razor Pages, EF Core 8, and PostgreSQL.
No runtime dependency on AICAPPS, DevExpress, WebForms, or Informix.

Read [`docs/legacy-mapping.md`](docs/legacy-mapping.md), [`docs/workflow-state-machine.md`](docs/workflow-state-machine.md),
[`docs/data-migration-plan.md`](docs/data-migration-plan.md) and [`docs/acceptance-tests.md`](docs/acceptance-tests.md)
before making further changes — they record every business-rule and data decision made during
the rebuild, including documented gaps in the legacy exports.

## Solution layout

```
src/Aic.Pm.Core/       Domain entities, EF Core DbContext + migrations, workflow/validation/
                        scoring/rating services, legacy CSV importer
src/Aic.Pm.Web/        ASP.NET Core Razor Pages app (PM Form, PM Form Summary, Employee
                        Master, Reference Master, User Management, Login As impersonation,
                        forced change-password flow, cookie auth)
src/Aic.Pm.Importer/   Console tool that loads References/Database exports into PostgreSQL
tests/Aic.Pm.Tests/    xUnit tests (SQLite in-memory) covering workflow, permissions,
                        scoring/validation, and import reconciliation
docs/                  Design docs (read these first)
docker-compose.yml     Local PostgreSQL 16 for development
```

## Prerequisites (Mac)

- **.NET 8 SDK.** If only later/earlier SDKs are installed, the projects target `net8.0` with
  `RollForward=LatestMajor`, so a newer installed runtime (e.g. .NET 9) will run them —
  `dotnet --list-sdks` / `dotnet --list-runtimes` to check what you have.
- **Docker Desktop** (or any Docker-compatible engine) for the local PostgreSQL container.
- No Informix client, no DevExpress license, no IIS — none of that is used.

## First-time setup

```bash
# 1. Start PostgreSQL (port 5445, to avoid clashing with any local Postgres install)
docker compose up -d

# 2. Restore the local dotnet-ef tool (already recorded in .config/dotnet-tools.json)
dotnet tool restore

# 3. Load the legacy exports (idempotent — safe to re-run any time)
#    Expects the approved snapshot at References/Database (kept out of git; see
#    References/DATABASE_EXPORT_CHECKLIST.md). Applies EF Core migrations first.
dotnet run --project src/Aic.Pm.Importer -- --data "References/Database"

# 4. Run the app
dotnet run --project src/Aic.Pm.Web
# Razor Pages listens on the URL printed at startup (see
# src/Aic.Pm.Web/Properties/launchSettings.json), typically http://localhost:5273.
```

The web app also applies migrations and re-seeds core reference data (departments, HR admin
accounts, exceptions, manager map) at every startup — `dotnet run` alone is safe without the
importer if you only need the login page and reference seeds, but PM forms/employees require
step 3.

## Local dev accounts

This is a standalone system, deliberately decoupled from the legacy AIC account list — the
six legacy `adm22/adm12/adm4/adm2/adm16/adm10` accounts are **not** seeded. Instead:

- A single administrator account is seeded on startup: username/password come from the
  `AdminAccount:Username` / `AdminAccount:Password` configuration keys (or the
  `PM_ADMIN_USERNAME` / `PM_ADMIN_PASSWORD` environment variables), defaulting to
  **`admin` / `admin123`** for local development. Change this in production.
- The importer additionally creates one login per employee (`AppUsers`, username = 4-digit
  padded employee code), sharing the development password `ChangeMe123!`
  (see `DatabaseSeeder.DevPassword`), flagged `MustChangePassword`.
- Additional accounts (Administrator, Employee, or Viewer) can be created from the in-app
  **User Management** page once signed in as `admin`.

**No production credentials, connection strings, or SMTP secrets are ever imported** — this is
a hard rule from the export checklist and is enforced by keeping `References/` out of source
control (`.gitignore`).

## User Management, forced password change, and Login As

**User Management** (`/Users`, administrator-only) is the primary account system. Creating a
user first asks for a **User Type**:

- **Employee** — pick an existing row from Employee Master; name/email auto-fill from that
  record (server-authoritative, avoids duplicate data entry). Username is still freely chosen.
- **Administrator** / **Viewer** — manually entered name, username, email. Administrator gets
  the `HR_ADMIN` role (full access, including User Management and Login As); Viewer gets read-only
  access to PM Form Summary, Employee Master, KPI/Competency/Reference Master (no create/edit,
  enforced server-side, not just hidden in the nav).

Every account has a password (custom, or the configurable default — `Security:DefaultUserPassword`,
`Password123` by default) and an optional **force password change at first login** flag. A user
with that flag set is redirected to `/Account/ChangePassword` before reaching anything else in
the app (current password + new + confirm required); on success the flag clears and they land on
the dashboard. Administrators can reset any user's password at any time from the Edit page —
custom or default, with or without forcing a change — without needing to know the old one.

**Login As** (`/Admin/LoginAs`, administrator-only) lets an admin impersonate another account for
troubleshooting. It's gated by a **separate verification password**
(`Security:LoginAsVerificationPassword`, default `Password*123` — change this in production),
valid for 5 minutes per admin session. After verifying, the admin picks any active user and
"Login As User" fully swaps their session to that user's real identity — same permissions, same
nav, same pending-password-change state, exactly as that user would experience it. A persistent
amber banner stays on every page while impersonating, with **Return to Administrator** always
one click away (this remains reachable even if the impersonated account itself has a pending
forced password change). Nested impersonation is blocked. Every session is audited in
`ImpersonationLogs` (admin, impersonated user, start/end timestamps, IP, session id), visible in
a history table on the same page.

## Email safety guardrail

Every workflow email is dispatched through the single choke point `EmailService.DispatchAsync`
(`src/Aic.Pm.Core/Services/EmailService.cs`). In this build **every dispatch with a non-empty
intended recipient list is redirected to a fixed address (`aryanbhandary@gmail.com`)** — no
legacy empmaster/employee address is ever used as an actual send target. The originally intended
recipients are recorded in the `EmailLogs.Note` column for traceability only. No SMTP client is
wired up anywhere in the codebase (`EmailLog.Status` stays `LOGGED`/`SKIPPED_NO_RECIPIENT`); wiring
real delivery is a deliberate future decision, not something this rebuild does implicitly.

## Running tests

```bash
dotnet test tests/Aic.Pm.Tests
```

Tests run against an in-memory SQLite database and don't require Docker. The import
reconciliation test (`ImportTests`) additionally runs against the real `References/Database`
export when that directory is present in the working tree; it's skipped otherwise.

## Database migrations

```bash
# Add a new migration after changing entities in Aic.Pm.Core/Domain or PmDbContext
PM_CONNECTION="Host=localhost;Port=5445;Database=aicpm;Username=aicpm;Password=aicpm_dev" \
  dotnet ef migrations add <Name> --project src/Aic.Pm.Core --startup-project src/Aic.Pm.Core --output-dir Data/Migrations

# Migrations apply automatically at Importer/Web startup; to apply manually:
dotnet ef database update --project src/Aic.Pm.Core --startup-project src/Aic.Pm.Core
```

## Known data gaps (see docs/data-migration-plan.md for detail)

- No real `empmaster` export exists; employees are synthesized from PM form HDR snapshots
  (`Employee.Source = "HDR_SNAPSHOT"`) and are editable in Employee Master once corrected.
- No `ta_users` export; no employee email addresses are on file, so employee-facing mail
  dispatch is currently always `SKIPPED_NO_RECIPIENT` regardless of the safety redirect above.
- Department/designation/section descriptions are partially seeded from hardcoded legacy
  mappings and partially harvested as bare codes — editable in Reference Master / Employee Master.

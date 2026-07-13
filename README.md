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
                        Master, Reference Master, cookie auth)
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

The importer creates one login per employee (`AppUsers`, username = 4-digit padded employee
code) plus the six approved PM HR administrators:

```
adm22, adm12, adm4, adm2, adm16, adm10
```

All seeded accounts share the development password `ChangeMe123!` (see
`DatabaseSeeder.DevPassword`) and are flagged `MustChangePassword`. **No production credentials,
connection strings, or SMTP secrets are ever imported** — this is a hard rule from the export
checklist and is enforced by keeping `References/` out of source control (`.gitignore`).

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

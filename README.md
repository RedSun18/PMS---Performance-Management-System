# Performance Management System

A standalone enterprise Performance Management System built with C#/.NET 8, ASP.NET Core Razor Pages, EF Core 8, and PostgreSQL.

Read [`docs/legacy-mapping.md`](docs/legacy-mapping.md), [`docs/workflow-state-machine.md`](docs/workflow-state-machine.md),
[`docs/data-migration-plan.md`](docs/data-migration-plan.md) and [`docs/acceptance-tests.md`](docs/acceptance-tests.md)
before making further changes — they record every business-rule and data decision made during
the rebuild, including documented gaps in the legacy exports.

## Solution layout

```
src/PerformanceManagement.Core/       Domain entities, EF Core DbContext + migrations, workflow/validation/
                                      scoring/rating services, legacy CSV importer
src/PerformanceManagement.Web/        ASP.NET Core Razor Pages app (role-based Dashboard, PM Form, PM Form
                                      Summary, Employee Master, Reference Master, User Management, Login As
                                      impersonation, forced change-password flow, System Settings, cookie auth)
src/PerformanceManagement.Importer/   Console tool that loads References/Database exports into PostgreSQL
tests/PerformanceManagement.Tests/    xUnit tests (SQLite in-memory) covering workflow, permissions,
                                      scoring/validation, and import reconciliation
docs/                                 Design docs (read these first)
docker-compose.yml                    Local PostgreSQL 16 for development
```

## Prerequisites

- **.NET 8 SDK.** If only later/earlier SDKs are installed, the projects target `net8.0` with
  `RollForward=LatestMajor`, so a newer installed runtime (e.g. .NET 9) will run them —
  `dotnet --list-sdks` / `dotnet --list-runtimes` to check what you have.
- **Docker Desktop** (or any Docker-compatible engine) for the local PostgreSQL container.

## First-time setup

```bash
# 1. Start PostgreSQL (port 5445, to avoid clashing with any local Postgres install)
docker compose up -d

# 2. Restore the local dotnet-ef tool (already recorded in .config/dotnet-tools.json)
dotnet tool restore

# 3. Load the legacy exports (idempotent — safe to re-run any time)
#    Expects the approved snapshot at References/Database (kept out of git; see
#    References/DATABASE_EXPORT_CHECKLIST.md). Applies EF Core migrations first.
dotnet run --project src/PerformanceManagement.Importer -- --data "References/Database"

# 4. Run the app
dotnet run --project src/PerformanceManagement.Web
# Razor Pages listens on the URL printed at startup (see
# src/PerformanceManagement.Web/Properties/launchSettings.json), typically http://localhost:5273.
```

The web app also applies migrations and re-seeds core reference data (departments, HR admin
accounts, exceptions, manager map) at every startup — `dotnet run` alone is safe without the
importer if you only need the login page and reference seeds, but PM forms/employees require
step 3.

## Local dev accounts

A single administrator account is seeded on startup: username/password come from the
`AdminAccount:Username` / `AdminAccount:Password` configuration keys (or the
`PM_ADMIN_USERNAME` / `PM_ADMIN_PASSWORD` environment variables), defaulting to
**`admin` / `admin123`** for local development. Change this in production.

The importer additionally creates one login per employee (`AppUsers`, username = 4-digit
padded employee code), sharing the development password `ChangeMe123!`
(see `DatabaseSeeder.DevPassword`), flagged `MustChangePassword`.

Additional accounts (Administrator, Employee, or Viewer) can be created from the in-app
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

## Dashboard

`/Dashboard` is the landing page after login (and the root `/`). It renders one of three views,
chosen server-side by role — nobody can reach another role's view by URL, since the content is
selected from the authenticated identity, not a query parameter:

- **Employee** (default): current review status, overall/KPI/competency completion, a computed
  deadline hint, and the 5 most recent notifications tied to the employee's own PM forms.
- **Direct manager** (`PermissionService.IsAManagerAsync`): team size, forms waiting on the
  manager's action, forms returned by HR, completed forms, and a per-employee status table.
- **Administrator / Viewer**: org-wide employee/forms counts, a Ready/In Progress/Finalized
  breakdown, and a department-by-department completion chart.

## Email

Every workflow email is dispatched through the single choke point `EmailService.DispatchAsync`
(`src/PerformanceManagement.Core/Services/EmailService.cs`), which now sends real mail via SMTP
(`System.Net.Mail.SmtpClient`, STARTTLS). Configuration lives in the database
(`SystemSettings`, singleton row) and is edited from the admin-only **System Settings** page
(`/Settings`), including a **Send Test Email** button. On first run, if no settings row exists yet,
it is seeded once from the `Email:*` configuration section (`appsettings.Development.json` or
`dotnet user-secrets` — see below) so a fresh dev machine works without ever visiting the page.
The SMTP password is encrypted at rest with ASP.NET Core Data Protection and is never displayed
in plaintext after saving — only whether one is set.

**Safety guardrail preserved:** while `DevelopmentRedirectEmail` is set (Settings page), every
dispatch with a non-empty intended recipient list is redirected there instead of any imported
employee address — no real employee inbox is ever used as an actual send target. The originally
intended recipients are recorded in `EmailLogs.Note` for traceability. `EmailLog.Status` is one of
`SENT`, `FAILED` (SMTP error, logged via `ILogger`, never silently swallowed), `LOGGED` (no SMTP
configured yet), `DISABLED` (notifications toggled off), `SKIPPED_NO_RECIPIENT`, or
`SKIPPED_DUPLICATE`.

Local dev SMTP configuration (never committed — set via User Secrets):

```bash
dotnet user-secrets set "Email:SmtpHost" "smtp.gmail.com" --project src/PerformanceManagement.Web
dotnet user-secrets set "Email:SmtpPort" "587" --project src/PerformanceManagement.Web
dotnet user-secrets set "Email:SmtpUsername" "you@example.com" --project src/PerformanceManagement.Web
dotnet user-secrets set "Email:SmtpPassword" "<app password>" --project src/PerformanceManagement.Web
dotnet user-secrets set "Email:SenderEmail" "you@example.com" --project src/PerformanceManagement.Web
dotnet user-secrets set "Email:DevelopmentRedirectEmail" "you@example.com" --project src/PerformanceManagement.Web
```

### Actionable emails and secure deep links

Every workflow email carries a prominent action button (e.g. "View Performance Form", "Open HR
Review") plus a plain-text fallback link, and states Current Status / Required Action / Previous
Action By / Next Action Required By / Review Year / Reference / timestamp — the email is actionable
without opening the app first (see `EmailTemplates` in `EmailService.cs`).

The button never links to a raw `?empcd=&year=` URL. Instead `FormLinkService`
(`src/PerformanceManagement.Core/Services/FormLinkService.cs`) issues a signed, expiring token
(ASP.NET Core Data Protection, 30-day default lifetime) encoding the employee code, review year,
and intended recipient username, built into a `{ApplicationBaseUrl}/OpenForm?token=...` link. The
base URL is a System Settings field ("General" section) — set it to `https://pms.company.com` in
production or `https://localhost:5273` for local testing.

`Pages/OpenForm` resolves the token: invalid/tampered/expired tokens and requests from a user who
is neither the intended recipient nor otherwise authorized (admin/direct manager/HR/branch viewer,
via the same `PermissionService` check `PmForm` itself uses) land on Access Denied with a friendly
message; everyone else is redirected straight to the right PM Form. Because `/OpenForm` requires
authentication like any other page, an unauthenticated click goes through the standard ASP.NET Core
cookie-auth challenge → Login (`?ReturnUrl=...`) → back to `/OpenForm` flow; `ReturnUrl` also
survives a forced password change if the account has one pending.

## Running tests

```bash
dotnet test tests/PerformanceManagement.Tests
```

Tests run against an in-memory SQLite database and don't require Docker. The import
reconciliation test (`ImportTests`) additionally runs against the real `References/Database`
export when that directory is present in the working tree; it's skipped otherwise.

## Database migrations

```bash
# Add a new migration after changing entities in PerformanceManagement.Core/Domain or PmDbContext
PM_CONNECTION="Host=localhost;Port=5445;Database=pms;Username=pms;Password=pms_dev" \
  dotnet ef migrations add <Name> --project src/PerformanceManagement.Core --startup-project src/PerformanceManagement.Core --output-dir Data/Migrations

# Migrations apply automatically at Importer/Web startup; to apply manually:
dotnet ef database update --project src/PerformanceManagement.Core --startup-project src/PerformanceManagement.Core
```

## Known data gaps (see docs/data-migration-plan.md for detail)

- No real `empmaster` export exists; employees are synthesized from PM form HDR snapshots
  (`Employee.Source = "HDR_SNAPSHOT"`) and are editable in Employee Master once corrected.
- No `ta_users` export; no employee email addresses are on file, so employee-facing mail
  dispatch is currently always `SKIPPED_NO_RECIPIENT` regardless of the safety redirect above.
- Department/designation/section descriptions are partially seeded from hardcoded legacy
  mappings and partially harvested as bare codes — editable in Reference Master / Employee Master.

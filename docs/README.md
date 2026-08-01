# Performance Management System

A production-ready employee performance review platform built with **ASP.NET Core 8 (Razor
Pages)**, **Entity Framework Core 8**, and **PostgreSQL 16** — bilingual (English / Arabic, full
RTL support), workflow-driven, and designed for a mid-size organization's annual review cycle.

> **v1.0.0** — feature complete, production-readiness reviewed (security, performance,
> localization, accessibility, testing). See [Release Readiness](#release-readiness) below.

---

## Table of contents

- [Overview](#overview)
- [Features](#features)
- [Technology stack](#technology-stack)
- [Architecture](#architecture)
- [Screenshots](#screenshots)
- [Installation](#installation)
- [Project structure](#project-structure)
- [Security](#security)
- [Testing](#testing)
- [Documentation set](#documentation-set)
- [Future roadmap](#future-roadmap)
- [Release readiness](#release-readiness)

---

## Overview

**Purpose.** Employee performance appraisal at most mid-size organizations runs on a mix of
spreadsheets, email chains, and a legacy system nobody wants to touch. This application replaces
that with a single, auditable, workflow-enforced system: every review moves through a fixed
sequence of stages (manager sets KPIs → employee acknowledges → manager scores achievement → two
independent HR reviewers approve), every transition is timestamped and attributed, and every
exceptional situation (a manager who leaves mid-cycle, a wrongly approved review, a stuck
workflow) has an explicit, audited recovery path rather than a database admin editing rows by
hand.

**Business problem.** The system was built to replace a legacy Informix-based appraisal module
(`pm_form_records` / `empmaster`) that had no workflow enforcement, no audit trail, no
localization, and no self-service reporting. Historical data from that system was migrated in as
part of the rebuild (see [`docs/data-migration-plan.md`](data-migration-plan.md) and
[`docs/legacy-mapping.md`](legacy-mapping.md)).

**Target users.**
- **Employees** — view their own KPIs/competencies, acknowledge, self-assess, view final ratings.
- **Managers** — set KPIs and competencies for direct reports, score achievement, submit to HR.
- **HR Reviewers** — two-stage independent approval (segregation of duties: the same person
  cannot perform both HR approvals on one review).
- **HR Administrators** — full system administration: employee/user master data, KPI/competency
  reference data, reports, system settings, and the Workflow Administration recovery console.

---

## Features

### Performance review workflow
- Seven-stage state machine (Draft → Pending Employee Acknowledgement → Employee Acknowledged →
  Submitted to HR → HR Review 1 Approved → Approved), enforced server-side on every transition.
- KPI scoring across four balanced-scorecard perspectives (Financial, Customer, Internal Process,
  Learning & Growth) and behavioral/technical competencies, with grade-based KPI/competency weight
  splits (job families) and per-employee business-rule exceptions.
- Optimistic concurrency (versioned rows) so two users acting on the same review at once can't
  silently overwrite each other.
- Segregation of duties: the HR reviewer who performs the first approval cannot perform the
  second.
- Self-assessment, development plan, and promotion-recommendation fields alongside the numeric
  scoring.

### Workflow Administration (HR recovery console)
- Search/filter every review by employee, department, manager, year, or status, with a paginated
  results grid.
- Per-review detail page: employee info, a five-node visual progress tracker, full workflow
  timeline, and audit history.
- Six administrative recovery actions — **Return to Employee**, **Return to Manager**, **Reopen
  Review**, **Resend Notification**, **Administrative Completion**, **Unlock Review** — each
  gated to valid source states, each requiring a mandatory typed reason, each fully audited
  (actor, reason, previous/new state, IP address).

### Reporting
- Employee Performance Report, Department Summary, Manager Summary, and Overall Organization
  Summary, each exportable to **PDF** (rendered via headless Chromium for pixel-perfect
  bilingual/RTL output) and **Excel** (via ClosedXML).
- Reports are generated fresh from the database on every request — never cached or stale.

### Dashboard
- Organization-wide completion/progress metrics, per-department breakdown, form-status summary,
  and a live recent-activity feed drawn from the audit trail.

### Master / reference data
- Employee Master (search, edit, department/manager assignment, termination).
- Department Master, KPI Master, Competency Master, Job Families, and Rating Scales — all
  editable by HR Administrators, all localized (English + Arabic names/descriptions).

### Localization
- Full English/Arabic bilingual UI with automatic RTL mirroring — 36 pages, every string
  translated (no hardcoded text), a persisted per-user language preference, and culture-aware
  PDF/email rendering.

### Notifications & email
- Every workflow transition sends a branded, bilingual HTML email with a signed deep link
  straight to the relevant review — no separate login step required.
- In-app notification centre (bell icon) mirrors the same events.
- Scheduled daily reminders and a weekly HR escalation digest for overdue reviews.
- A safety-net "development redirect" setting (opt-in) for testing against real data without
  emailing real employees.

### Scheduler
- Six background jobs on cron schedules: annual form generation (1 Jan), mid-year and end-year
  review window opening (1 Jun / 1 Nov), daily reminders, weekly escalation, and monthly cleanup
  of stale email logs. A Job Management page shows last-run status/duration/result for each.

### Security & administration
- Cookie authentication, role-based + per-record authorization, account lockout, password
  expiry/complexity policy, and an HR-admin "Login As" impersonation tool (separately verified,
  fully audited, never privilege-escalating).
- Append-only audit trail covering workflow overrides, settings changes, user management, login
  attempts, and department changes.
- System Settings page: General, Performance Review dates, Email/SMTP, Authentication, Security,
  Dashboard, and Branding — all admin-editable at runtime, SMTP/verification passwords encrypted
  at rest.

---

## Technology stack

| Layer | Technology |
|---|---|
| Runtime / framework | .NET 8, ASP.NET Core 8 Razor Pages |
| ORM / database access | Entity Framework Core 8, Npgsql |
| Database | PostgreSQL 16 |
| Frontend | Server-rendered Razor views, hand-written CSS (no framework), a small vanilla-JS searchable-combobox component |
| Localization | ASP.NET Core built-in localization (`IViewLocalizer` / `IStringLocalizer`), per-page `.resx`/`.ar.resx` pairs |
| Email | `System.Net.Mail` SMTP client, HTML email templates |
| Background jobs | Quartz.NET (in-process scheduler) |
| PDF generation | PuppeteerSharp (headless Chromium) |
| Excel generation | ClosedXML |
| Secrets at rest | ASP.NET Core Data Protection (SMTP password, Login-As verification password) |
| Testing | xUnit, in-memory SQLite fixture |
| CI | GitHub Actions (build + test on push/PR) |

---

## Architecture

Full diagrams (with Mermaid source) live under [`diagrams/`](diagrams/); rendered images under
[`diagrams/rendered/`](diagrams/rendered/). Summary:

![System architecture](diagrams/rendered/architecture_1.png)

The solution has three deployable/runnable projects plus a test project, with dependencies
flowing one way:

![Layer architecture](diagrams/rendered/architecture_2.png)

See [`diagrams/workflow.md`](diagrams/workflow.md) for the full workflow state machine,
[`diagrams/database.md`](diagrams/database.md) for the ER diagrams,
[`diagrams/authentication.md`](diagrams/authentication.md) for the auth/impersonation flow, and
[`diagrams/deployment.md`](diagrams/deployment.md) for deployment topology.

---

## Screenshots

| | |
|---|---|
| **Login** ![Login](screenshots/01_login.png) | **Dashboard** ![Dashboard](screenshots/02_dashboard.png) |
| **Employee Management** ![Employees](screenshots/03_employee_management.png) | **Department Master** ![Departments](screenshots/04_department_management.png) |
| **KPI Master** ![KPI Master](screenshots/05_kpi_master.png) | **Competency Master** ![Competency Master](screenshots/06_competency_master.png) |
| **PM Form Summary (search)** ![Summary](screenshots/08_search_pmformsummary.png) | **PM Form — Manager Review stage** ![PM Form](screenshots/11_pmform_manager_review.png) |
| **Workflow Administration — search** ![Workflow Admin](screenshots/12_workflow_administration_search.png) | **Workflow Administration — details** ![Workflow Details](screenshots/13_workflow_administration_details.png) |
| **Reports** ![Reports](screenshots/14_reports.png) | **Sample PDF export** ![PDF](screenshots/14b_pdf_report_sample.png) |
| **Scheduled Jobs** ![Jobs](screenshots/15_scheduler.png) | **System Settings** ![Settings](screenshots/16_settings_general.png) |
| **Arabic interface (RTL)** ![Arabic](screenshots/21_arabic_dashboard.png) | **Mobile layout** ![Mobile](screenshots/23_mobile_dashboard.png) |

*(Full set of 24 screenshots — including User Management, Login As, every Settings tab, both a
Draft- and an Acknowledgement-stage PM Form, and Arabic PM Form — is in
[`screenshots/`](screenshots/).)*

---

## Installation

### Prerequisites
- .NET 8 SDK
- PostgreSQL 16 (a `docker-compose.yml` is provided for local development)
- (Optional, for PDF export) no separate install needed — PuppeteerSharp downloads its own
  headless Chromium build on first run.

### Database (local development)
```bash
docker compose up -d
```
This starts a local Postgres instance on `localhost:5445` (see `docker-compose.yml`).

### Configuration
The app reads its connection string from `ConnectionStrings:Pm` (appsettings) or the
`PM_CONNECTION` environment variable, falling back to a documented local-dev default. See
[`Administrator_Guide.pdf`](Administrator_Guide.pdf) and
[`Deployment_Guide.pdf`](Deployment_Guide.pdf) for the full configuration reference (SMTP,
admin account, Login-As verification password, session/security settings).

### Running locally
```bash
dotnet run --project src/PerformanceManagement.Web
```
Migrations and core reference-data seeding run automatically on startup. Default dev accounts:
`admin` (HR Administrator) and one account per employee (4-digit employee code) — see the
repository root `README.md` for initial passwords.

### Demo environment
A separate, fully fictional "Apex Corporation" instance — its own database, its own
seeded data (~200 employees, three review years, every workflow stage represented), its
own branding — safe to run publicly or hand to a recruiter/client. See
[`DEMO.md`](DEMO.md) for setup, credentials, and how to reset it.

### Production deployment
See [`Deployment_Guide.pdf`](Deployment_Guide.pdf) for the full checklist: required environment
variables, HTTPS/reverse-proxy setup, the `/health` endpoint, logging, backups, and the
single-node deployment constraint (this app does not currently support horizontal scaling — see
[`diagrams/deployment.md`](diagrams/deployment.md)).

---

## Project structure

| Project | Responsibility |
|---|---|
| **`PerformanceManagement.Web`** | ASP.NET Core Razor Pages host: all UI pages, `Program.cs` startup/middleware pipeline, the Quartz job registrations (`Jobs/ScheduledJobs.cs`), static assets (`wwwroot/`), and per-page localization resources (`Resources/Pages/...`). |
| **`PerformanceManagement.Core`** | All business logic: domain entities (`Domain/Entities.cs`), the EF Core `DbContext` and migrations (`Data/`), and every domain service — `WorkflowService`, `WorkflowAdminService`, `PermissionService`, `FormValidationService`, `RatingService`, `ReportDataService`/`ReportExportService`, `EmailService`, `NotificationService`, `AuditService`, `SettingsService`, `FormLinkService`, `PdfRenderer`. |
| **`PerformanceManagement.Importer`** | One-time CLI tool for importing sanitized legacy CSV exports during the initial data migration (not used in normal operation). |
| **`PerformanceManagement.Tests`** | xUnit test suite against an in-memory SQLite fixture (`TestHost`) — exercises `Core` business logic directly, independent of the web host or a real Postgres instance. |

---

## Security

- **Authentication:** ASP.NET Core cookie authentication; account lockout after a configurable
  number of failed attempts (no exemption for administrator accounts); password expiry and
  complexity policy; generic error messages that don't distinguish "no such user" from "wrong
  password" from "locked out".
- **Authorization:** role-based (`HR_ADMIN`, `VIEWER`) on every admin page, plus per-record
  authorization on the PM Form itself (an employee's own record, their direct manager, an HR
  admin, or a read-only branch viewer — nothing else).
- **Audit:** an append-only `AuditLog` table records login attempts, Workflow Administration
  overrides, settings changes (including the audit-logging toggle itself, logged unconditionally
  so it can't erase its own record), user management, and department changes. A separate
  `ImpersonationLog` records every "Login As" session.
- **Localization:** every user-facing string is localized (English/Arabic); RTL is applied at the
  document level, not per-component.
- **Workflow integrity:** optimistic concurrency on every write path; segregation of duties on HR
  approval; mandatory typed justification + audit entry on every administrative override.
- **Transport/session security:** HTTPS redirection and HSTS in Production; `Secure`/`SameSite`
  cookies; standard security response headers (`X-Content-Type-Options`, `X-Frame-Options`,
  `Referrer-Policy`, a Content-Security-Policy); a startup guard that refuses to boot in
  Production with any shipped default credential still unchanged.
- **Secrets at rest:** SMTP and Login-As verification passwords are encrypted via ASP.NET Core
  Data Protection, never stored or displayed in plaintext.

---

## Testing

The `PerformanceManagement.Tests` project (xUnit) exercises the `Core` business-logic layer
directly against an in-memory SQLite database (`TestHost`, seeded with a representative
employee/department/KPI/competency fixture), covering:

- The full workflow state machine (every transition, valid and invalid source states, locking,
  history/audit generation) and all six Workflow Administration override actions.
- Permission resolution (self, direct manager, HR admin, branch viewer, self-managed exceptions).
- Scoring/weighting rules, rating-band resolution, and job-family KPI/competency splits.
- Email dispatch (deduplication, the opt-in development-redirect safety net, idempotency-based
  duplicate-send prevention).
- Legacy CSV import reconciliation and idempotency.

CI (`.github/workflows/ci.yml`) runs `dotnet build` + `dotnet test` on every push and pull
request. Areas not yet covered by automated tests — the login page's rate-limiting logic, the
"Login As" page, PDF/Excel rendering, and the Quartz job bodies themselves — are documented as
known gaps in the [release readiness report](#release-readiness) rather than left unstated.

---

## Documentation set

| Document | Audience |
|---|---|
| [`User_Guide.pdf`](User_Guide.pdf) | Employees, managers, HR reviewers |
| [`Administrator_Guide.pdf`](Administrator_Guide.pdf) | HR/system administrators |
| [`Technical_Architecture_Guide.pdf`](Technical_Architecture_Guide.pdf) | Engineers, architects |
| [`Database_Guide.pdf`](Database_Guide.pdf) | DBAs, engineers |
| [`Workflow_Guide.pdf`](Workflow_Guide.pdf) | HR, managers, process owners |
| [`Deployment_Guide.pdf`](Deployment_Guide.pdf) | Ops/infrastructure |
| [`Performance_Management_System.pptx`](Performance_Management_System.pptx) | Management, clients, portfolio presentation |

---

## Future roadmap

The following are realistic, *not-yet-implemented* enhancements — not a description of the
current release:

- Horizontal scaling (clustered Quartz job store, distributed session/cache, migrations moved to
  an explicit release step instead of app-startup).
- A nonce-based strict Content-Security-Policy (current CSP allows `unsafe-inline` for the
  inline scripts/handlers used throughout the UI).
- Durable, shared Data Protection key storage for multi-instance deployments.
- Long-term archival/partitioning strategy for the audit log once it grows very large.
- Automated test coverage for `Account/Login`, `Admin/LoginAs`, `FormLinkService` token
  security, PDF/Excel rendering, and the scheduled job bodies.
- A production-ready `Dockerfile` for the web application itself (only a local-dev Postgres
  `docker-compose.yml` exists today).
- Timezone-aware date gating (the mid/end-year and achievement-window dates currently compare
  against server-local time).

---

## Release readiness

This release has been through a full production-readiness review across security,
authorization, workflow integrity, localization, accessibility, database design/performance,
logging, deployment, documentation, and testing. Every Critical- and High-severity finding was
fixed and verified (build, automated tests, and live browser verification); remaining items are
disclosed above as roadmap, not hidden gaps.

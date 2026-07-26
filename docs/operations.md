# Operations Guide

Day-2 operational guidance for running the Performance Management System in production —
backups, restore, retention, and monitoring. This is distinct from
[`data-migration-plan.md`](data-migration-plan.md)/the (gitignored, local-only)
`References/DATABASE_EXPORT_CHECKLIST.md`, which cover the one-time initial migration from the
legacy Informix system, not ongoing operations of the live PostgreSQL database.

## Backups

The app stores everything in a single PostgreSQL database (connection string via
`ConnectionStrings:Pm` / `PM_CONNECTION`, see `README.md`). There is currently no automated
backup job shipped with the app — set one up at the infrastructure level:

- **Recommended cadence:** nightly `pg_dump` (or your managed Postgres provider's automated
  snapshot feature), retained for at least 30 days, plus WAL archiving / point-in-time recovery
  if your Postgres hosting supports it and the organization's RPO requires better than
  "since last nightly dump."
- **What's in scope:** the whole database. There is no separate blob/file store to back up
  except `wwwroot/uploads/branding/` (the uploaded company logo) — small, low-churn, and safe to
  include in the same backup cadence as a filesystem snapshot or simply re-uploaded via
  Settings > Branding after a restore.
- **Test the restore, not just the backup.** A backup nobody has ever restored is not a backup.
  Periodically restore into a scratch environment and run `dotnet run` against it to confirm the
  app actually starts and authenticates against the restored data.

## Restore

1. Provision a fresh PostgreSQL instance (or restore into the existing one after confirming the
   failure mode requires it — don't restore over a live database without a clear incident
   reason).
2. Restore the `pg_dump` output with `pg_restore` (or your provider's snapshot-restore flow).
3. Point `PM_CONNECTION` at the restored database and start the app — it runs
   `Database.MigrateAsync()` on startup (see `Program.cs`), so a restored dump from an older
   schema version will be migrated forward automatically. **Restoring a dump newer than the
   currently-deployed app version is not supported** — deploy the matching app version first.
4. Re-upload the branding logo via Settings > Branding if it wasn't restored from a filesystem
   backup.

## Retention

- **`EmailLog`**: automatically purged after 180 days by the `MonthlyCleanupJob` scheduled job
  (see `Jobs/ScheduledJobs.cs`) — no manual action needed.
- **`AuditLog` and `ImpersonationLog`**: retained forever by design (the compliance/accountability
  record) — the cleanup job explicitly excludes them. These will grow steadily over the life of
  the deployment; there is no automated archival yet. If retention becomes a real storage/query
  concern after a few years of use, the recommended approach is periodic export-and-archive (dump
  rows older than N years to cold storage, then delete) rather than indefinite unbounded growth —
  not implemented as of this writing, tracked as a roadmap item.
- **`PmFormStatusHistory`**: grows roughly linearly with the number of PmForm records (a handful
  of rows per form), not independently — no retention action needed; its growth is naturally
  bounded by organizational headcount and years of history.

## Monitoring

- **`GET /health`** — liveness/readiness probe (unauthenticated). Returns 200 when the app can
  reach the database, non-200 otherwise. Point your load balancer's or container orchestrator's
  health check at this.
- **Application logs**: as of this writing, operational logging in the Web project is minimal
  (see the release-readiness report for the current state and roadmap). The durable source of
  truth for "who did what" is the in-app Audit Log (`AuditService`, visible to HR Admins), not
  console/file logs.
- **Scheduled jobs**: `/Jobs` (HR Admin only) shows the last run time/result/duration for every
  registered job (`GenerateAnnualForms`, `OpenMidYearReview`, `OpenEndYearReview`,
  `DailyReminder`, `WeeklyEscalation`, `MonthlyCleanup`) — check here first if a scheduled
  notification or workflow-window change didn't happen as expected.

## Deployment topology

This app is a **single-node design** — see the in-code comments on session/cache configuration in
`Program.cs` ("standalone single-node app: in-memory is fine") and the Quartz scheduler
registration (uses the default in-memory `RAMJobStore`, no clustering configured). **Do not run
multiple replicas** without first: (a) moving session/cache to a shared distributed store, (b)
configuring Quartz for a clustered/persistent JobStore so scheduled jobs don't run duplicated once
per replica, and (c) moving the `Database.MigrateAsync()` startup call to an explicit,
single-runner release step instead of running on every instance's boot.

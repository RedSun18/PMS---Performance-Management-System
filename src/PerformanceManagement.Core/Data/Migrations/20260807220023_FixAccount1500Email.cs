using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerformanceManagement.Core.Data.Migrations
{
    /// <summary>
    /// Data-only fix, no schema change. Found during the CAT: AppUser/Employee 1500
    /// ("Abdullah Rehan Khan") in the live Demo database carries a real personal email
    /// address instead of a fictional one — this row was created directly against the live
    /// Demo database at some point outside DemoSeeder (confirmed: it does not exist in the
    /// seeded replica used for local testing, so this UPDATE is a genuine no-op there and
    /// only takes effect against the real Demo database). Replaces the email with a
    /// fictional @apexcorp.demo address, matching every other seeded account, without
    /// deleting the row (kept per explicit instruction so headcount/appraisal history for
    /// this account isn't lost).
    ///
    /// Also removes any stray "Login Succeeded" AuditLog rows tagged with the IPv6 loopback
    /// address (Details LIKE 'IP: ::1%') — a genuine leftover from local admin testing that
    /// found its way into the live Demo audit trail, not seeded content.
    ///
    /// Guarded to the Demo database by name (current_database() = 'pms_demo'), same
    /// reasoning as every other data migration in this project: this migration also runs
    /// against the real AIC Development database via the same db.Database.MigrateAsync()
    /// call in Program.cs, and must never touch real employee data there.
    /// Idempotent: setting the same fixed value is safe to re-run.
    /// </summary>
    public partial class FixAccount1500Email : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "AppUsers" SET "Email" = 'abdullah.khan@apexcorp.demo'
                WHERE ("UserName" = '1500' OR "EmpCode" = '1500') AND current_database() = 'pms_demo';

                UPDATE "Employees" SET "Email" = 'abdullah.khan@apexcorp.demo'
                WHERE "EmpCode" = '1500' AND current_database() = 'pms_demo';

                DELETE FROM "AuditLogs"
                WHERE "Action" = 'Login Succeeded' AND "Details" LIKE 'IP: ::1%'
                  AND current_database() = 'pms_demo';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately not reversible — there is nothing meaningful to revert to (the
            // prior value was a real personal email that must never be restored).
        }
    }
}

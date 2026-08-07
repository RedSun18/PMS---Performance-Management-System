using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerformanceManagement.Core.Data.Migrations
{
    /// <summary>
    /// One-time data repair, not a schema change. Notification.Link and EmailLog.Body both
    /// store an absolute URL at the moment the row is created (see
    /// FormLinkService.BuildFormUrlAsync), built from whatever SystemSettings.ApplicationBaseUrl
    /// held at that instant. A now-corrected setting (the DemoSeeder.cs literal and the live
    /// Settings row were both fixed to https://pms.aryanb.dev) only changes what NEW rows get —
    /// it can never rewrite rows that already exist. This migration is that rewrite: every
    /// already-stored row still pointing at a dev-only http://localhost:PORT address gets its
    /// origin swapped for the real public one, with the rest of the URL (the /OpenForm path and
    /// signed token) preserved byte-for-byte. Every other column, every other table, is
    /// untouched.
    ///
    /// Guarded to the Demo database by name (current_database() = 'pms_demo'), not just by the
    /// localhost pattern: this same migration also runs — via the identical
    /// db.Database.MigrateAsync() call in Program.cs — against the real AIC Development
    /// database on every developer's machine. Development's own SettingsService fallback IS
    /// "http://localhost:5273" (SettingsService.cs, GetApplicationBaseUrlAsync), so a developer
    /// who has ever triggered a real workflow notification locally legitimately has rows that
    /// match the same pattern this migration targets — and for Development those are CORRECT,
    /// not stale. Rewriting a local developer's own localhost links to the production domain
    /// would itself be a bug of the same shape as the one this migration fixes. The database-
    /// name guard is what makes this migration safe to ship globally while only ever changing
    /// Demo's data.
    ///
    /// Idempotent: after the first successful run, zero rows match and every later run (a
    /// redeploy, a rerun of `dotnet ef database update`) is a genuine no-op.
    /// </summary>
    public partial class FixLocalhostNotificationAndEmailLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Matches any port (the exact value has drifted across seeds — 5273 vs 5274 — and a
            // hardcoded port would miss whichever one isn't live today), rebuilds using the
            // literal position of "/OpenForm" so the token and every query-string character
            // after it survive completely unmodified.
            migrationBuilder.Sql(
                """
                UPDATE "Notifications"
                SET "Link" = 'https://pms.aryanb.dev' || substring("Link" from strpos("Link", '/OpenForm'))
                WHERE "Link" ~ '^http://localhost:[0-9]+/OpenForm'
                  AND current_database() = 'pms_demo';
                """);

            // EmailLog.Body is a full HTML email that can legitimately contain the same link
            // more than once (an action button's href plus a plain-text fallback URL in the
            // footer) — a global regexp_replace, not a single positional rebuild, so every
            // occurrence in a given row is corrected, not just the first.
            migrationBuilder.Sql(
                """
                UPDATE "EmailLogs"
                SET "Body" = regexp_replace("Body", 'http://localhost:[0-9]+', 'https://pms.aryanb.dev', 'g')
                WHERE "Body" ~ 'http://localhost:[0-9]+'
                  AND current_database() = 'pms_demo';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately not reversible: the original localhost URLs are gone by design (that
            // was the point), and re-introducing a broken dev-only address into production data
            // on a rollback would be actively harmful, not neutral. If this migration is ever
            // rolled back, the correct link text remains — nothing regresses.
        }
    }
}

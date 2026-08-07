using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerformanceManagement.Core.Data.Migrations
{
    /// <summary>
    /// One-time data repair, not a schema change — the same "fix the code, not the already-
    /// created rows" gap as FixLocalhostNotificationAndEmailLinks, this time for content instead
    /// of a URL. DemoSeeder.cs seeded all 18 KpiMaster and 12 CompetencyMaster rows with only a
    /// code/name/perspective — Description and Formula were never populated, and the per-form
    /// snapshot rows (PmFormKpi.KpiDefinition/FormulaMetric, PmFormCompetency.Description) were
    /// never copied from the master at all. Those columns render as their own "Purpose/
    /// Definition" and "Formula/Metric" table columns on every single KPI row of every single
    /// employee's PM Form — the result was a blank column across the board on every appraisal in
    /// the entire demo. DemoSeeder.cs/DemoReferenceData.cs are fixed for future reseeds; this
    /// migration backfills the rows that already exist so a reseed isn't required.
    ///
    /// Guarded to the Demo database by name (current_database() = 'pms_demo'), same reasoning as
    /// FixLocalhostNotificationAndEmailLinks: this migration also runs against the real AIC
    /// Development database via the same db.Database.MigrateAsync() call in Program.cs, and
    /// Development's own KPI/Competency master data is real, human-entered content that must
    /// never be overwritten by demo copy.
    ///
    /// Idempotent: the KpiMasters/CompetencyMasters UPDATEs always set the same fixed values
    /// (safe to re-run), and the PmFormKpis/PmFormCompetencies UPDATEs only touch rows where the
    /// definition is still null/empty, so a row a real user has since edited by hand is never
    /// clobbered by a later re-run of this same migration.
    /// </summary>
    public partial class BackfillKpiAndCompetencyContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "KpiMasters" SET "Description" = 'Measures the year-over-year increase in total gross written premium income.', "Formula" = '((Current Period GWP − Prior Period GWP) / Prior Period GWP) × 100' WHERE "KpiId" = 'KPI001' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Tracks the profitability of underwriting activity before investment income.', "Formula" = '(Earned Premium − Incurred Claims − Underwriting Expenses) / Earned Premium × 100' WHERE "KpiId" = 'KPI002' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Measures operating expenses as a proportion of earned premium.', "Formula" = 'Total Operating Expenses / Earned Premium × 100' WHERE "KpiId" = 'KPI003' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Return generated on the company''s invested asset portfolio.', "Formula" = 'Net Investment Income / Average Invested Assets × 100' WHERE "KpiId" = 'KPI004' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Measures how closely actual departmental spend tracks the approved annual budget.', "Formula" = '(1 − |Actual Spend − Budgeted Spend| / Budgeted Spend) × 100' WHERE "KpiId" = 'KPI005' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Captures overall client satisfaction from post-interaction surveys.', "Formula" = 'Average survey rating (1–5 scale), converted to a percentage' WHERE "KpiId" = 'KPI006' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Share of eligible policies renewed at expiry.', "Formula" = '(Policies Renewed / Policies Eligible for Renewal) × 100' WHERE "KpiId" = 'KPI007' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Average number of days to settle a claim from first notification to payout.', "Formula" = 'Total Days to Resolve All Claims / Number of Claims Resolved' WHERE "KpiId" = 'KPI008' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Measures client willingness to recommend the company.', "Formula" = '% Promoters − % Detractors' WHERE "KpiId" = 'KPI009' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Average number of distinct policies held per active client.', "Formula" = 'Total Policies in Force / Total Active Clients' WHERE "KpiId" = 'KPI010' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Share of eligible operational transactions completed without manual intervention.', "Formula" = '(Automated Transactions / Total Eligible Transactions) × 100' WHERE "KpiId" = 'KPI011' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Percentage of claims processed without a reopened case or payment correction.', "Formula" = '(Claims Processed Correctly / Total Claims Processed) × 100' WHERE "KpiId" = 'KPI012' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Reduction in aggregate underwritten risk exposure achieved through reinsurance and portfolio actions.', "Formula" = '((Prior Period Exposure − Current Period Exposure) / Prior Period Exposure) × 100' WHERE "KpiId" = 'KPI013' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Percentage of internal and regulatory audit checkpoints passed without a finding.', "Formula" = '(Checkpoints Passed / Total Checkpoints Audited) × 100' WHERE "KpiId" = 'KPI014' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Completion rate of milestones on the approved digital transformation roadmap.', "Formula" = '(Milestones Completed / Milestones Planned) × 100' WHERE "KpiId" = 'KPI015' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Share of assigned mandatory and role-based training completed on time.', "Formula" = '(Trainings Completed / Trainings Assigned) × 100' WHERE "KpiId" = 'KPI016' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Proportion of employees retained over the review period, excluding planned attrition.', "Formula" = '((Headcount at Start − Voluntary Departures) / Headcount at Start) × 100' WHERE "KpiId" = 'KPI017' AND current_database() = 'pms_demo';
                UPDATE "KpiMasters" SET "Description" = 'Participation rate in leadership and succession-planning development programs.', "Formula" = '(Employees Enrolled / Employees Eligible) × 100' WHERE "KpiId" = 'KPI018' AND current_database() = 'pms_demo';

                UPDATE "CompetencyMasters" SET "Description" = 'Ability to guide, motivate, and develop others toward shared objectives.' WHERE "CompId" = 'COM001' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "Description" = 'Clarity, professionalism, and effectiveness in written and verbal communication with colleagues and clients.' WHERE "CompId" = 'COM002' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "Description" = 'Works effectively with others across teams and departments to achieve common goals.' WHERE "CompId" = 'COM003' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "Description" = 'Responds effectively to changing priorities, processes, and business conditions.' WHERE "CompId" = 'COM004' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "Description" = 'Consistently acts with honesty, fairness, and adherence to company and industry ethical standards.' WHERE "CompId" = 'COM005' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "Description" = 'Anticipates and responds to client needs with a consistently high standard of service.' WHERE "CompId" = 'COM006' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "Description" = 'Breaks down complex problems and data to identify root causes and practical solutions.' WHERE "CompId" = 'COM007' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "Description" = 'Demonstrates the technical knowledge and skill required for the role''s core functions.' WHERE "CompId" = 'COM008' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "Description" = 'Identifies, evaluates, and appropriately mitigates operational and underwriting risk.' WHERE "CompId" = 'COM009' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "Description" = 'Uses relevant data and evidence to inform judgment and business decisions.' WHERE "CompId" = 'COM010' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "Description" = 'Plans, executes, and delivers initiatives on time and within scope.' WHERE "CompId" = 'COM011' AND current_database() = 'pms_demo';
                UPDATE "CompetencyMasters" SET "Description" = 'Maintains current, working knowledge of applicable insurance regulations and compliance requirements.' WHERE "CompId" = 'COM012' AND current_database() = 'pms_demo';

                UPDATE "PmFormKpis" pk
                SET "KpiDefinition" = km."Description", "FormulaMetric" = km."Formula"
                FROM "KpiMasters" km
                WHERE pk."KpiCode" = km."KpiId"
                  AND (pk."KpiDefinition" IS NULL OR pk."KpiDefinition" = '')
                  AND current_database() = 'pms_demo';

                UPDATE "PmFormCompetencies" pc
                SET "Description" = cm."Description"
                FROM "CompetencyMasters" cm
                WHERE pc."CompCode" = cm."CompId"
                  AND (pc."Description" IS NULL OR pc."Description" = '')
                  AND current_database() = 'pms_demo';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately not reversible — the content being added is strictly an improvement
            // (real definitions replacing blank columns) with nothing meaningful to revert to.
        }
    }
}

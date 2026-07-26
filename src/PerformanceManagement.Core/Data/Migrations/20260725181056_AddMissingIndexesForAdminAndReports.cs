using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerformanceManagement.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingIndexesForAdminAndReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PmForms_EvalYear_Status",
                table: "PmForms",
                columns: new[] { "EvalYear", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ManagerAssignments_ManagerEmpCode",
                table: "ManagerAssignments",
                column: "ManagerEmpCode");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PmForms_EvalYear_Status",
                table: "PmForms");

            migrationBuilder.DropIndex(
                name: "IX_ManagerAssignments_ManagerEmpCode",
                table: "ManagerAssignments");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs");
        }
    }
}

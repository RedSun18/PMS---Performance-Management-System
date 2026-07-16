using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerformanceManagement.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PmForms_EvalYear",
                table: "PmForms",
                column: "EvalYear");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_DeptCode",
                table: "Employees",
                column: "DeptCode");

            migrationBuilder.CreateIndex(
                name: "IX_EmailLogs_FormLegacyRefNo",
                table: "EmailLogs",
                column: "FormLegacyRefNo");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_EmpCode",
                table: "AppUsers",
                column: "EmpCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PmForms_EvalYear",
                table: "PmForms");

            migrationBuilder.DropIndex(
                name: "IX_Employees_DeptCode",
                table: "Employees");

            migrationBuilder.DropIndex(
                name: "IX_EmailLogs_FormLegacyRefNo",
                table: "EmailLogs");

            migrationBuilder.DropIndex(
                name: "IX_AppUsers_EmpCode",
                table: "AppUsers");
        }
    }
}

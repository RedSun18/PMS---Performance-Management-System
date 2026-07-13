using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aic.Pm.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddImpersonationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImpersonationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdminUserName = table.Column<string>(type: "text", nullable: false),
                    AdminDisplayName = table.Column<string>(type: "text", nullable: false),
                    ImpersonatedUserName = table.Column<string>(type: "text", nullable: false),
                    ImpersonatedDisplayName = table.Column<string>(type: "text", nullable: false),
                    ImpersonatedEmpCode = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImpersonationLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationLogs_SessionId",
                table: "ImpersonationLogs",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationLogs_StartedAt",
                table: "ImpersonationLogs",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImpersonationLogs");
        }
    }
}

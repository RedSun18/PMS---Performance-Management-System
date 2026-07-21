using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerformanceManagement.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase11And12SettingsAndPreferredCulture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "EndYearAchievementStartDate",
                table: "SystemSettings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LanguageSelectionEnabled",
                table: "SystemSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MidYearAchievementStartDate",
                table: "SystemSettings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SubmitToHrStartDate",
                table: "SystemSettings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreferredCulture",
                table: "AppUsers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndYearAchievementStartDate",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "LanguageSelectionEnabled",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "MidYearAchievementStartDate",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "SubmitToHrStartDate",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "PreferredCulture",
                table: "AppUsers");
        }
    }
}

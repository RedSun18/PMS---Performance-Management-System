using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PerformanceManagement.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase9SettingsAndUserSecurity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccountLockoutMinutes",
                table: "SystemSettings",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<string>(
                name: "AnnouncementBanner",
                table: "SystemSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationName",
                table: "SystemSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyAddress",
                table: "SystemSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyLogoPath",
                table: "SystemSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompanyName",
                table: "SystemSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactEmail",
                table: "SystemSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentReviewYear",
                table: "SystemSettings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultUserPassword",
                table: "SystemSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnableAuditLogging",
                table: "SystemSettings",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndYearEnd",
                table: "SystemSettings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndYearStart",
                table: "SystemSettings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FooterText",
                table: "SystemSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LoginAsVerificationPasswordProtected",
                table: "SystemSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxLoginAttempts",
                table: "SystemSettings",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MidYearEnd",
                table: "SystemSettings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "MidYearStart",
                table: "SystemSettings",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinimumPasswordLength",
                table: "SystemSettings",
                type: "integer",
                nullable: false,
                defaultValue: 6);

            migrationBuilder.AddColumn<bool>(
                name: "PasswordComplexityRequired",
                table: "SystemSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PasswordExpiryDays",
                table: "SystemSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PrimaryColorHex",
                table: "SystemSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RememberMeDurationDays",
                table: "SystemSettings",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryColorHex",
                table: "SystemSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SessionTimeoutMinutes",
                table: "SystemSettings",
                type: "integer",
                nullable: false,
                defaultValue: 480);

            migrationBuilder.AddColumn<string>(
                name: "WelcomeMessage",
                table: "SystemSettings",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedLoginAttempts",
                table: "AppUsers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedOutUntil",
                table: "AppUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordChangedAt",
                table: "AppUsers",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccountLockoutMinutes",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "AnnouncementBanner",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ApplicationName",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "CompanyAddress",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "CompanyLogoPath",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "CompanyName",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "ContactEmail",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "CurrentReviewYear",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "DefaultUserPassword",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "EnableAuditLogging",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "EndYearEnd",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "EndYearStart",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "FooterText",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "LoginAsVerificationPasswordProtected",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "MaxLoginAttempts",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "MidYearEnd",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "MidYearStart",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "MinimumPasswordLength",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "PasswordComplexityRequired",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "PasswordExpiryDays",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "PrimaryColorHex",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "RememberMeDurationDays",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "SecondaryColorHex",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "SessionTimeoutMinutes",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "WelcomeMessage",
                table: "SystemSettings");

            migrationBuilder.DropColumn(
                name: "FailedLoginAttempts",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "LockedOutUntil",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "PasswordChangedAt",
                table: "AppUsers");
        }
    }
}

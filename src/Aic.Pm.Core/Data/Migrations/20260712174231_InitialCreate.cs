using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Aic.Pm.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserName = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    EmpCode = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    MustChangePassword = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompetencyMasters",
                columns: table => new
                {
                    CompId = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: true),
                    CompType = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    TypeDesc = table.Column<string>(type: "text", nullable: true),
                    TypeDescAr = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DescriptionAr = table.Column<string>(type: "text", nullable: true),
                    DeptCsv = table.Column<string>(type: "text", nullable: false),
                    DeptDesc = table.Column<string>(type: "text", nullable: true),
                    DeptDescAr = table.Column<string>(type: "text", nullable: true),
                    WeightRange = table.Column<string>(type: "text", nullable: true),
                    MinWeight = table.Column<int>(type: "integer", nullable: false),
                    MaxWeight = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ModifiedBy = table.Column<string>(type: "text", nullable: true),
                    ModifiedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ModifiedTime = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetencyMasters", x => x.CompId);
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Code = table.Column<string>(type: "text", nullable: false),
                    NameEn = table.Column<string>(type: "text", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Designations",
                columns: table => new
                {
                    Code = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    DescriptionAr = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Designations", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "EmailLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TemplateKey = table.Column<string>(type: "text", nullable: false),
                    FormLegacyRefNo = table.Column<string>(type: "text", nullable: true),
                    ToRecipients = table.Column<string>(type: "text", nullable: false),
                    CcRecipients = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeExceptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmpCode = table.Column<string>(type: "text", nullable: false),
                    RuleCode = table.Column<string>(type: "text", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeExceptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    EmpCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    LatinName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ArabicName = table.Column<string>(type: "text", nullable: true),
                    DesignationCode = table.Column<string>(type: "text", nullable: true),
                    DeptCode = table.Column<string>(type: "text", nullable: true),
                    SectionCode = table.Column<string>(type: "text", nullable: true),
                    Grade = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    JoinDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TermDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.EmpCode);
                });

            migrationBuilder.CreateTable(
                name: "JobFamilies",
                columns: table => new
                {
                    Code = table.Column<string>(type: "text", nullable: false),
                    NameEn = table.Column<string>(type: "text", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: true),
                    GradesCsv = table.Column<string>(type: "text", nullable: false),
                    KpiWeight = table.Column<int>(type: "integer", nullable: false),
                    CompWeight = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobFamilies", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "KpiMasters",
                columns: table => new
                {
                    KpiId = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: true),
                    Perspective = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    PerspectiveDesc = table.Column<string>(type: "text", nullable: true),
                    PerspectiveDescAr = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    DescriptionAr = table.Column<string>(type: "text", nullable: true),
                    Formula = table.Column<string>(type: "text", nullable: true),
                    FormulaAr = table.Column<string>(type: "text", nullable: true),
                    DeptCsv = table.Column<string>(type: "text", nullable: false),
                    DeptDesc = table.Column<string>(type: "text", nullable: true),
                    DeptDescAr = table.Column<string>(type: "text", nullable: true),
                    WeightRange = table.Column<string>(type: "text", nullable: true),
                    MinWeight = table.Column<int>(type: "integer", nullable: false),
                    MaxWeight = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ModifiedBy = table.Column<string>(type: "text", nullable: true),
                    ModifiedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ModifiedTime = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KpiMasters", x => x.KpiId);
                });

            migrationBuilder.CreateTable(
                name: "ManagerAssignments",
                columns: table => new
                {
                    EmpCode = table.Column<string>(type: "text", nullable: false),
                    ManagerEmpCode = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagerAssignments", x => x.EmpCode);
                });

            migrationBuilder.CreateTable(
                name: "PmForms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LegacyRefNo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EmpCode = table.Column<string>(type: "text", nullable: false),
                    EvalYear = table.Column<int>(type: "integer", nullable: false),
                    EmpNameSnapshot = table.Column<string>(type: "text", nullable: false),
                    DesignationSnapshot = table.Column<string>(type: "text", nullable: true),
                    DeptCode = table.Column<string>(type: "text", nullable: true),
                    SectionCode = table.Column<string>(type: "text", nullable: true),
                    ManagerEmpCode = table.Column<string>(type: "text", nullable: true),
                    GradeSnapshot = table.Column<string>(type: "text", nullable: true),
                    JoinDateSnapshot = table.Column<DateOnly>(type: "date", nullable: true),
                    LastReviewDate = table.Column<DateOnly>(type: "date", nullable: true),
                    JobFamily = table.Column<string>(type: "text", nullable: true),
                    KpiWeightTotal = table.Column<int>(type: "integer", nullable: false),
                    CompWeightTotal = table.Column<int>(type: "integer", nullable: false),
                    KpiScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    CompScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    PerformanceScore = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    OverallRatingCode = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PreviousStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    StatusChangeDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SelfAssessment = table.Column<string>(type: "text", nullable: true),
                    DevelopmentPlan = table.Column<string>(type: "text", nullable: true),
                    EmployeeSign = table.Column<string>(type: "text", nullable: true),
                    ManagerSign = table.Column<string>(type: "text", nullable: true),
                    EmpAckBy = table.Column<string>(type: "text", nullable: true),
                    EmpAckDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EmpAckSign = table.Column<string>(type: "text", nullable: true),
                    EmpAckComments = table.Column<string>(type: "text", nullable: true),
                    Hr1ReviewerName = table.Column<string>(type: "text", nullable: true),
                    Hr1ReviewDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Hr1Sign = table.Column<string>(type: "text", nullable: true),
                    Hr1Remarks = table.Column<string>(type: "text", nullable: true),
                    Hr2ReviewerName = table.Column<string>(type: "text", nullable: true),
                    Hr2ReviewDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Hr2Sign = table.Column<string>(type: "text", nullable: true),
                    Hr2Remarks = table.Column<string>(type: "text", nullable: true),
                    PromotionRecommendationValue = table.Column<string>(type: "text", nullable: true),
                    PromotionComments = table.Column<string>(type: "text", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastRemindedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PmForms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RatingScales",
                columns: table => new
                {
                    Code = table.Column<string>(type: "text", nullable: false),
                    NameEn = table.Column<string>(type: "text", nullable: false),
                    NameAr = table.Column<string>(type: "text", nullable: true),
                    MinScore = table.Column<int>(type: "integer", nullable: false),
                    MaxScore = table.Column<int>(type: "integer", nullable: false),
                    Remarks = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RatingScales", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "Sections",
                columns: table => new
                {
                    Code = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    DescriptionAr = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sections", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AppUserId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoles_AppUsers_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PmFormCompetencies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PmFormId = table.Column<int>(type: "integer", nullable: false),
                    RecordSeq = table.Column<int>(type: "integer", nullable: false),
                    LegacyRefNo = table.Column<string>(type: "text", nullable: true),
                    CompType = table.Column<string>(type: "text", nullable: false),
                    CompCode = table.Column<string>(type: "text", nullable: false),
                    CompName = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ItemWeight = table.Column<int>(type: "integer", nullable: false),
                    AchievementScore = table.Column<int>(type: "integer", nullable: false),
                    WeightedCalculation = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    Comments = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PmFormCompetencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PmFormCompetencies_PmForms_PmFormId",
                        column: x => x.PmFormId,
                        principalTable: "PmForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PmFormKpis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PmFormId = table.Column<int>(type: "integer", nullable: false),
                    RecordSeq = table.Column<int>(type: "integer", nullable: false),
                    LegacyRefNo = table.Column<string>(type: "text", nullable: true),
                    Perspective = table.Column<string>(type: "text", nullable: false),
                    KpiCode = table.Column<string>(type: "text", nullable: false),
                    KpiName = table.Column<string>(type: "text", nullable: false),
                    KpiDefinition = table.Column<string>(type: "text", nullable: true),
                    FormulaMetric = table.Column<string>(type: "text", nullable: true),
                    Target = table.Column<string>(type: "text", nullable: true),
                    ItemWeight = table.Column<int>(type: "integer", nullable: false),
                    AchievementScore = table.Column<int>(type: "integer", nullable: false),
                    WeightedCalculation = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    Comments = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PmFormKpis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PmFormKpis_PmForms_PmFormId",
                        column: x => x.PmFormId,
                        principalTable: "PmForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PmFormStatusHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PmFormId = table.Column<int>(type: "integer", nullable: false),
                    FromStatus = table.Column<string>(type: "text", nullable: true),
                    ToStatus = table.Column<string>(type: "text", nullable: false),
                    ChangedBy = table.Column<string>(type: "text", nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PmFormStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PmFormStatusHistory_PmForms_PmFormId",
                        column: x => x.PmFormId,
                        principalTable: "PmForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_UserName",
                table: "AppUsers",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailLogs_IdempotencyKey",
                table: "EmailLogs",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeExceptions_EmpCode_RuleCode",
                table: "EmployeeExceptions",
                columns: new[] { "EmpCode", "RuleCode" });

            migrationBuilder.CreateIndex(
                name: "IX_PmFormCompetencies_PmFormId_RecordSeq",
                table: "PmFormCompetencies",
                columns: new[] { "PmFormId", "RecordSeq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PmFormKpis_PmFormId_RecordSeq",
                table: "PmFormKpis",
                columns: new[] { "PmFormId", "RecordSeq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PmForms_EmpCode_EvalYear",
                table: "PmForms",
                columns: new[] { "EmpCode", "EvalYear" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PmForms_LegacyRefNo",
                table: "PmForms",
                column: "LegacyRefNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PmFormStatusHistory_PmFormId",
                table: "PmFormStatusHistory",
                column: "PmFormId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_AppUserId_Role",
                table: "UserRoles",
                columns: new[] { "AppUserId", "Role" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompetencyMasters");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "Designations");

            migrationBuilder.DropTable(
                name: "EmailLogs");

            migrationBuilder.DropTable(
                name: "EmployeeExceptions");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "JobFamilies");

            migrationBuilder.DropTable(
                name: "KpiMasters");

            migrationBuilder.DropTable(
                name: "ManagerAssignments");

            migrationBuilder.DropTable(
                name: "PmFormCompetencies");

            migrationBuilder.DropTable(
                name: "PmFormKpis");

            migrationBuilder.DropTable(
                name: "PmFormStatusHistory");

            migrationBuilder.DropTable(
                name: "RatingScales");

            migrationBuilder.DropTable(
                name: "Sections");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "PmForms");

            migrationBuilder.DropTable(
                name: "AppUsers");
        }
    }
}

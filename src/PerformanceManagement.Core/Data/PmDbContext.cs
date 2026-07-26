using PerformanceManagement.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Core.Data;

public class PmDbContext : DbContext
{
    public PmDbContext(DbContextOptions<PmDbContext> options) : base(options) { }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Designation> Designations => Set<Designation>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<JobFamily> JobFamilies => Set<JobFamily>();
    public DbSet<RatingScale> RatingScales => Set<RatingScale>();
    public DbSet<KpiMaster> KpiMasters => Set<KpiMaster>();
    public DbSet<CompetencyMaster> CompetencyMasters => Set<CompetencyMaster>();
    public DbSet<PmForm> PmForms => Set<PmForm>();
    public DbSet<PmFormKpi> PmFormKpis => Set<PmFormKpi>();
    public DbSet<PmFormCompetency> PmFormCompetencies => Set<PmFormCompetency>();
    public DbSet<PmFormStatusHistory> PmFormStatusHistory => Set<PmFormStatusHistory>();
    public DbSet<ManagerAssignment> ManagerAssignments => Set<ManagerAssignment>();
    public DbSet<EmployeeException> EmployeeExceptions => Set<EmployeeException>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();
    public DbSet<ImpersonationLog> ImpersonationLogs => Set<ImpersonationLog>();
    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ScheduledJobRun> ScheduledJobRuns => Set<ScheduledJobRun>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Employee>(e =>
        {
            e.HasKey(x => x.EmpCode);
            e.Property(x => x.EmpCode).HasMaxLength(10);
            e.Property(x => x.LatinName).HasMaxLength(100);
            e.Property(x => x.Grade).HasMaxLength(10);
            e.HasIndex(x => x.DeptCode);
        });

        b.Entity<Department>(e =>
        {
            e.HasKey(x => x.Code);
            e.Property(x => x.IsActive).HasDefaultValue(true);
        });
        b.Entity<Designation>().HasKey(x => x.Code);
        b.Entity<Section>().HasKey(x => x.Code);
        b.Entity<JobFamily>().HasKey(x => x.Code);
        b.Entity<RatingScale>().HasKey(x => x.Code);

        b.Entity<KpiMaster>(e =>
        {
            e.HasKey(x => x.KpiId);
            e.Property(x => x.KpiId).HasMaxLength(10);
            e.Property(x => x.Perspective).HasMaxLength(1);
        });

        b.Entity<CompetencyMaster>(e =>
        {
            e.HasKey(x => x.CompId);
            e.Property(x => x.CompId).HasMaxLength(10);
            e.Property(x => x.CompType).HasMaxLength(1);
        });

        b.Entity<PmForm>(e =>
        {
            e.HasIndex(x => new { x.EmpCode, x.EvalYear }).IsUnique();
            e.HasIndex(x => x.LegacyRefNo).IsUnique();
            // Dashboard/summary pages filter by year alone — (EmpCode, EvalYear) above doesn't
            // help since EvalYear isn't the leading column.
            e.HasIndex(x => x.EvalYear);
            // WorkflowAdminService.SearchAsync and PmFormSummary/Index both filter by
            // (year, status) together — the common "this year, this status" search pattern.
            e.HasIndex(x => new { x.EvalYear, x.Status });
            e.Property(x => x.LegacyRefNo).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.PreviousStatus).HasMaxLength(20);
            e.Property(x => x.KpiScore).HasPrecision(7, 2);
            e.Property(x => x.CompScore).HasPrecision(7, 2);
            e.Property(x => x.PerformanceScore).HasPrecision(7, 2);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasMany(x => x.Kpis).WithOne(x => x.PmForm).HasForeignKey(x => x.PmFormId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Competencies).WithOne(x => x.PmForm).HasForeignKey(x => x.PmFormId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.History).WithOne(x => x.PmForm).HasForeignKey(x => x.PmFormId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PmFormKpi>(e =>
        {
            e.HasIndex(x => new { x.PmFormId, x.RecordSeq }).IsUnique();
            e.Property(x => x.WeightedCalculation).HasPrecision(7, 2);
        });

        b.Entity<PmFormCompetency>(e =>
        {
            e.HasIndex(x => new { x.PmFormId, x.RecordSeq }).IsUnique();
            e.Property(x => x.WeightedCalculation).HasPrecision(7, 2);
        });

        b.Entity<ManagerAssignment>(e =>
        {
            e.HasKey(x => x.EmpCode);
            // Every manager-filtered search/report (WorkflowAdminService, PmFormSummary,
            // WorkflowAdmin/Index, Reports/Index, ReportDataService) queries this column, not
            // the primary key.
            e.HasIndex(x => x.ManagerEmpCode);
        });

        b.Entity<EmployeeException>(e =>
        {
            e.HasIndex(x => new { x.EmpCode, x.RuleCode });
        });

        b.Entity<AppUser>(e =>
        {
            e.HasIndex(x => x.UserName).IsUnique();
            e.HasIndex(x => x.EmpCode);
            e.HasMany(x => x.RolesList).WithOne(x => x.AppUser).HasForeignKey(x => x.AppUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserRole>().HasIndex(x => new { x.AppUserId, x.Role }).IsUnique();

        b.Entity<EmailLog>(e =>
        {
            e.HasIndex(x => x.IdempotencyKey);
            e.HasIndex(x => x.FormLegacyRefNo);
        });

        b.Entity<ImpersonationLog>(e =>
        {
            e.HasIndex(x => x.SessionId).IsUnique();
            e.HasIndex(x => x.StartedAt);
        });

        b.Entity<AuditLog>(e =>
        {
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => x.EmpCode);
            e.HasIndex(x => x.DeptCode);
            e.HasIndex(x => x.Action);
            // AuditService.SearchAsync filters on exactly this pair, and WorkflowAdmin/Details
            // hits it on every page load (EntityType: "PmForm", EntityId: form.Id) — an
            // ever-growing, never-pruned table (see docs/operations.md), so this needs to be an
            // index from the start rather than retrofitted once it's already large.
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        b.Entity<ScheduledJobRun>(e =>
        {
            e.HasIndex(x => new { x.JobName, x.StartedAt });
        });

        b.Entity<Notification>(e =>
        {
            e.HasIndex(x => new { x.UserName, x.IsRead, x.CreatedAt });
        });

        // Singleton row: Id is always 1, set explicitly by SettingsService (never DB-generated).
        // Every non-zero/non-false default is spelled out explicitly — AddColumn/AlterColumn
        // migrations don't infer a DB-side default from a C# property initializer (only
        // CreateTable does), so leaving these off would silently reset existing rows to
        // 0/false the moment a new column is added (see AddDepartmentDescriptionAndIsActive).
        b.Entity<SystemSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.MinimumPasswordLength).HasDefaultValue(6);
            e.Property(x => x.SessionTimeoutMinutes).HasDefaultValue(480);
            e.Property(x => x.MaxLoginAttempts).HasDefaultValue(5);
            e.Property(x => x.EnableAuditLogging).HasDefaultValue(true);
            e.Property(x => x.AccountLockoutMinutes).HasDefaultValue(15);
            e.Property(x => x.RememberMeDurationDays).HasDefaultValue(30);
        });
    }
}

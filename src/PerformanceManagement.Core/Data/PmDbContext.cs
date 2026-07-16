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

        b.Entity<Department>().HasKey(x => x.Code);
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

        b.Entity<ManagerAssignment>().HasKey(x => x.EmpCode);

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

        // Singleton row: Id is always 1, set explicitly by SettingsService (never DB-generated).
        b.Entity<SystemSettings>(e => e.Property(x => x.Id).ValueGeneratedNever());
    }
}

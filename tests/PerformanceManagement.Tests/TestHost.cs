using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace PerformanceManagement.Tests;

public class FakeClock : IClock
{
    public DateOnly Today { get; set; } = new(2026, 6, 15);
    public DateTime Now { get; set; } = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
}

/// <summary>No-op stand-in for the real key-ring-backed Data Protection provider — tests
/// don't exercise encryption itself, only that a password round-trips through SettingsService.</summary>
public class FakeDataProtectionProvider : IDataProtectionProvider
{
    public IDataProtector CreateProtector(string purpose) => new FakeDataProtector();

    private class FakeDataProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;
        public byte[] Protect(byte[] plaintext) => plaintext;
        public byte[] Unprotect(byte[] protectedData) => protectedData;
    }
}

/// <summary>SQLite-backed test host with the standard seed fixture.</summary>
public sealed class TestHost : IDisposable
{
    private readonly SqliteConnection _conn;
    public PmDbContext Db { get; }
    public FakeClock Clock { get; } = new();
    public AchievementGate Gate { get; }
    public PermissionService Permissions { get; }
    public RatingService Ratings { get; }
    public JobFamilyService JobFamilies { get; }
    public SettingsService Settings { get; }
    public FormLinkService Links { get; }
    public EmailService Email { get; }
    public WorkflowService Workflow { get; }

    public TestHost()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Db = NewContext();
        Db.Database.EnsureCreated();

        Gate = new AchievementGate(Clock);
        Permissions = new PermissionService(Db, Clock);
        Ratings = new RatingService(Db);
        JobFamilies = new JobFamilyService(Db, Clock);
        // Empty configuration ⇒ SettingsService.GetSmtpCredentialsAsync() returns null ⇒
        // EmailService logs (Status=LOGGED) instead of attempting a real SMTP send.
        Settings = new SettingsService(Db, new ConfigurationBuilder().Build(), new FakeDataProtectionProvider());
        Links = new FormLinkService(new FakeDataProtectionProvider(), Settings, Clock);
        Email = new EmailService(Db, Clock, Settings, NullLogger<EmailService>.Instance);
        Workflow = new WorkflowService(Db, Clock, Gate, Permissions, Email, Ratings, Links);
    }

    public PmDbContext NewContext() =>
        new(new DbContextOptionsBuilder<PmDbContext>().UseSqlite(_conn).Options);

    public async Task SeedAsync()
    {
        await DatabaseSeeder.SeedCoreAsync(Db);

        // Rating scales as exported from reference (ADM/KPI subtype R)
        Db.RatingScales.AddRange(
            new RatingScale { Code = "1", NameEn = "Unsatisfactory", MinScore = 1, MaxScore = 59 },
            new RatingScale { Code = "2", NameEn = "Needs Improvement", MinScore = 60, MaxScore = 79 },
            new RatingScale { Code = "3", NameEn = "Meets Expectations", MinScore = 80, MaxScore = 89 },
            new RatingScale { Code = "4", NameEn = "Exceed Expectations", MinScore = 90, MaxScore = 94 },
            new RatingScale { Code = "5", NameEn = "Exceptional", MinScore = 95, MaxScore = 100 },
            new RatingScale { Code = "6", NameEn = "Pending", MinScore = 0, MaxScore = 0 });

        // Job families as exported (grades list → KPI/COMP split)
        Db.JobFamilies.AddRange(
            new JobFamily { Code = "JF001", NameEn = "Executive Leadership", GradesCsv = "0", KpiWeight = 80, CompWeight = 20 },
            new JobFamily { Code = "JF002", NameEn = "Senior Management", GradesCsv = "9", KpiWeight = 70, CompWeight = 30 },
            new JobFamily { Code = "JF003", NameEn = "Middle Management", GradesCsv = "6,7,8", KpiWeight = 60, CompWeight = 40 },
            new JobFamily { Code = "JF004", NameEn = "Specialists & Professionals", GradesCsv = "4,5", KpiWeight = 0, CompWeight = 100 },
            new JobFamily { Code = "JF005", NameEn = "Entry Level & Support Roles", GradesCsv = "2,3", KpiWeight = 0, CompWeight = 100 },
            new JobFamily { Code = "JF006", NameEn = "Others", GradesCsv = "1", KpiWeight = 0, CompWeight = 100 });

        // Employees: manager 854 (per HR map manages 1353/1495/1504), staff 1504 grade 7
        // (KPI-required for tests), exception employees 1058/1470, branch employee 1370.
        Db.Employees.AddRange(
            new Employee { EmpCode = "854", LatinName = "Manager EightFiveFour", DeptCode = "MAR", Grade = "8", Email = "854@test.local" },
            new Employee { EmpCode = "1504", LatinName = "Employee 1504", DeptCode = "MAR", Grade = "7", Email = "1504@test.local" },
            new Employee { EmpCode = "1058", LatinName = "Ahmad Fathi", DeptCode = "AC", Grade = "7", Email = "1058@test.local" },
            new Employee { EmpCode = "548", LatinName = "Manager 548", DeptCode = "AC", Grade = "8", Email = "548@test.local" },
            new Employee { EmpCode = "656", LatinName = "SelfManaged 656", DeptCode = "MT", Grade = "8", Email = "656@test.local" },
            new Employee { EmpCode = "1541", LatinName = "Branch Viewer 1541", DeptCode = "BDM", Grade = "6", Email = "1541@test.local" },
            new Employee { EmpCode = "1370", LatinName = "Branch Employee 1370", DeptCode = "PRO", SectionCode = "BR", Grade = "4", Email = "1370@test.local" });

        // KPI masters spanning 3 perspectives + a competency master
        Db.KpiMasters.AddRange(
            new KpiMaster { KpiId = "KPI001", Name = "Claims Cost Reduction", Perspective = "F", MinWeight = 10, MaxWeight = 30, DeptCsv = "*" },
            new KpiMaster { KpiId = "KPI002", Name = "Customer Satisfaction", Perspective = "C", MinWeight = 10, MaxWeight = 30, DeptCsv = "*" },
            new KpiMaster { KpiId = "KPI003", Name = "Process Efficiency", Perspective = "I", MinWeight = 10, MaxWeight = 30, DeptCsv = "*" },
            new KpiMaster { KpiId = "KPI004", Name = "Training Hours", Perspective = "L", MinWeight = 10, MaxWeight = 30, DeptCsv = "*" });
        Db.CompetencyMasters.AddRange(
            new CompetencyMaster { CompId = "COM001", Name = "Analytical Thinking", CompType = "B", MinWeight = 10, MaxWeight = 40 },
            new CompetencyMaster { CompId = "COM002", Name = "Problem-Solving", CompType = "B", MinWeight = 10, MaxWeight = 40 },
            new CompetencyMaster { CompId = "COM003", Name = "Risk Management", CompType = "T", MinWeight = 10, MaxWeight = 40 });

        await Db.SaveChangesAsync();
    }

    /// <summary>Permissions of user u{empCode} (employee account) toward target employee.</summary>
    public Task<FormPermissions> PermsAsync(string userEmpCode, string targetEmpCode, string? userName = null) =>
        Permissions.GetFormPermissionsAsync(userName ?? $"u{userEmpCode}", userEmpCode, targetEmpCode);

    /// <summary>
    /// Seeds an ad-hoc HR admin test account, decoupled from production seed data (which
    /// now seeds only a single configurable "admin" account — see DatabaseSeeder). Tests
    /// that need two distinct HR admins (segregation of duties) call this twice.
    /// </summary>
    public async Task AddHrAdminAsync(string userName)
    {
        var user = new AppUser
        {
            UserName = userName,
            DisplayName = $"HR Admin ({userName})",
            EmpCode = userName,
            PasswordHash = "test-fixture-no-login"
        };
        user.RolesList.Add(new UserRole { Role = Roles.HrAdmin });
        Db.AppUsers.Add(user);
        await Db.SaveChangesAsync();
    }

    /// <summary>Standard complete content for employee 1504 (grade 7 → 60/40): 4 KPIs / 3 comps.</summary>
    public WorkflowService.PmFormContent Content1504(int achievement = 0, int? year = null) =>
        new("1504", year ?? Clock.Today.Year, "Employee 1504", "TL", "MAR", null, "854", "7", null,
            "Middle Management", 60, 40, "self", "plan", "1504", "854", null, null,
            new List<PmFormKpi>
            {
                new() { RecordSeq = 1, Perspective = "F", KpiCode = "KPI001", KpiName = "Claims Cost Reduction", ItemWeight = 30, AchievementScore = achievement },
                new() { RecordSeq = 2, Perspective = "C", KpiCode = "KPI002", KpiName = "Customer Satisfaction", ItemWeight = 30, AchievementScore = achievement },
                new() { RecordSeq = 3, Perspective = "I", KpiCode = "KPI003", KpiName = "Process Efficiency", ItemWeight = 20, AchievementScore = achievement },
                new() { RecordSeq = 4, Perspective = "L", KpiCode = "KPI004", KpiName = "Training Hours", ItemWeight = 20, AchievementScore = achievement }
            },
            new List<PmFormCompetency>
            {
                new() { RecordSeq = 1, CompType = "B", CompCode = "COM001", CompName = "Analytical Thinking", ItemWeight = 40, AchievementScore = achievement },
                new() { RecordSeq = 2, CompType = "B", CompCode = "COM002", CompName = "Problem-Solving", ItemWeight = 30, AchievementScore = achievement },
                new() { RecordSeq = 3, CompType = "T", CompCode = "COM003", CompName = "Risk Management", ItemWeight = 30, AchievementScore = achievement }
            });

    public void Dispose()
    {
        Db.Dispose();
        _conn.Dispose();
    }
}

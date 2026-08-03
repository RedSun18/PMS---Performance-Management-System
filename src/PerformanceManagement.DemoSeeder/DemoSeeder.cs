using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace PerformanceManagement.DemoSeeder;

/// <summary>A no-op data protector — the Demo environment never stores a real SMTP password or
/// a non-default Login-As verification password via this seeder, so nothing sensitive is ever
/// actually protected/unprotected through it.</summary>
public sealed class PassthroughDataProtectionProvider : IDataProtectionProvider
{
    public IDataProtector CreateProtector(string purpose) => new PassthroughProtector();
    private sealed class PassthroughProtector : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => this;
        public byte[] Protect(byte[] plaintext) => plaintext;
        public byte[] Unprotect(byte[] protectedData) => protectedData;
    }
}

public record DemoSeedSummary(
    int Departments, int Designations, int Sections, int JobFamilies, int RatingScales,
    int Kpis, int Competencies, int Employees, int Managers, int ManagerAssignments,
    int Exceptions, int Users, int Forms, int WorkflowAdminActions);

/// <summary>
/// Populates a fresh, empty Demo database with entirely fictional "Apex Corporation"
/// data — realistic in shape and volume, but no name, code, or figure here corresponds to
/// any real company or person. Drives every PM Form through the real
/// <see cref="WorkflowService"/>/<see cref="WorkflowAdminService"/> (not raw row inserts) so
/// the resulting data is exactly as internally consistent as data produced by real use of the
/// application — same validation, same scoring, same audit trail. Fully deterministic: every
/// random choice below comes from one <see cref="Random"/> seeded with <see cref="Seed"/>, in a
/// fixed call order, so re-running against a fresh database always produces byte-identical data.
/// </summary>
public sealed class DemoSeeder
{
    public const int Seed = 20260101;

    private readonly PmDbContext _db;
    private readonly SeederClock _clock = new();
    private readonly Random _rng = new(Seed);
    private readonly SettingsService _settings;
    private readonly AchievementGate _gate;
    private readonly PermissionService _permissions;
    private readonly RatingService _ratings;
    private readonly FormLinkService _links;
    private readonly EmailService _email;
    private readonly NotificationService _notifications;
    private readonly AuditService _audit;
    private readonly WorkflowService _workflow;
    private readonly WorkflowAdminService _workflowAdmin;

    private const string AdminUserName = "admin";
    private int _workflowAdminActionCount;

    public DemoSeeder(PmDbContext db)
    {
        _db = db;
        var protector = new PassthroughDataProtectionProvider();
        var config = new ConfigurationBuilder().Build();
        _settings = new SettingsService(_db, config, protector);
        _gate = new AchievementGate(_clock, _settings);
        _permissions = new PermissionService(_db, _clock);
        _ratings = new RatingService(_db);
        _links = new FormLinkService(protector, _settings, _clock);
        _email = new EmailService(_db, _clock, _settings, NullLogger<EmailService>.Instance);
        _notifications = new NotificationService(_db, _clock);
        _audit = new AuditService(_db, _clock, _settings);
        _workflow = new WorkflowService(_db, _clock, _gate, _permissions, _email, _ratings, _links, _notifications, _settings);
        _workflowAdmin = new WorkflowAdminService(_db, _workflow, _audit, NullLogger<WorkflowAdminService>.Instance);
    }

    public async Task<DemoSeedSummary> SeedAllAsync()
    {
        if (await _db.Employees.AnyAsync())
            throw new InvalidOperationException(
                "The Demo database already has employee data. Pass --reset to wipe demo tables and reseed from scratch.");

        await SeedSystemSettingsAsync();
        var ratingCount = await SeedRatingScalesAsync();
        var deptCount = await SeedDepartmentsAsync();
        var (desigCount, sectionCount) = await SeedDesignationsAndSectionsAsync();
        var jobFamilyCount = await SeedJobFamiliesAsync();
        var kpiCount = await SeedKpisAsync();
        var compCount = await SeedCompetenciesAsync();

        var employees = await SeedEmployeesAsync();
        var managerCodes = await SeedManagerAssignmentsAsync(employees);
        var exceptionCount = await SeedExceptionsAsync(employees);
        var userCount = await SeedUsersAsync(employees, managerCodes);
        var formCount = await SeedPerformanceDataAsync(employees, managerCodes);

        var managerAssignmentCount = await _db.ManagerAssignments.CountAsync();
        return new DemoSeedSummary(deptCount, desigCount, sectionCount, jobFamilyCount, ratingCount,
            kpiCount, compCount, employees.Count, managerCodes.Count, managerAssignmentCount,
            exceptionCount, userCount, formCount, _workflowAdminActionCount);
    }

    // ================================================================ Reference data

    private async Task SeedSystemSettingsAsync()
    {
        // Direct row creation (rather than relying on SettingsService's config-driven first-run
        // seed) so the Demo branding is guaranteed correct regardless of whether this seeder or
        // the web app's own first request happens to touch the settings row first.
        var row = new SystemSettings
        {
            Id = 1,
            ApplicationName = "Performance Management System",
            CompanyName = "Apex Corporation",
            CompanyAddress = "1 Summit Plaza, Springfield, ST 00000",
            ContactEmail = "hr@apexcorp.demo",
            // The real public Demo URL, not a local dev placeholder — this value is baked into
            // every notification/email deep-link generated from it (FormLinkService), so a wrong
            // value here means every one of those links is broken on the live site. Local testing
            // of a freshly-seeded Demo database can still override this via Settings > General
            // if link-following matters for that session.
            ApplicationBaseUrl = "https://pms.aryanb.dev",
            CompanyLogoPath = "/images/demo/apex-logo.svg",
            PrimaryColorHex = "#0f2b5c",
            SecondaryColorHex = "#1e3a8a",
            FooterText = "© 2026 Apex Corporation — Demo Environment. All data shown is fictional.",
            WelcomeMessage = "Welcome to the Apex Corporation performance portal. HR training available Friday.",
            AnnouncementBanner = "Annual Review Cycle Open — performance calibration starts next week.",
            SenderName = "Apex Corporation",
            SenderEmail = "hr@apexcorp.demo",
            EnableEmailNotifications = true,
            EnableAuditLogging = true,
            CurrentReviewYear = 2026,
            // Deliberately left null: AchievementGate falls back to "1 December of the review
            // year itself" per evalYear when these are unset, which is exactly what lets the
            // seeder drive 2024/2025/2026 forms through their respective gates by simulating
            // each year's own December rather than one fixed absolute date across all years.
        };
        _db.SystemSettings.Add(row);
        await _db.SaveChangesAsync();
    }

    private async Task<int> SeedRatingScalesAsync()
    {
        foreach (var (code, name, min, max) in DemoReferenceData.RatingScales)
            _db.RatingScales.Add(new RatingScale { Code = code, NameEn = name, MinScore = min, MaxScore = max, Status = "A" });
        await _db.SaveChangesAsync();
        return DemoReferenceData.RatingScales.Length;
    }

    private async Task<int> SeedDepartmentsAsync()
    {
        foreach (var (code, name) in DemoReferenceData.Departments)
            _db.Departments.Add(new Department { Code = code, NameEn = name, IsActive = true });
        await _db.SaveChangesAsync();
        return DemoReferenceData.Departments.Length;
    }

    private async Task<(int, int)> SeedDesignationsAndSectionsAsync()
    {
        foreach (var (code, desc) in DemoReferenceData.Designations)
            _db.Designations.Add(new Designation { Code = code, Description = desc });
        foreach (var (code, desc) in DemoReferenceData.Sections)
            _db.Sections.Add(new Section { Code = code, Description = desc });
        await _db.SaveChangesAsync();
        return (DemoReferenceData.Designations.Length, DemoReferenceData.Sections.Length);
    }

    private async Task<int> SeedJobFamiliesAsync()
    {
        foreach (var (code, name, gradesCsv, kpiWeight, compWeight) in DemoReferenceData.JobFamilies)
            _db.JobFamilies.Add(new JobFamily
            {
                Code = code, NameEn = name, GradesCsv = gradesCsv,
                KpiWeight = kpiWeight, CompWeight = compWeight, Status = "A"
            });
        await _db.SaveChangesAsync();
        return DemoReferenceData.JobFamilies.Length;
    }

    private async Task<int> SeedKpisAsync()
    {
        foreach (var (id, name, perspective) in DemoReferenceData.Kpis)
            _db.KpiMasters.Add(new KpiMaster
            {
                KpiId = id, Name = name, Perspective = perspective, DeptCsv = "*",
                MinWeight = 10, MaxWeight = 25, Status = "A"
            });
        await _db.SaveChangesAsync();
        return DemoReferenceData.Kpis.Length;
    }

    private async Task<int> SeedCompetenciesAsync()
    {
        foreach (var (id, name, type) in DemoReferenceData.Competencies)
            _db.CompetencyMasters.Add(new CompetencyMaster
            {
                CompId = id, Name = name, CompType = type, DeptCsv = "*",
                MinWeight = 10, MaxWeight = 25, Status = "A"
            });
        await _db.SaveChangesAsync();
        return DemoReferenceData.Competencies.Length;
    }

    // ================================================================ Employees & org structure

    /// <summary>~50 employees: 5 executives (grade 1, no manager), 16 department managers
    /// (grade 2 — 1 per department, 2 for HR), ~29 staff (grades 3–8) spread across
    /// departments.</summary>
    private async Task<List<Employee>> SeedEmployeesAsync()
    {
        var employees = new List<Employee>();
        var empCodeSeq = 1001;
        var nameIndex = 0;

        (string First, string Last) NextName()
        {
            var group = nameIndex % DemoReferenceData.FirstNamesByGroup.Length;
            var firstPool = DemoReferenceData.FirstNamesByGroup[group];
            var lastPool = DemoReferenceData.LastNamesByGroup[group];
            var i = nameIndex / DemoReferenceData.FirstNamesByGroup.Length;
            var first = firstPool[i % firstPool.Length];
            var last = lastPool[(i / firstPool.Length + i) % lastPool.Length];
            nameIndex++;
            return (first, last);
        }

        DateOnly JoinDateFor(int index)
        {
            // Spread join dates across 2015–2025 deterministically — later-added employees
            // (higher index) skew toward more recent join dates, like a growing organization.
            var yearsAgo = 10 - (index % 11); // 10..0 years before 2025-ish
            var year = 2025 - yearsAgo;
            var month = 1 + (index * 7 % 12);
            var day = 1 + (index * 11 % 28);
            return new DateOnly(year, month, day);
        }

        // Round-robins each department's own 2 topical sections (DemoReferenceData.SectionsByDept)
        // across its employees, so Department/Section/Designation read as a coherent, realistic
        // structure (e.g. IT staff land in Software Development or Infrastructure & Support)
        // without an actual Department→Section foreign key — Section stays the flat, standalone
        // reference table it always was.
        var sectionIndexByDept = new Dictionary<string, int>();

        Employee MakeEmployee(string grade, string deptCode, string designationCode)
        {
            var (first, last) = NextName();
            var code = (empCodeSeq++).ToString();
            var sections = DemoReferenceData.SectionsByDept[deptCode];
            var sIdx = sectionIndexByDept.TryGetValue(deptCode, out var n) ? n : 0;
            sectionIndexByDept[deptCode] = sIdx + 1;
            var emp = new Employee
            {
                EmpCode = code,
                LatinName = $"{first} {last}",
                DesignationCode = designationCode,
                DeptCode = deptCode,
                SectionCode = sections[sIdx % sections.Length].Code,
                Grade = grade,
                JoinDate = JoinDateFor(empCodeSeq),
                Email = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}@apexcorp.demo",
                Source = "MANUAL",
            };
            employees.Add(emp);
            _db.Employees.Add(emp);
            return emp;
        }

        // 5 executives — spread across a handful of departments, no manager assignment.
        var execDepts = new[] { "STR", "FIN", "OPS", "RSK", "HRD" };
        foreach (var dept in execDepts)
            MakeEmployee(grade: "1", deptCode: dept, designationCode: "CO");

        // 1 manager per department, except HR which gets 2 — HR needs two independent grade-2
        // employees so the two HR-Reviewer roles (segregation of duties) are genuinely different
        // people, exactly like a real company's HR team would have more than one reviewer.
        foreach (var (deptCode, _) in DemoReferenceData.Departments)
        {
            MakeEmployee(grade: "2", deptCode, designationCode: "SMGR");
            if (deptCode == "HRD")
                MakeEmployee(grade: "2", deptCode, designationCode: "MGR");
        }

        // Remaining staff up to ~50 total, grades 3–8, spread across departments.
        var staffDesignations = new[] { "SAN", "AN", "CRD", "ASO" };
        var staffGrades = new[] { "3", "4", "5", "6", "6", "7", "8" };
        var targetTotal = 50;
        var i2 = 0;
        while (employees.Count < targetTotal)
        {
            var dept = DemoReferenceData.Departments[i2 % DemoReferenceData.Departments.Length].Code;
            var grade = staffGrades[i2 % staffGrades.Length];
            var designation = staffDesignations[i2 % staffDesignations.Length];
            MakeEmployee(grade, dept, designation);
            i2++;
        }

        await _db.SaveChangesAsync();
        return employees;
    }

    /// <summary>Executives get no manager row (top of hierarchy). Each department's 2 managers
    /// report round-robin to one of the 5 executives. Each department's staff report round-robin
    /// to one of that department's own 2 managers.</summary>
    private async Task<List<string>> SeedManagerAssignmentsAsync(List<Employee> employees)
    {
        var executives = employees.Where(e => e.Grade == "1").ToList();
        var managers = employees.Where(e => e.Grade == "2").ToList();
        var managerCodes = managers.Select(m => m.EmpCode).ToList();

        for (var i = 0; i < managers.Count; i++)
        {
            var exec = executives[i % executives.Count];
            _db.ManagerAssignments.Add(new ManagerAssignment
            {
                EmpCode = managers[i].EmpCode, ManagerEmpCode = exec.EmpCode,
                Source = "HR_LIST", Note = "Apex Corporation organizational chart (demo data)."
            });
        }

        foreach (var deptGroup in employees.Where(e => e.Grade is not "1" and not "2").GroupBy(e => e.DeptCode))
        {
            var deptManagers = managers.Where(m => m.DeptCode == deptGroup.Key).ToList();
            if (deptManagers.Count == 0) continue;
            var staffList = deptGroup.ToList();
            for (var i = 0; i < staffList.Count; i++)
            {
                var mgr = deptManagers[i % deptManagers.Count];
                staffList[i].DeptCode = deptGroup.Key; // unchanged, kept explicit for clarity
                _db.ManagerAssignments.Add(new ManagerAssignment
                {
                    EmpCode = staffList[i].EmpCode, ManagerEmpCode = mgr.EmpCode,
                    Source = "HR_LIST", Note = "Apex Corporation organizational chart (demo data)."
                });
            }
        }

        await _db.SaveChangesAsync();
        return managerCodes;
    }

    /// <summary>A small, believable set of exceptions mirroring the real rule types, on
    /// fictional employees — demonstrates the same business rules without reusing real data.</summary>
    private async Task<int> SeedExceptionsAsync(List<Employee> employees)
    {
        var count = 0;
        var lowGrade = employees.Where(e => e.Grade is "7" or "8").ToList();
        if (lowGrade.Count >= 3)
        {
            _db.EmployeeExceptions.Add(new EmployeeException
            {
                EmpCode = lowGrade[0].EmpCode, RuleCode = ExceptionRule.PerspectiveMinExempt,
                Reason = "Approved temporary exception — role does not span 3 KPI perspectives."
            });
            _db.EmployeeExceptions.Add(new EmployeeException
            {
                EmpCode = lowGrade[1].EmpCode, RuleCode = ExceptionRule.Kpi5050,
                Reason = "50/50 KPI-competency mix exception."
            });
            _db.EmployeeExceptions.Add(new EmployeeException
            {
                EmpCode = lowGrade[2].EmpCode, RuleCode = ExceptionRule.BranchViewer,
                Reason = "Temporary view-only access to branch employee forms."
            });
            count = 3;
        }
        await _db.SaveChangesAsync();
        return count;
    }

    // ================================================================ Users

    private async Task<int> SeedUsersAsync(List<Employee> employees, List<string> managerCodes)
    {
        // HR Administrator role: the primary "admin" account plus 2 real fictional employees
        // (the HR department's own 2 managers) — this gives HR-Review a genuine second,
        // independent reviewer for the segregation-of-duties rule, using real seeded people
        // rather than a second bespoke account.
        await DatabaseSeeder.SeedCoreAsync(_db, AdminUserName, "Admin@123", seedLegacyReferenceData: false);

        var hrManagers = employees.Where(e => e.DeptCode == "HRD" && e.Grade == "2").ToList();
        var hr1 = hrManagers[0];
        var hr2 = hrManagers[1];
        await GrantHrAdminAsync(hr1.EmpCode, hr1.LatinName, hr1.Email);
        await GrantHrAdminAsync(hr2.EmpCode, hr2.LatinName, hr2.Email);
        Hr1EmpCode = hr1.EmpCode;
        Hr2EmpCode = hr2.EmpCode;

        // Named "manager"/"employee" demo accounts (spec-required simple credentials) — pointed
        // at one real, genuinely-related staff/manager pair so logging in immediately shows a
        // real, populated form from both sides of the relationship.
        var demoStaff = employees.First(e => e.Grade is "5" or "6");
        var demoManagerCode = (await _db.ManagerAssignments.AsNoTracking()
            .FirstAsync(m => m.EmpCode == demoStaff.EmpCode)).ManagerEmpCode;
        DemoEmployeeEmpCode = demoStaff.EmpCode;
        DemoManagerEmpCode = demoManagerCode;

        await AddNamedAccountAsync("employee", "Demo@123", demoStaff.EmpCode, demoStaff.LatinName, demoStaff.Email);
        await AddNamedAccountAsync("manager", "Demo@123", demoManagerCode,
            employees.First(e => e.EmpCode == demoManagerCode).LatinName,
            employees.First(e => e.EmpCode == demoManagerCode).Email);

        var perEmployeeCount = await DatabaseSeeder.SeedUsersForEmployeesAsync(_db);
        return perEmployeeCount + 3; // + admin + manager + employee (HR-admin grants reuse existing per-employee accounts)
    }

    public string Hr1EmpCode { get; private set; } = "";
    public string Hr2EmpCode { get; private set; } = "";
    public string DemoEmployeeEmpCode { get; private set; } = "";
    public string DemoManagerEmpCode { get; private set; } = "";

    private async Task GrantHrAdminAsync(string empCode, string displayName, string? email)
    {
        var userName = empCode.Trim().PadLeft(4, '0');
        var user = await _db.AppUsers.Include(u => u.RolesList).FirstOrDefaultAsync(u => u.UserName == userName);
        if (user is null)
        {
            user = new AppUser
            {
                UserName = userName, DisplayName = displayName, EmpCode = empCode, Email = email,
                MustChangePassword = false
            };
            user.PasswordHash = DatabaseSeeder.HashPassword(user, "Demo@123");
            _db.AppUsers.Add(user);
        }
        if (!user.RolesList.Any(r => r.Role == Roles.HrAdmin))
            user.RolesList.Add(new UserRole { Role = Roles.HrAdmin });
        await _db.SaveChangesAsync();
    }

    private async Task AddNamedAccountAsync(string userName, string password, string empCode, string displayName, string? email)
    {
        if (await _db.AppUsers.AnyAsync(u => u.UserName == userName)) return;
        var user = new AppUser
        {
            UserName = userName, DisplayName = displayName, EmpCode = empCode, Email = email,
            MustChangePassword = false
        };
        user.PasswordHash = DatabaseSeeder.HashPassword(user, password);
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
    }

    // ================================================================ Performance data (PmForms)

    private enum Bucket { Draft, MidYear, HrReview, Completed, Returned, Reopened }

    /// <summary>Fixed percentages for the current (2026) review year — not uniform random —
    /// applied via the seeder's single seeded RNG stream so the same employee always lands in
    /// the same bucket on every run. 2024/2025 are fully closed cycles (100% Completed).</summary>
    private Bucket PickBucket()
    {
        var roll = _rng.NextDouble() * 100.0;
        return roll switch
        {
            < 70.0 => Bucket.Completed,        // 70%
            < 80.0 => Bucket.HrReview,         // 10%
            < 87.0 => Bucket.MidYear,          //  7%
            < 92.0 => Bucket.Draft,            //  5%
            < 97.0 => Bucket.Returned,         //  5%
            _ => Bucket.Reopened,              //  3%
        };
    }

    /// <summary>Believable rating distribution for a "Completed" form — mostly Good/Very Good,
    /// some Outstanding, a few Needs Improvement (never uniformly perfect).</summary>
    private int PickTargetScore()
    {
        var roll = _rng.NextDouble() * 100.0;
        return roll switch
        {
            < 10.0 => 90 + _rng.Next(0, 10),   // Outstanding  90–99  (10%)
            < 45.0 => 80 + _rng.Next(0, 9),    // Very Good    80–88  (35%)
            < 90.0 => 66 + _rng.Next(0, 13),   // Good         66–78  (45%)
            _ => 48 + _rng.Next(0, 15),        // Needs Improvement 48–62 (10%)
        };
    }

    /// <summary>Per-item achievement scores that average to <paramref name="target"/> exactly
    /// (sum-zero jitter), so the item-level numbers look like real distinct entries rather than
    /// N identical rows, while the resulting overall PerformanceScore still lands in the
    /// intended rating band deterministically.</summary>
    private static int[] JitteredScores(int target, int n)
    {
        var pattern = new[] { 3, -3, 2, -2, 1, -1, 0, 0 };
        var offsets = new int[n];
        for (var i = 0; i < n; i++) offsets[i] = pattern[i % pattern.Length];
        offsets[n - 1] -= offsets.Sum();
        return offsets.Select(o => Math.Clamp(target + o, 1, 100)).ToArray();
    }

    private WorkflowService.PmFormContent BuildContent(Employee emp, string? managerEmpCode,
        JobFamilyService.JobFamilyWeights weights, int? achievementTarget, int evalYear)
    {
        var kpiPicks = new[] { 0, 5, 10, 15, 3 }; // F, C, I, L, F — spans all 4 perspectives
        var kpiScores = achievementTarget is { } kt ? JitteredScores(kt, kpiPicks.Length) : new int[kpiPicks.Length];
        var kpis = new List<PmFormKpi>();
        for (var i = 0; i < kpiPicks.Length; i++)
        {
            var (id, name, perspective) = DemoReferenceData.Kpis[kpiPicks[i]];
            kpis.Add(new PmFormKpi
            {
                RecordSeq = i + 1, Perspective = perspective, KpiCode = id, KpiName = name,
                Target = "100% of plan", ItemWeight = 20, AchievementScore = kpiScores[i]
            });
        }

        var compPicks = new[] { 0, 2, 6, 8 }; // 2 behavioral + 2 technical
        var compScores = achievementTarget is { } ct ? JitteredScores(ct, compPicks.Length) : new int[compPicks.Length];
        var comps = new List<PmFormCompetency>();
        for (var i = 0; i < compPicks.Length; i++)
        {
            var (id, name, type) = DemoReferenceData.Competencies[compPicks[i]];
            comps.Add(new PmFormCompetency
            {
                RecordSeq = i + 1, CompType = type, CompCode = id, CompName = name,
                ItemWeight = 25, AchievementScore = compScores[i]
            });
        }

        return new WorkflowService.PmFormContent(
            emp.EmpCode, evalYear, emp.LatinName, emp.DesignationCode, emp.DeptCode, emp.SectionCode,
            managerEmpCode, emp.Grade, emp.JoinDate, weights.FamilyName,
            weights.KpiWeight, weights.CompWeight,
            "Consistently met assigned objectives and contributed to team goals throughout the review period.",
            "Continue building expertise in core responsibilities and pursue relevant professional development.",
            emp.LatinName, managerEmpCode is null ? null : "Manager Signature",
            null, null, kpis, comps);
    }

    private async Task<bool> CreateAndDriveFormAsync(Employee emp, string managerEmpCode, string managerUserName,
        int evalYear, Bucket bucket, int index)
    {
        var jobFamilyService = new JobFamilyService(_db, _clock);
        var weights = await jobFamilyService.ResolveAsync(emp.EmpCode, emp.Grade);
        var managerPerms = new FormPermissions(IsHrAdmin: false, IsDirectManager: true, IsSelf: false, IsBranchViewer: false, UserEmpCode: managerEmpCode);

        // Stage 1: Jan of the review year — content created, not yet sent (pure Draft bucket only).
        if (bucket == Bucket.Draft && index % 2 == 0)
        {
            _clock.Today = new DateOnly(evalYear, 1, 20);
            var content = BuildContent(emp, managerEmpCode, weights, achievementTarget: null, evalYear);
            var form = new PmForm
            {
                EmpCode = emp.EmpCode, EvalYear = evalYear,
                LegacyRefNo = RefNoGenerator.Header(emp.EmpCode, evalYear),
                Status = PmFormStatus.Draft, CreatedAt = _clock.Now, CreatedBy = managerUserName,
                EmpNameSnapshot = content.EmpName, DesignationSnapshot = content.DesignationCode,
                DeptCode = content.DeptCode, SectionCode = content.SectionCode, ManagerEmpCode = content.ManagerEmpCode,
                GradeSnapshot = content.Grade, JoinDateSnapshot = content.JoinDate, JobFamily = content.JobFamily,
                KpiWeightTotal = content.KpiWeightTotal, CompWeightTotal = content.CompWeightTotal,
                SelfAssessment = content.SelfAssessment, DevelopmentPlan = content.DevelopmentPlan,
                Version = 1,
            };
            foreach (var k in content.Kpis) form.Kpis.Add(k);
            foreach (var c in content.Competencies) form.Competencies.Add(c);
            _db.PmForms.Add(form);
            await _db.SaveChangesAsync();
            return true;
        }

        // Stage 2: send to employee (everything except the bare-Draft case above).
        _clock.Today = new DateOnly(evalYear, 1, 20);
        var sendContent = BuildContent(emp, managerEmpCode, weights, achievementTarget: null, evalYear);
        var sendResult = await _workflow.SendToEmployeeAsync(managerUserName, managerPerms, sendContent);
        if (!sendResult.Success) return false;
        if (bucket == Bucket.Draft) return true; // PendingEmployeeAck — the other half of the Draft bucket

        // Stage 3: employee acknowledges.
        _clock.Today = new DateOnly(evalYear, 2, 10);
        var ackResult = await _workflow.AcknowledgeAsync(emp.EmpCode, emp.EmpCode, emp.EmpCode, evalYear, "Objectives reviewed and acknowledged.");
        if (!ackResult.Success) return false;
        if (bucket == Bucket.MidYear) return true;

        // Stage 4: manager enters achievement + submits to HR (achievement gate: 1 Dec of evalYear).
        _clock.Today = new DateOnly(evalYear, 12, 5);
        var target = PickTargetScore();
        var submitContent = BuildContent(emp, managerEmpCode, weights, achievementTarget: target, evalYear);
        var submitResult = await _workflow.SubmitToHrAsync(managerUserName, managerPerms, submitContent, weights.Configured);
        if (!submitResult.Success) return false;

        // A handful of HR-Review forms are force-finalized by an administrator on behalf of a
        // reviewer who is unavailable, exercising the "Administrative Completion" action —
        // the one Workflow Administration action none of the other buckets naturally reach.
        if (bucket == Bucket.HrReview && index % 6 == 0)
        {
            _clock.Today = new DateOnly(evalYear, 12, 11);
            var completion = await _workflowAdmin.AdministrativeCompletionAsync(AdminUserName, emp.EmpCode, evalYear,
                "Second HR reviewer unavailable before the review window closed — administratively completed.",
                weights.Configured, perspectiveExempt: false, ip: null);
            if (completion.Success) _workflowAdminActionCount++;
            return completion.Success;
        }
        if (bucket == Bucket.HrReview && index % 2 == 0) return true; // stays at SubmittedToHr

        // Stage 5: HR reviewer 1.
        _clock.Today = new DateOnly(evalYear, 12, 10);
        var hrPerms1 = new FormPermissions(IsHrAdmin: true, IsDirectManager: false, IsSelf: false, IsBranchViewer: false, UserEmpCode: Hr1EmpCode);
        var hr1Result = await _workflow.HrApprove1Async(Hr1EmpCode.PadLeft(4, '0'), hrPerms1, emp.EmpCode, evalYear,
            "HR Reviewer One", Hr1EmpCode, "Achievement scores and competency ratings verified.");
        if (!hr1Result.Success) return false;
        if (bucket == Bucket.HrReview) return true; // stays at HrReview1Approved

        if (bucket == Bucket.Returned)
        {
            _clock.Today = new DateOnly(evalYear, 12, 12);
            var ok = index % 2 == 0
                ? await _workflowAdmin.ReturnToManagerAsync(AdminUserName, AdminUserName, emp.EmpCode, evalYear,
                    "HR requested additional supporting detail before final approval.", ip: null)
                : await _workflowAdmin.ReturnToEmployeeAsync(AdminUserName, emp.EmpCode, evalYear,
                    "Manager identified a correction needed to the employee's self-assessment.", ip: null);
            if (ok.Success) _workflowAdminActionCount++;
            return ok.Success;
        }

        // Stage 6: HR reviewer 2 (final) — a different reviewer than HR1 (segregation of duties).
        _clock.Today = new DateOnly(evalYear, 12, 15);
        var hrPerms2 = new FormPermissions(IsHrAdmin: true, IsDirectManager: false, IsSelf: false, IsBranchViewer: false, UserEmpCode: Hr2EmpCode);
        var hr2Result = await _workflow.HrFinalApproveAsync(Hr2EmpCode.PadLeft(4, '0'), hrPerms2, Hr2EmpCode, emp.EmpCode, evalYear,
            "HR Reviewer Two", Hr2EmpCode, "Final review complete — approved for the record.");
        if (!hr2Result.Success) return false;

        if (bucket == Bucket.Reopened)
        {
            _clock.Today = new DateOnly(evalYear, 12, 20);
            var reopen = await _workflowAdmin.ReopenReviewAsync(AdminUserName, emp.EmpCode, evalYear,
                "Manager flagged a scoring error after finalization — reopened for correction.", ip: null);
            if (reopen.Success) _workflowAdminActionCount++;
            return reopen.Success;
        }

        // A handful of otherwise-Completed forms also exercise Resend Notification / Unlock Review,
        // so both remaining Workflow Administration actions have real audit history too.
        if (bucket == Bucket.Completed && index % 20 == 0)
        {
            var resend = await _workflowAdmin.ResendNotificationAsync(AdminUserName, emp.EmpCode, evalYear,
                "Employee reported the confirmation email never arrived.", ip: null);
            if (resend.Success) _workflowAdminActionCount++;
        }
        if (bucket == Bucket.Completed && index % 25 == 0)
        {
            var unlock = await _workflowAdmin.UnlockAsync(AdminUserName, emp.EmpCode, evalYear,
                "Employee needs to correct a typo in one field.", ip: null);
            if (unlock.Success) _workflowAdminActionCount++;
        }

        return true;
    }

    private async Task<int> SeedPerformanceDataAsync(List<Employee> employees, List<string> managerCodes)
    {
        var formCount = 0;
        var managerByEmp = await _db.ManagerAssignments.AsNoTracking()
            .ToDictionaryAsync(m => m.EmpCode, m => m.ManagerEmpCode);
        var employeesByCode = employees.ToDictionary(e => e.EmpCode);

        var index = 0;
        foreach (var emp in employees.Where(e => e.Grade != "1").OrderBy(e => e.EmpCode))
        {
            if (!managerByEmp.TryGetValue(emp.EmpCode, out var managerEmpCode)) continue;
            var managerUserName = managerEmpCode.Trim().PadLeft(4, '0');

            foreach (var year in new[] { 2024, 2025, 2026 })
            {
                if (emp.JoinDate is { } joinDate && joinDate.Year > year) continue;

                var bucket = year == 2026 ? PickBucket() : Bucket.Completed;
                var created = await CreateAndDriveFormAsync(emp, managerEmpCode, managerUserName, year, bucket, index);
                if (created) formCount++;
                index++;
            }
        }
        return formCount;
    }
}

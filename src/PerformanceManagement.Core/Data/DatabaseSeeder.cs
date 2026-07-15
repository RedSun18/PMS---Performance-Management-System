using PerformanceManagement.Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Core.Data;

/// <summary>
/// Idempotent seeds. SeedCoreAsync runs at web-app startup; the importer additionally
/// creates per-employee dev accounts after employees are loaded.
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>
    /// Development-only default password for per-employee seeded accounts
    /// (MustChangePassword preserved as-is). No production credentials are ever imported
    /// (docs/data-migration-plan.md §2). Matches Security:DefaultUserPassword so every
    /// Employee-type account — seeded by the importer or created later via User
    /// Management — shares one predictable dev default.
    /// </summary>
    public const string DevPassword = "Password123";

    /// <summary>Default single-admin dev credentials, overridable via configuration (see Program.cs).</summary>
    public const string DefaultAdminUsername = "admin";
    public const string DefaultAdminPassword = "admin123";

    private static readonly PasswordHasher<AppUser> Hasher = new();

    public static async Task SeedCoreAsync(PmDbContext db,
        string adminUsername = DefaultAdminUsername, string adminPassword = DefaultAdminPassword)
    {
        foreach (var (code, name) in SeedData.Departments)
            if (await db.Departments.FindAsync(code) is null)
                db.Departments.Add(new Department { Code = code, NameEn = name });

        foreach (var (emp, mgr) in SeedData.DirectManagerMap)
            if (await db.ManagerAssignments.FindAsync(emp) is null)
                db.ManagerAssignments.Add(new ManagerAssignment
                {
                    EmpCode = emp,
                    ManagerEmpCode = mgr,
                    Source = "HR_LIST",
                    Note = "KPI_Direct_Managers_List_DEPT.xlsx via legacy KPIForm.aspx.vb"
                });

        foreach (var (emp, rule, reason) in SeedData.Exceptions)
            if (!await db.EmployeeExceptions.AnyAsync(x => x.EmpCode == emp && x.RuleCode == rule))
                db.EmployeeExceptions.Add(new EmployeeException { EmpCode = emp, RuleCode = rule, Reason = reason });

        // Single configurable administrator account — the standalone rebuild does not seed
        // the legacy adm22/adm12/... accounts (see SeedData.cs note). Override the
        // username/password via the "AdminAccount" configuration section or environment
        // variables in Program.cs; never hard-code real credentials here.
        adminUsername = (adminUsername ?? DefaultAdminUsername).Trim();
        var admin = await db.AppUsers.Include(u => u.RolesList)
            .FirstOrDefaultAsync(u => u.UserName == adminUsername);
        if (admin is null)
        {
            admin = new AppUser
            {
                UserName = adminUsername,
                DisplayName = "Administrator",
                // Pseudo employee code so HR1/HR2 segregation-of-duties comparison works
                EmpCode = adminUsername
            };
            admin.PasswordHash = Hasher.HashPassword(admin, adminPassword);
            db.AppUsers.Add(admin);
        }
        if (!admin.RolesList.Any(r => r.Role == Roles.HrAdmin))
            admin.RolesList.Add(new UserRole { Role = Roles.HrAdmin });

        await db.SaveChangesAsync();
    }

    /// <summary>One dev login per employee: username = 4-digit padded employee code.</summary>
    public static async Task<int> SeedUsersForEmployeesAsync(PmDbContext db)
    {
        var n = 0;
        var employees = await db.Employees.AsNoTracking().ToListAsync();
        var existing = (await db.AppUsers.AsNoTracking().Select(u => u.UserName).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var e in employees)
        {
            var username = e.EmpCode.Trim().PadLeft(4, '0');
            if (existing.Contains(username)) continue;
            var user = new AppUser
            {
                UserName = username,
                DisplayName = e.LatinName,
                EmpCode = e.EmpCode,
                Email = e.Email,
                MustChangePassword = true
            };
            user.PasswordHash = Hasher.HashPassword(user, DevPassword);
            db.AppUsers.Add(user);
            n++;
        }
        await db.SaveChangesAsync();
        return n;
    }

    /// <summary>
    /// Resets the password of every Employee-type account (i.e. holding neither HR_ADMIN
    /// nor VIEWER) to <paramref name="password"/>, hashed via the normal ASP.NET Core
    /// Identity PasswordHasher — never stored in plaintext. Administrator and Viewer
    /// accounts are left untouched, and each account's existing MustChangePassword flag
    /// is preserved exactly as-is. Idempotent — safe to run more than once.
    /// </summary>
    public static async Task<int> ResetEmployeePasswordsAsync(PmDbContext db, string password = DevPassword)
    {
        var users = await db.AppUsers.Include(u => u.RolesList).ToListAsync();
        var n = 0;
        foreach (var user in users)
        {
            var isPrivileged = user.RolesList.Any(r => r.Role is Roles.HrAdmin or Roles.Viewer);
            if (isPrivileged) continue;
            user.PasswordHash = HashPassword(user, password);
            n++;
        }
        await db.SaveChangesAsync();
        return n;
    }

    public static bool VerifyPassword(AppUser user, string password) =>
        Hasher.VerifyHashedPassword(user, user.PasswordHash, password)
            is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;

    public static string HashPassword(AppUser user, string password) => Hasher.HashPassword(user, password);
}

using Aic.Pm.Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aic.Pm.Core.Data;

/// <summary>
/// Idempotent seeds. SeedCoreAsync runs at web-app startup; the importer additionally
/// creates per-employee dev accounts after employees are loaded.
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>
    /// Development-only default password for seeded accounts (MustChangePassword = true).
    /// No production credentials are ever imported (docs/data-migration-plan.md §2).
    /// </summary>
    public const string DevPassword = "ChangeMe123!";

    private static readonly PasswordHasher<AppUser> Hasher = new();

    public static async Task SeedCoreAsync(PmDbContext db)
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

        // The six approved HR administrator accounts — the ONLY HR_ADMIN role holders.
        foreach (var account in SeedData.HrAdminAccounts)
        {
            var user = await db.AppUsers.Include(u => u.RolesList)
                .FirstOrDefaultAsync(u => u.UserName == account);
            if (user is null)
            {
                user = new AppUser
                {
                    UserName = account,
                    DisplayName = $"HR Administrator ({account})",
                    // Pseudo employee code so HR1/HR2 segregation-of-duties comparison works
                    EmpCode = account,
                    MustChangePassword = true
                };
                user.PasswordHash = Hasher.HashPassword(user, DevPassword);
                db.AppUsers.Add(user);
            }
            if (!user.RolesList.Any(r => r.Role == Roles.HrAdmin))
                user.RolesList.Add(new UserRole { Role = Roles.HrAdmin });
        }

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

    public static bool VerifyPassword(AppUser user, string password) =>
        Hasher.VerifyHashedPassword(user, user.PasswordHash, password)
            is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;

    public static string HashPassword(AppUser user, string password) => Hasher.HashPassword(user, password);
}

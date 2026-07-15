using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Core.Services;

/// <summary>
/// Effective permissions of a user with respect to one selected employee's form.
/// Mirrors the legacy cached flags (isHR / isManager / isEmployee / isBranchViewer)
/// with the legacy rule: a manager viewing their OWN form is an employee.
/// </summary>
public record FormPermissions(
    bool IsHrAdmin,
    bool IsDirectManager,
    bool IsSelf,
    bool IsBranchViewer,
    string UserEmpCode)
{
    /// <summary>
    /// Manager actions require managing another employee (never on own form).
    /// Administrator supersedes the direct-manager/branch-viewer assignment entirely —
    /// an admin can perform every Direct Manager action on every employee's form
    /// (edit KPI/Competency items, submit/approve/revert workflow steps, etc.) without
    /// being the employee's assigned manager. The self-view rule still applies to
    /// admins exactly as it does to everyone else: nobody gets manager controls on
    /// their own form.
    /// </summary>
    public bool CanActAsManager => IsHrAdmin ? !IsSelf : (IsDirectManager && !IsSelf && !IsBranchViewer);
    public bool CanActAsHr => IsHrAdmin && !IsSelf;
    /// <summary>Score/summary/validation visibility (legacy CanViewFullPerformanceScores).</summary>
    public bool CanViewFullScores => (CanActAsManager || IsHrAdmin) && !IsSelf;
    public bool CanView => IsHrAdmin || IsDirectManager || IsSelf || IsBranchViewer;
}

public class PermissionService
{
    private readonly PmDbContext _db;
    private readonly IClock _clock;
    public PermissionService(PmDbContext db, IClock clock) { _db = db; _clock = clock; }

    /// <summary>
    /// PM Form HR administrative access is explicit: only accounts holding the HR_ADMIN
    /// role (seeded adm22, adm12, adm4, adm2, adm16, adm10). Never department-based.
    /// </summary>
    public async Task<bool> IsHrAdminAsync(string userName)
    {
        var uname = (userName ?? "").Trim().ToLowerInvariant();
        return await _db.UserRoles.AsNoTracking()
            .Include(r => r.AppUser)
            .AnyAsync(r => r.Role == Roles.HrAdmin && r.AppUser!.UserName.ToLower() == uname);
    }

    public async Task<string?> GetManagerOfAsync(string empCode)
    {
        var row = await _db.ManagerAssignments.AsNoTracking()
            .FirstOrDefaultAsync(m => m.EmpCode == empCode.Trim());
        return row?.ManagerEmpCode;
    }

    public async Task<bool> HasExceptionAsync(string empCode, string ruleCode)
    {
        var rows = await _db.EmployeeExceptions.AsNoTracking()
            .Where(x => x.EmpCode == empCode && x.RuleCode == ruleCode).ToListAsync();
        return rows.Any(r => r.IsEffective(_clock.Today));
    }

    /// <summary>True when userEmpCode manages at least one employee (drives selector availability).</summary>
    public async Task<bool> IsAManagerAsync(string userEmpCode) =>
        await _db.ManagerAssignments.AsNoTracking().AnyAsync(m => m.ManagerEmpCode == userEmpCode);

    public async Task<List<string>> GetAssignedEmployeesAsync(string managerEmpCode) =>
        await _db.ManagerAssignments.AsNoTracking()
            .Where(m => m.ManagerEmpCode == managerEmpCode)
            .Select(m => m.EmpCode).ToListAsync();

    public async Task<FormPermissions> GetFormPermissionsAsync(string userName, string userEmpCode, string targetEmpCode)
    {
        userEmpCode = (userEmpCode ?? "").Trim();
        targetEmpCode = (targetEmpCode ?? "").Trim();

        var isHr = await IsHrAdminAsync(userName);
        var manager = await GetManagerOfAsync(targetEmpCode);
        var isDirectManager = manager is not null && manager == userEmpCode;
        var isSelf = userEmpCode.Length > 0 && userEmpCode == targetEmpCode;

        // Temporary legacy arrangement made data-driven: SELF_MANAGER employees act as
        // their own direct manager (their isSelf flag is suppressed for manager actions).
        if (isDirectManager && isSelf &&
            await HasExceptionAsync(targetEmpCode, ExceptionRule.SelfManager))
        {
            isSelf = false;
        }

        // BRANCH_VIEWER: view-only access to branch (PRO / section BR) employees.
        var isBranchViewer = false;
        if (!isDirectManager && !isSelf &&
            await HasExceptionAsync(userEmpCode, ExceptionRule.BranchViewer))
        {
            var target = await _db.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.EmpCode == targetEmpCode);
            isBranchViewer = target is { DeptCode: "PRO", SectionCode: "BR", TermDate: null };
        }

        return new FormPermissions(isHr, isDirectManager, isSelf, isBranchViewer, userEmpCode);
    }
}

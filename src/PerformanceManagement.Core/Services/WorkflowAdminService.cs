using System.Globalization;
using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PerformanceManagement.Core.Services;

public record WorkflowAdminFilter(
    string? EmpCode = null, string? EmpName = null, string? DeptCode = null,
    string? Manager = null, int? EvalYear = null, string? Status = null);

public record WorkflowAdminRow(
    string EmpCode, string EmpName, string DeptName, string ManagerName, int EvalYear,
    string Stage, string Owner, DateTime? LastUpdated, string Status);

public record WorkflowAdminSearchResult(IReadOnlyList<WorkflowAdminRow> Rows, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public record WorkflowAdminDetails(
    PmForm Form, string EmpName, string DeptName, string ManagerName,
    string Stage, string Owner, int StageOrdinal,
    IReadOnlyList<PmFormStatusHistory> Timeline, IReadOnlyList<AuditLog> AuditHistory);

/// <summary>
/// Presentation-only mapping from the real <see cref="PmFormStatus"/> machine states onto the
/// stage/owner vocabulary Workflow Administration displays — never used to drive the state
/// machine itself, only to label it. Same bilingual-switch idiom as
/// <see cref="PmFormStatus.DisplayName"/> (a small closed set doesn't need a resx). Ordinal
/// drives the 5-node progress tracker on the Details page; SubmittedToHr and
/// HrReview1Approved deliberately share ordinal 3 ("HR Review") — the tracker shows one HR
/// Review node, not two sub-steps.
/// </summary>
public static class WorkflowStageDisplay
{
    public static (string Stage, string Owner, int Ordinal) For(string? status, CultureInfo? culture = null)
    {
        var ar = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == "ar";
        return (status ?? "").Trim() switch
        {
            PmFormStatus.Draft or PmFormStatus.Ready =>
                (ar ? "إنشاء مؤشرات الأداء" : "KPI Creation", ar ? "المدير" : "Manager", 0),
            PmFormStatus.PendingEmployeeAck =>
                (ar ? "إقرار الموظف" : "Employee Acknowledgement", ar ? "الموظف" : "Employee", 1),
            PmFormStatus.EmployeeAcknowledged =>
                (ar ? "مراجعة المدير" : "Manager Review", ar ? "المدير" : "Manager", 2),
            PmFormStatus.SubmittedToHr =>
                (ar ? "مراجعة الموارد البشرية — الأولى" : "HR Review — First", ar ? "الموارد البشرية" : "HR", 3),
            PmFormStatus.HrReview1Approved =>
                (ar ? "مراجعة الموارد البشرية — النهائية" : "HR Review — Final", ar ? "الموارد البشرية" : "HR", 3),
            PmFormStatus.Approved =>
                (ar ? "مكتمل" : "Completed", "—", 4),
            _ => (status ?? "", "", -1)
        };
    }

    /// <summary>The five tracker node labels, in order, for the Details page progress tracker.</summary>
    public static (string Label, int Ordinal)[] TrackerNodes(CultureInfo? culture = null)
    {
        var ar = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == "ar";
        return new[]
        {
            (ar ? "إنشاء مؤشرات الأداء" : "Manager KPIs", 0),
            (ar ? "إقرار الموظف" : "Employee Ack", 1),
            (ar ? "مراجعة المدير" : "Manager Review", 2),
            (ar ? "مراجعة الموارد البشرية" : "HR Review", 3),
            (ar ? "مكتمل" : "Completed", 4)
        };
    }
}

/// <summary>
/// Read-model assembly (search grid, Details page) and the six Workflow Administration
/// override actions, each of which delegates the actual transition/email to
/// <see cref="WorkflowService"/> and then writes one <see cref="AuditService"/> entry tagged
/// with the "Workflow Administration: " action-source prefix — see the HR-Admin-only
/// Pages/WorkflowAdmin/Index and Details pages (Web project) that call this.
/// </summary>
public class WorkflowAdminService
{
    private readonly PmDbContext _db;
    private readonly WorkflowService _workflow;
    private readonly AuditService _audit;
    private readonly ILogger<WorkflowAdminService> _logger;

    public WorkflowAdminService(PmDbContext db, WorkflowService workflow, AuditService audit,
        ILogger<WorkflowAdminService> logger)
    {
        _db = db; _workflow = workflow; _audit = audit; _logger = logger;
    }

    public async Task<WorkflowAdminSearchResult> SearchAsync(WorkflowAdminFilter filter, int page, int pageSize)
    {
        var q = from f in _db.PmForms.AsNoTracking()
                join e in _db.Employees.AsNoTracking() on f.EmpCode equals e.EmpCode
                select new { f, e };

        if (!string.IsNullOrWhiteSpace(filter.EmpCode)) q = q.Where(x => x.f.EmpCode == filter.EmpCode.Trim());
        if (!string.IsNullOrWhiteSpace(filter.EmpName))
        {
            var needle = filter.EmpName.Trim().ToLower();
            q = q.Where(x => x.e.LatinName.ToLower().Contains(needle));
        }
        if (!string.IsNullOrWhiteSpace(filter.DeptCode)) q = q.Where(x => x.e.DeptCode == filter.DeptCode);
        if (filter.EvalYear is { } yr) q = q.Where(x => x.f.EvalYear == yr);
        if (!string.IsNullOrWhiteSpace(filter.Status)) q = q.Where(x => x.f.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.Manager))
        {
            var managedEmpCodes = await _db.ManagerAssignments.AsNoTracking()
                .Where(m => m.ManagerEmpCode == filter.Manager).Select(m => m.EmpCode).ToListAsync();
            q = q.Where(x => managedEmpCodes.Contains(x.f.EmpCode));
        }

        var totalCount = await q.CountAsync();
        var page1 = Math.Max(1, page);
        var pageSize1 = Math.Clamp(pageSize, 10, 200);
        var data = await q.OrderByDescending(x => x.f.UpdatedAt ?? x.f.CreatedAt).ThenBy(x => x.e.LatinName)
            .Skip((page1 - 1) * pageSize1).Take(pageSize1).ToListAsync();

        var depts = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Code, d => d.NameEn);
        var employeeNames = await _db.Employees.AsNoTracking().ToDictionaryAsync(e => e.EmpCode, e => e.LatinName);

        var rows = data.Select(x =>
        {
            var (stage, owner, _) = WorkflowStageDisplay.For(x.f.Status);
            var managerName = string.IsNullOrWhiteSpace(x.f.ManagerEmpCode) ? "" :
                employeeNames.GetValueOrDefault(x.f.ManagerEmpCode, x.f.ManagerEmpCode);
            return new WorkflowAdminRow(
                x.f.EmpCode, x.e.LatinName,
                depts.GetValueOrDefault(x.e.DeptCode ?? "", x.e.DeptCode ?? ""),
                managerName, x.f.EvalYear, stage, owner,
                x.f.UpdatedAt ?? x.f.CreatedAt, x.f.Status);
        }).ToList();

        return new WorkflowAdminSearchResult(rows, totalCount, page1, pageSize1);
    }

    public async Task<WorkflowAdminDetails?> GetDetailsAsync(string empCode, int evalYear)
    {
        var form = await _workflow.FindFormAsync(empCode, evalYear);
        if (form is null) return null;

        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmpCode == empCode.Trim());
        var deptName = emp?.DeptCode is null ? "" :
            (await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Code == emp.DeptCode))?.NameEn ?? emp.DeptCode;
        var managerName = string.IsNullOrWhiteSpace(form.ManagerEmpCode) ? "" :
            (await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmpCode == form.ManagerEmpCode))?.LatinName
                ?? form.ManagerEmpCode;

        var (stage, owner, ordinal) = WorkflowStageDisplay.For(form.Status);

        var timeline = await _db.PmFormStatusHistory.AsNoTracking()
            .Where(h => h.PmFormId == form.Id).OrderBy(h => h.ChangedAt).ToListAsync();

        var auditHistory = await _audit.SearchAsync(
            new AuditLogFilter(EntityType: "PmForm", EntityId: form.Id.ToString()), take: 100);

        return new WorkflowAdminDetails(form, emp?.LatinName ?? form.EmpNameSnapshot, deptName, managerName,
            stage, owner, ordinal, timeline, auditHistory);
    }

    // ================================================================ Administrative actions

    private record Snapshot(int Id, string DeptCode, string Status);

    private async Task<Snapshot?> SnapshotAsync(string empCode, int evalYear)
    {
        var form = await _workflow.FindFormAsync(empCode, evalYear);
        return form is null ? null : new Snapshot(form.Id, form.DeptCode ?? "", form.Status);
    }

    // The workflow transition (WorkflowService, its own transaction, already committed by the
    // time control reaches here) and this audit write are necessarily two separate database
    // operations — WorkflowService's public methods don't expose their transaction for a caller
    // to join. If the audit write itself fails (DB hiccup right after a successful commit), the
    // admin action has already genuinely succeeded and must be reported as such — silently
    // losing the audit trail without a trace anywhere would be worse than reporting success with
    // no server-side record of why, so the failure is logged operationally instead.
    private async Task LogAsync(string friendlyAction, string actor, string empCode, Snapshot snap,
        string newStatus, string reason, string? ip)
    {
        var details = $"Reason: {reason}; Previous: {PmFormStatus.DisplayName(snap.Status)}; " +
            $"New: {PmFormStatus.DisplayName(newStatus)};" +
            (string.IsNullOrWhiteSpace(ip) ? "" : $" IP: {ip};");
        try
        {
            await _audit.LogAsync($"Workflow Administration: {friendlyAction}", actor, empCode, snap.DeptCode,
                "PmForm", snap.Id.ToString(), details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Audit log write failed after a successful Workflow Administration action ({Action}) by {Actor} on {EmpCode}. Details: {Details}",
                friendlyAction, actor, empCode, details);
        }
    }

    public async Task<WorkflowResult> ReturnToEmployeeAsync(string actor, string empCode, int evalYear, string reason, string? ip)
    {
        var snap = await SnapshotAsync(empCode, evalYear);
        if (snap is null) return WorkflowResult.Fail("No PM form exists for this employee and year.");
        var result = await _workflow.AdminReturnToEmployeeAsync(actor, empCode, evalYear, reason);
        if (result.Success) await LogAsync("Return to Employee", actor, empCode, snap, PmFormStatus.PendingEmployeeAck, reason, ip);
        return result;
    }

    public async Task<WorkflowResult> ReturnToManagerAsync(string actor, string adminEmpCode, string empCode, int evalYear, string reason, string? ip)
    {
        var snap = await SnapshotAsync(empCode, evalYear);
        if (snap is null) return WorkflowResult.Fail("No PM form exists for this employee and year.");
        var adminPerms = new FormPermissions(IsHrAdmin: true, IsDirectManager: false, IsSelf: false,
            IsBranchViewer: false, UserEmpCode: adminEmpCode);
        var result = await _workflow.HrRevertAsync(actor, adminPerms, empCode, evalYear, reason);
        if (result.Success) await LogAsync("Return to Manager", actor, empCode, snap, PmFormStatus.EmployeeAcknowledged, reason, ip);
        return result;
    }

    public async Task<WorkflowResult> ReopenReviewAsync(string actor, string empCode, int evalYear, string reason, string? ip)
    {
        var snap = await SnapshotAsync(empCode, evalYear);
        if (snap is null) return WorkflowResult.Fail("No PM form exists for this employee and year.");
        var result = await _workflow.AdminReopenReviewAsync(actor, empCode, evalYear, reason);
        if (result.Success) await LogAsync("Reopen Review", actor, empCode, snap, PmFormStatus.EmployeeAcknowledged, reason, ip);
        return result;
    }

    public async Task<WorkflowResult> ResendNotificationAsync(string actor, string empCode, int evalYear, string reason, string? ip)
    {
        var snap = await SnapshotAsync(empCode, evalYear);
        if (snap is null) return WorkflowResult.Fail("No PM form exists for this employee and year.");
        var result = await _workflow.AdminResendNotificationAsync(actor, empCode, evalYear);
        if (result.Success) await LogAsync("Resend Notification", actor, empCode, snap, snap.Status, reason, ip);
        return result;
    }

    public async Task<WorkflowResult> AdministrativeCompletionAsync(string actor, string empCode, int evalYear, string reason,
        bool jobFamilyConfigured, bool perspectiveExempt, string? ip)
    {
        var snap = await SnapshotAsync(empCode, evalYear);
        if (snap is null) return WorkflowResult.Fail("No PM form exists for this employee and year.");
        var result = await _workflow.AdminForceFinalizeAsync(actor, empCode, evalYear, reason, jobFamilyConfigured, perspectiveExempt);
        if (result.Success) await LogAsync("Administrative Completion", actor, empCode, snap, PmFormStatus.Approved, reason, ip);
        return result;
    }

    public async Task<WorkflowResult> UnlockAsync(string actor, string empCode, int evalYear, string reason, string? ip)
    {
        var snap = await SnapshotAsync(empCode, evalYear);
        if (snap is null) return WorkflowResult.Fail("No PM form exists for this employee and year.");
        var result = await _workflow.AdminUnlockAsync(actor, empCode, evalYear, reason);
        if (result.Success) await LogAsync("Unlock Review", actor, empCode, snap, snap.Status, reason, ip);
        return result;
    }
}

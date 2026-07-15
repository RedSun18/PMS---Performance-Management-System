using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Web.Pages.Dashboard;

/// <summary>
/// Landing page after login. One page, three views selected server-side by role —
/// Administrator/Viewer see the org-wide view, direct managers see their team view,
/// everyone else sees their own review.
/// </summary>
public class IndexModel : AppPageModel
{
    private readonly PmDbContext _db;
    private readonly IClock _clock;
    private readonly PermissionService _permissions;
    private readonly AchievementGate _gate;

    public IndexModel(PmDbContext db, IClock clock, PermissionService permissions, AchievementGate gate)
    {
        _db = db; _clock = clock; _permissions = permissions; _gate = gate;
    }

    public int EvalYear => _clock.Today.Year;
    public string DashboardKind { get; private set; } = "employee";

    // ---- Employee view ----------------------------------------------------
    public PerformanceManagement.Core.Domain.PmForm? MyForm { get; private set; }
    public int MyProgressPercent { get; private set; }
    public int MyKpiCompletionPercent { get; private set; }
    public int MyCompCompletionPercent { get; private set; }
    public string MyStatusLabel { get; private set; } = "";
    public string? MyDeadlineHint { get; private set; }
    public List<EmailLog> MyRecentNotifications { get; private set; } = new();

    // ---- Manager view -------------------------------------------------------
    public int TeamCount { get; private set; }
    public int WaitingForReviewCount { get; private set; }
    public int ReturnedCount { get; private set; }
    public int CompletedCount { get; private set; }
    public List<TeamRow> TeamRows { get; private set; } = new();
    public record TeamRow(string EmpCode, string Name, string Status, bool NeedsAttention);

    // ---- Administrator view --------------------------------------------------
    public int EmployeeCount { get; private set; }
    public int FormsGeneratedCount { get; private set; }
    public int ReadyCount { get; private set; }
    public int InProgressCount { get; private set; }
    public int FinalizedCount { get; private set; }
    public int OverallCompletionPercent { get; private set; }
    public List<DeptRow> DeptCompletion { get; private set; } = new();
    public record DeptRow(string Name, int TotalEmployees, int Finalized, int Percent);

    public async Task OnGetAsync()
    {
        if (IsHrAdmin || IsViewer)
        {
            DashboardKind = "admin";
            await LoadAdminAsync();
        }
        else if (await _permissions.IsAManagerAsync(CurrentEmpCode))
        {
            DashboardKind = "manager";
            await LoadManagerAsync();
        }
        else
        {
            DashboardKind = "employee";
            await LoadEmployeeAsync();
        }
    }

    // ======================================================================
    private async Task LoadEmployeeAsync()
    {
        if (CurrentEmpCode.Length == 0) return;

        MyForm = await _db.PmForms.AsNoTracking()
            .Include(f => f.Kpis).Include(f => f.Competencies)
            .FirstOrDefaultAsync(f => f.EmpCode == CurrentEmpCode && f.EvalYear == EvalYear);

        MyStatusLabel = PmFormStatus.DisplayName(MyForm?.Status ?? PmFormStatus.Draft);
        (MyProgressPercent, MyKpiCompletionPercent, MyCompCompletionPercent) = ComputeProgress(MyForm);
        MyDeadlineHint = ComputeDeadlineHint(MyForm);

        var myRefNos = await _db.PmForms.AsNoTracking()
            .Where(f => f.EmpCode == CurrentEmpCode)
            .Select(f => f.LegacyRefNo).ToListAsync();
        MyRecentNotifications = await _db.EmailLogs.AsNoTracking()
            .Where(e => e.FormLegacyRefNo != null && myRefNos.Contains(e.FormLegacyRefNo))
            .OrderByDescending(e => e.CreatedAt).Take(5).ToListAsync();
    }

    private static (int Progress, int Kpi, int Comp) ComputeProgress(PerformanceManagement.Core.Domain.PmForm? form)
    {
        if (form is null) return (0, 0, 0);

        var kpiOk = 0;
        if (form.Kpis.Count is >= FormValidationRules.MinKpiCount and <= FormValidationRules.MaxKpiCount) kpiOk++;
        if (form.Kpis.Sum(k => k.ItemWeight) == 100) kpiOk++;
        if (form.Kpis.Select(k => k.Perspective.ToUpperInvariant()).Distinct().Count() >= FormValidationRules.RequiredPerspectives) kpiOk++;
        var kpiPercent = form.Kpis.Count == 0 ? 0 : kpiOk * 100 / 3;

        var compOk = 0;
        if (form.Competencies.Count is >= FormValidationRules.MinCompCount and <= FormValidationRules.MaxCompCount) compOk++;
        if (form.Competencies.Sum(c => c.ItemWeight) == 100) compOk++;
        var compPercent = form.Competencies.Count == 0 ? 0 : compOk * 100 / 2;

        var total = 0; var passed = 0;
        total += 3; passed += kpiOk;
        total += 2; passed += compOk;
        total += 2;
        if (!string.IsNullOrWhiteSpace(form.SelfAssessment)) passed++;
        if (!string.IsNullOrWhiteSpace(form.DevelopmentPlan)) passed++;

        return (total == 0 ? 0 : passed * 100 / total, kpiPercent, compPercent);
    }

    private string? ComputeDeadlineHint(PerformanceManagement.Core.Domain.PmForm? form)
    {
        if (form is null || form.Status == PmFormStatus.Draft)
            return "Your manager has not yet set your performance objectives for this year.";

        if (form.Status == PmFormStatus.PendingEmployeeAck)
        {
            var due = (form.StatusChangeDate ?? _clock.Today).AddDays(7);
            return due < _clock.Today
                ? "Acknowledgement is overdue — please review and acknowledge your objectives."
                : $"Please acknowledge your objectives by {due:dd MMM yyyy}.";
        }

        if (form.Status == PmFormStatus.EmployeeAcknowledged && !_gate.IsOpen(EvalYear))
            return $"Achievement scoring opens 01 December {EvalYear}.";

        return null;
    }

    // ======================================================================
    private async Task LoadManagerAsync()
    {
        var assigned = await _permissions.GetAssignedEmployeesAsync(CurrentEmpCode);
        TeamCount = assigned.Count;
        if (assigned.Count == 0) return;

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => assigned.Contains(e.EmpCode)).ToDictionaryAsync(e => e.EmpCode, e => e.LatinName);

        var forms = await _db.PmForms.AsNoTracking()
            .Include(f => f.History)
            .Where(f => assigned.Contains(f.EmpCode) && f.EvalYear == EvalYear)
            .ToListAsync();
        var formsByEmp = forms.ToDictionary(f => f.EmpCode);

        foreach (var empCode in assigned)
        {
            var name = employees.GetValueOrDefault(empCode, empCode);
            if (!formsByEmp.TryGetValue(empCode, out var form))
            {
                TeamRows.Add(new TeamRow(empCode, name, "Not started", true));
                WaitingForReviewCount++;
                continue;
            }

            var lastNote = form.History.OrderByDescending(h => h.ChangedAt).FirstOrDefault()?.Note;
            var wasReverted = form.Status == PmFormStatus.EmployeeAcknowledged && lastNote == "HR reverted to manager";

            var needsAttention = form.Status is PmFormStatus.Draft or PmFormStatus.Ready || wasReverted;
            TeamRows.Add(new TeamRow(empCode, name,
                wasReverted ? "Returned by HR" : PmFormStatus.DisplayName(form.Status), needsAttention));

            if (wasReverted) ReturnedCount++;
            else if (form.Status is PmFormStatus.Draft or PmFormStatus.Ready or PmFormStatus.EmployeeAcknowledged) WaitingForReviewCount++;
            if (form.Status == PmFormStatus.Approved) CompletedCount++;
        }

        TeamRows = TeamRows.OrderByDescending(r => r.NeedsAttention).ThenBy(r => r.Name).ToList();
    }

    // ======================================================================
    private async Task LoadAdminAsync()
    {
        EmployeeCount = await _db.Employees.AsNoTracking().CountAsync(e => e.TermDate == null);

        var forms = await _db.PmForms.AsNoTracking()
            .Where(f => f.EvalYear == EvalYear)
            .Select(f => new { f.Status, f.DeptCode })
            .ToListAsync();
        FormsGeneratedCount = forms.Count;
        ReadyCount = forms.Count(f => f.Status is PmFormStatus.Draft or PmFormStatus.Ready);
        InProgressCount = forms.Count(f => f.Status is PmFormStatus.PendingEmployeeAck or PmFormStatus.EmployeeAcknowledged
            or PmFormStatus.SubmittedToHr or PmFormStatus.HrReview1Approved);
        FinalizedCount = forms.Count(f => f.Status == PmFormStatus.Approved);
        OverallCompletionPercent = EmployeeCount == 0 ? 0 : FinalizedCount * 100 / EmployeeCount;

        var depts = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Code, d => d.NameEn);
        var employeesByDept = await _db.Employees.AsNoTracking()
            .Where(e => e.TermDate == null && e.DeptCode != null)
            .GroupBy(e => e.DeptCode!)
            .Select(g => new { Dept = g.Key, Count = g.Count() })
            .ToListAsync();
        var finalizedByDept = forms.Where(f => f.Status == PmFormStatus.Approved && f.DeptCode != null)
            .GroupBy(f => f.DeptCode!).ToDictionary(g => g.Key, g => g.Count());

        DeptCompletion = employeesByDept
            .Select(d => new DeptRow(
                depts.GetValueOrDefault(d.Dept, d.Dept), d.Count,
                finalizedByDept.GetValueOrDefault(d.Dept, 0),
                d.Count == 0 ? 0 : finalizedByDept.GetValueOrDefault(d.Dept, 0) * 100 / d.Count))
            .OrderByDescending(d => d.TotalEmployees)
            .ToList();
    }
}

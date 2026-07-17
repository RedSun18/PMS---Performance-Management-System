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
    private readonly SettingsService _settings;

    public IndexModel(PmDbContext db, IClock clock, PermissionService permissions, AchievementGate gate, SettingsService settings)
    {
        _db = db; _clock = clock; _permissions = permissions; _gate = gate; _settings = settings;
    }

    public int EvalYear => _clock.Today.Year;
    public string DashboardKind { get; private set; } = "employee";
    public string? WelcomeMessage { get; private set; }
    public string? AnnouncementBanner { get; private set; }

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
    public record TeamRow(string EmpCode, string Name, string DeptName, string Status, bool NeedsAttention);

    // ---- Administrator view --------------------------------------------------
    public int EmployeeCount { get; private set; }
    public int FormsGeneratedCount { get; private set; }
    public int ReadyCount { get; private set; }
    public int InProgressCount { get; private set; }
    public int FinalizedCount { get; private set; }
    public int OverallCompletionPercent { get; private set; }
    public List<DeptRow> DeptCompletion { get; private set; } = new();
    public record DeptRow(string Code, string Name, int TotalEmployees, int FormCount, int Started, int Finalized,
        decimal AverageScore, int CompletionPercent, int ProgressPercent);

    // ---- Recent Activity (all three views) -----------------------------------
    /// <summary>Administrator: every recent admin action (AuditLog). Manager: recent workflow
    /// events for the team's forms. Employee: recent workflow events for their own form.</summary>
    public List<ActivityRow> RecentActivity { get; private set; } = new();
    public record ActivityRow(DateTime When, string Text, string By);

    public async Task OnGetAsync()
    {
        var dashboardSettings = await _settings.GetDashboardSettingsAsync();
        WelcomeMessage = dashboardSettings.WelcomeMessage;
        AnnouncementBanner = dashboardSettings.AnnouncementBanner;

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

        var myHistory = await _db.PmFormStatusHistory.AsNoTracking()
            .Where(h => h.PmForm!.EmpCode == CurrentEmpCode)
            .OrderByDescending(h => h.ChangedAt).Take(8).ToListAsync();
        RecentActivity = myHistory
            .Select(h => new ActivityRow(h.ChangedAt, h.Note ?? PmFormStatus.DisplayName(h.ToStatus), h.ChangedBy))
            .ToList();
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
            .Where(e => assigned.Contains(e.EmpCode)).ToDictionaryAsync(e => e.EmpCode, e => e);
        var deptNames = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Code, d => d.NameEn);

        var forms = await _db.PmForms.AsNoTracking()
            .Include(f => f.History)
            .Where(f => assigned.Contains(f.EmpCode) && f.EvalYear == EvalYear)
            .ToListAsync();
        var formsByEmp = forms.ToDictionary(f => f.EmpCode);

        foreach (var empCode in assigned)
        {
            var emp = employees.GetValueOrDefault(empCode);
            var name = emp?.LatinName ?? empCode;
            var deptName = deptNames.GetValueOrDefault(emp?.DeptCode ?? "", "Unassigned");

            if (!formsByEmp.TryGetValue(empCode, out var form))
            {
                TeamRows.Add(new TeamRow(empCode, name, deptName, "Not started", true));
                WaitingForReviewCount++;
                continue;
            }

            var lastNote = form.History.OrderByDescending(h => h.ChangedAt).FirstOrDefault()?.Note;
            var wasReverted = form.Status == PmFormStatus.EmployeeAcknowledged && lastNote == "HR reverted to manager";

            var needsAttention = form.Status is PmFormStatus.Draft or PmFormStatus.Ready || wasReverted;
            TeamRows.Add(new TeamRow(empCode, name, deptName,
                wasReverted ? "Returned by HR" : PmFormStatus.DisplayName(form.Status), needsAttention));

            if (wasReverted) ReturnedCount++;
            else if (form.Status is PmFormStatus.Draft or PmFormStatus.Ready or PmFormStatus.EmployeeAcknowledged) WaitingForReviewCount++;
            if (form.Status == PmFormStatus.Approved) CompletedCount++;
        }

        TeamRows = TeamRows.OrderBy(r => r.DeptName).ThenByDescending(r => r.NeedsAttention).ThenBy(r => r.Name).ToList();

        var teamHistory = await _db.PmFormStatusHistory.AsNoTracking()
            .Where(h => assigned.Contains(h.PmForm!.EmpCode))
            .OrderByDescending(h => h.ChangedAt).Take(8)
            .Select(h => new { h.ChangedAt, h.ChangedBy, h.Note, h.ToStatus, EmpName = h.PmForm!.EmpNameSnapshot })
            .ToListAsync();
        RecentActivity = teamHistory
            .Select(h => new ActivityRow(h.ChangedAt, $"{h.EmpName}: {h.Note ?? PmFormStatus.DisplayName(h.ToStatus)}", h.ChangedBy))
            .ToList();
    }

    // ======================================================================
    private async Task LoadAdminAsync()
    {
        EmployeeCount = await _db.Employees.AsNoTracking().CountAsync(e => e.TermDate == null);

        var forms = await _db.PmForms.AsNoTracking()
            .Where(f => f.EvalYear == EvalYear)
            .Select(f => new { f.Status, f.DeptCode, f.PerformanceScore })
            .ToListAsync();
        FormsGeneratedCount = forms.Count;
        ReadyCount = forms.Count(f => f.Status is PmFormStatus.Draft or PmFormStatus.Ready);
        InProgressCount = forms.Count(f => f.Status is PmFormStatus.PendingEmployeeAck or PmFormStatus.EmployeeAcknowledged
            or PmFormStatus.SubmittedToHr or PmFormStatus.HrReview1Approved);
        FinalizedCount = forms.Count(f => f.Status == PmFormStatus.Approved);
        OverallCompletionPercent = EmployeeCount == 0 ? 0 : FinalizedCount * 100 / EmployeeCount;

        // Real Department Master join: an employee's DeptCode only groups into a department
        // row here if it matches an actual Department Master record (never a raw legacy code).
        var depts = await _db.Departments.AsNoTracking().ToListAsync();
        var employeesByDept = await _db.Employees.AsNoTracking()
            .Where(e => e.TermDate == null && e.DeptCode != null)
            .GroupBy(e => e.DeptCode!)
            .Select(g => new { Dept = g.Key, Count = g.Count() })
            .ToListAsync();
        var employeeCountByDept = employeesByDept.ToDictionary(x => x.Dept, x => x.Count);

        DeptCompletion = depts
            .Where(d => employeeCountByDept.ContainsKey(d.Code))
            .Select(d =>
            {
                var deptForms = forms.Where(f => f.DeptCode == d.Code).ToList();
                var empCount = employeeCountByDept.GetValueOrDefault(d.Code, 0);
                var started = deptForms.Count(f => f.Status is not (PmFormStatus.Draft or PmFormStatus.Ready));
                var finalized = deptForms.Count(f => f.Status == PmFormStatus.Approved);
                var avgScore = deptForms.Count == 0 ? 0 : Math.Round(deptForms.Average(f => f.PerformanceScore), 2);
                var completionPercent = empCount == 0 ? 0 : finalized * 100 / empCount;
                var progressPercent = empCount == 0 ? 0 : started * 100 / empCount;
                return new DeptRow(d.Code, d.NameEn, empCount, deptForms.Count, started, finalized, avgScore, completionPercent, progressPercent);
            })
            .OrderByDescending(d => d.TotalEmployees)
            .ToList();

        RecentActivity = await _db.AuditLogs.AsNoTracking()
            .OrderByDescending(a => a.OccurredAt).Take(8)
            .Select(a => new ActivityRow(a.OccurredAt,
                a.Details == null ? a.Action : (a.Action + ": " + a.Details), a.PerformedBy))
            .ToListAsync();
    }
}

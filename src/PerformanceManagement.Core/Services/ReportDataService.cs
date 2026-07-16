using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Core.Services;

public record ReportKpiRow(string Perspective, string Code, string Name, string? Target, int Weight, int Achievement, decimal Weighted, string? Comments);
public record ReportCompRow(string Type, string Code, string Name, int Weight, int Achievement, decimal Weighted, string? Comments);
public record ReportHistoryRow(string? FromStatus, string ToStatus, string ChangedBy, DateTime ChangedAt, string? Note);

public record EmployeePerformanceReport(
    string EmpCode, string EmpName, string? Department, string? Designation, string? Grade,
    string? ManagerName, int EvalYear, string RefNo, string Status,
    decimal KpiScore, decimal CompScore, decimal OverallScore, string Rating,
    IReadOnlyList<ReportKpiRow> Kpis, IReadOnlyList<ReportCompRow> Competencies,
    string? SelfAssessment, string? DevelopmentPlan, string? EmployeeAckComments,
    string? Hr1ReviewerName, DateOnly? Hr1ReviewDate, string? Hr1Remarks,
    string? Hr2ReviewerName, DateOnly? Hr2ReviewDate, string? Hr2Remarks,
    IReadOnlyList<ReportHistoryRow> History, DateTime GeneratedAt);

public record ReportEmployeeRow(string EmpCode, string Name, string? Designation, decimal OverallScore, string Rating, string Status);

public record DepartmentSummaryReport(string DeptCode, string DeptName, int EvalYear,
    IReadOnlyList<ReportEmployeeRow> Employees, int TotalEmployees, int FinalizedCount,
    decimal AverageScore, DateTime GeneratedAt);

public record ManagerSummaryReport(string ManagerEmpCode, string ManagerName, int EvalYear,
    IReadOnlyList<ReportEmployeeRow> TeamMembers, int TotalEmployees, int FinalizedCount,
    decimal AverageScore, DateTime GeneratedAt);

public record OrgDeptRow(string DeptCode, string DeptName, int EmployeeCount, int FinalizedCount, decimal AverageScore, int CompletionPercent);

public record OverallOrganizationReport(int EvalYear, int TotalEmployees, int TotalForms, int FinalizedCount,
    decimal AverageScore, IReadOnlyList<OrgDeptRow> Departments, DateTime GeneratedAt);

/// <summary>
/// Assembles the data models behind every exportable report (Employee Performance,
/// Department Summary, Manager Summary, Overall Organization Summary). Rendering to
/// PDF/Excel lives separately in <see cref="ReportExportService"/> — this service only
/// reads and shapes data, so the same models can back a future on-screen report view.
/// </summary>
public class ReportDataService
{
    private readonly PmDbContext _db;
    private readonly IClock _clock;
    public ReportDataService(PmDbContext db, IClock clock) { _db = db; _clock = clock; }

    public async Task<EmployeePerformanceReport?> GetEmployeeReportAsync(string empCode, int evalYear)
    {
        var form = await _db.PmForms.AsNoTracking()
            .Include(f => f.Kpis.OrderBy(k => k.RecordSeq))
            .Include(f => f.Competencies.OrderBy(c => c.RecordSeq))
            .Include(f => f.History.OrderBy(h => h.ChangedAt))
            .FirstOrDefaultAsync(f => f.EmpCode == empCode.Trim() && f.EvalYear == evalYear);
        if (form is null) return null;

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmpCode == empCode.Trim());
        var deptName = await DeptNameAsync(form.DeptCode ?? employee?.DeptCode);
        var desigName = await DesigNameAsync(form.DesignationSnapshot ?? employee?.DesignationCode);
        var managerName = await EmployeeNameAsync(form.ManagerEmpCode);
        var rating = await RatingNameAsync(form.PerformanceScore);

        return new EmployeePerformanceReport(
            form.EmpCode, form.EmpNameSnapshot, deptName, desigName, form.GradeSnapshot ?? employee?.Grade,
            managerName, form.EvalYear, form.LegacyRefNo, PmFormStatus.DisplayName(form.Status),
            form.KpiScore, form.CompScore, form.PerformanceScore, rating,
            form.Kpis.Select(k => new ReportKpiRow(k.Perspective, k.KpiCode, k.KpiName, k.Target, k.ItemWeight, k.AchievementScore, k.WeightedCalculation, k.Comments)).ToList(),
            form.Competencies.Select(c => new ReportCompRow(c.CompType, c.CompCode, c.CompName, c.ItemWeight, c.AchievementScore, c.WeightedCalculation, c.Comments)).ToList(),
            form.SelfAssessment, form.DevelopmentPlan, form.EmpAckComments,
            form.Hr1ReviewerName, form.Hr1ReviewDate, form.Hr1Remarks,
            form.Hr2ReviewerName, form.Hr2ReviewDate, form.Hr2Remarks,
            form.History.Select(h => new ReportHistoryRow(h.FromStatus, h.ToStatus, h.ChangedBy, h.ChangedAt, h.Note)).ToList(),
            _clock.Now);
    }

    public async Task<DepartmentSummaryReport?> GetDepartmentReportAsync(string deptCode, int evalYear)
    {
        var dept = await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Code == deptCode);
        if (dept is null) return null;

        var rows = await EmployeeRowsAsync(evalYear, e => e.DeptCode == deptCode);
        return new DepartmentSummaryReport(dept.Code, dept.NameEn, evalYear, rows.Rows,
            rows.TotalEmployees, rows.FinalizedCount, rows.AverageScore, _clock.Now);
    }

    public async Task<ManagerSummaryReport?> GetManagerReportAsync(string managerEmpCode, int evalYear)
    {
        var manager = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmpCode == managerEmpCode);
        var managerName = manager?.LatinName ?? managerEmpCode;

        var teamEmpCodes = await _db.ManagerAssignments.AsNoTracking()
            .Where(m => m.ManagerEmpCode == managerEmpCode).Select(m => m.EmpCode).ToListAsync();
        if (teamEmpCodes.Count == 0) return new ManagerSummaryReport(managerEmpCode, managerName, evalYear,
            new List<ReportEmployeeRow>(), 0, 0, 0, _clock.Now);

        var rows = await EmployeeRowsAsync(evalYear, e => teamEmpCodes.Contains(e.EmpCode));
        return new ManagerSummaryReport(managerEmpCode, managerName, evalYear, rows.Rows,
            rows.TotalEmployees, rows.FinalizedCount, rows.AverageScore, _clock.Now);
    }

    public async Task<OverallOrganizationReport> GetOverallReportAsync(int evalYear)
    {
        var totalEmployees = await _db.Employees.AsNoTracking().CountAsync(e => e.TermDate == null);
        var forms = await _db.PmForms.AsNoTracking().Where(f => f.EvalYear == evalYear)
            .Select(f => new { f.Status, f.DeptCode, f.PerformanceScore }).ToListAsync();
        var finalizedCount = forms.Count(f => f.Status == PmFormStatus.Approved);
        var averageScore = forms.Count == 0 ? 0 : Math.Round(forms.Average(f => f.PerformanceScore), 2);

        var depts = await _db.Departments.AsNoTracking().ToListAsync();
        var employeesByDept = await _db.Employees.AsNoTracking()
            .Where(e => e.TermDate == null && e.DeptCode != null)
            .GroupBy(e => e.DeptCode!).Select(g => new { Dept = g.Key, Count = g.Count() }).ToListAsync();
        var employeeCountByDept = employeesByDept.ToDictionary(x => x.Dept, x => x.Count);

        var deptRows = depts
            .Where(d => employeeCountByDept.ContainsKey(d.Code))
            .Select(d =>
            {
                var deptForms = forms.Where(f => f.DeptCode == d.Code).ToList();
                var empCount = employeeCountByDept.GetValueOrDefault(d.Code, 0);
                var finalized = deptForms.Count(f => f.Status == PmFormStatus.Approved);
                var avg = deptForms.Count == 0 ? 0 : Math.Round(deptForms.Average(f => f.PerformanceScore), 2);
                var completion = empCount == 0 ? 0 : finalized * 100 / empCount;
                return new OrgDeptRow(d.Code, d.NameEn, empCount, finalized, avg, completion);
            })
            .OrderByDescending(d => d.EmployeeCount)
            .ToList();

        return new OverallOrganizationReport(evalYear, totalEmployees, forms.Count, finalizedCount, averageScore, deptRows, _clock.Now);
    }

    // ==================================================================
    private async Task<(List<ReportEmployeeRow> Rows, int TotalEmployees, int FinalizedCount, decimal AverageScore)>
        EmployeeRowsAsync(int evalYear, System.Linq.Expressions.Expression<Func<Employee, bool>> employeeFilter)
    {
        var employees = await _db.Employees.AsNoTracking().Where(e => e.TermDate == null).Where(employeeFilter).ToListAsync();
        var empCodes = employees.Select(e => e.EmpCode).ToList();
        var forms = await _db.PmForms.AsNoTracking()
            .Where(f => f.EvalYear == evalYear && empCodes.Contains(f.EmpCode)).ToListAsync();
        var formsByEmp = forms.ToDictionary(f => f.EmpCode);
        var desigs = await _db.Designations.AsNoTracking().ToDictionaryAsync(d => d.Code, d => d.Description);
        var scales = await _db.RatingScales.AsNoTracking().Where(r => r.Status == "A").ToListAsync();

        var rows = employees.Select(e =>
        {
            formsByEmp.TryGetValue(e.EmpCode, out var f);
            var score = f?.PerformanceScore ?? 0;
            var ratingScore = (int)Math.Round(score, MidpointRounding.AwayFromZero);
            var rating = RatingService.Resolve(scales, ratingScore);
            return new ReportEmployeeRow(e.EmpCode, e.LatinName,
                desigs.GetValueOrDefault(e.DesignationCode ?? "", e.DesignationCode ?? ""),
                score, RatingService.RatingName(rating),
                f is null ? "Not Started" : PmFormStatus.DisplayName(f.Status));
        }).OrderBy(r => r.Name).ToList();

        var finalizedCount = forms.Count(f => f.Status == PmFormStatus.Approved);
        var averageScore = forms.Count == 0 ? 0 : Math.Round(forms.Average(f => f.PerformanceScore), 2);
        return (rows, employees.Count, finalizedCount, averageScore);
    }

    private async Task<string?> DeptNameAsync(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : (await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Code == code))?.NameEn ?? code;

    private async Task<string?> DesigNameAsync(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : (await _db.Designations.AsNoTracking().FirstOrDefaultAsync(d => d.Code == code))?.Description ?? code;

    private async Task<string?> EmployeeNameAsync(string? empCode)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return null;
        var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmpCode == empCode.Trim());
        return e?.LatinName ?? empCode;
    }

    private async Task<string> RatingNameAsync(decimal performanceScore)
    {
        var scales = await _db.RatingScales.AsNoTracking().Where(r => r.Status == "A").ToListAsync();
        var rating = RatingService.Resolve(scales, (int)Math.Round(performanceScore, MidpointRounding.AwayFromZero));
        return RatingService.RatingName(rating);
    }
}

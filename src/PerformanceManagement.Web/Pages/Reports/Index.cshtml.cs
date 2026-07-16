using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Web.Pages.Reports;

/// <summary>
/// Administrator-only reporting module: Employee Performance, Department Summary,
/// Manager Summary, and Overall Organization Summary, each exportable to PDF and Excel.
/// Every export handler re-derives its data fresh from <see cref="ReportDataService"/> —
/// nothing is cached, so a report always reflects the current database state.
/// </summary>
[Authorize(Roles = Roles.HrAdmin)]
public class IndexModel : AppPageModel
{
    private readonly PmDbContext _db;
    private readonly IClock _clock;
    private readonly ReportDataService _reports;

    public IndexModel(PmDbContext db, IClock clock, ReportDataService reports)
    {
        _db = db; _clock = clock; _reports = reports;
    }

    public List<(string Code, string Label)> EmployeeOptions { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public List<(string Code, string Label)> ManagerOptions { get; set; } = new();
    public List<int> YearOptions { get; set; } = new();
    public int DefaultYear => _clock.Today.Year;

    [TempData] public string? ReportError { get; set; }

    public async Task OnGetAsync() => await LoadOptionsAsync();

    // ---- Employee Performance Report ----------------------------------------
    public async Task<IActionResult> OnGetEmployeePdfAsync(string? empcd, int year)
    {
        var report = await RequireEmployeeReportAsync(empcd, year);
        if (report is null) return RedirectToPage();
        return PdfFile(ReportExportService.EmployeeReportToPdf(report), $"EmployeePerformanceReport_{empcd}_{year}");
    }

    public async Task<IActionResult> OnGetEmployeeExcelAsync(string? empcd, int year)
    {
        var report = await RequireEmployeeReportAsync(empcd, year);
        if (report is null) return RedirectToPage();
        return ExcelFile(ReportExportService.EmployeeReportToExcel(report), $"EmployeePerformanceReport_{empcd}_{year}");
    }

    // ---- Department Summary --------------------------------------------------
    public async Task<IActionResult> OnGetDepartmentPdfAsync(string? dept, int year)
    {
        var report = await RequireDepartmentReportAsync(dept, year);
        if (report is null) return RedirectToPage();
        return PdfFile(ReportExportService.DepartmentReportToPdf(report), $"DepartmentSummary_{dept}_{year}");
    }

    public async Task<IActionResult> OnGetDepartmentExcelAsync(string? dept, int year)
    {
        var report = await RequireDepartmentReportAsync(dept, year);
        if (report is null) return RedirectToPage();
        return ExcelFile(ReportExportService.DepartmentReportToExcel(report), $"DepartmentSummary_{dept}_{year}");
    }

    // ---- Manager Summary --------------------------------------------------
    public async Task<IActionResult> OnGetManagerPdfAsync(string? manager, int year)
    {
        var report = await RequireManagerReportAsync(manager, year);
        if (report is null) return RedirectToPage();
        return PdfFile(ReportExportService.ManagerReportToPdf(report), $"ManagerSummary_{manager}_{year}");
    }

    public async Task<IActionResult> OnGetManagerExcelAsync(string? manager, int year)
    {
        var report = await RequireManagerReportAsync(manager, year);
        if (report is null) return RedirectToPage();
        return ExcelFile(ReportExportService.ManagerReportToExcel(report), $"ManagerSummary_{manager}_{year}");
    }

    // ---- Overall Organization Summary ----------------------------------------
    public async Task<IActionResult> OnGetOverallPdfAsync(int year)
    {
        var report = await _reports.GetOverallReportAsync(year);
        return PdfFile(ReportExportService.OverallReportToPdf(report), $"OverallOrganizationSummary_{year}");
    }

    public async Task<IActionResult> OnGetOverallExcelAsync(int year)
    {
        var report = await _reports.GetOverallReportAsync(year);
        return ExcelFile(ReportExportService.OverallReportToExcel(report), $"OverallOrganizationSummary_{year}");
    }

    // ======================================================================
    private async Task<EmployeePerformanceReport?> RequireEmployeeReportAsync(string? empcd, int year)
    {
        await LoadOptionsAsync();
        if (string.IsNullOrWhiteSpace(empcd)) { ReportError = "Please select an employee."; return null; }
        var report = await _reports.GetEmployeeReportAsync(empcd, year);
        if (report is null) ReportError = $"No PM form found for employee {empcd} in {year}.";
        return report;
    }

    private async Task<DepartmentSummaryReport?> RequireDepartmentReportAsync(string? dept, int year)
    {
        await LoadOptionsAsync();
        if (string.IsNullOrWhiteSpace(dept)) { ReportError = "Please select a department."; return null; }
        var report = await _reports.GetDepartmentReportAsync(dept, year);
        if (report is null) ReportError = $"Department '{dept}' was not found.";
        return report;
    }

    private async Task<ManagerSummaryReport?> RequireManagerReportAsync(string? manager, int year)
    {
        await LoadOptionsAsync();
        if (string.IsNullOrWhiteSpace(manager)) { ReportError = "Please select a manager."; return null; }
        return await _reports.GetManagerReportAsync(manager, year);
    }

    private FileContentResult PdfFile(byte[] bytes, string fileNameNoExtension) =>
        File(bytes, "application/pdf", $"{fileNameNoExtension}.pdf");

    private FileContentResult ExcelFile(byte[] bytes, string fileNameNoExtension) =>
        File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{fileNameNoExtension}.xlsx");

    private async Task LoadOptionsAsync()
    {
        EmployeeOptions = (await _db.Employees.AsNoTracking().Where(e => e.TermDate == null)
                .OrderBy(e => e.LatinName).ToListAsync())
            .Select(e => (e.EmpCode, $"{e.EmpCode} - {e.LatinName}")).ToList();
        Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.NameEn).ToListAsync();

        var employeeNames = await _db.Employees.AsNoTracking().ToDictionaryAsync(e => e.EmpCode, e => e.LatinName);
        ManagerOptions = (await _db.ManagerAssignments.AsNoTracking()
                .Select(m => m.ManagerEmpCode).Distinct().ToListAsync())
            .OrderBy(m => employeeNames.GetValueOrDefault(m, m))
            .Select(m => (m, $"{m} - {employeeNames.GetValueOrDefault(m, m)}")).ToList();

        YearOptions = await _db.PmForms.AsNoTracking().Select(f => f.EvalYear).Distinct()
            .OrderByDescending(y => y).ToListAsync();
        if (!YearOptions.Contains(DefaultYear)) YearOptions.Insert(0, DefaultYear);
    }
}

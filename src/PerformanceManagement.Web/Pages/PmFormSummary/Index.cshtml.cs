using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Web.Pages.PmFormSummary;

/// <summary>
/// HR tracking summary: one row per HDR per employee/year (never detail rows).
/// HR administrators only — the explicit approved account list, not HR-department membership.
/// </summary>
[Authorize(Roles = Roles.HrAdminOrViewer)]
public class IndexModel : AppPageModel
{
    private readonly PmDbContext _db;
    public IndexModel(PmDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public string? Dept { get; set; }
    [BindProperty(SupportsGet = true)] public string? Empcd { get; set; }
    [BindProperty(SupportsGet = true)] public string? Year { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }

    public List<Department> Departments { get; set; } = new();
    public List<(string Code, string Label)> EmployeeOptions { get; set; } = new();
    public List<int> YearOptions { get; set; } = new();
    public List<Row> Rows { get; set; } = new();
    public string Title { get; set; } = "PM Summary for All Departments";

    public record Row(int Srl, string Employee, string Designation, decimal KpiScore, decimal CompScore,
        decimal OverallScore, int RatingScore, string Rating, string Status, string RefNo,
        string LastUpdated, string EmpCode, string DeptName);

    public async Task OnGetAsync()
    {
        Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.NameEn).ToListAsync();
        YearOptions = await _db.PmForms.AsNoTracking().Select(f => f.EvalYear).Distinct()
            .OrderByDescending(y => y).ToListAsync();

        var empQ = _db.Employees.AsNoTracking().Where(e => e.TermDate == null);
        if (!string.IsNullOrEmpty(Dept)) empQ = empQ.Where(e => e.DeptCode == Dept);
        EmployeeOptions = (await empQ.OrderBy(e => e.JoinDate).ToListAsync())
            .Select(e => (e.EmpCode, $"{e.EmpCode} - {e.LatinName}")).ToList();

        var q = from f in _db.PmForms.AsNoTracking()
                join e in _db.Employees.AsNoTracking() on f.EmpCode equals e.EmpCode
                where e.TermDate == null
                select new { f, e };

        if (!string.IsNullOrEmpty(Dept)) q = q.Where(x => x.e.DeptCode == Dept);
        if (!string.IsNullOrEmpty(Empcd)) q = q.Where(x => x.f.EmpCode == Empcd);
        if (int.TryParse(Year, out var yr)) q = q.Where(x => x.f.EvalYear == yr);
        if (!string.IsNullOrEmpty(Status)) q = q.Where(x => x.f.Status == Status);

        var data = await q.OrderBy(x => x.e.DeptCode).ThenBy(x => x.e.LatinName).ToListAsync();

        var scales = await _db.RatingScales.AsNoTracking().Where(r => r.Status == "A").ToListAsync();
        var depts = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Code, d => d.NameEn);
        var desigs = await _db.Designations.AsNoTracking().ToDictionaryAsync(d => d.Code, d => d.Description);

        var srl = 1;
        foreach (var x in data)
        {
            var ratingScore = (int)Math.Round(x.f.PerformanceScore, MidpointRounding.AwayFromZero);
            var rating = RatingService.Resolve(scales, ratingScore);
            Rows.Add(new Row(
                srl++,
                $"{x.f.EmpCode} - {x.e.LatinName}",
                desigs.GetValueOrDefault(x.e.DesignationCode ?? "", x.e.DesignationCode ?? ""),
                x.f.KpiScore, x.f.CompScore, x.f.PerformanceScore,
                ratingScore,
                rating?.NameEn ?? "N/A",
                PmFormStatus.DisplayName(x.f.Status),
                x.f.LegacyRefNo,
                x.f.UpdatedAt?.ToString("dd/MM/yyyy") ?? "",
                x.f.EmpCode,
                depts.GetValueOrDefault(x.e.DeptCode ?? "", x.e.DeptCode ?? "")));
        }

        Title = "PM Summary for " + (string.IsNullOrEmpty(Dept) ? "All Departments" : depts.GetValueOrDefault(Dept, Dept));
    }
}

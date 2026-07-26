using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Web.Pages.WorkflowAdmin;

/// <summary>
/// Workflow Administration search screen — HR Admin only. Recovery/maintenance console for
/// exceptional situations (a manager who resigned, a wrongly approved review, a stuck
/// workflow, a failed notification) that the normal manager/employee/HR flows on
/// <c>PmForm/Index</c> have no way to handle. See <see cref="WorkflowAdminService"/> for the
/// search/read-model logic and the six administrative override actions themselves, which
/// live on the <see cref="Details.DetailsModel"/> page.
/// </summary>
[Authorize(Roles = Roles.HrAdmin)]
public class IndexModel : AppPageModel
{
    private const int PageSize = 20;

    private readonly PmDbContext _db;
    private readonly WorkflowAdminService _admin;

    public IndexModel(PmDbContext db, WorkflowAdminService admin)
    {
        _db = db; _admin = admin;
    }

    [BindProperty(SupportsGet = true)] public string? Empcd { get; set; }
    [BindProperty(SupportsGet = true)] public string? Empname { get; set; }
    [BindProperty(SupportsGet = true)] public string? Dept { get; set; }
    [BindProperty(SupportsGet = true)] public string? Manager { get; set; }
    [BindProperty(SupportsGet = true)] public string? Year { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    // Query key is "pageNum", not "page": Razor Pages reserves "page" as an AMBIENT ROUTE VALUE
    // (the target page's own path) — both binding a property from it AND generating a link with
    // asp-route-page="N" collide with that reserved value (the composite value provider prefers
    // the route value over the query string on the way in, and LinkGenerator produced an empty
    // href trying to resolve "N" as a page path on the way out). A non-colliding key sidesteps
    // both failure modes; [FromQuery] additionally restricts binding to the query string only.
    [FromQuery(Name = "pageNum")] public int PageNumber { get; set; } = 1;

    public List<Department> Departments { get; set; } = new();
    public List<(string Code, string Label)> ManagerOptions { get; set; } = new();
    public List<int> YearOptions { get; set; } = new();
    public WorkflowAdminSearchResult Result { get; set; } = new(Array.Empty<WorkflowAdminRow>(), 0, 1, PageSize);

    /// <summary>Route-data dictionary for pager links — every current filter value except `page`,
    /// so Prev/Next preserve the search the admin is looking at.</summary>
    public Dictionary<string, string?> FilterRouteData => new()
    {
        ["empcd"] = Empcd, ["empname"] = Empname, ["dept"] = Dept,
        ["manager"] = Manager, ["year"] = Year, ["status"] = Status
    };

    public async Task OnGetAsync()
    {
        Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.NameEn).ToListAsync();
        YearOptions = await _db.PmForms.AsNoTracking().Select(f => f.EvalYear).Distinct()
            .OrderByDescending(y => y).ToListAsync();

        var employeeNames = await _db.Employees.AsNoTracking().ToDictionaryAsync(e => e.EmpCode, e => e.LatinName);
        ManagerOptions = (await _db.ManagerAssignments.AsNoTracking()
                .Select(m => m.ManagerEmpCode).Distinct().ToListAsync())
            .OrderBy(m => employeeNames.GetValueOrDefault(m, m))
            .Select(m => (m, $"{m} - {employeeNames.GetValueOrDefault(m, m)}")).ToList();

        int.TryParse(Year, out var yr);
        var filter = new WorkflowAdminFilter(
            EmpCode: string.IsNullOrWhiteSpace(Empcd) ? null : Empcd,
            EmpName: string.IsNullOrWhiteSpace(Empname) ? null : Empname,
            DeptCode: string.IsNullOrWhiteSpace(Dept) ? null : Dept,
            Manager: string.IsNullOrWhiteSpace(Manager) ? null : Manager,
            EvalYear: yr > 0 ? yr : null,
            Status: string.IsNullOrWhiteSpace(Status) ? null : Status);

        Result = await _admin.SearchAsync(filter, PageNumber, PageSize);
    }
}

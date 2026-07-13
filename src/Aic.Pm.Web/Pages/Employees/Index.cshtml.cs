using Aic.Pm.Core.Data;
using Aic.Pm.Core.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aic.Pm.Web.Pages.Employees;

[Authorize(Roles = Roles.HrAdmin)]
public class IndexModel : AppPageModel
{
    private readonly PmDbContext _db;
    public IndexModel(PmDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Dept { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowInactive { get; set; }

    public List<Department> Departments { get; set; } = new();
    public List<Row> Rows { get; set; } = new();

    public record Row(Employee E, string DeptName, string DesigName, string ManagerLabel);

    public async Task OnGetAsync()
    {
        Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.NameEn).ToListAsync();

        var q = _db.Employees.AsNoTracking().AsQueryable();
        if (!ShowInactive) q = q.Where(e => e.TermDate == null);
        if (!string.IsNullOrEmpty(Dept)) q = q.Where(e => e.DeptCode == Dept);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim().ToLower();
            q = q.Where(e => e.EmpCode.Contains(term) || e.LatinName.ToLower().Contains(term));
        }

        var emps = await q.OrderBy(e => e.EmpCode.PadLeft(6, '0')).ToListAsync();
        var depts = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Code, d => d.NameEn);
        var desigs = await _db.Designations.AsNoTracking().ToDictionaryAsync(d => d.Code, d => d.Description);
        var mgrs = await _db.ManagerAssignments.AsNoTracking().ToDictionaryAsync(m => m.EmpCode, m => m.ManagerEmpCode);
        var names = await _db.Employees.AsNoTracking().ToDictionaryAsync(e => e.EmpCode, e => e.LatinName);

        foreach (var e in emps)
        {
            var mgr = mgrs.GetValueOrDefault(e.EmpCode);
            Rows.Add(new Row(e,
                depts.GetValueOrDefault(e.DeptCode ?? "", e.DeptCode ?? ""),
                desigs.GetValueOrDefault(e.DesignationCode ?? "", e.DesignationCode ?? ""),
                mgr is null ? "—" : $"{mgr} - {names.GetValueOrDefault(mgr, "")}"));
        }
    }
}

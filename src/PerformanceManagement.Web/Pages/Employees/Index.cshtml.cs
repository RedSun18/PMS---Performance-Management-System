using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Web.Pages.Employees;

[Authorize(Roles = Roles.HrAdminOrViewer)]
public class IndexModel : AppPageModel
{
    private readonly PmDbContext _db;
    public IndexModel(PmDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Dept { get; set; }
    /// <summary>"active" (default), "inactive", or "all".</summary>
    [BindProperty(SupportsGet = true)] public string ActiveStatus { get; set; } = "active";

    public List<Department> Departments { get; set; } = new();
    public List<Row> Rows { get; set; } = new();

    public record Row(Employee E, string DeptName, string DesigName, string ManagerLabel, string? UserName);

    public async Task OnGetAsync()
    {
        Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.NameEn).ToListAsync();

        var q = _db.Employees.AsNoTracking().AsQueryable();
        q = ActiveStatus switch
        {
            "inactive" => q.Where(e => e.TermDate != null),
            "all" => q,
            _ => q.Where(e => e.TermDate == null)
        };
        if (!string.IsNullOrEmpty(Dept)) q = q.Where(e => e.DeptCode == Dept);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim().ToLower();
            // Employee/Name/Email search happens in SQL; username lives on AppUser, so resolve
            // matching employee codes first (small result set) and OR it into the same filter.
            var empCodesByUsername = await _db.AppUsers.AsNoTracking()
                .Where(u => u.EmpCode != null && u.UserName.ToLower().Contains(term))
                .Select(u => u.EmpCode!)
                .ToListAsync();
            q = q.Where(e => e.EmpCode.Contains(term) || e.LatinName.ToLower().Contains(term)
                || (e.Email != null && e.Email.ToLower().Contains(term))
                || empCodesByUsername.Contains(e.EmpCode));
        }

        var emps = await q.OrderBy(e => e.EmpCode.PadLeft(6, '0')).ToListAsync();
        var depts = await _db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Code, d => d.NameEn);
        var desigs = await _db.Designations.AsNoTracking().ToDictionaryAsync(d => d.Code, d => d.Description);
        var mgrs = await _db.ManagerAssignments.AsNoTracking().ToDictionaryAsync(m => m.EmpCode, m => m.ManagerEmpCode);
        var names = await _db.Employees.AsNoTracking().ToDictionaryAsync(e => e.EmpCode, e => e.LatinName);
        var users = await _db.AppUsers.AsNoTracking()
            .Where(u => u.EmpCode != null)
            .ToDictionaryAsync(u => u.EmpCode!, u => u.UserName);

        foreach (var e in emps)
        {
            var mgr = mgrs.GetValueOrDefault(e.EmpCode);
            Rows.Add(new Row(e,
                depts.GetValueOrDefault(e.DeptCode ?? "", e.DeptCode ?? ""),
                desigs.GetValueOrDefault(e.DesignationCode ?? "", e.DesignationCode ?? ""),
                mgr is null ? "—" : $"{mgr} - {names.GetValueOrDefault(mgr, "")}",
                users.GetValueOrDefault(e.EmpCode)));
        }
    }
}

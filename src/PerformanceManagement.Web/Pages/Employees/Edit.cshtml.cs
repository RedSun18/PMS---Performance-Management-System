using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Web.Pages.Employees;

[Authorize(Roles = Roles.HrAdmin)]
public class EditModel : AppPageModel
{
    private readonly PmDbContext _db;
    public EditModel(PmDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public string? Empcd { get; set; }
    [BindProperty] public Input Form { get; set; } = new();

    public bool IsNew => string.IsNullOrEmpty(Empcd);
    public List<Department> Departments { get; set; } = new();
    public List<Designation> Designations { get; set; } = new();
    public List<Section> Sections { get; set; } = new();
    public List<(string Code, string Label)> ManagerOptions { get; set; } = new();
    [TempData] public string? Message { get; set; }
    public string? Error { get; set; }

    public class Input
    {
        public string EmpCode { get; set; } = "";
        public string LatinName { get; set; } = "";
        public string? ArabicName { get; set; }
        public string? DeptCode { get; set; }
        public string? SectionCode { get; set; }
        public string? DesignationCode { get; set; }
        public string? Grade { get; set; }
        public DateOnly? JoinDate { get; set; }
        public DateOnly? TermDate { get; set; }
        public string? Email { get; set; }
        public string? ManagerEmpCode { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadListsAsync();
        if (!IsNew)
        {
            var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmpCode == Empcd);
            if (e is null) return RedirectToPage("Index");
            var mgr = await _db.ManagerAssignments.AsNoTracking().FirstOrDefaultAsync(m => m.EmpCode == Empcd);
            Form = new Input
            {
                EmpCode = e.EmpCode, LatinName = e.LatinName, ArabicName = e.ArabicName,
                DeptCode = e.DeptCode, SectionCode = e.SectionCode, DesignationCode = e.DesignationCode,
                Grade = e.Grade, JoinDate = e.JoinDate, TermDate = e.TermDate, Email = e.Email,
                ManagerEmpCode = mgr?.ManagerEmpCode
            };
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadListsAsync();
        var code = (IsNew ? Form.EmpCode : Empcd)?.Trim() ?? "";
        if (code.Length == 0 || string.IsNullOrWhiteSpace(Form.LatinName))
        {
            Error = "Employee code and name are required.";
            return Page();
        }

        var e = await _db.Employees.FirstOrDefaultAsync(x => x.EmpCode == code);
        if (e is null)
        {
            if (!IsNew) return RedirectToPage("Index");
            e = new Employee { EmpCode = code, Source = "MANUAL" };
            _db.Employees.Add(e);
        }
        else if (IsNew)
        {
            Error = $"Employee {code} already exists.";
            return Page();
        }
        else if (e.Source == "HDR_SNAPSHOT")
        {
            e.Source = "MANUAL"; // reviewed & corrected by HR
        }

        e.LatinName = Form.LatinName.Trim();
        e.ArabicName = Form.ArabicName?.Trim();
        e.DeptCode = Form.DeptCode;
        e.SectionCode = Form.SectionCode;
        e.DesignationCode = Form.DesignationCode;
        e.Grade = Form.Grade?.Trim();
        e.JoinDate = Form.JoinDate;
        e.TermDate = Form.TermDate;
        e.Email = Form.Email?.Trim();

        var mgr = await _db.ManagerAssignments.FirstOrDefaultAsync(m => m.EmpCode == code);
        if (string.IsNullOrEmpty(Form.ManagerEmpCode))
        {
            if (mgr is not null) _db.ManagerAssignments.Remove(mgr);
        }
        else if (mgr is null)
        {
            _db.ManagerAssignments.Add(new ManagerAssignment
            {
                EmpCode = code, ManagerEmpCode = Form.ManagerEmpCode, Source = "MANUAL"
            });
        }
        else if (mgr.ManagerEmpCode != Form.ManagerEmpCode)
        {
            mgr.ManagerEmpCode = Form.ManagerEmpCode;
            mgr.Source = "MANUAL";
        }

        await _db.SaveChangesAsync();
        Message = $"Employee {code} saved.";
        return RedirectToPage("Index");
    }

    private async Task LoadListsAsync()
    {
        Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.NameEn).ToListAsync();
        Designations = await _db.Designations.AsNoTracking().OrderBy(d => d.Code).ToListAsync();
        Sections = await _db.Sections.AsNoTracking().OrderBy(s => s.Code).ToListAsync();
        ManagerOptions = (await _db.Employees.AsNoTracking().Where(e => e.TermDate == null)
                .OrderBy(e => e.LatinName).ToListAsync())
            .Select(e => (e.EmpCode, $"{e.EmpCode} - {e.LatinName}")).ToList();
    }
}

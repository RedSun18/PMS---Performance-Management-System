using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace PerformanceManagement.Web.Pages.ReferenceMaster;

/// <summary>
/// Reference Master (legacy EmpREFMaster PM-scope tabs): KPI Master, Competency Master,
/// and the KPI reference codes (job families + rating scales). Legacy leave/loan/deduction
/// tabs are outside PM scope.
/// </summary>
[Authorize(Roles = Roles.HrAdminOrViewer)]
public class IndexModel : AppPageModel
{
    private readonly PmDbContext _db;
    private readonly IClock _clock;
    private readonly AuditService _audit;
    private readonly NotificationService _notifications;
    private readonly IStringLocalizer<IndexModel> _localizer;
    public IndexModel(PmDbContext db, IClock clock, AuditService audit, NotificationService notifications,
        IStringLocalizer<IndexModel> localizer)
    {
        _db = db; _clock = clock; _audit = audit; _notifications = notifications; _localizer = localizer;
    }

    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "kpi";
    [BindProperty(SupportsGet = true)] public string? Perspective { get; set; }
    [BindProperty(SupportsGet = true)] public string? DeptFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? CompType { get; set; }
    [BindProperty(SupportsGet = true)] public string? EditId { get; set; }
    [BindProperty(SupportsGet = true)] public string? KpiQuery { get; set; }
    [BindProperty(SupportsGet = true)] public string? CompQuery { get; set; }
    [BindProperty(SupportsGet = true)] public string? DeptQuery { get; set; }
    [BindProperty(SupportsGet = true)] public string? DeptEditCode { get; set; }
    [BindProperty(SupportsGet = true)] public string? SectionQuery { get; set; }
    [BindProperty(SupportsGet = true)] public string? SectionEditCode { get; set; }
    [BindProperty(SupportsGet = true)] public string? DesigQuery { get; set; }
    [BindProperty(SupportsGet = true)] public string? DesigEditCode { get; set; }

    public List<KpiMaster> Kpis { get; set; } = new();
    public List<CompetencyMaster> Comps { get; set; } = new();
    public List<JobFamily> JobFamilies { get; set; } = new();
    public List<RatingScale> RatingScales { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public List<Department> DeptRows { get; set; } = new();
    public List<Section> SectionRows { get; set; } = new();
    public List<Designation> DesigRows { get; set; } = new();
    public KpiMaster? KpiEdit { get; set; }
    public CompetencyMaster? CompEdit { get; set; }
    public Department? DeptEdit { get; set; }
    public Section? SectionEdit { get; set; }
    public Designation? DesigEdit { get; set; }
    public string NextKpiId { get; set; } = "";
    public string NextCompId { get; set; } = "";

    [TempData] public string? Message { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public async Task OnGetAsync()
    {
        Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.NameEn).ToListAsync();

        switch (Tab)
        {
            case "kpi":
                var kq = _db.KpiMasters.AsNoTracking().AsQueryable();
                if (!string.IsNullOrEmpty(Perspective)) kq = kq.Where(k => k.Perspective == Perspective);
                if (!string.IsNullOrWhiteSpace(KpiQuery))
                {
                    var term = KpiQuery.Trim().ToLower();
                    kq = kq.Where(k => k.KpiId.ToLower().Contains(term) || k.Name.ToLower().Contains(term));
                }
                Kpis = (await kq.OrderByDescending(k => k.ModifiedDate).ThenBy(k => k.KpiId).ToListAsync())
                    .Where(k => string.IsNullOrEmpty(DeptFilter) || k.AppliesToDept(DeptFilter)).ToList();
                KpiEdit = EditId is null ? null : await _db.KpiMasters.AsNoTracking().FirstOrDefaultAsync(k => k.KpiId == EditId);
                NextKpiId = await NextIdAsync("KPI", _db.KpiMasters.Select(k => k.KpiId));
                break;
            case "comp":
                var cq = _db.CompetencyMasters.AsNoTracking().AsQueryable();
                if (!string.IsNullOrEmpty(CompType)) cq = cq.Where(c => c.CompType == CompType);
                if (!string.IsNullOrWhiteSpace(CompQuery))
                {
                    var term = CompQuery.Trim().ToLower();
                    cq = cq.Where(c => c.CompId.ToLower().Contains(term) || c.Name.ToLower().Contains(term));
                }
                Comps = await cq.OrderByDescending(c => c.ModifiedDate).ThenBy(c => c.CompId).ToListAsync();
                CompEdit = EditId is null ? null : await _db.CompetencyMasters.AsNoTracking().FirstOrDefaultAsync(c => c.CompId == EditId);
                NextCompId = await NextIdAsync("COM", _db.CompetencyMasters.Select(c => c.CompId));
                break;
            case "dept":
                var dq = _db.Departments.AsNoTracking().AsQueryable();
                if (!string.IsNullOrWhiteSpace(DeptQuery))
                {
                    var term = DeptQuery.Trim().ToLower();
                    dq = dq.Where(d => d.Code.ToLower().Contains(term) || d.NameEn.ToLower().Contains(term)
                        || (d.Description != null && d.Description.ToLower().Contains(term)));
                }
                DeptRows = await dq.OrderBy(d => d.NameEn).ToListAsync();
                DeptEdit = DeptEditCode is null ? null : await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Code == DeptEditCode);
                break;
            case "sections":
                var secq = _db.Sections.AsNoTracking().AsQueryable();
                if (!string.IsNullOrWhiteSpace(SectionQuery))
                {
                    var term = SectionQuery.Trim().ToLower();
                    secq = secq.Where(s => s.Code.ToLower().Contains(term) || s.Description.ToLower().Contains(term));
                }
                SectionRows = await secq.OrderBy(s => s.Description).ToListAsync();
                SectionEdit = SectionEditCode is null ? null : await _db.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.Code == SectionEditCode);
                break;
            case "designations":
                var desq = _db.Designations.AsNoTracking().AsQueryable();
                if (!string.IsNullOrWhiteSpace(DesigQuery))
                {
                    var term = DesigQuery.Trim().ToLower();
                    desq = desq.Where(d => d.Code.ToLower().Contains(term) || d.Description.ToLower().Contains(term));
                }
                DesigRows = await desq.OrderBy(d => d.Description).ToListAsync();
                DesigEdit = DesigEditCode is null ? null : await _db.Designations.AsNoTracking().FirstOrDefaultAsync(d => d.Code == DesigEditCode);
                break;
            default:
                JobFamilies = await _db.JobFamilies.AsNoTracking().OrderBy(j => j.Code).ToListAsync();
                RatingScales = await _db.RatingScales.AsNoTracking().OrderBy(r => r.MinScore).ToListAsync();
                break;
        }
    }

    /// <summary>Legacy GenerateNextKPICode: prefix + next number, 3-digit padded.</summary>
    private static async Task<string> NextIdAsync(string prefix, IQueryable<string> ids)
    {
        var all = await ids.ToListAsync();
        var next = all
            .Where(i => i.StartsWith(prefix) && int.TryParse(i[prefix.Length..], out _))
            .Select(i => int.Parse(i[prefix.Length..]))
            .DefaultIfEmpty(0).Max() + 1;
        return $"{prefix}{next:000}";
    }

    public async Task<IActionResult> OnPostSaveKpiAsync(KpiMaster input)
    {
        if (RequireHrAdmin() is { } denied) return denied;

        var id = input.KpiId.Trim();
        if (id.Length == 0 || string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.Perspective))
        {
            ErrorMessage = _localizer["KpiRequiredFieldsError"];
            return RedirectToPage(new { Tab = "kpi" });
        }

        var e = await _db.KpiMasters.FindAsync(id);
        var isNew = e is null;
        if (e is null) { e = new KpiMaster { KpiId = id, CreatedBy = CurrentUserName, CreatedDate = _clock.Today }; _db.KpiMasters.Add(e); }

        e.Name = input.Name.Trim();
        e.NameAr = input.NameAr;
        e.Perspective = input.Perspective.Trim();
        e.PerspectiveDesc = input.Perspective.Trim() switch
        { "F" => "Financial", "C" => "Customer", "I" => "Internal Processes", "L" => "Learning & Growth", _ => "" };
        e.Description = input.Description;
        e.DescriptionAr = input.DescriptionAr;
        e.Formula = input.Formula;
        e.FormulaAr = input.FormulaAr;
        e.DeptCsv = string.IsNullOrWhiteSpace(input.DeptCsv) ? "*" : input.DeptCsv.Trim();
        e.MinWeight = input.MinWeight;
        e.MaxWeight = input.MaxWeight;
        e.WeightRange = $"{input.MinWeight}-{input.MaxWeight}%";
        e.Status = input.Status is "I" ? "I" : "A";
        e.Remarks = input.Remarks;
        e.ModifiedBy = CurrentUserName;
        e.ModifiedDate = _clock.Today;
        e.ModifiedTime = _clock.Now.ToString("HH:mm");

        await _db.SaveChangesAsync();
        Message = isNew ? _localizer["KpiSavedMessage"] : _localizer["KpiUpdatedMessage"];
        return RedirectToPage(new { Tab = "kpi" });
    }

    public async Task<IActionResult> OnPostSaveCompAsync(CompetencyMaster input)
    {
        if (RequireHrAdmin() is { } denied) return denied;

        var id = input.CompId.Trim();
        if (id.Length == 0 || string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.CompType))
        {
            ErrorMessage = _localizer["CompRequiredFieldsError"];
            return RedirectToPage(new { Tab = "comp" });
        }

        var e = await _db.CompetencyMasters.FindAsync(id);
        var isNew = e is null;
        if (e is null) { e = new CompetencyMaster { CompId = id, CreatedBy = CurrentUserName, CreatedDate = _clock.Today }; _db.CompetencyMasters.Add(e); }

        e.Name = input.Name.Trim();
        e.NameAr = input.NameAr;
        e.CompType = input.CompType.Trim();
        e.TypeDesc = input.CompType.Trim() == "T" ? "Technical" : "Behavioral";
        e.Description = input.Description;
        e.DescriptionAr = input.DescriptionAr;
        e.DeptCsv = string.IsNullOrWhiteSpace(input.DeptCsv) ? "*" : input.DeptCsv.Trim();
        e.MinWeight = input.MinWeight;
        e.MaxWeight = input.MaxWeight;
        e.WeightRange = $"{input.MinWeight}-{input.MaxWeight}%";
        e.Status = input.Status is "I" ? "I" : "A";
        e.Remarks = input.Remarks;
        e.ModifiedBy = CurrentUserName;
        e.ModifiedDate = _clock.Today;
        e.ModifiedTime = _clock.Now.ToString("HH:mm");

        await _db.SaveChangesAsync();
        Message = isNew ? _localizer["CompSavedMessage"] : _localizer["CompUpdatedMessage"];
        return RedirectToPage(new { Tab = "comp" });
    }

    public async Task<IActionResult> OnPostSaveDepartmentAsync(string code, string nameEn, string? description, bool isActive, bool isNew)
    {
        if (RequireHrAdmin() is { } denied) return denied;

        code = (code ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0 || string.IsNullOrWhiteSpace(nameEn))
        {
            ErrorMessage = _localizer["DeptRequiredFieldsError"];
            return RedirectToPage(new { Tab = "dept" });
        }

        var dept = await _db.Departments.FindAsync(code);
        if (isNew)
        {
            if (dept is not null)
            {
                ErrorMessage = _localizer["DeptCodeInUseError", code];
                return RedirectToPage(new { Tab = "dept" });
            }
            dept = new Department { Code = code };
            _db.Departments.Add(dept);
        }
        else if (dept is null)
        {
            ErrorMessage = _localizer["DeptNotFoundError", code];
            return RedirectToPage(new { Tab = "dept" });
        }

        dept.NameEn = nameEn.Trim();
        dept.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        dept.IsActive = isActive;

        await _db.SaveChangesAsync();
        await _audit.LogAsync(isNew ? "Department Created" : "Department Updated", CurrentUserName,
            deptCode: code, entityType: "Department", entityId: code, details: dept.NameEn);
        await NotifyHrAdminsAsync(isNew ? "Department Created" : "Department Updated",
            $"{code} — {dept.NameEn}", "DepartmentUpdated");
        Message = isNew ? _localizer["DeptCreatedMessage", code] : _localizer["DeptUpdatedMessage", code];
        return RedirectToPage(new { Tab = "dept" });
    }

    public async Task<IActionResult> OnPostToggleDepartmentAsync(string code)
    {
        if (RequireHrAdmin() is { } denied) return denied;

        var dept = await _db.Departments.FindAsync(code);
        if (dept is null)
        {
            ErrorMessage = _localizer["DeptToggleNotFoundError"];
            return RedirectToPage(new { Tab = "dept" });
        }

        dept.IsActive = !dept.IsActive;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(dept.IsActive ? "Department Enabled" : "Department Disabled", CurrentUserName,
            deptCode: code, entityType: "Department", entityId: code, details: dept.NameEn);
        await NotifyHrAdminsAsync(dept.IsActive ? "Department Enabled" : "Department Disabled",
            $"{code} — {dept.NameEn}", "DepartmentUpdated");
        Message = dept.IsActive ? _localizer["DeptEnabledMessage", code] : _localizer["DeptDisabledMessage", code];
        return RedirectToPage(new { Tab = "dept" });
    }

    private async Task NotifyHrAdminsAsync(string title, string? message, string type)
    {
        var userNames = await _db.UserRoles.AsNoTracking().Where(r => r.Role == Roles.HrAdmin)
            .Include(r => r.AppUser).Select(r => r.AppUser!)
            .Where(u => u.IsActive).Select(u => u.UserName).Distinct().ToListAsync();
        foreach (var userName in userNames)
            await _notifications.CreateAsync(userName, title, message, type);
    }

    public async Task<IActionResult> OnPostSaveJobFamilyAsync(string code, string nameEn, string gradesCsv, int kpiWeight, int compWeight)
    {
        if (RequireHrAdmin() is { } denied) return denied;

        var e = await _db.JobFamilies.FindAsync(code);
        if (e is null) { ErrorMessage = _localizer["JobFamilyNotFoundError", code]; return RedirectToPage(new { Tab = "ref" }); }
        if (kpiWeight + compWeight != 100)
        {
            ErrorMessage = _localizer["JobFamilyWeightError"];
            return RedirectToPage(new { Tab = "ref" });
        }
        e.NameEn = nameEn.Trim();
        e.GradesCsv = gradesCsv.Trim();
        e.KpiWeight = kpiWeight;
        e.CompWeight = compWeight;
        await _db.SaveChangesAsync();
        Message = _localizer["JobFamilyUpdatedMessage", code];
        return RedirectToPage(new { Tab = "ref" });
    }

    public async Task<IActionResult> OnPostCreateJobFamilyAsync(string code, string nameEn, string gradesCsv, int kpiWeight, int compWeight)
    {
        if (RequireHrAdmin() is { } denied) return denied;

        code = (code ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0 || string.IsNullOrWhiteSpace(nameEn) || string.IsNullOrWhiteSpace(gradesCsv))
        {
            ErrorMessage = _localizer["JobFamilyRequiredFieldsError"];
            return RedirectToPage(new { Tab = "ref" });
        }
        if (kpiWeight + compWeight != 100)
        {
            ErrorMessage = _localizer["JobFamilyWeightError"];
            return RedirectToPage(new { Tab = "ref" });
        }
        if (await _db.JobFamilies.FindAsync(code) is not null)
        {
            ErrorMessage = _localizer["JobFamilyCodeInUseError", code];
            return RedirectToPage(new { Tab = "ref" });
        }

        _db.JobFamilies.Add(new JobFamily
        {
            Code = code, NameEn = nameEn.Trim(), GradesCsv = gradesCsv.Trim(),
            KpiWeight = kpiWeight, CompWeight = compWeight, Status = "A"
        });
        await _db.SaveChangesAsync();
        Message = _localizer["JobFamilyCreatedMessage", code];
        return RedirectToPage(new { Tab = "ref" });
    }

    public async Task<IActionResult> OnPostDeleteJobFamilyAsync(string code)
    {
        if (RequireHrAdmin() is { } denied) return denied;

        var family = await _db.JobFamilies.FindAsync(code);
        if (family is null) { ErrorMessage = _localizer["JobFamilyNotFoundError", code]; return RedirectToPage(new { Tab = "ref" }); }

        var grades = family.Grades.ToHashSet();
        var inUse = await _db.Employees.AnyAsync(e => e.Grade != null && grades.Contains(e.Grade));
        if (inUse)
        {
            ErrorMessage = _localizer["JobFamilyInUseError", code];
            return RedirectToPage(new { Tab = "ref" });
        }

        _db.JobFamilies.Remove(family);
        await _db.SaveChangesAsync();
        Message = _localizer["JobFamilyDeletedMessage", code];
        return RedirectToPage(new { Tab = "ref" });
    }

    public async Task<IActionResult> OnPostSaveRatingScaleAsync(string code, string nameEn, string? nameAr, int minScore, int maxScore, string? remarks, bool isNew)
    {
        if (RequireHrAdmin() is { } denied) return denied;

        code = (code ?? "").Trim();
        if (code.Length == 0 || string.IsNullOrWhiteSpace(nameEn) || minScore > maxScore)
        {
            ErrorMessage = _localizer["RatingScaleRequiredFieldsError"];
            return RedirectToPage(new { Tab = "ref" });
        }

        var row = await _db.RatingScales.FindAsync(code);
        if (isNew)
        {
            if (row is not null)
            {
                ErrorMessage = _localizer["RatingScaleCodeInUseError", code];
                return RedirectToPage(new { Tab = "ref" });
            }
            row = new RatingScale { Code = code, Status = "A" };
            _db.RatingScales.Add(row);
        }
        else if (row is null)
        {
            ErrorMessage = _localizer["RatingScaleNotFoundError", code];
            return RedirectToPage(new { Tab = "ref" });
        }

        row.NameEn = nameEn.Trim();
        row.NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr.Trim();
        row.MinScore = minScore;
        row.MaxScore = maxScore;
        row.Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();
        await _db.SaveChangesAsync();
        Message = isNew ? _localizer["RatingScaleCreatedMessage", code] : _localizer["RatingScaleUpdatedMessage", code];
        return RedirectToPage(new { Tab = "ref" });
    }

    public async Task<IActionResult> OnPostDeleteRatingScaleAsync(string code)
    {
        if (RequireHrAdmin() is { } denied) return denied;

        var inUse = await _db.PmForms.AnyAsync(f => f.OverallRatingCode == code);
        if (inUse)
        {
            ErrorMessage = _localizer["RatingScaleInUseError", code];
            return RedirectToPage(new { Tab = "ref" });
        }

        var row = await _db.RatingScales.FindAsync(code);
        if (row is not null) { _db.RatingScales.Remove(row); await _db.SaveChangesAsync(); }
        Message = _localizer["RatingScaleDeletedMessage", code];
        return RedirectToPage(new { Tab = "ref" });
    }

    public async Task<IActionResult> OnPostSaveSectionAsync(string code, string description, string? descriptionAr, bool isNew)
    {
        if (RequireHrAdmin() is { } denied) return denied;

        code = (code ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0 || code.Length > 5 || string.IsNullOrWhiteSpace(description))
        {
            ErrorMessage = _localizer["SectionRequiredFieldsError"];
            return RedirectToPage(new { Tab = "sections" });
        }

        var row = await _db.Sections.FindAsync(code);
        if (isNew)
        {
            if (row is not null)
            {
                ErrorMessage = _localizer["SectionCodeInUseError", code];
                return RedirectToPage(new { Tab = "sections" });
            }
            row = new Section { Code = code };
            _db.Sections.Add(row);
        }
        else if (row is null)
        {
            ErrorMessage = _localizer["SectionNotFoundError", code];
            return RedirectToPage(new { Tab = "sections" });
        }

        row.Description = description.Trim();
        row.DescriptionAr = string.IsNullOrWhiteSpace(descriptionAr) ? null : descriptionAr.Trim();
        await _db.SaveChangesAsync();
        Message = isNew ? _localizer["SectionCreatedMessage", code] : _localizer["SectionUpdatedMessage", code];
        return RedirectToPage(new { Tab = "sections" });
    }

    public async Task<IActionResult> OnPostDeleteSectionAsync(string code)
    {
        if (RequireHrAdmin() is { } denied) return denied;

        var inUse = await _db.Employees.AnyAsync(e => e.SectionCode == code);
        if (inUse)
        {
            ErrorMessage = _localizer["SectionInUseError", code];
            return RedirectToPage(new { Tab = "sections" });
        }

        var row = await _db.Sections.FindAsync(code);
        if (row is not null) { _db.Sections.Remove(row); await _db.SaveChangesAsync(); }
        Message = _localizer["SectionDeletedMessage", code];
        return RedirectToPage(new { Tab = "sections" });
    }

    public async Task<IActionResult> OnPostSaveDesignationAsync(string code, string description, string? descriptionAr, bool isNew)
    {
        if (RequireHrAdmin() is { } denied) return denied;

        code = (code ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0 || code.Length > 5 || string.IsNullOrWhiteSpace(description))
        {
            ErrorMessage = _localizer["DesigRequiredFieldsError"];
            return RedirectToPage(new { Tab = "designations" });
        }

        var row = await _db.Designations.FindAsync(code);
        if (isNew)
        {
            if (row is not null)
            {
                ErrorMessage = _localizer["DesigCodeInUseError", code];
                return RedirectToPage(new { Tab = "designations" });
            }
            row = new Designation { Code = code };
            _db.Designations.Add(row);
        }
        else if (row is null)
        {
            ErrorMessage = _localizer["DesigNotFoundError", code];
            return RedirectToPage(new { Tab = "designations" });
        }

        row.Description = description.Trim();
        row.DescriptionAr = string.IsNullOrWhiteSpace(descriptionAr) ? null : descriptionAr.Trim();
        await _db.SaveChangesAsync();
        Message = isNew ? _localizer["DesigCreatedMessage", code] : _localizer["DesigUpdatedMessage", code];
        return RedirectToPage(new { Tab = "designations" });
    }

    public async Task<IActionResult> OnPostDeleteDesignationAsync(string code)
    {
        if (RequireHrAdmin() is { } denied) return denied;

        var inUse = await _db.Employees.AnyAsync(e => e.DesignationCode == code);
        if (inUse)
        {
            ErrorMessage = _localizer["DesigInUseError", code];
            return RedirectToPage(new { Tab = "designations" });
        }

        var row = await _db.Designations.FindAsync(code);
        if (row is not null) { _db.Designations.Remove(row); await _db.SaveChangesAsync(); }
        Message = _localizer["DesigDeletedMessage", code];
        return RedirectToPage(new { Tab = "designations" });
    }
}

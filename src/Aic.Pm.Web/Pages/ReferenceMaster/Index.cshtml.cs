using Aic.Pm.Core.Data;
using Aic.Pm.Core.Domain;
using Aic.Pm.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aic.Pm.Web.Pages.ReferenceMaster;

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
    public IndexModel(PmDbContext db, IClock clock) { _db = db; _clock = clock; }

    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "kpi";
    [BindProperty(SupportsGet = true)] public string? Perspective { get; set; }
    [BindProperty(SupportsGet = true)] public string? DeptFilter { get; set; }
    [BindProperty(SupportsGet = true)] public string? CompType { get; set; }
    [BindProperty(SupportsGet = true)] public string? EditId { get; set; }

    public List<KpiMaster> Kpis { get; set; } = new();
    public List<CompetencyMaster> Comps { get; set; } = new();
    public List<JobFamily> JobFamilies { get; set; } = new();
    public List<RatingScale> RatingScales { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public KpiMaster? KpiEdit { get; set; }
    public CompetencyMaster? CompEdit { get; set; }
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
                Kpis = (await kq.OrderByDescending(k => k.ModifiedDate).ThenBy(k => k.KpiId).ToListAsync())
                    .Where(k => string.IsNullOrEmpty(DeptFilter) || k.AppliesToDept(DeptFilter)).ToList();
                KpiEdit = EditId is null ? null : await _db.KpiMasters.AsNoTracking().FirstOrDefaultAsync(k => k.KpiId == EditId);
                NextKpiId = await NextIdAsync("KPI", _db.KpiMasters.Select(k => k.KpiId));
                break;
            case "comp":
                var cq = _db.CompetencyMasters.AsNoTracking().AsQueryable();
                if (!string.IsNullOrEmpty(CompType)) cq = cq.Where(c => c.CompType == CompType);
                Comps = await cq.OrderByDescending(c => c.ModifiedDate).ThenBy(c => c.CompId).ToListAsync();
                CompEdit = EditId is null ? null : await _db.CompetencyMasters.AsNoTracking().FirstOrDefaultAsync(c => c.CompId == EditId);
                NextCompId = await NextIdAsync("COM", _db.CompetencyMasters.Select(c => c.CompId));
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
            ErrorMessage = "KPI code, name and perspective are required.";
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
        Message = isNew ? "KPI Successfully Saved!" : "KPI Successfully Updated!";
        return RedirectToPage(new { Tab = "kpi" });
    }

    public async Task<IActionResult> OnPostSaveCompAsync(CompetencyMaster input)
    {
        if (RequireHrAdmin() is { } denied) return denied;

        var id = input.CompId.Trim();
        if (id.Length == 0 || string.IsNullOrWhiteSpace(input.Name) || string.IsNullOrWhiteSpace(input.CompType))
        {
            ErrorMessage = "Competency code, name and type are required.";
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
        Message = isNew ? "Competency Successfully Saved!" : "Competency Successfully Updated!";
        return RedirectToPage(new { Tab = "comp" });
    }

    public async Task<IActionResult> OnPostSaveJobFamilyAsync(string code, string nameEn, string gradesCsv, int kpiWeight, int compWeight)
    {
        if (RequireHrAdmin() is { } denied) return denied;

        var e = await _db.JobFamilies.FindAsync(code);
        if (e is null) { ErrorMessage = $"Job family {code} not found."; return RedirectToPage(new { Tab = "ref" }); }
        if (kpiWeight + compWeight != 100)
        {
            ErrorMessage = "KPI weight + Competency weight must total 100.";
            return RedirectToPage(new { Tab = "ref" });
        }
        e.NameEn = nameEn.Trim();
        e.GradesCsv = gradesCsv.Trim();
        e.KpiWeight = kpiWeight;
        e.CompWeight = compWeight;
        await _db.SaveChangesAsync();
        Message = $"Job family {code} updated.";
        return RedirectToPage(new { Tab = "ref" });
    }
}

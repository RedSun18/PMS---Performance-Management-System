using Aic.Pm.Core.Data;
using Aic.Pm.Core.Domain;
using Aic.Pm.Core.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aic.Pm.Web.Pages.PmForm;

public class IndexModel : AppPageModel
{
    private readonly PmDbContext _db;
    private readonly IClock _clock;
    private readonly PermissionService _permissions;
    private readonly WorkflowService _workflow;
    private readonly JobFamilyService _jobFamilies;
    private readonly RatingService _ratings;
    private readonly AchievementGate _gate;

    public IndexModel(PmDbContext db, IClock clock, PermissionService permissions,
        WorkflowService workflow, JobFamilyService jobFamilies, RatingService ratings, AchievementGate gate)
    {
        _db = db; _clock = clock; _permissions = permissions;
        _workflow = workflow; _jobFamilies = jobFamilies; _ratings = ratings; _gate = gate;
    }

    // ---- selection state ------------------------------------------------
    [BindProperty(SupportsGet = true)] public string? Dept { get; set; }
    [BindProperty(SupportsGet = true)] public string? Empcd { get; set; }
    [BindProperty(SupportsGet = true)] public int? Year { get; set; }
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "kpi";
    [BindProperty(SupportsGet = true)] public int? EditKpi { get; set; }
    [BindProperty(SupportsGet = true)] public int? EditComp { get; set; }

    public int EvalYear => Year ?? _clock.Today.Year;

    // ---- view data -------------------------------------------------------
    public List<Department> Departments { get; set; } = new();
    public List<(string Code, string Label)> EmployeeOptions { get; set; } = new();
    public List<int> YearOptions { get; set; } = new();
    public bool CanChangeDept { get; set; }
    public bool CanChangeEmployee { get; set; }

    public Employee? SelectedEmployee { get; set; }
    public string ManagerName { get; set; } = "";
    public string ManagerCode { get; set; } = "";
    public string DeptName { get; set; } = "";
    public string SectionName { get; set; } = "";
    public string DesignationName { get; set; } = "";
    public string JobFamilyName { get; set; } = "";
    public int KpiWeightTotal { get; set; }
    public int CompWeightTotal { get; set; }
    public bool JobFamilyConfigured { get; set; }
    public string RefNo { get; set; } = "";

    public Aic.Pm.Core.Domain.PmForm? Form { get; set; }
    public string Status { get; set; } = PmFormStatus.Draft;
    public FormPermissions? Perms { get; set; }
    public WorkingSet Work { get; set; } = new();
    public bool AchievementOpen { get; set; }
    public bool ShowKpiTab { get; set; } = true;

    public List<KpiMaster> KpiOptions { get; set; } = new();
    public List<CompetencyMaster> CompOptions { get; set; } = new();
    public WorkingSet.Item? KpiBeingEdited { get; set; }
    public WorkingSet.Item? CompBeingEdited { get; set; }

    public string RatingName { get; set; } = "";
    private List<RatingScale> _scales = new();
    /// <summary>Row-level rating for an achievement %, from the cached rating scales.</summary>
    public string RatingFor(int score) => RatingService.Resolve(_scales, score)?.NameEn ?? "Not Rated";
    public decimal KpiScore { get; set; }
    public decimal CompScore { get; set; }
    public decimal OverallScore { get; set; }
    public int ProgressPercent { get; set; }
    public List<string> KpiValidationMessages { get; set; } = new();
    public List<string> CompValidationMessages { get; set; } = new();

    [TempData] public string? Message { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    // ======================================================================
    public async Task<IActionResult> OnGetAsync(bool keep = false)
    {
        await BuildSelectionListsAsync();

        // Regular employees always land on their own form (legacy auto-load)
        if (!IsHrAdmin && !await _permissions.IsAManagerAsync(CurrentEmpCode) &&
            !await _permissions.HasExceptionAsync(CurrentEmpCode, ExceptionRule.BranchViewer))
        {
            if (string.IsNullOrEmpty(Empcd)) Empcd = CurrentEmpCode;
            if (Empcd != CurrentEmpCode)
            {
                ErrorMessage = "Access Denied: You can only view your own form.";
                Empcd = CurrentEmpCode;
            }
        }

        if (string.IsNullOrWhiteSpace(Empcd)) return Page();

        SelectedEmployee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmpCode == Empcd);
        if (SelectedEmployee is null)
        {
            ErrorMessage = $"Employee {Empcd} not found.";
            Empcd = null;
            return Page();
        }

        Perms = await _permissions.GetFormPermissionsAsync(CurrentUserName, CurrentEmpCode, Empcd);
        if (!Perms.CanView)
        {
            ErrorMessage = "Access Denied: You are not the designated direct manager for this employee.";
            Empcd = null;
            SelectedEmployee = null;
            return Page();
        }

        await LoadFormViewAsync(keep);
        return Page();
    }

    // ---- selection postbacks --------------------------------------------
    public IActionResult OnPostSelect(string? dept, string? empcd, int? year) =>
        RedirectToPage(new { Dept = dept, Empcd = empcd, Year = year });

    // ---- KPI item handlers (session working set, like legacy grids) ------
    public async Task<IActionResult> OnPostAddKpiAsync(string perspective, string kpiCode, string? target,
        int weight, int? achievement, string? comments, int? editingSeq)
    {
        var redirect = RedirectKeep("kpi");
        if (!await CanEditItemsAsync()) return redirect;

        var master = await _db.KpiMasters.AsNoTracking().FirstOrDefaultAsync(k => k.KpiId == kpiCode);
        if (master is null) { ErrorMessage = "Please select a KPI."; return redirect; }
        if (string.IsNullOrWhiteSpace(perspective)) { ErrorMessage = "Please select a Perspective."; return redirect; }
        if (string.IsNullOrWhiteSpace(target)) { ErrorMessage = "Please enter a Target."; return redirect; }
        if (weight <= 0) { ErrorMessage = "Please enter KPI Weight."; return redirect; }

        var work = await GetWorkAsync();
        var form = ToValidationForm(work);
        var errors = FormValidationService.ValidateKpiItem(form, master, weight, editingSeq);
        if (errors.Count > 0) { ErrorMessage = string.Join(" ", errors); return redirect; }

        var item = editingSeq is int seq ? work.Kpis.FirstOrDefault(k => k.Seq == seq) : null;
        if (item is null)
        {
            item = new WorkingSet.Item { Seq = work.Kpis.Count == 0 ? 1 : work.Kpis.Max(k => k.Seq) + 1 };
            work.Kpis.Add(item);
        }
        item.Kind = perspective.Trim();
        item.Code = master.KpiId;
        item.Name = master.Name;
        item.Definition = master.Description;
        item.Formula = master.Formula;
        item.Target = target;
        item.Weight = weight;
        item.Achievement = _gate.NormalizeAchievement(EvalYear, achievement);
        item.Comments = comments;

        work.Save(HttpContext.Session);
        return redirect;
    }

    public async Task<IActionResult> OnPostDeleteKpiAsync(int seq)
    {
        var redirect = RedirectKeep("kpi");
        if (!await CanEditItemsAsync()) return redirect;
        var work = await GetWorkAsync();
        work.Kpis.RemoveAll(k => k.Seq == seq);
        work.Resequence();
        work.Save(HttpContext.Session);
        return redirect;
    }

    // ---- Competency item handlers -----------------------------------------
    public async Task<IActionResult> OnPostAddCompAsync(string compType, string compCode,
        int weight, int? achievement, string? comments, int? editingSeq)
    {
        var redirect = RedirectKeep("comp");
        if (!await CanEditItemsAsync()) return redirect;

        var master = await _db.CompetencyMasters.AsNoTracking().FirstOrDefaultAsync(c => c.CompId == compCode);
        if (master is null) { ErrorMessage = "Please select a Competency."; return redirect; }
        if (string.IsNullOrWhiteSpace(compType)) { ErrorMessage = "Please select Competency Type."; return redirect; }
        if (weight <= 0) { ErrorMessage = "Please enter Competency Weight."; return redirect; }

        var work = await GetWorkAsync();
        var form = ToValidationForm(work);
        var errors = FormValidationService.ValidateCompItem(form, master, weight, editingSeq);
        if (errors.Count > 0) { ErrorMessage = string.Join(" ", errors); return redirect; }

        var item = editingSeq is int seq ? work.Comps.FirstOrDefault(c => c.Seq == seq) : null;
        if (item is null)
        {
            item = new WorkingSet.Item { Seq = work.Comps.Count == 0 ? 1 : work.Comps.Max(c => c.Seq) + 1 };
            work.Comps.Add(item);
        }
        item.Kind = compType.Trim();
        item.Code = master.CompId;
        item.Name = master.Name;
        item.Definition = master.Description;
        item.Weight = weight;
        item.Achievement = _gate.NormalizeAchievement(EvalYear, achievement);
        item.Comments = comments;

        work.Save(HttpContext.Session);
        return redirect;
    }

    public async Task<IActionResult> OnPostDeleteCompAsync(int seq)
    {
        var redirect = RedirectKeep("comp");
        if (!await CanEditItemsAsync()) return redirect;
        var work = await GetWorkAsync();
        work.Comps.RemoveAll(c => c.Seq == seq);
        work.Resequence();
        work.Save(HttpContext.Session);
        return redirect;
    }

    // ---- workflow actions --------------------------------------------------
    public async Task<IActionResult> OnPostSaveDraftAsync(FormFields fields)
    {
        var (perms, content) = await PrepareContentAsync(fields);
        var result = await _workflow.SaveDraftAsync(CurrentUserName, perms, content);
        return Finish(result, "Draft saved successfully. You can continue editing or click 'Send to Employee' when ready.");
    }

    public async Task<IActionResult> OnPostSendToEmployeeAsync(FormFields fields)
    {
        var (perms, content) = await PrepareContentAsync(fields);
        var result = await _workflow.SendToEmployeeAsync(CurrentUserName, perms, content);
        return Finish(result, "Form sent to employee successfully. Notification email has been logged.");
    }

    public async Task<IActionResult> OnPostSubmitToHrAsync(FormFields fields)
    {
        var (perms, content) = await PrepareContentAsync(fields);
        var jf = await _jobFamilies.ResolveAsync(Empcd!, SelectedEmployeeGrade());
        var result = await _workflow.SubmitToHrAsync(CurrentUserName, perms, content, jf.Configured);
        return Finish(result, "Form submitted to HR successfully.");
    }

    public async Task<IActionResult> OnPostAcknowledgeAsync(string? ackComments)
    {
        var result = await _workflow.AcknowledgeAsync(CurrentUserName, CurrentEmpCode, Empcd ?? "", EvalYear, ackComments);
        return Finish(result, "Thank you! Your acknowledgement has been recorded and your manager has been notified.");
    }

    public async Task<IActionResult> OnPostHrActionAsync(string hrAction,
        string? hr1Name, string? hr1Remarks, string? hr2Name, string? hr2Remarks)
    {
        var perms = await _permissions.GetFormPermissionsAsync(CurrentUserName, CurrentEmpCode, Empcd ?? "");
        var result = hrAction switch
        {
            PmFormStatus.HrReview1Approved => await _workflow.HrApprove1Async(
                CurrentUserName, perms, Empcd ?? "", EvalYear, hr1Name ?? "", CurrentEmpCode, hr1Remarks),
            PmFormStatus.Approved => await _workflow.HrFinalApproveAsync(
                CurrentUserName, perms, CurrentEmpCode, Empcd ?? "", EvalYear, hr2Name ?? "", CurrentEmpCode, hr2Remarks),
            PmFormStatus.EmployeeAcknowledged => await _workflow.HrRevertAsync(
                CurrentUserName, perms, Empcd ?? "", EvalYear, hr1Remarks ?? hr2Remarks),
            _ => WorkflowResult.Fail("Please select an HR Action.")
        };
        var success = hrAction switch
        {
            PmFormStatus.HrReview1Approved => "First HR review completed. Form moved to Second HR Reviewer.",
            PmFormStatus.Approved => "Final HR approval completed successfully. Form is now locked.",
            _ => "Form reverted to Manager status successfully."
        };
        return Finish(result, success);
    }

    public async Task<IActionResult> OnPostCancelDeleteAsync()
    {
        var perms = await _permissions.GetFormPermissionsAsync(CurrentUserName, CurrentEmpCode, Empcd ?? "");
        var result = await _workflow.CancelDeleteAsync(CurrentUserName, perms, Empcd ?? "", EvalYear);
        if (result.Success) WorkingSet.Clear(HttpContext.Session, Empcd ?? "", EvalYear);
        return Finish(result, "Evaluation records deleted successfully.");
    }

    // ======================================================================
    public record FormFields(string? SelfAssessment, string? DevelopmentPlan,
        string? PromotionRecommendation, string? PromotionComments);

    private IActionResult Finish(WorkflowResult result, string successMessage)
    {
        if (result.Success)
        {
            Message = successMessage;
            WorkingSet.Clear(HttpContext.Session, Empcd ?? "", EvalYear);
            return RedirectToPage(new { Dept, Empcd, Year, Tab });
        }
        ErrorMessage = result.ErrorText;
        return RedirectKeep(Tab);
    }

    private IActionResult RedirectKeep(string tab) =>
        RedirectToPage(new { Dept, Empcd, Year, Tab = tab, keep = true });

    private async Task<bool> CanEditItemsAsync()
    {
        var perms = await _permissions.GetFormPermissionsAsync(CurrentUserName, CurrentEmpCode, Empcd ?? "");
        if (!perms.CanActAsManager)
        {
            ErrorMessage = "Only the direct manager of this employee can edit this form.";
            return false;
        }
        var form = await _workflow.FindFormAsync(Empcd ?? "", EvalYear);
        var status = form?.Status ?? PmFormStatus.Draft;
        if (!PmFormStatus.AllowsEdit(status) || (form?.IsLocked == true && status != PmFormStatus.EmployeeAcknowledged))
        {
            ErrorMessage = $"This form cannot be edited in its current status ({PmFormStatus.DisplayName(status)}).";
            return false;
        }
        return true;
    }

    private string? SelectedEmployeeGrade() => SelectedEmployee?.Grade;

    private async Task<(FormPermissions, WorkflowService.PmFormContent)> PrepareContentAsync(FormFields fields)
    {
        SelectedEmployee ??= await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmpCode == Empcd);
        var perms = await _permissions.GetFormPermissionsAsync(CurrentUserName, CurrentEmpCode, Empcd ?? "");
        var work = await GetWorkAsync();

        work.SelfAssessment = fields.SelfAssessment;
        work.DevelopmentPlan = fields.DevelopmentPlan;
        work.PromotionRecommendation = fields.PromotionRecommendation;
        work.PromotionComments = fields.PromotionComments;
        work.Save(HttpContext.Session);

        var jf = await _jobFamilies.ResolveAsync(Empcd ?? "", SelectedEmployee?.Grade);
        var managerCode = await _permissions.GetManagerOfAsync(Empcd ?? "") ?? "";

        var content = new WorkflowService.PmFormContent(
            Empcd ?? "", EvalYear,
            SelectedEmployee?.LatinName ?? "",
            SelectedEmployee?.DesignationCode, SelectedEmployee?.DeptCode, SelectedEmployee?.SectionCode,
            managerCode, SelectedEmployee?.Grade, SelectedEmployee?.JoinDate, jf.FamilyName,
            jf.KpiWeight, jf.CompWeight,
            work.SelfAssessment, work.DevelopmentPlan,
            Empcd, managerCode,
            work.PromotionRecommendation, work.PromotionComments,
            work.Kpis.Select(k => new PmFormKpi
            {
                RecordSeq = k.Seq, Perspective = k.Kind, KpiCode = k.Code, KpiName = k.Name,
                KpiDefinition = k.Definition, FormulaMetric = k.Formula, Target = k.Target,
                ItemWeight = k.Weight, AchievementScore = k.Achievement, Comments = k.Comments
            }).ToList(),
            work.Comps.Select(c => new PmFormCompetency
            {
                RecordSeq = c.Seq, CompType = c.Kind, CompCode = c.Code, CompName = c.Name,
                Description = c.Definition, ItemWeight = c.Weight, AchievementScore = c.Achievement,
                Comments = c.Comments
            }).ToList());

        return (perms, content);
    }

    /// <summary>Working set: session buffer, refreshed from the DB form when absent or stale.</summary>
    private async Task<WorkingSet> GetWorkAsync()
    {
        var db = await _workflow.FindFormAsync(Empcd ?? "", EvalYear);
        var work = WorkingSet.Load(HttpContext.Session, Empcd ?? "", EvalYear);
        if (work is null || work.LoadedVersion != (db?.Version ?? -1))
        {
            work = FromDb(db);
            work.Save(HttpContext.Session);
        }
        return work;
    }

    private WorkingSet FromDb(Aic.Pm.Core.Domain.PmForm? form)
    {
        var w = new WorkingSet { EmpCode = Empcd ?? "", EvalYear = EvalYear, LoadedVersion = form?.Version ?? -1 };
        if (form is null) return w;
        w.SelfAssessment = form.SelfAssessment;
        w.DevelopmentPlan = form.DevelopmentPlan;
        w.PromotionRecommendation = form.PromotionRecommendationValue;
        w.PromotionComments = form.PromotionComments;
        w.Kpis = form.Kpis.OrderBy(k => k.RecordSeq).Select(k => new WorkingSet.Item
        {
            Seq = k.RecordSeq, Kind = k.Perspective, Code = k.KpiCode, Name = k.KpiName,
            Definition = k.KpiDefinition, Formula = k.FormulaMetric, Target = k.Target,
            Weight = k.ItemWeight, Achievement = k.AchievementScore, Comments = k.Comments
        }).ToList();
        w.Comps = form.Competencies.OrderBy(c => c.RecordSeq).Select(c => new WorkingSet.Item
        {
            Seq = c.RecordSeq, Kind = c.CompType, Code = c.CompCode, Name = c.CompName,
            Definition = c.Description, Weight = c.ItemWeight, Achievement = c.AchievementScore,
            Comments = c.Comments
        }).ToList();
        return w;
    }

    private static Aic.Pm.Core.Domain.PmForm ToValidationForm(WorkingSet w)
    {
        var f = new Aic.Pm.Core.Domain.PmForm { EmpCode = w.EmpCode, EvalYear = w.EvalYear };
        f.Kpis.AddRange(w.Kpis.Select(k => new PmFormKpi
        {
            RecordSeq = k.Seq, Perspective = k.Kind, KpiCode = k.Code, KpiName = k.Name,
            ItemWeight = k.Weight, AchievementScore = k.Achievement
        }));
        f.Competencies.AddRange(w.Comps.Select(c => new PmFormCompetency
        {
            RecordSeq = c.Seq, CompType = c.Kind, CompCode = c.Code, CompName = c.Name,
            ItemWeight = c.Weight, AchievementScore = c.Achievement
        }));
        return f;
    }

    // ---- view assembly ----------------------------------------------------
    private async Task BuildSelectionListsAsync()
    {
        var isBranchViewer = await _permissions.HasExceptionAsync(CurrentEmpCode, ExceptionRule.BranchViewer);
        CanChangeDept = IsHrAdmin || isBranchViewer;

        if (IsHrAdmin)
        {
            Departments = await _db.Departments.AsNoTracking().OrderBy(d => d.NameEn).ToListAsync();
            var q = _db.Employees.AsNoTracking().Where(e => e.TermDate == null);
            if (!string.IsNullOrEmpty(Dept)) q = q.Where(e => e.DeptCode == Dept);
            EmployeeOptions = (await q.OrderBy(e => e.JoinDate).ToListAsync())
                .Select(e => (e.EmpCode, $"{e.EmpCode} - {e.LatinName}")).ToList();
            CanChangeEmployee = true;
        }
        else if (isBranchViewer)
        {
            // Branch viewer: own department plus Branches (PRO/BR employees)
            var own = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmpCode == CurrentEmpCode);
            Departments = await _db.Departments.AsNoTracking()
                .Where(d => d.Code == "PRO" || d.Code == (own!.DeptCode ?? ""))
                .OrderBy(d => d.NameEn).ToListAsync();
            var target = Dept ?? own?.DeptCode;
            var q = _db.Employees.AsNoTracking().Where(e => e.TermDate == null);
            q = target == "PRO"
                ? q.Where(e => e.DeptCode == "PRO" && e.SectionCode == "BR")
                : q.Where(e => e.EmpCode == CurrentEmpCode ||
                               _db.ManagerAssignments.Any(m => m.ManagerEmpCode == CurrentEmpCode && m.EmpCode == e.EmpCode));
            EmployeeOptions = (await q.OrderBy(e => e.JoinDate).ToListAsync())
                .Select(e => (e.EmpCode, $"{e.EmpCode} - {e.LatinName}")).ToList();
            CanChangeEmployee = true;
        }
        else if (await _permissions.IsAManagerAsync(CurrentEmpCode))
        {
            // Direct managers browse their assigned staff (selector stays enabled) + own form
            var assigned = await _permissions.GetAssignedEmployeesAsync(CurrentEmpCode);
            assigned.Add(CurrentEmpCode);
            var emps = await _db.Employees.AsNoTracking()
                .Where(e => assigned.Contains(e.EmpCode) && e.TermDate == null)
                .OrderBy(e => e.JoinDate).ToListAsync();
            var deptCodes = emps.Select(e => e.DeptCode).Where(c => c != null).Distinct().ToList();
            Departments = await _db.Departments.AsNoTracking()
                .Where(d => deptCodes.Contains(d.Code)).OrderBy(d => d.NameEn).ToListAsync();
            if (!string.IsNullOrEmpty(Dept)) emps = emps.Where(e => e.DeptCode == Dept).ToList();
            EmployeeOptions = emps.Select(e => (e.EmpCode, $"{e.EmpCode} - {e.LatinName}")).ToList();
            CanChangeEmployee = true;
        }
        else
        {
            var own = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmpCode == CurrentEmpCode);
            if (own is not null)
            {
                Departments = await _db.Departments.AsNoTracking().Where(d => d.Code == own.DeptCode).ToListAsync();
                EmployeeOptions = new() { (own.EmpCode, $"{own.EmpCode} - {own.LatinName}") };
            }
            CanChangeEmployee = false;
        }
    }

    private async Task LoadFormViewAsync(bool keep)
    {
        var e = SelectedEmployee!;
        DeptName = (await _db.Departments.FindAsync(e.DeptCode ?? ""))?.NameEn ?? e.DeptCode ?? "";
        SectionName = (await _db.Sections.FindAsync(e.SectionCode ?? ""))?.Description ?? e.SectionCode ?? "";
        DesignationName = (await _db.Designations.FindAsync(e.DesignationCode ?? ""))?.Description ?? e.DesignationCode ?? "";
        ManagerCode = await _permissions.GetManagerOfAsync(e.EmpCode) ?? "";
        ManagerName = ManagerCode.Length == 0 ? "" :
            (await _db.Employees.FindAsync(ManagerCode))?.LatinName ?? ManagerCode;

        YearOptions = await _db.PmForms.AsNoTracking()
            .Where(f => f.EmpCode == e.EmpCode).Select(f => f.EvalYear)
            .Distinct().OrderByDescending(y => y).ToListAsync();
        if (!YearOptions.Contains(_clock.Today.Year)) YearOptions.Insert(0, _clock.Today.Year);

        Form = await _workflow.FindFormAsync(e.EmpCode, EvalYear);
        Status = Form?.Status ?? PmFormStatus.Draft;
        AchievementOpen = _gate.IsOpen(EvalYear);

        var jf = await _jobFamilies.ResolveAsync(e.EmpCode, e.Grade);
        JobFamilyConfigured = jf.Configured;
        if (Form is not null && (Form.KpiWeightTotal > 0 || Form.CompWeightTotal > 0))
        {
            JobFamilyName = Form.JobFamily ?? jf.FamilyName;
            KpiWeightTotal = Form.KpiWeightTotal;
            CompWeightTotal = Form.CompWeightTotal;
        }
        else
        {
            JobFamilyName = jf.FamilyName;
            KpiWeightTotal = jf.KpiWeight;
            CompWeightTotal = jf.CompWeight;
        }

        RefNo = Form?.LegacyRefNo ?? RefNoGenerator.Header(e.EmpCode, EvalYear);
        ShowKpiTab = JobFamilyService.ShowKpiTab(e.Grade, KpiWeightTotal);
        if (!ShowKpiTab && Tab == "kpi") Tab = "comp";

        if (!keep) WorkingSet.Clear(HttpContext.Session, e.EmpCode, EvalYear);
        Work = await GetWorkAsync();

        KpiBeingEdited = EditKpi is int ks ? Work.Kpis.FirstOrDefault(k => k.Seq == ks) : null;
        CompBeingEdited = EditComp is int cs ? Work.Comps.FirstOrDefault(c => c.Seq == cs) : null;

        // Masters offered for this employee's department (legacy dept filter, '*' = all)
        var dept = e.DeptCode ?? "";
        KpiOptions = (await _db.KpiMasters.AsNoTracking().Where(k => k.Status == "A").OrderBy(k => k.KpiId).ToListAsync())
            .Where(k => k.AppliesToDept(dept)).ToList();
        CompOptions = await _db.CompetencyMasters.AsNoTracking().Where(c => c.Status == "A").OrderBy(c => c.CompId).ToListAsync();

        // Scores from the working buffer (same math as server-side recalculation)
        var kpiSum = Work.Kpis.Sum(k => ScoringService.WeightedItem(k.Weight, k.Achievement));
        var compSum = Work.Comps.Sum(c => ScoringService.WeightedItem(c.Weight, c.Achievement));
        KpiScore = Math.Round(kpiSum * KpiWeightTotal / 100m, 2);
        CompScore = Math.Round(compSum * CompWeightTotal / 100m, 2);
        OverallScore = Math.Round(KpiScore + CompScore, 2);
        _scales = await _db.RatingScales.AsNoTracking().Where(r => r.Status == "A").ToListAsync();
        var rating = RatingService.Resolve(_scales, (int)Math.Round(OverallScore, MidpointRounding.AwayFromZero));
        RatingName = RatingService.RatingName(rating);

        BuildValidationMessages();
        ProgressPercent = CalculateProgress();
    }

    private void BuildValidationMessages()
    {
        var kpiCount = Work.Kpis.Count;
        var kpiWeight = Work.Kpis.Sum(k => k.Weight);
        var perspectives = Work.Kpis.Select(k => k.Kind.ToUpperInvariant()).Where(p => p.Length > 0).Distinct().Count();

        KpiValidationMessages.Add(kpiCount is >= FormValidationRules.MinKpiCount and <= FormValidationRules.MaxKpiCount
            ? $"OK|✓ KPI Count: {kpiCount}"
            : $"ERR|✗ Minimum {FormValidationRules.MinKpiCount}, Maximum {FormValidationRules.MaxKpiCount} KPIs required. Current: {kpiCount}");
        KpiValidationMessages.Add(kpiWeight == 100
            ? $"OK|✓ Total KPI Weight: {kpiWeight}%"
            : $"ERR|✗ Total KPI Weight must be 100%. Current: {kpiWeight}%");
        KpiValidationMessages.Add(perspectives >= FormValidationRules.RequiredPerspectives
            ? $"OK|✓ Perspectives: {perspectives}"
            : $"ERR|✗ At least 3 different perspectives required. Current: {perspectives}");

        var compCount = Work.Comps.Count;
        var compWeight = Work.Comps.Sum(c => c.Weight);
        CompValidationMessages.Add(compCount is >= FormValidationRules.MinCompCount and <= FormValidationRules.MaxCompCount
            ? $"OK|✓ Competency Count: {compCount}"
            : $"ERR|✗ Minimum {FormValidationRules.MinCompCount}, Maximum {FormValidationRules.MaxCompCount} Competencies required. Current: {compCount}");
        CompValidationMessages.Add(compWeight == 100
            ? $"OK|✓ Total Competency Weight: {compWeight}%"
            : $"ERR|✗ Total Competency Weight must be 100%. Current: {compWeight}%");
    }

    /// <summary>Legacy CalculateFormProgress.</summary>
    private int CalculateProgress()
    {
        int total = 0, passed = 0;
        if (ShowKpiTab)
        {
            total += 3;
            if (Work.Kpis.Count is >= FormValidationRules.MinKpiCount and <= FormValidationRules.MaxKpiCount) passed++;
            if (Work.Kpis.Sum(k => k.Weight) == 100) passed++;
            if (Work.Kpis.Select(k => k.Kind.ToUpperInvariant()).Distinct().Count() >= 3) passed++;
        }
        total += 2;
        if (Work.Comps.Count is >= FormValidationRules.MinCompCount and <= FormValidationRules.MaxCompCount) passed++;
        if (Work.Comps.Sum(c => c.Weight) == 100) passed++;
        total += 2;
        if (!string.IsNullOrWhiteSpace(Work.SelfAssessment)) passed++;
        if (!string.IsNullOrWhiteSpace(Work.DevelopmentPlan)) passed++;
        return total == 0 ? 0 : passed * 100 / total;
    }
}

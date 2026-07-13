using Aic.Pm.Core.Data;
using Aic.Pm.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aic.Pm.Core.Import;

/// <summary>
/// Loads the approved legacy Informix exports into PostgreSQL.
/// Idempotent: upserts by natural key. See docs/data-migration-plan.md.
///
/// NOTE the export naming quirk (verified): "empmaster-informix-csv" contains the FULL
/// pm_form_records export (1,321 rows); no real empmaster export exists. Employees are
/// synthesized from HDR snapshots and flagged Source = HDR_SNAPSHOT.
/// </summary>
public class LegacyImportService
{
    private readonly PmDbContext _db;
    private readonly ILogger<LegacyImportService> _log;

    public LegacyImportService(PmDbContext db, ILogger<LegacyImportService> log)
    {
        _db = db; _log = log;
    }

    public record ImportSummary(
        int Departments, int JobFamilies, int RatingScales, int KpiMasters, int CompetencyMasters,
        int Employees, int ManagerAssignments, int Exceptions, int Users,
        int Forms, int KpiItems, int CompItems, IReadOnlyList<string> Warnings);

    public async Task<ImportSummary> RunAsync(string dataDir,
        string adminUsername = DatabaseSeeder.DefaultAdminUsername,
        string adminPassword = DatabaseSeeder.DefaultAdminPassword)
    {
        var warnings = new List<string>();

        await DatabaseSeeder.SeedCoreAsync(_db, adminUsername, adminPassword);

        var jobFamilies = 0; var ratings = 0;
        var referencePath = Path.Combine(dataDir, "reference-informix-csv");
        if (File.Exists(referencePath))
            (jobFamilies, ratings) = await ImportReferenceAsync(referencePath);
        else warnings.Add($"reference export not found at {referencePath}");

        var kpis = 0;
        var kpiPath = Path.Combine(dataDir, "kpi_master-informix-csv");
        if (File.Exists(kpiPath)) kpis = await ImportKpiMasterAsync(kpiPath);
        else warnings.Add($"kpi_master export not found at {kpiPath}");

        var comps = 0;
        var compPath = Path.Combine(dataDir, "competency_master-informix-csv");
        if (File.Exists(compPath)) comps = await ImportCompMasterAsync(compPath);
        else warnings.Add($"competency_master export not found at {compPath}");

        // Full pm_form_records lives in the mislabeled empmaster file; fall back to the
        // pm_form_records file (subset) if the big one is absent.
        var formsPath = Path.Combine(dataDir, "empmaster-informix-csv");
        if (!File.Exists(formsPath))
        {
            formsPath = Path.Combine(dataDir, "pm_form_records-informix-csv");
            warnings.Add("empmaster-informix-csv (full pm_form_records) missing; using the smaller pm_form_records export.");
        }

        int employees = 0, forms = 0, kpiItems = 0, compItems = 0;
        if (File.Exists(formsPath))
        {
            employees = await SynthesizeEmployeesAsync(formsPath, warnings);
            (forms, kpiItems, compItems) = await ImportFormsAsync(formsPath, warnings);
        }
        else warnings.Add($"pm_form_records export not found at {formsPath}");

        var users = await DatabaseSeeder.SeedUsersForEmployeesAsync(_db);

        var summary = new ImportSummary(
            await _db.Departments.CountAsync(), jobFamilies, ratings, kpis, comps,
            employees, await _db.ManagerAssignments.CountAsync(),
            await _db.EmployeeExceptions.CountAsync(), users,
            forms, kpiItems, compItems, warnings);

        _log.LogInformation("Import complete: {@Summary}", summary);
        return summary;
    }

    private async Task<(int Families, int Ratings)> ImportReferenceAsync(string path)
    {
        var t = Csv.Table.Load(path);
        int fam = 0, rat = 0;
        foreach (var row in t.Rows)
        {
            if (t.Get(row, "rf_codetype") != "ADM" || t.Get(row, "rf_moduleno") != "KPI") continue;
            var subtype = t.Get(row, "rf_subtype");
            var code = t.Get(row, "rf_codeno");
            if (subtype == "J")
            {
                var e = await _db.JobFamilies.FindAsync(code) ?? _db.JobFamilies.Add(new JobFamily { Code = code }).Entity;
                e.NameEn = t.Get(row, "rf_codedesc");
                e.NameAr = t.Get(row, "rf_acodedesc");
                e.GradesCsv = t.Get(row, "rf_lastsrl");
                e.KpiWeight = t.GetInt(row, "rf_frac");
                e.CompWeight = t.GetInt(row, "rf_toac");
                e.Status = t.Get(row, "rf_status") is "" or "A" ? "A" : "I";
                fam++;
            }
            else if (subtype == "R")
            {
                var e = await _db.RatingScales.FindAsync(code) ?? _db.RatingScales.Add(new RatingScale { Code = code }).Entity;
                e.NameEn = t.Get(row, "rf_codedesc");
                e.NameAr = t.Get(row, "rf_acodedesc");
                e.MinScore = t.GetInt(row, "rf_frac");
                e.MaxScore = t.GetInt(row, "rf_toac");
                e.Remarks = t.Get(row, "rf_remks");
                e.Status = t.Get(row, "rf_status") is "" or "A" ? "A" : "I";
                rat++;
            }
        }
        await _db.SaveChangesAsync();
        return (fam, rat);
    }

    private async Task<int> ImportKpiMasterAsync(string path)
    {
        var t = Csv.Table.Load(path);
        var n = 0;
        foreach (var row in t.Rows)
        {
            var id = t.Get(row, "kpi_id");
            if (id.Length == 0) continue;
            var e = await _db.KpiMasters.FindAsync(id) ?? _db.KpiMasters.Add(new KpiMaster { KpiId = id }).Entity;
            e.Name = t.Get(row, "kpi_name");
            e.NameAr = t.Get(row, "kpi_arname");
            e.Perspective = t.Get(row, "kpi_type");
            e.PerspectiveDesc = t.Get(row, "kpi_typedesc");
            e.PerspectiveDescAr = t.Get(row, "kpi_typedesc_ar");
            e.Description = t.Get(row, "kpi_desc");
            e.DescriptionAr = t.Get(row, "kpi_adesc");
            e.Formula = t.Get(row, "kpi_calc");
            e.FormulaAr = t.Get(row, "kpi_acalc");
            e.DeptCsv = t.Get(row, "kpi_dept") is { Length: > 0 } d ? d : "*";
            e.DeptDesc = t.Get(row, "kpi_deptdesc");
            e.DeptDescAr = t.Get(row, "kpi_deptdesc_ar");
            e.WeightRange = t.Get(row, "weight_range");
            e.MinWeight = t.GetInt(row, "min_weight", 10);
            e.MaxWeight = t.GetInt(row, "max_weight", 25);
            e.Status = t.Get(row, "status") is { Length: > 0 } s ? s : "A";
            e.Remarks = t.Get(row, "remarks");
            e.CreatedBy = t.Get(row, "cre_by");
            e.CreatedDate = t.GetDate(row, "cre_date");
            e.ModifiedBy = t.Get(row, "modified_by");
            e.ModifiedDate = t.GetDate(row, "modified_date");
            e.ModifiedTime = t.Get(row, "modified_time");
            n++;
        }
        await _db.SaveChangesAsync();
        return n;
    }

    private async Task<int> ImportCompMasterAsync(string path)
    {
        var t = Csv.Table.Load(path);
        var n = 0;
        foreach (var row in t.Rows)
        {
            var id = t.Get(row, "comp_id");
            if (id.Length == 0) continue;
            var e = await _db.CompetencyMasters.FindAsync(id) ?? _db.CompetencyMasters.Add(new CompetencyMaster { CompId = id }).Entity;
            e.Name = t.Get(row, "comp_name");
            e.NameAr = t.Get(row, "comp_aname");
            e.CompType = t.Get(row, "comp_type");
            e.TypeDesc = t.Get(row, "comp_typedesc");
            e.TypeDescAr = t.Get(row, "comp_typedesc_ar");
            e.Description = t.Get(row, "comp_desc");
            e.DescriptionAr = t.Get(row, "comp_adesc");
            e.DeptCsv = t.Get(row, "comp_dept") is { Length: > 0 } d ? d : "*";
            e.DeptDesc = t.Get(row, "comp_deptdesc");
            e.DeptDescAr = t.Get(row, "comp_deptdesc_ar");
            e.WeightRange = t.Get(row, "weight_range");
            e.MinWeight = t.GetInt(row, "min_weight", 10);
            e.MaxWeight = t.GetInt(row, "max_weight", 20);
            e.Status = t.Get(row, "status") is { Length: > 0 } s ? s : "A";
            e.Remarks = t.Get(row, "remarks");
            e.CreatedBy = t.Get(row, "cre_by");
            e.CreatedDate = t.GetDate(row, "cre_date");
            e.ModifiedBy = t.Get(row, "modified_by");
            e.ModifiedDate = t.GetDate(row, "modified_date");
            e.ModifiedTime = t.Get(row, "modified_time");
            n++;
        }
        await _db.SaveChangesAsync();
        return n;
    }

    /// <summary>
    /// No empmaster export exists — synthesize employees from HDR snapshots plus every
    /// manager referenced by the seed map. Manually-entered rows are never overwritten.
    /// </summary>
    private async Task<int> SynthesizeEmployeesAsync(string path, List<string> warnings)
    {
        var t = Csv.Table.Load(path);
        var n = 0;
        foreach (var row in t.Rows)
        {
            if (t.Get(row, "record_type") != "HDR") continue;
            var code = t.Get(row, "empcd");
            if (code.Length == 0) continue;

            var e = await _db.Employees.FindAsync(code);
            if (e is null)
            {
                e = new Employee { EmpCode = code, Source = "HDR_SNAPSHOT" };
                _db.Employees.Add(e);
            }
            else if (e.Source != "HDR_SNAPSHOT") continue;

            e.LatinName = t.Get(row, "empname");
            e.DesignationCode = t.Get(row, "em_design");
            e.DeptCode = t.Get(row, "deptcd");
            e.SectionCode = t.Get(row, "dept_sec");
            e.Grade = t.Get(row, "em_grade");
            e.JoinDate = t.GetDate(row, "em_join_dt");
            n++;

            // Harvest designation/section codes (descriptions unknown — editable later)
            await EnsureLookupAsync(e.DesignationCode, _db.Designations,
                c => new Designation { Code = c, Description = c });
            await EnsureLookupAsync(e.SectionCode, _db.Sections,
                c => new Section { Code = c, Description = c });
        }

        // Managers referenced by the map but absent from HDR data
        foreach (var (emp, mgr) in SeedData.DirectManagerMap)
        {
            foreach (var code in new[] { emp, mgr })
            {
                if (await _db.Employees.FindAsync(code) is null)
                {
                    _db.Employees.Add(new Employee
                    {
                        EmpCode = code,
                        LatinName = $"Employee {code}",
                        Source = "HDR_SNAPSHOT"
                    });
                    warnings.Add($"Employee {code} referenced by the manager map has no HDR row; placeholder created.");
                }
            }
        }

        await _db.SaveChangesAsync();
        return n;
    }

    private async Task EnsureLookupAsync<T>(string? code, DbSet<T> set, Func<string, T> factory) where T : class
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        if (await set.FindAsync(code.Trim()) is null)
            set.Add(factory(code.Trim()));
    }

    private async Task<(int Forms, int KpiItems, int CompItems)> ImportFormsAsync(string path, List<string> warnings)
    {
        var t = Csv.Table.Load(path);

        var hdrRows = t.Rows.Where(r => t.Get(r, "record_type") == "HDR").ToList();
        var kpiRows = t.Rows.Where(r => t.Get(r, "record_type") == "KPI")
            .ToLookup(r => (t.Get(r, "empcd"), Year(t, r)));
        var compRows = t.Rows.Where(r => t.Get(r, "record_type") == "COMP")
            .ToLookup(r => (t.Get(r, "empcd"), Year(t, r)));

        int forms = 0, kpiItems = 0, compItems = 0;

        foreach (var hdr in hdrRows)
        {
            var empCode = t.Get(hdr, "empcd");
            var year = Year(t, hdr);
            if (empCode.Length == 0 || year == 0) { warnings.Add($"HDR row skipped (empcd/year): {t.Get(hdr, "ref_no")}"); continue; }

            // One transaction per form (all-or-nothing per handoff)
            await using var tx = await _db.Database.BeginTransactionAsync();

            var form = await _db.PmForms.Include(f => f.Kpis).Include(f => f.Competencies).Include(f => f.History)
                .FirstOrDefaultAsync(f => f.EmpCode == empCode && f.EvalYear == year);
            var isNew = form is null;
            if (form is null)
            {
                form = new PmForm { EmpCode = empCode, EvalYear = year };
                _db.PmForms.Add(form);
            }

            form.LegacyRefNo = t.Get(hdr, "ref_no");                 // preserved verbatim, incl. unpadded
            form.EmpNameSnapshot = t.Get(hdr, "empname");
            form.DesignationSnapshot = t.Get(hdr, "em_design");
            form.DeptCode = t.Get(hdr, "deptcd");
            form.SectionCode = t.Get(hdr, "dept_sec");
            form.ManagerEmpCode = t.Get(hdr, "app_by");
            form.GradeSnapshot = t.Get(hdr, "em_grade");
            form.JoinDateSnapshot = t.GetDate(hdr, "em_join_dt");
            form.LastReviewDate = t.GetDate(hdr, "last_rev_dt");
            form.JobFamily = t.Get(hdr, "job_family");
            form.KpiWeightTotal = t.GetInt(hdr, "kpi_weight_tot");
            form.CompWeightTotal = t.GetInt(hdr, "comp_weight_tot");
            form.KpiScore = t.GetDecimal(hdr, "kpi_score");
            form.CompScore = t.GetDecimal(hdr, "comp_score");
            form.PerformanceScore = t.GetDecimal(hdr, "performance_score");
            form.OverallRatingCode = NullIfEmpty(t.Get(hdr, "overall_rating_code"));
            form.Status = t.Get(hdr, "status");
            form.PreviousStatus = NullIfEmpty(t.Get(hdr, "previous_status"));
            form.StatusChangeDate = t.GetDate(hdr, "status_change_date");
            form.SelfAssessment = NullIfEmpty(t.Get(hdr, "self_assm_text"));
            form.DevelopmentPlan = NullIfEmpty(t.Get(hdr, "dev_plan_text"));
            form.EmployeeSign = NullIfEmpty(t.Get(hdr, "empsign"));
            form.ManagerSign = NullIfEmpty(t.Get(hdr, "mgr_sign"));
            form.EmpAckBy = NullIfEmpty(t.Get(hdr, "emp_ack_by"));
            form.EmpAckDate = t.GetDate(hdr, "emp_ack_date");
            form.EmpAckSign = NullIfEmpty(t.Get(hdr, "emp_ack_sign"));
            form.EmpAckComments = NullIfEmpty(t.Get(hdr, "emp_ack_comments"));
            form.Hr1ReviewerName = NullIfEmpty(t.Get(hdr, "hr_app_by"));
            form.Hr1ReviewDate = t.GetDate(hdr, "hr_app_dt");
            form.Hr1Sign = NullIfEmpty(t.Get(hdr, "hr_app_sign"));
            form.Hr1Remarks = NullIfEmpty(t.Get(hdr, "hr_remarks"));
            form.Hr2ReviewerName = NullIfEmpty(t.Get(hdr, "hr_app_by_2"));
            form.Hr2ReviewDate = t.GetDate(hdr, "hr_app_dt_2");
            form.Hr2Sign = NullIfEmpty(t.Get(hdr, "hr_app_sign_2"));
            form.Hr2Remarks = NullIfEmpty(t.Get(hdr, "hr_remarks_2"));
            form.PromotionRecommendationValue = NullIfEmpty(t.Get(hdr, "promotion_recommendation"));
            form.PromotionComments = NullIfEmpty(t.Get(hdr, "promotion_comments"));
            form.IsLocked = t.Get(hdr, "form_locked") == "Y";
            form.IsActive = t.Get(hdr, "is_active").StartsWith("Y");
            form.LastRemindedDate = t.GetDate(hdr, "last_reminded_date");
            form.CreatedBy = NullIfEmpty(t.Get(hdr, "cre_by"));
            var creDt = t.GetDate(hdr, "cre_dt");
            form.CreatedAt = creDt?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            form.UpdatedBy = NullIfEmpty(t.Get(hdr, "upd_by"));
            form.UpdatedAt = t.GetDate(hdr, "upd_date")?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

            form.Kpis.Clear();
            foreach (var r in kpiRows[(empCode, year)].OrderBy(r => t.GetInt(r, "record_seq")))
            {
                form.Kpis.Add(new PmFormKpi
                {
                    RecordSeq = t.GetInt(r, "record_seq"),
                    LegacyRefNo = t.Get(r, "ref_no"),
                    Perspective = t.Get(r, "perspective"),
                    KpiCode = t.Get(r, "kpi_code"),
                    KpiName = t.Get(r, "kpi_name"),
                    KpiDefinition = NullIfEmpty(t.Get(r, "kpi_definition")),
                    FormulaMetric = NullIfEmpty(t.Get(r, "formula_metric_kpi")),
                    Target = NullIfEmpty(t.Get(r, "target")),
                    ItemWeight = t.GetInt(r, "item_weight"),
                    AchievementScore = t.GetInt(r, "achievement_score"),
                    WeightedCalculation = t.GetDecimal(r, "weighted_calculation"),
                    Comments = NullIfEmpty(t.Get(r, "comments"))
                });
                kpiItems++;
            }

            form.Competencies.Clear();
            foreach (var r in compRows[(empCode, year)].OrderBy(r => t.GetInt(r, "record_seq")))
            {
                form.Competencies.Add(new PmFormCompetency
                {
                    RecordSeq = t.GetInt(r, "record_seq"),
                    LegacyRefNo = t.Get(r, "ref_no"),
                    CompType = t.Get(r, "comp_type"),
                    CompCode = t.Get(r, "comp_code"),
                    CompName = t.Get(r, "comp_name"),
                    // Legacy stored the competency description in kpi_definition
                    Description = NullIfEmpty(t.Get(r, "kpi_definition")),
                    ItemWeight = t.GetInt(r, "item_weight"),
                    AchievementScore = t.GetInt(r, "achievement_score"),
                    WeightedCalculation = t.GetDecimal(r, "weighted_calculation"),
                    Comments = NullIfEmpty(t.Get(r, "comments"))
                });
                compItems++;
            }

            if (isNew)
            {
                form.History.Add(new PmFormStatusHistory
                {
                    FromStatus = form.PreviousStatus,
                    ToStatus = form.Status,
                    ChangedBy = form.UpdatedBy ?? "import",
                    ChangedAt = (form.StatusChangeDate ?? DateOnly.FromDateTime(DateTime.UtcNow))
                        .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    Note = "Imported from Informix export"
                });
            }

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
            forms++;
        }

        return (forms, kpiItems, compItems);
    }

    private static int Year(Csv.Table t, string[] row) =>
        int.TryParse(t.Get(row, "eval_year"), out var y) ? y : 0;

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}

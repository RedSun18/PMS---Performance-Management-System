using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Resources;

namespace PerformanceManagement.Core.Services;

/// <summary>
/// Form-level validation rules (legacy KPIForm constants and ValidateFormBeforeSave).
/// </summary>
public static class FormValidationRules
{
    public const int MinKpiCount = 4;
    public const int MaxKpiCount = 8;
    public const int MinCompCount = 3;
    public const int MaxCompCount = 5;
    public const int MinGradeForKpi = 6;
    public const int RequiredPerspectives = 3;
    public const int TotalWeightRequired = 100;
}

public class FormValidationService
{
    /// <summary>
    /// Item-level checks when adding/updating a KPI row
    /// (duplicate + master weight range, legacy gvKPIs_RowInserting).
    /// </summary>
    public static List<string> ValidateKpiItem(PmForm form, KpiMaster master, int weight, int? editingSeq = null)
    {
        var errors = new List<string>();
        if (form.Kpis.Any(k => k.RecordSeq != editingSeq &&
                               k.KpiCode.Equals(master.KpiId, StringComparison.OrdinalIgnoreCase)))
            errors.Add(ValidationResource.Get("DuplicateKpi"));
        if (editingSeq is null && form.Kpis.Count >= FormValidationRules.MaxKpiCount)
            errors.Add(ValidationResource.Get("MaxKpisAllowed", FormValidationRules.MaxKpiCount));
        if (weight < master.MinWeight || weight > master.MaxWeight)
            errors.Add(ValidationResource.Get("KpiWeightRange", master.Name, master.MinWeight, master.MaxWeight));
        var otherWeight = form.Kpis.Where(k => k.RecordSeq != editingSeq).Sum(k => k.ItemWeight);
        if (otherWeight + weight > 100)
            errors.Add(ValidationResource.Get("TotalWeightExceeds100", otherWeight, 100 - otherWeight));
        return errors;
    }

    /// <summary>
    /// The Competency Master's configured weight range is reference/guidance information only
    /// (shown to managers so they know the company's recommended values) — NOT a hard rule.
    /// Managers may enter any weight; it previously blocked out-of-range values with a
    /// validation error identical to the KPI rule above, which was wrong for Competencies
    /// specifically (Phase 12 Part 3). Existing weighted-score calculations are unaffected since
    /// they only ever read the weight the manager entered, never the master's min/max.
    /// </summary>
    public static List<string> ValidateCompItem(PmForm form, CompetencyMaster master, int weight, int? editingSeq = null)
    {
        var errors = new List<string>();
        if (form.Competencies.Any(c => c.RecordSeq != editingSeq &&
                                       c.CompCode.Equals(master.CompId, StringComparison.OrdinalIgnoreCase)))
            errors.Add(ValidationResource.Get("DuplicateCompetency"));
        if (editingSeq is null && form.Competencies.Count >= FormValidationRules.MaxCompCount)
            errors.Add(ValidationResource.Get("MaxCompetenciesAllowed", FormValidationRules.MaxCompCount));
        var otherWeight = form.Competencies.Where(c => c.RecordSeq != editingSeq).Sum(c => c.ItemWeight);
        if (otherWeight + weight > 100)
            errors.Add(ValidationResource.Get("TotalWeightExceeds100", otherWeight, 100 - otherWeight));
        return errors;
    }

    /// <summary>
    /// Send-to-Employee profile (legacy STATUS_SEND_TO_EMPLOYEE validation): completeness
    /// of weights, plus the distinct-perspectives rule when KPIs are required.
    /// </summary>
    public static List<string> ValidateForSendToEmployee(PmForm form, bool perspectiveExempt)
    {
        var errors = new List<string>();
        var kpiRequired = form.KpiWeightTotal > 0;

        if (kpiRequired)
        {
            if (form.Kpis.Count == 0)
                errors.Add(ValidationResource.Get("AtLeastOneKpiRequired"));
            else
            {
                var total = form.Kpis.Sum(k => k.ItemWeight);
                if (total != FormValidationRules.TotalWeightRequired)
                    errors.Add(ValidationResource.Get("KpiWeightsMustTotal100", total));
                errors.AddRange(ValidatePerspectives(form, perspectiveExempt));
            }
        }

        if (form.Competencies.Count == 0)
            errors.Add(ValidationResource.Get("AtLeastOneCompetencyRequired"));
        else
        {
            var total = form.Competencies.Sum(c => c.ItemWeight);
            if (total != FormValidationRules.TotalWeightRequired)
                errors.Add(ValidationResource.Get("CompWeightsMustTotal100", total));
        }

        return errors;
    }

    /// <summary>
    /// Submit-to-HR profile (legacy ValidateFormBeforeSave for SUBMITTED_TO_HR):
    /// achievement completeness + full count/weight rules per job family requirement.
    /// </summary>
    public static List<string> ValidateForSubmitToHr(PmForm form, bool jobFamilyConfigured, bool perspectiveExempt)
    {
        var errors = new List<string>();

        var missingKpi = form.Kpis.Where(k => k.AchievementScore == 0).Select(k => k.KpiName).ToList();
        if (form.Kpis.Count > 0 && missingKpi.Count > 0)
            errors.Add(ValidationResource.Get("MissingKpiAchievementScores", string.Join(", ", missingKpi)));

        var missingComp = form.Competencies.Where(c => c.AchievementScore == 0).Select(c => c.CompName).ToList();
        if (form.Competencies.Count > 0 && missingComp.Count > 0)
            errors.Add(ValidationResource.Get("MissingCompAchievementScores", string.Join(", ", missingComp)));

        if (!jobFamilyConfigured)
            errors.Add(ValidationResource.Get("JobFamilyNotConfigured"));

        _ = int.TryParse((form.GradeSnapshot ?? "").Trim(), out var grade);
        var kpisRequired = form.KpiWeightTotal > 0 || grade >= FormValidationRules.MinGradeForKpi;
        if (kpisRequired)
        {
            if (form.Kpis.Count < FormValidationRules.MinKpiCount || form.Kpis.Count > FormValidationRules.MaxKpiCount)
                errors.Add(ValidationResource.Get("KpiCountValidationFailed", FormValidationRules.MinKpiCount, FormValidationRules.MaxKpiCount, form.Kpis.Count));
            var totalKpi = form.Kpis.Sum(k => k.ItemWeight);
            if (totalKpi != FormValidationRules.TotalWeightRequired)
                errors.Add(ValidationResource.Get("KpiWeightMustBe100", totalKpi));
            errors.AddRange(ValidatePerspectives(form, perspectiveExempt));
        }

        var compsRequired = form.CompWeightTotal > 0;
        if (compsRequired)
        {
            if (form.Competencies.Count < FormValidationRules.MinCompCount || form.Competencies.Count > FormValidationRules.MaxCompCount)
                errors.Add(ValidationResource.Get("CompCountValidationFailed", FormValidationRules.MinCompCount, FormValidationRules.MaxCompCount, form.Competencies.Count));
            var totalComp = form.Competencies.Sum(c => c.ItemWeight);
            if (totalComp != FormValidationRules.TotalWeightRequired)
                errors.Add(ValidationResource.Get("CompWeightMustBe100", totalComp));
        }

        return errors;
    }

    /// <summary>
    /// At least 3 distinct perspectives, unless the employee holds a
    /// PERSPECTIVE_MIN_EXEMPT exception (seeded: 1058, 1470).
    /// </summary>
    public static List<string> ValidatePerspectives(PmForm form, bool exempt)
    {
        if (exempt) return new List<string>();
        var distinct = form.Kpis.Select(k => k.Perspective.Trim().ToUpperInvariant())
            .Where(p => p.Length > 0).Distinct().Count();
        return distinct >= FormValidationRules.RequiredPerspectives
            ? new List<string>()
            : new List<string> { ValidationResource.Get("MinPerspectivesRequired", FormValidationRules.RequiredPerspectives, distinct) };
    }
}

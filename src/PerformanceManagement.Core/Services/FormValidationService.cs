using PerformanceManagement.Core.Domain;

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
            errors.Add("Duplicate KPI is not allowed.");
        if (editingSeq is null && form.Kpis.Count >= FormValidationRules.MaxKpiCount)
            errors.Add($"Maximum {FormValidationRules.MaxKpiCount} KPIs allowed.");
        if (weight < master.MinWeight || weight > master.MaxWeight)
            errors.Add($"KPI '{master.Name}' weight must be between {master.MinWeight} and {master.MaxWeight}.");
        var otherWeight = form.Kpis.Where(k => k.RecordSeq != editingSeq).Sum(k => k.ItemWeight);
        if (otherWeight + weight > 100)
            errors.Add($"Total weight cannot exceed 100%. Current: {otherWeight}%, available: {100 - otherWeight}%.");
        return errors;
    }

    public static List<string> ValidateCompItem(PmForm form, CompetencyMaster master, int weight, int? editingSeq = null)
    {
        var errors = new List<string>();
        if (form.Competencies.Any(c => c.RecordSeq != editingSeq &&
                                       c.CompCode.Equals(master.CompId, StringComparison.OrdinalIgnoreCase)))
            errors.Add("Duplicate Competency is not allowed.");
        if (editingSeq is null && form.Competencies.Count >= FormValidationRules.MaxCompCount)
            errors.Add($"Maximum {FormValidationRules.MaxCompCount} Competencies allowed.");
        if ((master.MinWeight > 0 || master.MaxWeight > 0) &&
            (weight < master.MinWeight || weight > master.MaxWeight))
            errors.Add($"Competency '{master.Name}' weight must be between {master.MinWeight} and {master.MaxWeight}.");
        var otherWeight = form.Competencies.Where(c => c.RecordSeq != editingSeq).Sum(c => c.ItemWeight);
        if (otherWeight + weight > 100)
            errors.Add($"Total weight cannot exceed 100%. Current: {otherWeight}%, available: {100 - otherWeight}%.");
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
                errors.Add("Please add at least one KPI before sending to employee.");
            else
            {
                var total = form.Kpis.Sum(k => k.ItemWeight);
                if (total != FormValidationRules.TotalWeightRequired)
                    errors.Add($"KPI weights must total 100% before sending to employee. Current total: {total}%.");
                errors.AddRange(ValidatePerspectives(form, perspectiveExempt));
            }
        }

        if (form.Competencies.Count == 0)
            errors.Add("Please add at least one Competency before sending to employee.");
        else
        {
            var total = form.Competencies.Sum(c => c.ItemWeight);
            if (total != FormValidationRules.TotalWeightRequired)
                errors.Add($"Competency weights must total 100% before sending to employee. Current total: {total}%.");
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
            errors.Add("Cannot Submit to HR: achievement scores missing for KPIs: " + string.Join(", ", missingKpi) + ".");

        var missingComp = form.Competencies.Where(c => c.AchievementScore == 0).Select(c => c.CompName).ToList();
        if (form.Competencies.Count > 0 && missingComp.Count > 0)
            errors.Add("Cannot Submit to HR: achievement scores missing for Competencies: " + string.Join(", ", missingComp) + ".");

        if (!jobFamilyConfigured)
            errors.Add("Job Family is not configured in the system. Please contact admin.");

        _ = int.TryParse((form.GradeSnapshot ?? "").Trim(), out var grade);
        var kpisRequired = form.KpiWeightTotal > 0 || grade >= FormValidationRules.MinGradeForKpi;
        if (kpisRequired)
        {
            if (form.Kpis.Count < FormValidationRules.MinKpiCount || form.Kpis.Count > FormValidationRules.MaxKpiCount)
                errors.Add($"KPI validation failed: minimum {FormValidationRules.MinKpiCount}, maximum {FormValidationRules.MaxKpiCount} KPIs required. Current: {form.Kpis.Count}.");
            var totalKpi = form.Kpis.Sum(k => k.ItemWeight);
            if (totalKpi != FormValidationRules.TotalWeightRequired)
                errors.Add($"KPI weight must be 100%. Current: {totalKpi}%.");
            errors.AddRange(ValidatePerspectives(form, perspectiveExempt));
        }

        var compsRequired = form.CompWeightTotal > 0;
        if (compsRequired)
        {
            if (form.Competencies.Count < FormValidationRules.MinCompCount || form.Competencies.Count > FormValidationRules.MaxCompCount)
                errors.Add($"Competency validation failed: minimum {FormValidationRules.MinCompCount}, maximum {FormValidationRules.MaxCompCount} Competencies required. Current: {form.Competencies.Count}.");
            var totalComp = form.Competencies.Sum(c => c.ItemWeight);
            if (totalComp != FormValidationRules.TotalWeightRequired)
                errors.Add($"Competency weight must be 100%. Current: {totalComp}%.");
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
            : new List<string> { $"At least {FormValidationRules.RequiredPerspectives} different perspectives required. Current: {distinct}." };
    }
}

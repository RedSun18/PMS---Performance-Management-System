using Aic.Pm.Core.Domain;

namespace Aic.Pm.Core.Services;

public interface IClock
{
    DateOnly Today { get; }
    DateTime Now { get; }
}

public class SystemClock : IClock
{
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    public DateTime Now => DateTime.UtcNow;
}

/// <summary>
/// Canonical PM reference numbers:
///   PM + trimmed eval year + employee code padded to 4 digits + record type + 2-digit sequence
///   employee 907 HDR  -> PM20260907HDR01
///   employee 1504 KPI 2 -> PM20261504KPI02
/// Historic data contains unpadded 3-digit codes; lookups must always use
/// (empCode, evalYear, recordType), never ref-no parsing. New records always pad.
/// </summary>
public static class RefNoGenerator
{
    public static string For(string empCode, int evalYear, string recordType, int sequence) =>
        $"PM{evalYear}{empCode.Trim().PadLeft(4, '0')}{recordType}{sequence:D2}";

    public static string Header(string empCode, int evalYear) => For(empCode, evalYear, "HDR", 1);
}

/// <summary>
/// Achievement (%) entry, final scoring and Submit-to-HR are unavailable until
/// 1 December of the evaluation year. Enforced server-side, not only in the UI.
/// </summary>
public class AchievementGate
{
    private readonly IClock _clock;
    public AchievementGate(IClock clock) => _clock = clock;

    public bool IsOpen(int evalYear) => _clock.Today >= new DateOnly(evalYear, 12, 1);

    /// <summary>Legacy NormalizeAchievementScore: values arriving while the gate is closed are discarded.</summary>
    public int NormalizeAchievement(int evalYear, int? value)
    {
        if (!IsOpen(evalYear)) return 0;
        if (value is null) return 0;
        return Math.Clamp(value.Value, 0, 100);
    }
}

public static class ScoringService
{
    /// <summary>Item weighted score = round(weight × achievement / 100), half away from zero (legacy rule).</summary>
    public static decimal WeightedItem(int weight, int achievement)
    {
        if (weight == 0 || achievement == 0) return 0m;
        return Math.Round(weight * achievement / 100m, 0, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Recomputes item weighted values and the three header scores from current items.
    /// KPI score = Σ weighted × kpiWeightTotal% ; COMP likewise; overall = sum.
    /// </summary>
    public static void Recalculate(PmForm form)
    {
        foreach (var k in form.Kpis)
            k.WeightedCalculation = WeightedItem(k.ItemWeight, k.AchievementScore);
        foreach (var c in form.Competencies)
            c.WeightedCalculation = WeightedItem(c.ItemWeight, c.AchievementScore);

        var kpiSum = form.Kpis.Sum(k => k.WeightedCalculation);
        var compSum = form.Competencies.Sum(c => c.WeightedCalculation);

        form.KpiScore = Math.Round(kpiSum * form.KpiWeightTotal / 100m, 2);
        form.CompScore = Math.Round(compSum * form.CompWeightTotal / 100m, 2);
        form.PerformanceScore = Math.Round(form.KpiScore + form.CompScore, 2);
    }

    /// <summary>
    /// Score/weight card colour rule: 0 → grey, 100 → green, 1–99 → red.
    /// Returns a CSS class suffix used by the UI.
    /// </summary>
    public static string CardColor(int value) => value switch
    {
        0 => "grey",
        100 => "green",
        _ => "red"
    };
}

public record WorkflowResult(bool Success, IReadOnlyList<string> Errors)
{
    public static WorkflowResult Ok() => new(true, Array.Empty<string>());
    public static WorkflowResult Fail(params string[] errors) => new(false, errors);
    public static WorkflowResult Fail(IEnumerable<string> errors) => new(false, errors.ToList());
    public string ErrorText => string.Join(" ", Errors);
}

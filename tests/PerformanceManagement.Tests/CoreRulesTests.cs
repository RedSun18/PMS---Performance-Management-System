using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;

namespace PerformanceManagement.Tests;

/// <summary>Acceptance tests §C (scoring/validation) and §D.1–3 (reference numbers).</summary>
public class CoreRulesTests
{
    // ---- D. Reference numbers -------------------------------------------

    [Theory]
    [InlineData("907", 2026, "HDR", 1, "PM20260907HDR01")]
    [InlineData("1504", 2026, "KPI", 2, "PM20261504KPI02")]
    [InlineData("1504", 2026, "HDR", 1, "PM20261504HDR01")]
    [InlineData("22", 2027, "COMP", 11, "PM20270022COMP11")]
    public void RefNo_pads_employee_code_to_four_digits(string emp, int year, string type, int seq, string expected) =>
        Assert.Equal(expected, RefNoGenerator.For(emp, year, type, seq));

    // ---- C.4 weighted item rounding ---------------------------------------

    [Theory]
    [InlineData(20, 97, 19)]   // 19.4 → 19
    [InlineData(15, 50, 8)]    // 7.5  → 8 (away from zero, legacy rule)
    [InlineData(25, 90, 23)]   // 22.5 → 23
    [InlineData(10, 0, 0)]
    [InlineData(0, 100, 0)]
    public void WeightedItem_rounds_half_away_from_zero(int weight, int achievement, int expected) =>
        Assert.Equal(expected, ScoringService.WeightedItem(weight, achievement));

    // ---- C.5 header scores -------------------------------------------------

    [Fact]
    public void Recalculate_applies_job_family_split()
    {
        var form = new PmForm { KpiWeightTotal = 60, CompWeightTotal = 40 };
        form.Kpis.Add(new PmFormKpi { ItemWeight = 50, AchievementScore = 90 });  // 45
        form.Kpis.Add(new PmFormKpi { ItemWeight = 50, AchievementScore = 90 });  // 45
        form.Competencies.Add(new PmFormCompetency { ItemWeight = 100, AchievementScore = 100 }); // 100

        ScoringService.Recalculate(form);

        Assert.Equal(54.00m, form.KpiScore);          // 90 × 60%
        Assert.Equal(40.00m, form.CompScore);         // 100 × 40%
        Assert.Equal(94.00m, form.PerformanceScore);
    }

    // ---- C.7 card colour rule: 0 grey / 1-99 red / 100 green ---------------

    [Theory]
    [InlineData(0, "grey")]
    [InlineData(1, "red")]
    [InlineData(55, "red")]
    [InlineData(99, "red")]
    [InlineData(100, "green")]
    [InlineData(120, "red")]
    public void CardColor_follows_score_card_rule(int value, string expected) =>
        Assert.Equal(expected, ScoringService.CardColor(value));

    // ---- C.6 rating bands ----------------------------------------------------

    [Theory]
    [InlineData(0, "Pending")]
    [InlineData(1, "Unsatisfactory")]
    [InlineData(59, "Unsatisfactory")]
    [InlineData(60, "Needs Improvement")]
    [InlineData(79, "Needs Improvement")]
    [InlineData(80, "Meets Expectations")]
    [InlineData(89, "Meets Expectations")]
    [InlineData(90, "Exceed Expectations")]
    [InlineData(94, "Exceed Expectations")]
    [InlineData(95, "Exceptional")]
    [InlineData(100, "Exceptional")]
    public async Task Rating_bands_match_reference_data(int score, string expected)
    {
        using var host = new TestHost();
        await host.SeedAsync();
        var rating = await host.Ratings.GetRatingAsync(score);
        Assert.Equal(expected, RatingService.RatingName(rating));
    }

    // ---- C.1/C.2/C.3 form validation profiles -------------------------------

    private static PmForm FormWith(int kpiCount, int kpiEach, string[] perspectives, int compCount, int compEach)
    {
        var f = new PmForm { GradeSnapshot = "7", KpiWeightTotal = 60, CompWeightTotal = 40 };
        for (var i = 0; i < kpiCount; i++)
            f.Kpis.Add(new PmFormKpi { RecordSeq = i + 1, KpiCode = $"K{i}", KpiName = $"K{i}", ItemWeight = kpiEach, Perspective = perspectives[i % perspectives.Length], AchievementScore = 100 });
        for (var i = 0; i < compCount; i++)
            f.Competencies.Add(new PmFormCompetency { RecordSeq = i + 1, CompCode = $"C{i}", CompName = $"C{i}", ItemWeight = compEach, AchievementScore = 100 });
        return f;
    }

    [Fact]
    public void SubmitToHr_requires_counts_weights_and_perspectives()
    {
        // Valid form: 4 KPIs × 25 = 100, 3 perspectives, 4 comps × 25 = 100
        var ok = FormWith(4, 25, new[] { "F", "C", "I" }, 4, 25);
        Assert.Empty(FormValidationService.ValidateForSubmitToHr(ok, jobFamilyConfigured: true, perspectiveExempt: false));

        // 3 KPIs — under minimum
        var few = FormWith(3, 25, new[] { "F", "C", "I" }, 4, 25);
        Assert.Contains(FormValidationService.ValidateForSubmitToHr(few, true, false), e => e.Contains("minimum 4"));

        // Weights ≠ 100
        var badWeight = FormWith(4, 20, new[] { "F", "C", "I" }, 4, 25);
        Assert.Contains(FormValidationService.ValidateForSubmitToHr(badWeight, true, false), e => e.Contains("KPI weight must be 100%"));

        // Only 2 perspectives
        var twoP = FormWith(4, 25, new[] { "F", "C" }, 4, 25);
        Assert.Contains(FormValidationService.ValidateForSubmitToHr(twoP, true, false), e => e.Contains("3 different perspectives"));

        // Same form is valid for an exempt employee (1058 / 1470 rule, data-driven)
        Assert.Empty(FormValidationService.ValidateForSubmitToHr(twoP, true, perspectiveExempt: true));

        // Missing achievements
        var noAch = FormWith(4, 25, new[] { "F", "C", "I" }, 4, 25);
        noAch.Kpis[0].AchievementScore = 0;
        Assert.Contains(FormValidationService.ValidateForSubmitToHr(noAch, true, false), e => e.Contains("achievement scores missing"));

        // Unconfigured job family
        Assert.Contains(FormValidationService.ValidateForSubmitToHr(ok, jobFamilyConfigured: false, perspectiveExempt: false),
            e => e.Contains("Job Family is not configured"));

        // Comps outside 3–5
        var sixComps = FormWith(4, 25, new[] { "F", "C", "I" }, 6, 20);
        Assert.Contains(FormValidationService.ValidateForSubmitToHr(sixComps, true, false), e => e.Contains("maximum 5 Competencies"));
    }

    [Fact]
    public void SendToEmployee_requires_complete_weights_only()
    {
        var f = FormWith(4, 25, new[] { "F", "C", "I" }, 3, 30);
        // comp weights 90 ≠ 100
        Assert.Contains(FormValidationService.ValidateForSendToEmployee(f, false),
            e => e.Contains("Competency weights must total 100%"));

        f.Competencies[0].ItemWeight = 40;
        Assert.Empty(FormValidationService.ValidateForSendToEmployee(f, false));

        // Competency-only employee (KPI weight total 0) needs no KPIs
        var compOnly = new PmForm { KpiWeightTotal = 0, CompWeightTotal = 100 };
        for (var i = 0; i < 5; i++)
            compOnly.Competencies.Add(new PmFormCompetency { RecordSeq = i + 1, CompCode = $"C{i}", CompName = $"C{i}", ItemWeight = 20 });
        Assert.Empty(FormValidationService.ValidateForSendToEmployee(compOnly, false));
    }

    [Fact]
    public void Item_validation_rejects_duplicates_and_out_of_range_weights()
    {
        var master = new KpiMaster { KpiId = "KPI001", Name = "Claims Cost Reduction", MinWeight = 15, MaxWeight = 20 };
        var form = new PmForm();
        form.Kpis.Add(new PmFormKpi { RecordSeq = 1, KpiCode = "KPI001", ItemWeight = 20 });

        Assert.Contains(FormValidationService.ValidateKpiItem(form, master, 20), e => e.Contains("Duplicate KPI"));
        var other = new KpiMaster { KpiId = "KPI009", Name = "Other", MinWeight = 15, MaxWeight = 20 };
        Assert.Contains(FormValidationService.ValidateKpiItem(form, other, 30), e => e.Contains("between 15 and 20"));
        Assert.Empty(FormValidationService.ValidateKpiItem(form, other, 18));
        // Editing the same row is not a duplicate
        Assert.Empty(FormValidationService.ValidateKpiItem(form, master, 18, editingSeq: 1));
    }

    // ---- Achievement gate: 1 December of the evaluation year ------------------

    [Fact]
    public void Achievement_gate_opens_on_december_first_of_eval_year()
    {
        var clock = new FakeClock { Today = new DateOnly(2026, 11, 30) };
        var gate = new AchievementGate(clock);
        Assert.False(gate.IsOpen(2026));
        Assert.Equal(0, gate.NormalizeAchievement(2026, 85)); // discarded while closed

        clock.Today = new DateOnly(2026, 12, 1);
        Assert.True(gate.IsOpen(2026));
        Assert.Equal(85, gate.NormalizeAchievement(2026, 85));
        Assert.True(gate.IsOpen(2025));   // past year stays open
        Assert.False(gate.IsOpen(2027));  // future year still closed
    }

    // ---- KPI tab visibility (legacy SetupTabsByJobFamily) --------------------

    [Theory]
    [InlineData("5", 0, false)]
    [InlineData("5", 50, true)]
    [InlineData("6", 0, true)]
    [InlineData("7", 60, true)]
    public void Kpi_tab_hidden_only_for_low_grade_with_zero_weight(string grade, int kpiWeight, bool visible) =>
        Assert.Equal(visible, JobFamilyService.ShowKpiTab(grade, kpiWeight));
}

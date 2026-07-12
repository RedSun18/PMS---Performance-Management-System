using Aic.Pm.Core.Data;
using Aic.Pm.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Aic.Pm.Core.Services;

/// <summary>
/// Rating band resolution from the rating_scales reference data
/// (legacy reference rows ADM/KPI subtype R; legacy GetRatingCode).
/// </summary>
public class RatingService
{
    private readonly PmDbContext _db;
    public RatingService(PmDbContext db) => _db = db;

    public async Task<RatingScale?> GetRatingAsync(int roundedScore)
    {
        var scales = await _db.RatingScales.AsNoTracking()
            .Where(r => r.Status == "A").ToListAsync();
        return Resolve(scales, roundedScore);
    }

    public static RatingScale? Resolve(IEnumerable<RatingScale> scales, int roundedScore) =>
        scales.FirstOrDefault(r => roundedScore >= r.MinScore && roundedScore <= r.MaxScore);

    public static string RatingName(RatingScale? scale) => scale?.NameEn ?? "Not Rated";
}

/// <summary>
/// Job-family weight split resolution by employee grade (legacy LoadJobFamilyWeights),
/// with the data-driven 50/50 exception list.
/// </summary>
public class JobFamilyService
{
    private readonly PmDbContext _db;
    private readonly IClock _clock;
    public JobFamilyService(PmDbContext db, IClock clock) { _db = db; _clock = clock; }

    public record JobFamilyWeights(string FamilyName, int KpiWeight, int CompWeight, bool Configured);

    public async Task<JobFamilyWeights> ResolveAsync(string empCode, string? grade)
    {
        var g = (grade ?? "").Trim();
        var families = await _db.JobFamilies.AsNoTracking().Where(f => f.Status == "A").ToListAsync();
        var family = families.FirstOrDefault(f => f.Grades.Contains(g));

        var is5050 = await _db.EmployeeExceptions.AsNoTracking()
            .Where(x => x.EmpCode == empCode && x.RuleCode == ExceptionRule.Kpi5050)
            .AnyAsyncEffective(_clock.Today);

        if (family is null)
            // Legacy fallback: "Not Assigned", 50/50
            return new JobFamilyWeights("Not Assigned", 50, 50, Configured: false);

        if (is5050)
            // Exception employees keep the family name but get a 50/50 split (legacy rule)
            return new JobFamilyWeights(family.NameEn.Trim(), 50, 50, Configured: true);

        return new JobFamilyWeights(family.NameEn.Trim(), family.KpiWeight, family.CompWeight, Configured: true);
    }

    /// <summary>Legacy SetupTabsByJobFamily: KPI tab hidden iff grade &lt; 6 AND KPI weight total = 0.</summary>
    public static bool ShowKpiTab(string? grade, int kpiWeightTotal)
    {
        _ = int.TryParse((grade ?? "").Trim(), out var g);
        return !(g < 6 && kpiWeightTotal == 0);
    }
}

internal static class ExceptionQueryExtensions
{
    public static async Task<bool> AnyAsyncEffective(this IQueryable<EmployeeException> query, DateOnly today)
    {
        var rows = await query.ToListAsync();
        return rows.Any(r => r.IsEffective(today));
    }
}

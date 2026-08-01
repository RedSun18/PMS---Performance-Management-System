using PerformanceManagement.Core.Services;

namespace PerformanceManagement.DemoSeeder;

/// <summary>
/// A settable <see cref="IClock"/> so the seeder can drive forms through their workflow at
/// whatever simulated date each stage needs (e.g. after the achievement-entry gate opens),
/// independent of the real wall-clock date the seeder happens to run on.
/// </summary>
public sealed class SeederClock : IClock
{
    public DateOnly Today { get; set; } = new(2026, 1, 15);

    /// <summary>Fixed at noon rather than the real wall-clock time-of-day — every timestamp this
    /// clock produces (PmFormStatusHistory.ChangedAt, EmailLog.CreatedAt, etc.) must depend only
    /// on <see cref="Today"/> so that two seeder runs on the same seed, at different times of
    /// day, still produce byte-identical data. Explicitly UTC, matching the real
    /// <see cref="SystemClock"/> — Npgsql requires Kind=Utc for "timestamp with time zone" columns.</summary>
    public DateTime Now => DateTime.SpecifyKind(Today.ToDateTime(new TimeOnly(12, 0)), DateTimeKind.Utc);
}

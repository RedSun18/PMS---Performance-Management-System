using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Core.Services;

public record AuditLogFilter(
    string? EmpCode = null, string? DeptCode = null, string? PerformedBy = null,
    string? Action = null, DateOnly? FromDate = null, DateOnly? ToDate = null);

/// <summary>
/// Writes and queries the append-only admin action trail (AuditLog). Writes are skipped
/// (not queued, not buffered — simply not written) whenever SystemSettings.EnableAuditLogging
/// is off, so toggling the setting takes effect immediately with no restart.
/// </summary>
public class AuditService
{
    private readonly PmDbContext _db;
    private readonly IClock _clock;
    private readonly SettingsService _settings;
    public AuditService(PmDbContext db, IClock clock, SettingsService settings)
    {
        _db = db; _clock = clock; _settings = settings;
    }

    public async Task LogAsync(string action, string performedBy, string? empCode = null,
        string? deptCode = null, string? entityType = null, string? entityId = null, string? details = null)
    {
        var rules = await _settings.GetSecurityRulesAsync();
        if (!rules.EnableAuditLogging) return;

        _db.AuditLogs.Add(new AuditLog
        {
            OccurredAt = _clock.Now,
            Action = action,
            PerformedBy = performedBy,
            EmpCode = empCode,
            DeptCode = deptCode,
            EntityType = entityType,
            EntityId = entityId,
            Details = details
        });
        await _db.SaveChangesAsync();
    }

    public async Task<List<AuditLog>> SearchAsync(AuditLogFilter filter, int take = 200)
    {
        var query = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.EmpCode)) query = query.Where(a => a.EmpCode == filter.EmpCode);
        if (!string.IsNullOrWhiteSpace(filter.DeptCode)) query = query.Where(a => a.DeptCode == filter.DeptCode);
        if (!string.IsNullOrWhiteSpace(filter.PerformedBy)) query = query.Where(a => a.PerformedBy.ToLower().Contains(filter.PerformedBy.ToLower()));
        if (!string.IsNullOrWhiteSpace(filter.Action)) query = query.Where(a => a.Action.ToLower().Contains(filter.Action.ToLower()));
        if (filter.FromDate is { } from) query = query.Where(a => a.OccurredAt >= from.ToDateTime(TimeOnly.MinValue));
        if (filter.ToDate is { } to) query = query.Where(a => a.OccurredAt < to.ToDateTime(TimeOnly.MinValue).AddDays(1));

        return await query.OrderByDescending(a => a.OccurredAt).Take(take).ToListAsync();
    }
}

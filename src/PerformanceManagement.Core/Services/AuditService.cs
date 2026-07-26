using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Core.Services;

public record AuditLogFilter(
    string? EmpCode = null, string? DeptCode = null, string? PerformedBy = null,
    string? Action = null, DateOnly? FromDate = null, DateOnly? ToDate = null,
    string? EntityType = null, string? EntityId = null);

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
        await WriteAsync(action, performedBy, empCode, deptCode, entityType, entityId, details);
    }

    /// <summary>
    /// Writes an entry unconditionally, ignoring SystemSettings.EnableAuditLogging — reserved
    /// for the toggle itself (Settings/Index.cshtml.cs's Security tab) so an admin turning audit
    /// logging off (or back on) is always recorded, never silently suppressed by the very flag
    /// being changed. Not for general use — every other call site should use LogAsync.
    /// </summary>
    public async Task LogAlwaysAsync(string action, string performedBy, string? empCode = null,
        string? deptCode = null, string? entityType = null, string? entityId = null, string? details = null) =>
        await WriteAsync(action, performedBy, empCode, deptCode, entityType, entityId, details);

    private async Task WriteAsync(string action, string performedBy, string? empCode,
        string? deptCode, string? entityType, string? entityId, string? details)
    {
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
        if (!string.IsNullOrWhiteSpace(filter.EntityType)) query = query.Where(a => a.EntityType == filter.EntityType);
        if (!string.IsNullOrWhiteSpace(filter.EntityId)) query = query.Where(a => a.EntityId == filter.EntityId);

        return await query.OrderByDescending(a => a.OccurredAt).Take(take).ToListAsync();
    }
}

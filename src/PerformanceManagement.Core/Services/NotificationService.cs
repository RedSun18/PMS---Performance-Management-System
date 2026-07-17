using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Core.Services;

/// <summary>In-app Notification Centre backing service — the bell icon (recent + unread count)
/// and the full /Notifications list both read through here.</summary>
public class NotificationService
{
    private readonly PmDbContext _db;
    private readonly IClock _clock;
    public NotificationService(PmDbContext db, IClock clock) { _db = db; _clock = clock; }

    /// <summary>No-ops silently for a blank recipient (e.g. an employee with no linked user account
    /// yet) — a missing notification target is not itself an error worth failing the calling action over.</summary>
    public async Task CreateAsync(string? userName, string title, string? message, string type, string? link = null)
    {
        if (string.IsNullOrWhiteSpace(userName)) return;
        _db.Notifications.Add(new Notification
        {
            UserName = userName.Trim(), Title = title, Message = message, Type = type, Link = link,
            CreatedAt = _clock.Now
        });
        await _db.SaveChangesAsync();
    }

    public async Task<int> UnreadCountAsync(string userName) =>
        await _db.Notifications.AsNoTracking().CountAsync(n => n.UserName == userName && !n.IsRead);

    public async Task<List<Notification>> GetRecentAsync(string userName, int take = 8) =>
        await _db.Notifications.AsNoTracking().Where(n => n.UserName == userName)
            .OrderByDescending(n => n.CreatedAt).Take(take).ToListAsync();

    public async Task<List<Notification>> GetAllAsync(string userName) =>
        await _db.Notifications.AsNoTracking().Where(n => n.UserName == userName)
            .OrderByDescending(n => n.CreatedAt).ToListAsync();

    public async Task MarkReadAsync(int id, string userName)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserName == userName);
        if (n is null || n.IsRead) return;
        n.IsRead = true;
        await _db.SaveChangesAsync();
    }

    public async Task MarkAllReadAsync(string userName) =>
        await _db.Notifications.Where(n => n.UserName == userName && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

    public async Task DeleteAsync(int id, string userName) =>
        await _db.Notifications.Where(n => n.Id == id && n.UserName == userName).ExecuteDeleteAsync();
}

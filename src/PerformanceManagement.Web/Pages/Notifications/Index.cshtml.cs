using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace PerformanceManagement.Web.Pages.Notifications;

/// <summary>Full Notification Centre list — every notification for the current user, newest
/// first, with Mark Read/Mark All Read/Delete. The bell dropdown in the layout shows only the
/// most recent few; this page is the complete history.</summary>
public class IndexModel : AppPageModel
{
    private readonly NotificationService _notifications;
    public IndexModel(NotificationService notifications) => _notifications = notifications;

    public List<Notification> Items { get; set; } = new();

    public async Task OnGetAsync() => Items = await _notifications.GetAllAsync(CurrentUserName);

    public async Task<IActionResult> OnPostMarkReadAsync(int id)
    {
        await _notifications.MarkReadAsync(id, CurrentUserName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostMarkAllReadAsync()
    {
        await _notifications.MarkAllReadAsync(CurrentUserName);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await _notifications.DeleteAsync(id, CurrentUserName);
        return RedirectToPage();
    }
}

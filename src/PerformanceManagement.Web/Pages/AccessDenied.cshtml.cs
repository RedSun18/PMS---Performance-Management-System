using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PerformanceManagement.Web.Pages;

/// <summary>
/// Central Access Denied target: both role-based [Authorize] failures (cookie
/// AccessDeniedPath) and per-record authorization failures (PmForm handlers) land here.
/// Always returns HTTP 403, satisfying "return 403 Forbidden (or redirect to an Access
/// Denied page)" for every access-control failure in the app.
/// </summary>
public class AccessDeniedModel : AppPageModel
{
    [Microsoft.AspNetCore.Mvc.TempData]
    public string? Detail { get; set; }

    public void OnGet()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
    }
}

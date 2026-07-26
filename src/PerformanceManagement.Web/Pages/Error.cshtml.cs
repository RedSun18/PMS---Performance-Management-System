using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PerformanceManagement.Web.Pages;

/// <summary>
/// Catch-all target for app.UseExceptionHandler("/Error") — the last line of defense so a
/// real user in Production never sees a raw stack trace/internal paths. Deliberately shows
/// no exception detail; the request id is enough to correlate with server-side logs.
/// </summary>
[AllowAnonymous]
public class ErrorModel : PageModel
{
    public string? RequestId { get; set; }

    public void OnGet()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        RequestId = HttpContext.Features.Get<IExceptionHandlerPathFeature>() is not null
            ? System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier
            : HttpContext.TraceIdentifier;
    }
}

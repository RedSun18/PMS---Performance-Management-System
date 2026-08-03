using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PerformanceManagement.Web.Pages;

/// <summary>
/// Shared target for both app.UseExceptionHandler("/Error") (unhandled exceptions, no route
/// value — StatusCode is already 500 by the time this runs) and
/// app.UseStatusCodePagesWithReExecute("/Error/{0}") (any other non-2xx response with an
/// empty body, e.g. 404/403 — statusCode carries the original code). Deliberately shows no
/// exception detail; the request id is enough to correlate with server-side logs.
/// </summary>
[AllowAnonymous]
public class ErrorModel : PageModel
{
    public string? RequestId { get; set; }
    public int ResponseStatusCode { get; set; }

    public void OnGet(int? statusCode)
    {
        Response.StatusCode = statusCode ?? StatusCodes.Status500InternalServerError;
        ResponseStatusCode = Response.StatusCode;
        RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }
}

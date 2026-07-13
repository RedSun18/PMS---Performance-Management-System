using Aic.Pm.Core.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Aic.Pm.Web.Pages;

/// <summary>Shared page plumbing: current-user identity claims, nav role flags, forced
/// password-change gate, and impersonation state.</summary>
public abstract class AppPageModel : PageModel
{
    public string CurrentUserName => User.Identity?.Name ?? "";
    public string CurrentEmpCode => User.FindFirst("EmpCode")?.Value?.Trim() ?? "";
    public string CurrentDisplayName => User.FindFirst("DisplayName")?.Value ?? CurrentUserName;
    public bool IsHrAdmin => User.IsInRole(Roles.HrAdmin);
    public bool IsViewer => User.IsInRole(Roles.Viewer);
    public bool MustChangePassword => User.FindFirst("MustChangePassword")?.Value == "true";

    // ---- impersonation breadcrumb (set only while an admin is impersonating another user) ----
    public bool IsImpersonating => User.FindFirst("ImpersonationSessionId") is not null;
    public string? OriginalAdminUserName => User.FindFirst("OriginalAdminUserName")?.Value;
    public string? OriginalAdminDisplayName => User.FindFirst("OriginalAdminDisplayName")?.Value;

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        ViewData["IsHrAdmin"] = IsHrAdmin;
        ViewData["IsViewer"] = IsViewer;
        ViewData["IsImpersonating"] = IsImpersonating;
        ViewData["OriginalAdminDisplayName"] = OriginalAdminDisplayName;

        // Force any authenticated user with a pending password-change requirement to the
        // Change Password page before they can reach anything else. (Logout inherits
        // PageModel directly, not AppPageModel, so it never runs this filter at all and
        // always remains reachable.)
        //
        // Exception: while impersonating, the admin is DELIBERATELY subjected to the
        // target user's real state (including a pending password change) so permissions
        // and page visibility match exactly what that user sees. But "Return to
        // Administrator" must always stay reachable regardless — almost every seeded
        // employee account has MustChangePassword=true, so without this exception the
        // admin would be trapped on Change Password with no way back except full logout.
        // HandlerMethod.Name is the short Razor Pages handler name ("Return", matching
        // ?handler=Return / asp-page-handler="Return") — NOT the C# method name.
        var isReturnToAdminHandler = this is Aic.Pm.Web.Pages.Admin.LoginAsModel &&
                                      context.HandlerMethod?.Name == "Return";

        if (User.Identity?.IsAuthenticated == true && MustChangePassword &&
            this is not Aic.Pm.Web.Pages.Account.ChangePasswordModel &&
            !isReturnToAdminHandler)
        {
            context.Result = new RedirectToPageResult("/Account/ChangePassword");
            return;
        }

        await next();
    }

    /// <summary>
    /// Handler-level admin-only guard for pages whose class-level [Authorize] also admits
    /// Viewer (read-only) — call at the top of every mutating (POST) handler on such a page.
    /// </summary>
    protected IActionResult? RequireHrAdmin()
    {
        if (IsHrAdmin) return null;
        TempData["Detail"] = "Only an administrator can perform this action.";
        return RedirectToPage("/AccessDenied");
    }
}

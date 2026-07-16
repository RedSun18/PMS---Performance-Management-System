using PerformanceManagement.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace PerformanceManagement.Web.Pages;

/// <summary>
/// Resolves a signed, expiring email deep-link token (see <see cref="FormLinkService"/>)
/// into the actual PM Form URL. Requires authentication (no [AllowAnonymous] — the cookie
/// auth challenge redirects to Login with ReturnUrl set to this same page, and Login sends
/// the user back here once signed in, per the standard ASP.NET Core ReturnUrl flow).
/// Never itself grants access — <see cref="PmForm.IndexModel"/> re-checks authorization
/// independently once redirected there.
/// </summary>
public class OpenFormModel : AppPageModel
{
    private readonly FormLinkService _links;
    private readonly PermissionService _permissions;

    public OpenFormModel(FormLinkService links, PermissionService permissions)
    {
        _links = links; _permissions = permissions;
    }

    public async Task<IActionResult> OnGetAsync(string? token)
    {
        var payload = _links.TryDecode(token);
        if (payload is null)
        {
            TempData["Detail"] = "This link is invalid or has expired. Please open the form from your Dashboard instead.";
            return RedirectToPage("/AccessDenied");
        }

        // The token names an intended recipient, but access is ultimately governed by the
        // same rules as opening the form directly — an admin, the assigned manager, or a
        // branch viewer may all legitimately use a link that was addressed to someone else
        // (e.g. forwarded, or a role-based recipient like "any HR admin").
        var isIntendedRecipient = CurrentUserName.Equals(payload.IntendedUserName, StringComparison.OrdinalIgnoreCase);
        if (!isIntendedRecipient)
        {
            var perms = await _permissions.GetFormPermissionsAsync(CurrentUserName, CurrentEmpCode, payload.EmpCode);
            if (!perms.CanView)
            {
                TempData["Detail"] = "This link was issued for a different user and you do not otherwise have access to this employee's form.";
                return RedirectToPage("/AccessDenied");
            }
        }

        return RedirectToPage("/PmForm/Index", new { empcd = payload.EmpCode, year = payload.EvalYear });
    }
}

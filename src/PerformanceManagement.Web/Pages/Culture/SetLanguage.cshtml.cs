using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PerformanceManagement.Web.Pages.Culture;

/// <summary>
/// Global language switcher (see the .lang-switch links in _Layout.cshtml). Sets the standard
/// ASP.NET Core culture cookie and bounces back to wherever the user was — a plain GET rather
/// than a POST, matching how every other language-switch link on the web works (idempotent,
/// bookmarkable, no confirmation needed to just change a display preference).
/// </summary>
[AllowAnonymous]
public class SetLanguageModel : PageModel
{
    public IActionResult OnGet(string culture, string? returnUrl)
    {
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });

        return LocalRedirect(returnUrl is { Length: > 0 } && Url.IsLocalUrl(returnUrl) ? returnUrl : "/Dashboard");
    }
}

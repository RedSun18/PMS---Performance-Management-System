using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Services;

namespace PerformanceManagement.Web.Pages.Culture;

/// <summary>
/// Global language switcher (see the .lang-switch links in _Layout.cshtml). Sets the standard
/// ASP.NET Core culture cookie and bounces back to wherever the user was — a plain GET rather
/// than a POST, matching how every other language-switch link on the web works (idempotent,
/// bookmarkable, no confirmation needed to just change a display preference). Also persists the
/// choice to AppUser.PreferredCulture when signed in, so it survives across browsers/devices —
/// see SettingsAwareCultureProvider, which reads it back when no cookie is present.
/// </summary>
[AllowAnonymous]
public class SetLanguageModel : PageModel
{
    private static readonly string[] SupportedCultures = { "en", "ar" };

    private readonly PmDbContext _db;
    private readonly SettingsService _settings;
    public SetLanguageModel(PmDbContext db, SettingsService settings) { _db = db; _settings = settings; }

    public async Task<IActionResult> OnGetAsync(string culture, string? returnUrl)
    {
        var redirect = LocalRedirect(returnUrl is { Length: > 0 } && Url.IsLocalUrl(returnUrl) ? returnUrl : "/Dashboard");

        // Defense in depth: the switcher itself is hidden by _Layout while disabled, but a
        // directly-typed or bookmarked URL could still reach this handler — while disabled, the
        // language never changes regardless of what's requested here.
        if (!await _settings.IsLanguageSelectionEnabledAsync()) return redirect;
        if (!SupportedCultures.Contains(culture)) return redirect;

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });

        if (User.Identity?.IsAuthenticated == true)
        {
            var username = User.Identity.Name;
            var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.UserName == username);
            if (user is not null)
            {
                user.PreferredCulture = culture;
                await _db.SaveChangesAsync();
            }
        }

        return redirect;
    }
}

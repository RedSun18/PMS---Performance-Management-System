using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Services;

namespace PerformanceManagement.Web.Culture;

/// <summary>
/// Wraps the standard cookie culture provider with the System Settings "Language Selection"
/// toggle (Phase 11 Part 6): while disabled, every request is forced to English and any culture
/// cookie is ignored outright — the switcher itself is hidden by _Layout for the same reason, but
/// the request-level enforcement here is what actually matters, since a cookie set before the
/// toggle was disabled must not silently keep working.
///
/// While enabled: falls back to the signed-in user's saved <see cref="AppUser.PreferredCulture"/>
/// when no cookie is present yet (e.g. a new browser/device) — see SetLanguage.cshtml.cs, which
/// is what keeps that field up to date. Requires UseAuthentication to run before
/// UseRequestLocalization (see Program.cs) so HttpContext.User is already populated here.
/// </summary>
public class SettingsAwareCultureProvider : IRequestCultureProvider
{
    private readonly CookieRequestCultureProvider _cookieProvider =
        new() { CookieName = CookieRequestCultureProvider.DefaultCookieName };

    public async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var settings = httpContext.RequestServices.GetRequiredService<SettingsService>();
        if (!await settings.IsLanguageSelectionEnabledAsync())
            return new ProviderCultureResult("en");

        var fromCookie = await _cookieProvider.DetermineProviderCultureResult(httpContext);
        if (fromCookie is not null) return fromCookie;

        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var db = httpContext.RequestServices.GetRequiredService<PmDbContext>();
            var username = httpContext.User.Identity.Name;
            var preferred = await db.AppUsers.AsNoTracking()
                .Where(u => u.UserName == username)
                .Select(u => u.PreferredCulture)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrEmpty(preferred))
                return new ProviderCultureResult(preferred);
        }

        return null;
    }
}

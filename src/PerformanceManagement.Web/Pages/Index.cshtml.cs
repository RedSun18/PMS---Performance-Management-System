using PerformanceManagement.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PerformanceManagement.Web.Pages;

/// <summary>
/// Root route. A signed-in user is sent straight to their Dashboard, exactly as before —
/// this page only ever renders for an unauthenticated visitor, as a public landing/marketing
/// page ahead of the login screen (a demo/portfolio entry point), not a functional change to
/// the authenticated experience.
/// </summary>
[AllowAnonymous]
public class IndexModel : PageModel
{
    private readonly SettingsService _settings;
    public IndexModel(SettingsService settings) => _settings = settings;

    public string BrandName { get; set; } = "Performance Management System";

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Dashboard/Index");

        var general = await _settings.GetGeneralSettingsAsync();
        BrandName = !string.IsNullOrWhiteSpace(general.CompanyName) ? general.CompanyName
            : !string.IsNullOrWhiteSpace(general.ApplicationName) ? general.ApplicationName
            : BrandName;
        return Page();
    }
}

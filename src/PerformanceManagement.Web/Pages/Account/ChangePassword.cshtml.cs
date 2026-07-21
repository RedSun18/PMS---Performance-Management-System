using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Services;
using PerformanceManagement.Web.Security;
using PerformanceManagement.Web.Validation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace PerformanceManagement.Web.Pages.Account;

/// <summary>
/// Reachable by any authenticated user regardless of MustChangePassword (it is the target
/// of that forced redirect). Requires the current password even when the change is forced,
/// per spec — the user already knows the temporary password since they just logged in with it.
/// </summary>
public class ChangePasswordModel : AppPageModel
{
    private readonly PmDbContext _db;
    private readonly SettingsService _settings;
    private readonly IStringLocalizer<ChangePasswordModel> _localizer;
    public ChangePasswordModel(PmDbContext db, SettingsService settings, IStringLocalizer<ChangePasswordModel> localizer)
    {
        _db = db; _settings = settings; _localizer = localizer;
    }

    public string? Error { get; set; }
    /// <summary>True when the user was forced here (vs. a voluntary password change from settings).</summary>
    public bool IsForced { get; set; }
    public string PasswordRuleHint { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public async Task OnGetAsync()
    {
        IsForced = MustChangePassword;
        PasswordRuleHint = await BuildPasswordRuleHintAsync();
    }

    public async Task<IActionResult> OnPostAsync(string currentPassword, string newPassword, string confirmPassword)
    {
        IsForced = MustChangePassword;
        var rules = await _settings.GetSecurityRulesAsync();
        PasswordRuleHint = BuildPasswordRuleHint(rules);

        var user = await _db.AppUsers.Include(u => u.RolesList)
            .FirstOrDefaultAsync(u => u.UserName == CurrentUserName);
        if (user is null) return RedirectToPage("/Account/Login");

        if (!DatabaseSeeder.VerifyPassword(user, currentPassword ?? ""))
        {
            Error = _localizer["CurrentPasswordIncorrect"];
            return Page();
        }
        var passwordError = InputValidation.ValidatePassword(newPassword ?? "", rules.MinimumPasswordLength, rules.PasswordComplexityRequired);
        if (passwordError is not null)
        {
            Error = passwordError;
            return Page();
        }
        if (newPassword != confirmPassword)
        {
            Error = _localizer["PasswordsDoNotMatch"];
            return Page();
        }

        user.PasswordHash = DatabaseSeeder.HashPassword(user, newPassword!);
        user.MustChangePassword = false;
        user.PasswordChangedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Re-issue the cookie so MustChangePassword clears immediately (no re-login required).
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            AppUserClaims.ToPrincipal(AppUserClaims.Build(user), CookieAuthenticationDefaults.AuthenticationScheme));

        return LocalRedirect(ReturnUrl is { Length: > 0 } && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/Dashboard");
    }

    private async Task<string> BuildPasswordRuleHintAsync() => BuildPasswordRuleHint(await _settings.GetSecurityRulesAsync());

    private string BuildPasswordRuleHint(SecurityRules rules) =>
        rules.PasswordComplexityRequired
            ? _localizer["PasswordRuleWithComplexity", rules.MinimumPasswordLength]
            : _localizer["PasswordRuleLengthOnly", rules.MinimumPasswordLength];
}

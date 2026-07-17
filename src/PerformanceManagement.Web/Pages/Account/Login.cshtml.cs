using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;
using PerformanceManagement.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Web.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly PmDbContext _db;
    private readonly SettingsService _settings;
    private readonly IClock _clock;
    public LoginModel(PmDbContext db, SettingsService settings, IClock clock)
    {
        _db = db;
        _settings = settings;
        _clock = clock;
    }

    public string? Error { get; set; }
    public string Username { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string username, string password, bool rememberMe)
    {
        Username = username?.Trim() ?? "";
        var rules = await _settings.GetSecurityRulesAsync();
        var now = _clock.Now;

        var user = await _db.AppUsers.Include(u => u.RolesList)
            .FirstOrDefaultAsync(u => u.UserName.ToLower() == Username.ToLower() && u.IsActive);

        var isAdminUser = user is not null && user.RolesList.Any(r => r.Role == Roles.HrAdmin);
        if (user is not null && isAdminUser)
        {
            user.FailedLoginAttempts = 0;
            user.LockedOutUntil = null;
        }

        // Same generic wording for "no such user" and "locked out" as for a wrong password so
        // lockout state itself can't be used to enumerate valid usernames.
        if (user is not null && !isAdminUser && user.LockedOutUntil is { } lockedUntil && lockedUntil > now)
        {
            var minutesLeft = Math.Max(1, (int)Math.Ceiling((lockedUntil - now).TotalMinutes));
            Error = $"This account is temporarily locked due to repeated failed sign-in attempts. Try again in {minutesLeft} minute(s).";
            return Page();
        }

        if (user is null || !DatabaseSeeder.VerifyPassword(user, password ?? ""))
        {
            if (user is not null && rules.MaxLoginAttempts > 0 && !isAdminUser)
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= rules.MaxLoginAttempts)
                {
                    user.LockedOutUntil = now.AddMinutes(rules.AccountLockoutMinutes);
                    user.FailedLoginAttempts = 0;
                }
                await _db.SaveChangesAsync();
            }
            Error = "Invalid username or password.";
            return Page();
        }

        user.FailedLoginAttempts = 0;
        user.LockedOutUntil = null;

        // Password expiry takes effect on next successful login (not mid-session): once past
        // the configured age, the user is routed through the same forced-change flow used for
        // brand-new accounts, and PasswordChangedAt resets the clock once they comply.
        var expired = rules.PasswordExpiryDays > 0 && user.PasswordChangedAt is { } changedAt &&
                      (now - changedAt).TotalDays >= rules.PasswordExpiryDays;
        if (expired) user.MustChangePassword = true;
        await _db.SaveChangesAsync();

        var props = rememberMe
            ? new AuthenticationProperties { IsPersistent = true, ExpiresUtc = now.AddDays(rules.RememberMeDurationDays) }
            : new AuthenticationProperties { IsPersistent = false };

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            AppUserClaims.ToPrincipal(AppUserClaims.Build(user), CookieAuthenticationDefaults.AuthenticationScheme), props);

        // MustChangePassword takes precedence over any deep link, but the deep link itself
        // survives the detour — ChangePassword forwards ReturnUrl again on success.
        if (user.MustChangePassword) return RedirectToPage("/Account/ChangePassword", new { ReturnUrl });

        return LocalRedirect(ReturnUrl is { Length: > 0 } && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/Dashboard");
    }
}

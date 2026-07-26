using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Services;
using PerformanceManagement.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace PerformanceManagement.Web.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly PmDbContext _db;
    private readonly SettingsService _settings;
    private readonly IClock _clock;
    private readonly IStringLocalizer<LoginModel> _localizer;
    private readonly AuditService _audit;
    public LoginModel(PmDbContext db, SettingsService settings, IClock clock, IStringLocalizer<LoginModel> localizer, AuditService audit)
    {
        _db = db;
        _settings = settings;
        _clock = clock;
        _localizer = localizer;
        _audit = audit;
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

        // Admin accounts get no special exemption from lockout — they are the highest-value
        // credential in the system and need this protection more than anyone, not less. A locked
        // admin account recovers automatically after AccountLockoutMinutes; an org that needs a
        // faster break-glass path can clear LockedOutUntil directly in the database.

        // Same generic wording for "no such user" and "locked out" as for a wrong password so
        // lockout state itself can't be used to enumerate valid usernames.
        if (user is not null && user.LockedOutUntil is { } lockedUntil && lockedUntil > now)
        {
            var minutesLeft = Math.Max(1, (int)Math.Ceiling((lockedUntil - now).TotalMinutes));
            await _audit.LogAsync("Login Blocked: Account Locked", Username, empCode: user.EmpCode,
                details: $"IP: {HttpContext.Connection.RemoteIpAddress}");
            Error = _localizer["AccountLockedError", minutesLeft];
            return Page();
        }

        if (user is null || !DatabaseSeeder.VerifyPassword(user, password ?? ""))
        {
            if (user is not null && rules.MaxLoginAttempts > 0)
            {
                user.FailedLoginAttempts++;
                if (user.FailedLoginAttempts >= rules.MaxLoginAttempts)
                {
                    user.LockedOutUntil = now.AddMinutes(rules.AccountLockoutMinutes);
                    user.FailedLoginAttempts = 0;
                }
                await _db.SaveChangesAsync();
            }
            // Username is logged as typed even when it doesn't match a real account — that's
            // exactly the signal needed to spot a username-guessing/credential-stuffing attempt,
            // and the generic on-page error message above still gives an attacker no such signal.
            await _audit.LogAsync("Login Failed", Username, empCode: user?.EmpCode,
                details: $"IP: {HttpContext.Connection.RemoteIpAddress}");
            Error = _localizer["InvalidCredentialsError"];
            return Page();
        }

        user.FailedLoginAttempts = 0;
        user.LockedOutUntil = null;
        await _audit.LogAsync("Login Succeeded", Username, empCode: user.EmpCode,
            details: $"IP: {HttpContext.Connection.RemoteIpAddress}");

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

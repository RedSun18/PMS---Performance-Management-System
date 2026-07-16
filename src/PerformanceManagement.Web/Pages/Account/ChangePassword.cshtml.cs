using PerformanceManagement.Core.Data;
using PerformanceManagement.Web.Security;
using PerformanceManagement.Web.Validation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Web.Pages.Account;

/// <summary>
/// Reachable by any authenticated user regardless of MustChangePassword (it is the target
/// of that forced redirect). Requires the current password even when the change is forced,
/// per spec — the user already knows the temporary password since they just logged in with it.
/// </summary>
public class ChangePasswordModel : AppPageModel
{
    private readonly PmDbContext _db;
    public ChangePasswordModel(PmDbContext db) => _db = db;

    public string? Error { get; set; }
    /// <summary>True when the user was forced here (vs. a voluntary password change from settings).</summary>
    public bool IsForced { get; set; }
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public void OnGet() => IsForced = MustChangePassword;

    public async Task<IActionResult> OnPostAsync(string currentPassword, string newPassword, string confirmPassword)
    {
        IsForced = MustChangePassword;

        var user = await _db.AppUsers.Include(u => u.RolesList)
            .FirstOrDefaultAsync(u => u.UserName == CurrentUserName);
        if (user is null) return RedirectToPage("/Account/Login");

        if (!DatabaseSeeder.VerifyPassword(user, currentPassword ?? ""))
        {
            Error = "Current password is incorrect.";
            return Page();
        }
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < InputValidation.MinPasswordLength)
        {
            Error = $"New password must be at least {InputValidation.MinPasswordLength} characters.";
            return Page();
        }
        if (newPassword != confirmPassword)
        {
            Error = "New password and confirmation do not match.";
            return Page();
        }

        user.PasswordHash = DatabaseSeeder.HashPassword(user, newPassword);
        user.MustChangePassword = false;
        await _db.SaveChangesAsync();

        // Re-issue the cookie so MustChangePassword clears immediately (no re-login required).
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            AppUserClaims.ToPrincipal(AppUserClaims.Build(user), CookieAuthenticationDefaults.AuthenticationScheme));

        return LocalRedirect(ReturnUrl is { Length: > 0 } && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/Dashboard");
    }
}

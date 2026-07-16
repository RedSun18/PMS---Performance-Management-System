using PerformanceManagement.Core.Data;
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
    public LoginModel(PmDbContext db) => _db = db;

    public string? Error { get; set; }
    public string Username { get; set; } = "";
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string username, string password)
    {
        Username = username?.Trim() ?? "";
        var user = await _db.AppUsers.Include(u => u.RolesList)
            .FirstOrDefaultAsync(u => u.UserName.ToLower() == Username.ToLower() && u.IsActive);

        if (user is null || !DatabaseSeeder.VerifyPassword(user, password ?? ""))
        {
            Error = "Invalid username or password.";
            return Page();
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            AppUserClaims.ToPrincipal(AppUserClaims.Build(user), CookieAuthenticationDefaults.AuthenticationScheme));

        // MustChangePassword takes precedence over any deep link, but the deep link itself
        // survives the detour — ChangePassword forwards ReturnUrl again on success.
        if (user.MustChangePassword) return RedirectToPage("/Account/ChangePassword", new { ReturnUrl });

        return LocalRedirect(ReturnUrl is { Length: > 0 } && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/Dashboard");
    }
}

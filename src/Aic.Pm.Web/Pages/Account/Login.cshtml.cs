using System.Security.Claims;
using Aic.Pm.Core.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Aic.Pm.Web.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly PmDbContext _db;
    public LoginModel(PmDbContext db) => _db = db;

    public string? Error { get; set; }
    public string Username { get; set; } = "";

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(string username, string password, string? returnUrl)
    {
        Username = username?.Trim() ?? "";
        var user = await _db.AppUsers.Include(u => u.RolesList)
            .FirstOrDefaultAsync(u => u.UserName.ToLower() == Username.ToLower() && u.IsActive);

        if (user is null || !DatabaseSeeder.VerifyPassword(user, password ?? ""))
        {
            Error = "Invalid username or password.";
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.UserName),
            new("DisplayName", user.DisplayName),
            new("EmpCode", user.EmpCode ?? "")
        };
        claims.AddRange(user.RolesList.Select(r => new Claim(ClaimTypes.Role, r.Role)));

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

        return LocalRedirect(returnUrl is { Length: > 0 } && Url.IsLocalUrl(returnUrl) ? returnUrl : "/PmForm");
    }
}

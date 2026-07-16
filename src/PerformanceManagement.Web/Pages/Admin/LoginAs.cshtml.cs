using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;
using PerformanceManagement.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Web.Pages.Admin;

/// <summary>
/// Administrator "Login As" impersonation, gated by a secondary verification password
/// (separate from any account password). Deliberately NOT [Authorize(Roles=HrAdmin)] at the
/// class level: the "Return to Administrator" handler must remain reachable while the
/// current principal is the IMPERSONATED user, who typically holds no admin role at all.
/// Every handler enforces its own precise requirement instead. Nested impersonation
/// (impersonating while already impersonating) is blocked outright.
/// </summary>
public class LoginAsModel : AppPageModel
{
    private const string VerifiedSessionKey = "LoginAsVerifiedUntil";
    private static readonly TimeSpan VerificationValidity = TimeSpan.FromMinutes(5);

    private readonly PmDbContext _db;
    private readonly IConfiguration _config;
    private readonly IClock _clock;
    public LoginAsModel(PmDbContext db, IConfiguration config, IClock clock) { _db = db; _config = config; _clock = clock; }

    public bool Verified { get; set; }
    public string? Error { get; set; }
    public List<(int Id, string UserName, string DisplayName, string? EmpCode)> Users { get; set; } = new();
    public List<ImpersonationLog> RecentHistory { get; set; } = new();

    private string VerificationPassword => _config["Security:LoginAsVerificationPassword"] ?? "Password*123";

    public async Task<IActionResult> OnGetAsync()
    {
        if (IsImpersonating)
            return AccessDenied("You cannot start a new impersonation session while already impersonating another user. Return to your administrator session first.");
        if (!IsHrAdmin)
            return AccessDenied("Only an administrator can use Login As.");

        Verified = IsVerified();
        if (Verified) await LoadUsersAndHistoryAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostVerifyAsync(string verificationPassword)
    {
        if (IsImpersonating || !IsHrAdmin) return AccessDenied("Not permitted.");

        if (verificationPassword != VerificationPassword)
        {
            Error = "Incorrect verification password.";
            Verified = false;
            return Page();
        }

        HttpContext.Session.SetString(VerifiedSessionKey, (_clock.Now + VerificationValidity).ToString("O"));
        Verified = true;
        await LoadUsersAndHistoryAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostImpersonateAsync(int userId)
    {
        if (IsImpersonating || !IsHrAdmin) return AccessDenied("Not permitted.");
        if (!IsVerified())
        {
            Error = "Verification has expired. Please re-enter the verification password.";
            return Page();
        }

        var target = await _db.AppUsers.Include(u => u.RolesList).FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (target is null)
        {
            Error = "Selected user was not found or is disabled.";
            Verified = true;
            await LoadUsersAndHistoryAsync();
            return Page();
        }
        if (target.UserName.Equals(CurrentUserName, StringComparison.OrdinalIgnoreCase))
        {
            Error = "You are already signed in as this account.";
            Verified = true;
            await LoadUsersAndHistoryAsync();
            return Page();
        }

        var sessionId = Guid.NewGuid();
        _db.ImpersonationLogs.Add(new ImpersonationLog
        {
            AdminUserName = CurrentUserName,
            AdminDisplayName = CurrentDisplayName,
            ImpersonatedUserName = target.UserName,
            ImpersonatedDisplayName = target.DisplayName,
            ImpersonatedEmpCode = target.EmpCode,
            StartedAt = _clock.Now,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            SessionId = sessionId
        });
        await _db.SaveChangesAsync();

        var claims = AppUserClaims.Build(target);
        claims.Add(new(AppUserClaims.ImpersonationSessionId, sessionId.ToString()));
        claims.Add(new(AppUserClaims.OriginalAdminUserName, CurrentUserName));
        claims.Add(new(AppUserClaims.OriginalAdminDisplayName, CurrentDisplayName));

        HttpContext.Session.Remove(VerifiedSessionKey);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            AppUserClaims.ToPrincipal(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        return RedirectToPage("/Dashboard/Index");
    }

    public async Task<IActionResult> OnPostReturnAsync()
    {
        if (!IsImpersonating) return RedirectToPage("/Dashboard/Index");

        var sessionIdValue = User.FindFirst(AppUserClaims.ImpersonationSessionId)?.Value;
        if (Guid.TryParse(sessionIdValue, out var sessionId))
        {
            var log = await _db.ImpersonationLogs.FirstOrDefaultAsync(l => l.SessionId == sessionId && l.EndedAt == null);
            if (log is not null)
            {
                log.EndedAt = _clock.Now;
                await _db.SaveChangesAsync();
            }
        }

        var adminUserName = OriginalAdminUserName ?? "";
        var admin = await _db.AppUsers.Include(u => u.RolesList)
            .FirstOrDefaultAsync(u => u.UserName == adminUserName && u.IsActive);

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (admin is null) return RedirectToPage("/Account/Login");

        // Re-derive claims fresh from the DB — never trust the breadcrumb claims for roles.
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            AppUserClaims.ToPrincipal(AppUserClaims.Build(admin), CookieAuthenticationDefaults.AuthenticationScheme));

        return RedirectToPage("/Dashboard/Index");
    }

    private bool IsVerified()
    {
        var raw = HttpContext.Session.GetString(VerifiedSessionKey);
        return raw is not null && DateTime.TryParse(raw, out var until) && _clock.Now < until;
    }

    private async Task LoadUsersAndHistoryAsync()
    {
        // Project only the display fields actually needed — avoids pulling PasswordHash and
        // every other column (including RolesList) over the wire just to list users. The
        // anonymous type keeps this translatable to a plain multi-column SELECT; Npgsql
        // can't materialize a ValueTuple projection directly (it tries to read it as a
        // composite "record" type and throws), so the tuple itself is built client-side
        // afterward from the already-fetched rows.
        Users = (await _db.AppUsers.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.DisplayName)
                .Select(u => new { u.Id, u.UserName, u.DisplayName, u.EmpCode })
                .ToListAsync())
            .Select(u => (u.Id, u.UserName, u.DisplayName, u.EmpCode))
            .ToList();
        RecentHistory = await _db.ImpersonationLogs.AsNoTracking()
            .OrderByDescending(l => l.StartedAt).Take(50).ToListAsync();
    }

    private IActionResult AccessDenied(string detail)
    {
        TempData["Detail"] = detail;
        return RedirectToPage("/AccessDenied");
    }
}

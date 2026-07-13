using System.Security.Claims;
using Aic.Pm.Core.Domain;

namespace Aic.Pm.Web.Security;

/// <summary>
/// Single place that builds the claims for an authenticated AppUser, so Login, forced
/// Change Password re-sign-in, and Login-As impersonation/return all agree on shape.
/// </summary>
public static class AppUserClaims
{
    public const string EmpCode = "EmpCode";
    public const string DisplayName = "DisplayName";
    public const string MustChangePassword = "MustChangePassword";
    public const string ImpersonationSessionId = "ImpersonationSessionId";
    public const string OriginalAdminUserName = "OriginalAdminUserName";
    public const string OriginalAdminDisplayName = "OriginalAdminDisplayName";

    public static List<Claim> Build(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.UserName),
            new(DisplayName, user.DisplayName),
            new(EmpCode, user.EmpCode ?? ""),
            new(MustChangePassword, user.MustChangePassword ? "true" : "false")
        };
        claims.AddRange(user.RolesList.Select(r => new Claim(ClaimTypes.Role, r.Role)));
        return claims;
    }

    public static ClaimsPrincipal ToPrincipal(IEnumerable<Claim> claims, string scheme) =>
        new(new ClaimsIdentity(claims, scheme));
}

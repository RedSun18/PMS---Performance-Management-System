using Aic.Pm.Core.Domain;

namespace Aic.Pm.Web.Pages.Users;

/// <summary>User type is derived from role membership, never stored as its own column.</summary>
public static class UserTypes
{
    public static string Derive(AppUser u) =>
        u.RolesList.Any(r => r.Role == Roles.HrAdmin) ? UserType.Administrator
        : u.RolesList.Any(r => r.Role == Roles.Viewer) ? UserType.Viewer
        : UserType.Employee;

    public static string Label(string userType) => userType switch
    {
        UserType.Administrator => "Administrator",
        UserType.Viewer => "Viewer (read-only)",
        _ => "Employee"
    };
}

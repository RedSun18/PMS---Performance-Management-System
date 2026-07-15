using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Web.Pages.Users;

/// <summary>
/// User Management: the primary authentication system for this standalone app, replacing
/// the legacy accounts entirely. Administrator-only.
/// </summary>
[Authorize(Roles = Roles.HrAdmin)]
public class IndexModel : AppPageModel
{
    private readonly PmDbContext _db;
    public IndexModel(PmDbContext db) => _db = db;

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public List<Row> Rows { get; set; } = new();
    [TempData] public string? Message { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public record Row(int Id, string UserName, string DisplayName, string? Email,
        string UserType, string? EmpCode, bool IsActive, bool MustChangePassword);

    public async Task OnGetAsync()
    {
        var q = _db.AppUsers.AsNoTracking().Include(u => u.RolesList).AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim().ToLower();
            q = q.Where(u => u.UserName.ToLower().Contains(term) || u.DisplayName.ToLower().Contains(term)
                          || (u.Email != null && u.Email.ToLower().Contains(term)));
        }

        var users = await q.OrderBy(u => u.UserName).ToListAsync();
        Rows = users.Select(u => new Row(
            u.Id, u.UserName, u.DisplayName, u.Email,
            Users.UserTypes.Derive(u), u.EmpCode, u.IsActive, u.MustChangePassword)).ToList();
    }
}

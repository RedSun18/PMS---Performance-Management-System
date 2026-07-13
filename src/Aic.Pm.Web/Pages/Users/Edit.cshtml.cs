using Aic.Pm.Core.Data;
using Aic.Pm.Core.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aic.Pm.Web.Pages.Users;

[Authorize(Roles = Roles.HrAdmin)]
public class EditModel : AppPageModel
{
    private readonly PmDbContext _db;
    private readonly IConfiguration _config;
    public EditModel(PmDbContext db, IConfiguration config) { _db = db; _config = config; }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    public bool IsNew => Id is null;
    public string DefaultPasswordHint => _config["Security:DefaultUserPassword"] ?? "Password123";

    [BindProperty] public Input Form { get; set; } = new();
    public List<(string Code, string Label, string? Email)> EmployeeOptions { get; set; } = new();
    public string? Error { get; set; }
    [TempData] public string? Message { get; set; }

    public class Input
    {
        public string UserType { get; set; } = Aic.Pm.Core.Domain.UserType.Employee;
        public string? EmpCode { get; set; }
        public string FullName { get; set; } = "";
        public string UserName { get; set; } = "";
        public string? Email { get; set; }
        public bool IsActive { get; set; } = true;

        // Create-time password / reset-password panel
        public string? Password { get; set; }
        public bool UseDefaultPassword { get; set; }
        public bool ForceChangePassword { get; set; } = true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadEmployeeOptionsAsync();

        if (Id is int id)
        {
            var u = await _db.AppUsers.AsNoTracking().Include(x => x.RolesList).FirstOrDefaultAsync(x => x.Id == id);
            if (u is null) return RedirectToPage("Index");
            Form = new Input
            {
                UserType = UserTypes.Derive(u),
                EmpCode = u.EmpCode,
                FullName = u.DisplayName,
                UserName = u.UserName,
                Email = u.Email,
                IsActive = u.IsActive
            };
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await LoadEmployeeOptionsAsync();

        var username = (Form.UserName ?? "").Trim();
        if (username.Length == 0) { Error = "Username is required."; return Page(); }

        var dupe = await _db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserName.ToLower() == username.ToLower() && u.Id != (Id ?? 0));
        if (dupe is not null) { Error = $"Username '{username}' is already in use."; return Page(); }

        Employee? employee = null;
        if (Form.UserType == Aic.Pm.Core.Domain.UserType.Employee)
        {
            if (string.IsNullOrWhiteSpace(Form.EmpCode)) { Error = "Please select an employee."; return Page(); }
            employee = await _db.Employees.FindAsync(Form.EmpCode);
            if (employee is null) { Error = "Selected employee was not found."; return Page(); }

            var linkedToAnother = await _db.AppUsers.AsNoTracking()
                .AnyAsync(u => u.EmpCode == Form.EmpCode && u.Id != (Id ?? 0));
            if (linkedToAnother) { Error = $"Employee {Form.EmpCode} already has a user account."; return Page(); }
        }
        else if (string.IsNullOrWhiteSpace(Form.FullName))
        {
            Error = "Full name is required.";
            return Page();
        }

        AppUser user;
        var isNew = Id is null;
        if (isNew)
        {
            if (string.IsNullOrWhiteSpace(Form.Password) && !Form.UseDefaultPassword)
            {
                Error = "Enter a password or check 'Use default password'.";
                return Page();
            }
            user = new AppUser();
            _db.AppUsers.Add(user);
        }
        else
        {
            user = await _db.AppUsers.Include(u => u.RolesList).FirstOrDefaultAsync(u => u.Id == Id)
                   ?? throw new InvalidOperationException("User not found.");
        }

        user.UserName = username;
        user.DisplayName = Form.UserType == Aic.Pm.Core.Domain.UserType.Employee ? employee!.LatinName : Form.FullName.Trim();
        user.EmpCode = Form.UserType == Aic.Pm.Core.Domain.UserType.Employee ? employee!.EmpCode : null;
        user.Email = string.IsNullOrWhiteSpace(Form.Email)
            ? (Form.UserType == Aic.Pm.Core.Domain.UserType.Employee ? employee!.Email : null)
            : Form.Email.Trim();
        user.IsActive = Form.IsActive;

        if (isNew)
        {
            var password = Form.UseDefaultPassword ? DefaultPasswordHint : Form.Password!;
            user.PasswordHash = DatabaseSeeder.HashPassword(user, password);
            user.MustChangePassword = Form.ForceChangePassword;
        }

        user.RolesList.Clear();
        if (Form.UserType == Aic.Pm.Core.Domain.UserType.Administrator)
            user.RolesList.Add(new UserRole { Role = Roles.HrAdmin });
        else if (Form.UserType == Aic.Pm.Core.Domain.UserType.Viewer)
            user.RolesList.Add(new UserRole { Role = Roles.Viewer });

        await _db.SaveChangesAsync();
        Message = isNew ? $"User '{username}' created." : $"User '{username}' updated.";
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostResetPasswordAsync()
    {
        if (Id is not int id) return RedirectToPage("Index");

        var user = await _db.AppUsers.FindAsync(id);
        if (user is null) return RedirectToPage("Index");

        if (string.IsNullOrWhiteSpace(Form.Password) && !Form.UseDefaultPassword)
        {
            await LoadEmployeeOptionsAsync();
            Form.UserType = UserTypes.Derive(await _db.AppUsers.Include(u => u.RolesList).FirstAsync(u => u.Id == id));
            Error = "Enter a password or check 'Use default password'.";
            return Page();
        }

        var password = Form.UseDefaultPassword ? DefaultPasswordHint : Form.Password!;
        user.PasswordHash = DatabaseSeeder.HashPassword(user, password);
        user.MustChangePassword = Form.ForceChangePassword;
        await _db.SaveChangesAsync();

        Message = $"Password reset for '{user.UserName}'." + (Form.ForceChangePassword ? " They must change it at next login." : "");
        return RedirectToPage("Edit", new { id });
    }

    private async Task LoadEmployeeOptionsAsync()
    {
        EmployeeOptions = (await _db.Employees.AsNoTracking().Where(e => e.TermDate == null)
                .OrderBy(e => e.LatinName).ToListAsync())
            .Select(e => (e.EmpCode, $"{e.EmpCode} | {e.LatinName}", e.Email)).ToList();
    }
}

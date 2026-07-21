using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;
using PerformanceManagement.Web.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace PerformanceManagement.Web.Pages.Users;

[Authorize(Roles = Roles.HrAdmin)]
public class EditModel : AppPageModel
{
    private readonly PmDbContext _db;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;
    private readonly NotificationService _notifications;
    private readonly IStringLocalizer<EditModel> _localizer;
    public EditModel(PmDbContext db, SettingsService settings, AuditService audit, NotificationService notifications,
        IStringLocalizer<EditModel> localizer)
    {
        _db = db; _settings = settings; _audit = audit; _notifications = notifications; _localizer = localizer;
    }

    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    public bool IsNew => Id is null;
    public string DefaultPasswordHint { get; set; } = "Password123";
    public string PasswordRuleHint { get; set; } = "";

    [BindProperty] public Input Form { get; set; } = new();
    public List<(string Code, string Label, string? Email)> EmployeeOptions { get; set; } = new();
    public string? Error { get; set; }
    [TempData] public string? Message { get; set; }

    public class Input
    {
        public string UserType { get; set; } = PerformanceManagement.Core.Domain.UserType.Employee;
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
        await LoadPasswordHintsAsync();

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
        var rules = await LoadPasswordHintsAsync();

        var username = (Form.UserName ?? "").Trim();
        if (username.Length == 0) { Error = _localizer["UsernameRequiredError"]; return Page(); }

        var dupe = await _db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserName.ToLower() == username.ToLower() && u.Id != (Id ?? 0));
        if (dupe is not null) { Error = _localizer["UsernameInUseError", username]; return Page(); }

        Employee? employee = null;
        if (Form.UserType == PerformanceManagement.Core.Domain.UserType.Employee)
        {
            if (string.IsNullOrWhiteSpace(Form.EmpCode)) { Error = _localizer["SelectEmployeeError"]; return Page(); }
            employee = await _db.Employees.FindAsync(Form.EmpCode);
            if (employee is null) { Error = _localizer["EmployeeNotFoundError"]; return Page(); }

            var linkedToAnother = await _db.AppUsers.AsNoTracking()
                .AnyAsync(u => u.EmpCode == Form.EmpCode && u.Id != (Id ?? 0));
            if (linkedToAnother) { Error = _localizer["EmployeeAlreadyLinkedError", Form.EmpCode]; return Page(); }
        }
        else if (string.IsNullOrWhiteSpace(Form.FullName))
        {
            Error = _localizer["FullNameRequiredError"];
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Form.Email) && !InputValidation.IsValidEmail(Form.Email.Trim()))
        {
            Error = _localizer["InvalidEmailError", Form.Email];
            return Page();
        }

        AppUser user;
        var isNew = Id is null;
        if (isNew)
        {
            if (string.IsNullOrWhiteSpace(Form.Password) && !Form.UseDefaultPassword)
            {
                Error = _localizer["EnterPasswordOrDefaultError"];
                return Page();
            }
            if (!Form.UseDefaultPassword)
            {
                var passwordError = InputValidation.ValidatePassword(Form.Password!, rules.MinimumPasswordLength, rules.PasswordComplexityRequired);
                if (passwordError is not null) { Error = passwordError; return Page(); }
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
        user.DisplayName = Form.UserType == PerformanceManagement.Core.Domain.UserType.Employee ? employee!.LatinName : Form.FullName.Trim();
        user.EmpCode = Form.UserType == PerformanceManagement.Core.Domain.UserType.Employee ? employee!.EmpCode : null;
        user.Email = string.IsNullOrWhiteSpace(Form.Email)
            ? (Form.UserType == PerformanceManagement.Core.Domain.UserType.Employee ? employee!.Email : null)
            : Form.Email.Trim();
        user.IsActive = Form.IsActive;

        if (isNew)
        {
            var password = Form.UseDefaultPassword ? DefaultPasswordHint : Form.Password!;
            user.PasswordHash = DatabaseSeeder.HashPassword(user, password);
            user.MustChangePassword = Form.ForceChangePassword;
            user.PasswordChangedAt = DateTime.UtcNow;
        }

        user.RolesList.Clear();
        if (Form.UserType == PerformanceManagement.Core.Domain.UserType.Administrator)
            user.RolesList.Add(new UserRole { Role = Roles.HrAdmin });
        else if (Form.UserType == PerformanceManagement.Core.Domain.UserType.Viewer)
            user.RolesList.Add(new UserRole { Role = Roles.Viewer });

        await _db.SaveChangesAsync();
        await _audit.LogAsync(isNew ? "User Created" : "User Updated", CurrentUserName,
            empCode: user.EmpCode, entityType: "AppUser", entityId: user.Id.ToString(), details: username);
        if (isNew)
            await _notifications.CreateAsync(user.UserName, "Account Created",
                "Your account has been created. Sign in with the password provided by your administrator.", "UserCreated");
        Message = isNew ? _localizer["UserCreatedMessage", username] : _localizer["UserUpdatedMessage", username];
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostResetPasswordAsync()
    {
        if (Id is not int id) return RedirectToPage("Index");

        var user = await _db.AppUsers.FindAsync(id);
        if (user is null) return RedirectToPage("Index");

        var rules = await LoadPasswordHintsAsync();

        if (string.IsNullOrWhiteSpace(Form.Password) && !Form.UseDefaultPassword)
        {
            await LoadEmployeeOptionsAsync();
            Form.UserType = UserTypes.Derive(await _db.AppUsers.Include(u => u.RolesList).FirstAsync(u => u.Id == id));
            Error = _localizer["EnterPasswordOrDefaultError"];
            return Page();
        }
        if (!Form.UseDefaultPassword)
        {
            var passwordError = InputValidation.ValidatePassword(Form.Password!, rules.MinimumPasswordLength, rules.PasswordComplexityRequired);
            if (passwordError is not null)
            {
                await LoadEmployeeOptionsAsync();
                Form.UserType = UserTypes.Derive(await _db.AppUsers.Include(u => u.RolesList).FirstAsync(u => u.Id == id));
                Error = passwordError;
                return Page();
            }
        }

        var password = Form.UseDefaultPassword ? DefaultPasswordHint : Form.Password!;
        user.PasswordHash = DatabaseSeeder.HashPassword(user, password);
        user.MustChangePassword = Form.ForceChangePassword;
        user.FailedLoginAttempts = 0;
        user.LockedOutUntil = null;
        user.PasswordChangedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Password Reset", CurrentUserName,
            empCode: user.EmpCode, entityType: "AppUser", entityId: user.Id.ToString(), details: user.UserName);
        await _notifications.CreateAsync(user.UserName, "Password Reset",
            "Your password was reset by an administrator." + (Form.ForceChangePassword ? " You must change it at next login." : ""), "PasswordReset");

        Message = Form.ForceChangePassword
            ? _localizer["PasswordResetMessageMustChange", user.UserName]
            : _localizer["PasswordResetMessage", user.UserName];
        return RedirectToPage("Edit", new { id });
    }

    private async Task LoadEmployeeOptionsAsync()
    {
        EmployeeOptions = (await _db.Employees.AsNoTracking().Where(e => e.TermDate == null)
                .OrderBy(e => e.LatinName).ToListAsync())
            .Select(e => (e.EmpCode, $"{e.EmpCode} | {e.LatinName}", e.Email)).ToList();
    }

    private async Task<SecurityRules> LoadPasswordHintsAsync()
    {
        var rules = await _settings.GetSecurityRulesAsync();
        DefaultPasswordHint = rules.DefaultUserPassword;
        PasswordRuleHint = rules.PasswordComplexityRequired
            ? _localizer["PasswordRuleHintComplexity", rules.MinimumPasswordLength]
            : _localizer["PasswordRuleHintSimple", rules.MinimumPasswordLength];
        return rules;
    }
}

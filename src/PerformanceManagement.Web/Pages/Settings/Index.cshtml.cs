using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PerformanceManagement.Web.Pages.Settings;

/// <summary>
/// System Settings — administrator only. Started with the Email/SMTP category (Phase 7);
/// further categories are added as new form sections on this same page in later phases.
/// </summary>
[Authorize(Roles = Roles.HrAdmin)]
public class IndexModel : AppPageModel
{
    private readonly SettingsService _settings;
    public IndexModel(SettingsService settings) => _settings = settings;

    [BindProperty] public EmailForm Form { get; set; } = new();
    public bool HasPassword { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    [TempData] public string? Message { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public class EmailForm
    {
        public string? SmtpHost { get; set; }
        public int SmtpPort { get; set; } = 587;
        public string? SmtpUsername { get; set; }
        public string? NewPassword { get; set; }
        public string? SenderName { get; set; }
        public string? SenderEmail { get; set; }
        public bool EnableSsl { get; set; } = true;
        public bool EnableEmailNotifications { get; set; } = true;
        public string? DevelopmentRedirectEmail { get; set; }
    }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await _settings.SaveEmailSettingsAsync(new EmailSettingsInput(
            Form.SmtpHost, Form.SmtpPort, Form.SmtpUsername, Form.NewPassword,
            Form.SenderName, Form.SenderEmail, Form.EnableSsl, Form.EnableEmailNotifications,
            Form.DevelopmentRedirectEmail), CurrentUserName);

        Message = "Email settings saved.";
        return RedirectToPage();
    }

    /// <summary>Tests the currently SAVED configuration (click Save first if you just changed something).</summary>
    public async Task<IActionResult> OnPostTestEmailAsync()
    {
        var creds = await _settings.GetSmtpCredentialsAsync();
        if (creds is null)
        {
            ErrorMessage = "SMTP is not configured (host/username missing). Save your settings first.";
            return RedirectToPage();
        }

        var target = string.IsNullOrWhiteSpace(creds.DevelopmentRedirectEmail) ? creds.SenderEmail : creds.DevelopmentRedirectEmail;
        try
        {
            await EmailService.SendTestEmailAsync(creds, target, "Performance Management System");
            Message = $"Test email sent successfully to {target}. Check the inbox to confirm delivery.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Test email failed: {ex.Message}";
        }
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var s = await _settings.GetEmailSettingsAsync();
        Form = new EmailForm
        {
            SmtpHost = s.SmtpHost,
            SmtpPort = s.SmtpPort ?? 587,
            SmtpUsername = s.SmtpUsername,
            SenderName = s.SenderName,
            SenderEmail = s.SenderEmail,
            EnableSsl = s.EnableSsl,
            EnableEmailNotifications = s.EnableEmailNotifications,
            DevelopmentRedirectEmail = s.DevelopmentRedirectEmail
        };
        HasPassword = s.HasPassword;
        UpdatedAt = s.UpdatedAt;
        UpdatedBy = s.UpdatedBy;
    }
}

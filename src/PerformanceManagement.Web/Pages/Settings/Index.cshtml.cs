using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace PerformanceManagement.Web.Pages.Settings;

/// <summary>
/// System Settings — administrator only. One tabbed page: General, Performance Review,
/// Email, Authentication, Security, Dashboard, Branding. Each tab saves independently via
/// its own POST handler so editing one category never risks clobbering another.
/// </summary>
[Authorize(Roles = Roles.HrAdmin)]
public class IndexModel : AppPageModel
{
    private static readonly string[] AllowedLogoExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".svg" };
    private const long MaxLogoBytes = 2 * 1024 * 1024;

    private readonly SettingsService _settings;
    private readonly IWebHostEnvironment _env;
    public IndexModel(SettingsService settings, IWebHostEnvironment env) { _settings = settings; _env = env; }

    private static readonly string[] KnownTabs = { "general", "review", "email", "auth", "security", "dashboard", "branding" };
    private string _tab = "general";

    // Normalizes case and falls back to "general" for anything unrecognized (a stale bookmark,
    // a hand-typed URL, a differently-cased link) — the view's tab conditionals have no final
    // else branch, so an unmatched value previously rendered a blank content area with only the
    // tab bar showing, silently, with nothing telling the user what went wrong.
    [BindProperty(SupportsGet = true)]
    public string Tab
    {
        get => _tab;
        set => _tab = value is not null && KnownTabs.Contains(value.Trim().ToLowerInvariant())
            ? value.Trim().ToLowerInvariant()
            : "general";
    }

    [BindProperty] public GeneralForm General { get; set; } = new();
    [BindProperty] public ReviewForm Review { get; set; } = new();
    [BindProperty] public EmailForm Email { get; set; } = new();
    [BindProperty] public AuthForm Auth { get; set; } = new();
    [BindProperty] public SecurityForm Security { get; set; } = new();
    [BindProperty] public DashboardForm DashboardSettings { get; set; } = new();
    [BindProperty] public BrandingForm Branding { get; set; } = new();
    [BindProperty] public IFormFile? LogoFile { get; set; }

    public bool HasEmailPassword { get; set; }
    public bool HasCustomVerificationPassword { get; set; }
    public DateTime? EmailUpdatedAt { get; set; }
    public string? EmailUpdatedBy { get; set; }

    [TempData] public string? Message { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public class GeneralForm
    {
        public string? CompanyName { get; set; }
        public string? ApplicationName { get; set; }
        public string? CompanyAddress { get; set; }
        public string? ContactEmail { get; set; }
        public string? ApplicationBaseUrl { get; set; }
    }

    public class ReviewForm
    {
        public int? CurrentReviewYear { get; set; }
        public DateOnly? MidYearStart { get; set; }
        public DateOnly? MidYearEnd { get; set; }
        public DateOnly? EndYearStart { get; set; }
        public DateOnly? EndYearEnd { get; set; }
    }

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

    public class AuthForm
    {
        public string? DefaultUserPassword { get; set; }
        public bool PasswordComplexityRequired { get; set; }
        public int MinimumPasswordLength { get; set; } = 6;
        public int SessionTimeoutMinutes { get; set; } = 480;
        public string? NewLoginAsVerificationPassword { get; set; }
        public int MaxLoginAttempts { get; set; } = 5;
    }

    public class SecurityForm
    {
        public bool EnableAuditLogging { get; set; } = true;
        public int AccountLockoutMinutes { get; set; } = 15;
        public int PasswordExpiryDays { get; set; }
        public int RememberMeDurationDays { get; set; } = 30;
    }

    public class DashboardForm
    {
        public string? WelcomeMessage { get; set; }
        public string? AnnouncementBanner { get; set; }
    }

    public class BrandingForm
    {
        public string? CompanyLogoPath { get; set; }
        public string? PrimaryColorHex { get; set; }
        public string? SecondaryColorHex { get; set; }
        public string? FooterText { get; set; }
    }

    public async Task OnGetAsync() => await LoadAsync();

    // ---- General --------------------------------------------------------
    public async Task<IActionResult> OnPostSaveGeneralAsync()
    {
        await _settings.SaveGeneralSettingsAsync(new GeneralSettings(
            General.CompanyName, General.ApplicationName, null, General.CompanyAddress,
            General.ContactEmail, General.ApplicationBaseUrl), CurrentUserName);
        Message = "General settings saved.";
        return RedirectToPage(new { Tab = "general" });
    }

    // ---- Performance Review --------------------------------------------------
    public async Task<IActionResult> OnPostSaveReviewAsync()
    {
        await _settings.SavePerformanceReviewSettingsAsync(new PerformanceReviewSettings(
            Review.CurrentReviewYear, Review.MidYearStart, Review.MidYearEnd, Review.EndYearStart, Review.EndYearEnd), CurrentUserName);
        Message = "Performance review settings saved.";
        return RedirectToPage(new { Tab = "review" });
    }

    // ---- Email --------------------------------------------------------------
    public async Task<IActionResult> OnPostSaveEmailAsync()
    {
        await _settings.SaveEmailSettingsAsync(new EmailSettingsInput(
            Email.SmtpHost, Email.SmtpPort, Email.SmtpUsername, Email.NewPassword,
            Email.SenderName, Email.SenderEmail, Email.EnableSsl, Email.EnableEmailNotifications,
            Email.DevelopmentRedirectEmail), CurrentUserName);
        Message = "Email settings saved.";
        return RedirectToPage(new { Tab = "email" });
    }

    /// <summary>Tests the currently SAVED configuration (click Save first if you just changed something).</summary>
    public async Task<IActionResult> OnPostTestEmailAsync()
    {
        var creds = await _settings.GetSmtpCredentialsAsync();
        if (creds is null)
        {
            ErrorMessage = "SMTP is not configured (host/username missing). Save your settings first.";
            return RedirectToPage(new { Tab = "email" });
        }

        var appName = await _settings.GetApplicationNameAsync();
        var target = string.IsNullOrWhiteSpace(creds.DevelopmentRedirectEmail) ? creds.SenderEmail : creds.DevelopmentRedirectEmail;
        try
        {
            await EmailService.SendTestEmailAsync(creds, target, appName);
            Message = $"Test email sent successfully to {target}. Check the inbox to confirm delivery.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Test email failed: {ex.Message}";
        }
        return RedirectToPage(new { Tab = "email" });
    }

    // ---- Authentication ----------------------------------------------------
    public async Task<IActionResult> OnPostSaveAuthAsync()
    {
        await _settings.SaveAuthenticationSettingsAsync(new AuthenticationSettingsInput(
            Auth.DefaultUserPassword, Auth.PasswordComplexityRequired, Auth.MinimumPasswordLength,
            Auth.SessionTimeoutMinutes, Auth.NewLoginAsVerificationPassword, Auth.MaxLoginAttempts), CurrentUserName);
        Message = "Authentication settings saved. Session timeout changes take effect after the app restarts.";
        return RedirectToPage(new { Tab = "auth" });
    }

    // ---- Security ------------------------------------------------------------
    public async Task<IActionResult> OnPostSaveSecurityAsync()
    {
        await _settings.SaveSecuritySettingsAsync(new SecuritySettings(
            Security.EnableAuditLogging, Security.AccountLockoutMinutes,
            Security.PasswordExpiryDays, Security.RememberMeDurationDays), CurrentUserName);
        Message = "Security settings saved.";
        return RedirectToPage(new { Tab = "security" });
    }

    // ---- Dashboard -----------------------------------------------------------
    public async Task<IActionResult> OnPostSaveDashboardAsync()
    {
        await _settings.SaveDashboardSettingsAsync(new DashboardSettings(
            DashboardSettings.WelcomeMessage, DashboardSettings.AnnouncementBanner), CurrentUserName);
        Message = "Dashboard settings saved.";
        return RedirectToPage(new { Tab = "dashboard" });
    }

    // ---- Branding ------------------------------------------------------------
    public async Task<IActionResult> OnPostSaveBrandingAsync()
    {
        string? logoPath = null;
        if (LogoFile is { Length: > 0 })
        {
            var ext = Path.GetExtension(LogoFile.FileName).ToLowerInvariant();
            if (!AllowedLogoExtensions.Contains(ext) || !LogoFile.ContentType.StartsWith("image/"))
            {
                ErrorMessage = "Logo must be an image file (PNG, JPG, GIF or SVG).";
                await LoadAsync();
                return Page();
            }
            if (LogoFile.Length > MaxLogoBytes)
            {
                ErrorMessage = "Logo file must be smaller than 2 MB.";
                await LoadAsync();
                return Page();
            }

            var dir = Path.Combine(_env.WebRootPath, "uploads", "branding");
            Directory.CreateDirectory(dir);
            var fileName = $"logo-{Guid.NewGuid():N}{ext}";
            await using (var stream = System.IO.File.Create(Path.Combine(dir, fileName)))
                await LogoFile.CopyToAsync(stream);
            logoPath = $"/uploads/branding/{fileName}";
        }

        await _settings.SaveBrandingSettingsAsync(new BrandingSettings(
            logoPath, Branding.PrimaryColorHex, Branding.SecondaryColorHex, Branding.FooterText), CurrentUserName);
        Message = "Branding settings saved.";
        return RedirectToPage(new { Tab = "branding" });
    }

    // ======================================================================
    private async Task LoadAsync()
    {
        var general = await _settings.GetGeneralSettingsAsync();
        General = new GeneralForm
        {
            CompanyName = general.CompanyName, ApplicationName = general.ApplicationName,
            CompanyAddress = general.CompanyAddress, ContactEmail = general.ContactEmail,
            ApplicationBaseUrl = general.ApplicationBaseUrl
        };

        var review = await _settings.GetPerformanceReviewSettingsAsync();
        Review = new ReviewForm
        {
            CurrentReviewYear = review.CurrentReviewYear, MidYearStart = review.MidYearStart,
            MidYearEnd = review.MidYearEnd, EndYearStart = review.EndYearStart, EndYearEnd = review.EndYearEnd
        };

        var email = await _settings.GetEmailSettingsAsync();
        Email = new EmailForm
        {
            SmtpHost = email.SmtpHost, SmtpPort = email.SmtpPort ?? 587, SmtpUsername = email.SmtpUsername,
            SenderName = email.SenderName, SenderEmail = email.SenderEmail, EnableSsl = email.EnableSsl,
            EnableEmailNotifications = email.EnableEmailNotifications, DevelopmentRedirectEmail = email.DevelopmentRedirectEmail
        };
        HasEmailPassword = email.HasPassword;
        EmailUpdatedAt = email.UpdatedAt;
        EmailUpdatedBy = email.UpdatedBy;

        var auth = await _settings.GetAuthenticationSettingsAsync();
        Auth = new AuthForm
        {
            DefaultUserPassword = auth.DefaultUserPassword, PasswordComplexityRequired = auth.PasswordComplexityRequired,
            MinimumPasswordLength = auth.MinimumPasswordLength, SessionTimeoutMinutes = auth.SessionTimeoutMinutes,
            MaxLoginAttempts = auth.MaxLoginAttempts
        };
        HasCustomVerificationPassword = auth.HasCustomLoginAsVerificationPassword;

        var security = await _settings.GetSecuritySettingsAsync();
        Security = new SecurityForm
        {
            EnableAuditLogging = security.EnableAuditLogging, AccountLockoutMinutes = security.AccountLockoutMinutes,
            PasswordExpiryDays = security.PasswordExpiryDays, RememberMeDurationDays = security.RememberMeDurationDays
        };

        var dashboard = await _settings.GetDashboardSettingsAsync();
        DashboardSettings = new DashboardForm { WelcomeMessage = dashboard.WelcomeMessage, AnnouncementBanner = dashboard.AnnouncementBanner };

        var branding = await _settings.GetBrandingSettingsAsync();
        Branding = new BrandingForm
        {
            CompanyLogoPath = branding.CompanyLogoPath, PrimaryColorHex = branding.PrimaryColorHex,
            SecondaryColorHex = branding.SecondaryColorHex, FooterText = branding.FooterText
        };
    }
}

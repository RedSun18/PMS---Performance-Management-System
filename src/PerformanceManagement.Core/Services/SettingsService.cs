using PerformanceManagement.Core.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;

namespace PerformanceManagement.Core.Services;

/// <summary>Email settings as shown on the System Settings page — password is never exposed, only whether one is set.</summary>
public record EmailSettingsView(
    string? SmtpHost, int? SmtpPort, string? SmtpUsername, bool HasPassword,
    string? SenderName, string? SenderEmail, bool EnableSsl, bool EnableEmailNotifications,
    string? DevelopmentRedirectEmail, DateTime? UpdatedAt, string? UpdatedBy);

/// <summary>Submitted form values. NewPassword is null/blank ⇒ keep the existing stored password.</summary>
public record EmailSettingsInput(
    string? SmtpHost, int? SmtpPort, string? SmtpUsername, string? NewPassword,
    string? SenderName, string? SenderEmail, bool EnableSsl, bool EnableEmailNotifications,
    string? DevelopmentRedirectEmail);

/// <summary>Decrypted credentials for actual SMTP use — never returned to any page.</summary>
public record SmtpCredentials(
    string Host, int Port, string Username, string Password,
    string SenderName, string SenderEmail, bool EnableSsl,
    bool EnableEmailNotifications, string? DevelopmentRedirectEmail);

public record GeneralSettings(string? CompanyName, string? ApplicationName, string? CompanyLogoPath,
    string? CompanyAddress, string? ContactEmail, string? ApplicationBaseUrl, bool LanguageSelectionEnabled);

public record PerformanceReviewSettings(int? CurrentReviewYear,
    DateOnly? MidYearStart, DateOnly? MidYearEnd, DateOnly? EndYearStart, DateOnly? EndYearEnd,
    DateOnly? MidYearAchievementStartDate, DateOnly? EndYearAchievementStartDate, DateOnly? SubmitToHrStartDate);

/// <summary>Authentication settings as shown on the Settings page — the verification password is
/// never exposed, only whether a non-default one is set.</summary>
public record AuthenticationSettingsView(string? DefaultUserPassword, bool PasswordComplexityRequired,
    int MinimumPasswordLength, int SessionTimeoutMinutes, bool HasCustomLoginAsVerificationPassword,
    int MaxLoginAttempts);

public record AuthenticationSettingsInput(string? DefaultUserPassword, bool PasswordComplexityRequired,
    int MinimumPasswordLength, int SessionTimeoutMinutes, string? NewLoginAsVerificationPassword,
    int MaxLoginAttempts);

public record SecuritySettings(bool EnableAuditLogging, int AccountLockoutMinutes,
    int PasswordExpiryDays, int RememberMeDurationDays);

public record DashboardSettings(string? WelcomeMessage, string? AnnouncementBanner);

public record BrandingSettings(string? CompanyLogoPath, string? PrimaryColorHex,
    string? SecondaryColorHex, string? FooterText);

/// <summary>Combined rules consumed at runtime by Login/ChangePassword/AppPageModel — one round
/// trip instead of several separate Get calls when multiple values are needed together.</summary>
public record SecurityRules(int MinimumPasswordLength, bool PasswordComplexityRequired,
    int MaxLoginAttempts, int AccountLockoutMinutes, int PasswordExpiryDays,
    int RememberMeDurationDays, int SessionTimeoutMinutes, bool EnableAuditLogging,
    string DefaultUserPassword);

/// <summary>
/// Database-backed application settings (singleton row, Id = 1): General, Performance Review,
/// Email, Authentication, Security, Dashboard, and Branding categories. On first run with no
/// row present, seeds itself once from IConfiguration (appsettings/User Secrets) so local dev
/// works without ever visiting the Settings page. After that the DB row is authoritative.
/// </summary>
public class SettingsService
{
    private const string DefaultLoginAsVerificationPassword = "Password123";
    private const string DefaultSeedPassword = "Password123";

    private readonly PmDbContext _db;
    private readonly IConfiguration _config;
    private readonly IDataProtector _smtpProtector;
    private readonly IDataProtector _verificationProtector;

    public SettingsService(PmDbContext db, IConfiguration config, IDataProtectionProvider dataProtection)
    {
        _db = db;
        _config = config;
        _smtpProtector = dataProtection.CreateProtector("PerformanceManagement.Settings.Smtp");
        _verificationProtector = dataProtection.CreateProtector("PerformanceManagement.Settings.LoginAsVerification");
    }

    private async Task<Domain.SystemSettings> GetOrCreateAsync()
    {
        var row = await _db.SystemSettings.FirstOrDefaultAsync(x => x.Id == 1);
        if (row is not null) return row;

        // First run: seed from configuration (Email:*/General:* sections) so a dev machine
        // with User Secrets/appsettings configured works immediately, with no manual DB step.
        var section = _config.GetSection("Email");
        row = new Domain.SystemSettings
        {
            Id = 1,
            ApplicationName = "Performance Management System",
            ApplicationBaseUrl = _config["General:ApplicationBaseUrl"],
            SmtpHost = section["SmtpHost"],
            SmtpPort = int.TryParse(section["SmtpPort"], out var p) ? p : 587,
            SmtpUsername = section["SmtpUsername"],
            SmtpPasswordProtected = string.IsNullOrEmpty(section["SmtpPassword"])
                ? null : _smtpProtector.Protect(section["SmtpPassword"]!),
            SenderName = section["SenderName"] ?? "Performance Management System",
            SenderEmail = section["SenderEmail"] ?? section["SmtpUsername"],
            SmtpEnableSsl = !bool.TryParse(section["EnableSsl"], out var ssl) || ssl,
            EnableEmailNotifications = !bool.TryParse(section["EnableEmailNotifications"], out var en) || en,
            DevelopmentRedirectEmail = section["DevelopmentRedirectEmail"]
        };
        _db.SystemSettings.Add(row);
        await _db.SaveChangesAsync();
        return row;
    }

    private void Touch(Domain.SystemSettings row, string updatedBy)
    {
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = updatedBy;
    }

    // ==================================================================== General
    public async Task<GeneralSettings> GetGeneralSettingsAsync()
    {
        var row = await GetOrCreateAsync();
        return new GeneralSettings(row.CompanyName, row.ApplicationName, row.CompanyLogoPath,
            row.CompanyAddress, row.ContactEmail, row.ApplicationBaseUrl, row.LanguageSelectionEnabled);
    }

    public async Task SaveGeneralSettingsAsync(GeneralSettings input, string updatedBy)
    {
        var row = await GetOrCreateAsync();
        row.CompanyName = Trimmed(input.CompanyName);
        row.ApplicationName = Trimmed(input.ApplicationName);
        row.CompanyAddress = Trimmed(input.CompanyAddress);
        row.ContactEmail = Trimmed(input.ContactEmail);
        row.ApplicationBaseUrl = string.IsNullOrWhiteSpace(input.ApplicationBaseUrl) ? null : input.ApplicationBaseUrl.Trim().TrimEnd('/');
        row.LanguageSelectionEnabled = input.LanguageSelectionEnabled;
        Touch(row, updatedBy);
        await _db.SaveChangesAsync();
    }

    /// <summary>Cheap standalone check for the culture provider (Program.cs) — avoids building a
    /// full GeneralSettings/EF-tracked row just to read one flag on every request.</summary>
    public async Task<bool> IsLanguageSelectionEnabledAsync()
    {
        var row = await GetOrCreateAsync();
        return row.LanguageSelectionEnabled;
    }

    /// <summary>The application's display name for branding (email headers, layout title) — falls back to the product default.</summary>
    public async Task<string> GetApplicationNameAsync()
    {
        var row = await GetOrCreateAsync();
        return string.IsNullOrWhiteSpace(row.ApplicationName) ? "Performance Management System" : row.ApplicationName;
    }

    /// <summary>Public origin used to build absolute links in outgoing email. Falls back to the local dev URL if unset.</summary>
    public async Task<string> GetApplicationBaseUrlAsync()
    {
        var row = await GetOrCreateAsync();
        return string.IsNullOrWhiteSpace(row.ApplicationBaseUrl)
            ? "http://localhost:5273" : row.ApplicationBaseUrl.Trim().TrimEnd('/');
    }

    public async Task SaveCompanyLogoPathAsync(string? path, string updatedBy)
    {
        var row = await GetOrCreateAsync();
        row.CompanyLogoPath = path;
        Touch(row, updatedBy);
        await _db.SaveChangesAsync();
    }

    // ==================================================================== Performance Review
    public async Task<PerformanceReviewSettings> GetPerformanceReviewSettingsAsync()
    {
        var row = await GetOrCreateAsync();
        return new PerformanceReviewSettings(row.CurrentReviewYear, row.MidYearStart, row.MidYearEnd, row.EndYearStart, row.EndYearEnd,
            row.MidYearAchievementStartDate, row.EndYearAchievementStartDate, row.SubmitToHrStartDate);
    }

    public async Task SavePerformanceReviewSettingsAsync(PerformanceReviewSettings input, string updatedBy)
    {
        var row = await GetOrCreateAsync();
        row.CurrentReviewYear = input.CurrentReviewYear;
        row.MidYearStart = input.MidYearStart;
        row.MidYearEnd = input.MidYearEnd;
        row.EndYearStart = input.EndYearStart;
        row.EndYearEnd = input.EndYearEnd;
        row.MidYearAchievementStartDate = input.MidYearAchievementStartDate;
        row.EndYearAchievementStartDate = input.EndYearAchievementStartDate;
        row.SubmitToHrStartDate = input.SubmitToHrStartDate;
        Touch(row, updatedBy);
        await _db.SaveChangesAsync();
    }

    // ==================================================================== Email
    public async Task<EmailSettingsView> GetEmailSettingsAsync()
    {
        var row = await GetOrCreateAsync();
        return new EmailSettingsView(
            row.SmtpHost, row.SmtpPort, row.SmtpUsername, !string.IsNullOrEmpty(row.SmtpPasswordProtected),
            row.SenderName, row.SenderEmail, row.SmtpEnableSsl, row.EnableEmailNotifications,
            row.DevelopmentRedirectEmail, row.UpdatedAt, row.UpdatedBy);
    }

    /// <summary>Decrypted credentials for the mail sender only. Returns null if no host/username is configured.</summary>
    public async Task<SmtpCredentials?> GetSmtpCredentialsAsync()
    {
        var row = await GetOrCreateAsync();
        if (string.IsNullOrWhiteSpace(row.SmtpHost) || string.IsNullOrWhiteSpace(row.SmtpUsername))
            return null;

        string password;
        try
        {
            password = string.IsNullOrEmpty(row.SmtpPasswordProtected)
                ? "" : _smtpProtector.Unprotect(row.SmtpPasswordProtected);
        }
        catch (CryptographicException)
        {
            // Data Protection key ring rotated/reset since the password was saved — treat as unset.
            return null;
        }
        if (password.Length == 0) return null;

        return new SmtpCredentials(
            row.SmtpHost, row.SmtpPort ?? 587, row.SmtpUsername, password,
            row.SenderName ?? "Performance Management System", row.SenderEmail ?? row.SmtpUsername,
            row.SmtpEnableSsl, row.EnableEmailNotifications, row.DevelopmentRedirectEmail);
    }

    public async Task SaveEmailSettingsAsync(EmailSettingsInput input, string updatedBy)
    {
        var row = await GetOrCreateAsync();
        row.SmtpHost = input.SmtpHost?.Trim();
        row.SmtpPort = input.SmtpPort;
        row.SmtpUsername = input.SmtpUsername?.Trim();
        if (!string.IsNullOrWhiteSpace(input.NewPassword))
            row.SmtpPasswordProtected = _smtpProtector.Protect(input.NewPassword);
        row.SenderName = input.SenderName?.Trim();
        row.SenderEmail = input.SenderEmail?.Trim();
        row.SmtpEnableSsl = input.EnableSsl;
        row.EnableEmailNotifications = input.EnableEmailNotifications;
        row.DevelopmentRedirectEmail = string.IsNullOrWhiteSpace(input.DevelopmentRedirectEmail)
            ? null : input.DevelopmentRedirectEmail.Trim();
        Touch(row, updatedBy);
        await _db.SaveChangesAsync();
    }

    // ==================================================================== Authentication
    public async Task<AuthenticationSettingsView> GetAuthenticationSettingsAsync()
    {
        var row = await GetOrCreateAsync();
        return new AuthenticationSettingsView(
            string.IsNullOrWhiteSpace(row.DefaultUserPassword) ? DefaultSeedPassword : row.DefaultUserPassword,
            row.PasswordComplexityRequired, row.MinimumPasswordLength, row.SessionTimeoutMinutes,
            !string.IsNullOrEmpty(row.LoginAsVerificationPasswordProtected), row.MaxLoginAttempts);
    }

    public async Task SaveAuthenticationSettingsAsync(AuthenticationSettingsInput input, string updatedBy)
    {
        var row = await GetOrCreateAsync();
        row.DefaultUserPassword = Trimmed(input.DefaultUserPassword);
        row.PasswordComplexityRequired = input.PasswordComplexityRequired;
        row.MinimumPasswordLength = input.MinimumPasswordLength < 4 ? 4 : input.MinimumPasswordLength;
        row.SessionTimeoutMinutes = input.SessionTimeoutMinutes < 5 ? 5 : input.SessionTimeoutMinutes;
        if (!string.IsNullOrWhiteSpace(input.NewLoginAsVerificationPassword))
            row.LoginAsVerificationPasswordProtected = _verificationProtector.Protect(input.NewLoginAsVerificationPassword);
        row.MaxLoginAttempts = input.MaxLoginAttempts;
        Touch(row, updatedBy);
        await _db.SaveChangesAsync();
    }

    public async Task<string> GetLoginAsVerificationPasswordAsync()
    {
        var row = await GetOrCreateAsync();
        if (string.IsNullOrEmpty(row.LoginAsVerificationPasswordProtected)) return DefaultLoginAsVerificationPassword;
        try { return _verificationProtector.Unprotect(row.LoginAsVerificationPasswordProtected); }
        catch (CryptographicException) { return DefaultLoginAsVerificationPassword; }
    }

    // ==================================================================== Security
    public async Task<SecuritySettings> GetSecuritySettingsAsync()
    {
        var row = await GetOrCreateAsync();
        return new SecuritySettings(row.EnableAuditLogging, row.AccountLockoutMinutes, row.PasswordExpiryDays, row.RememberMeDurationDays);
    }

    public async Task SaveSecuritySettingsAsync(SecuritySettings input, string updatedBy)
    {
        var row = await GetOrCreateAsync();
        row.EnableAuditLogging = input.EnableAuditLogging;
        row.AccountLockoutMinutes = input.AccountLockoutMinutes < 1 ? 1 : input.AccountLockoutMinutes;
        row.PasswordExpiryDays = input.PasswordExpiryDays < 0 ? 0 : input.PasswordExpiryDays;
        row.RememberMeDurationDays = input.RememberMeDurationDays < 1 ? 1 : input.RememberMeDurationDays;
        Touch(row, updatedBy);
        await _db.SaveChangesAsync();
    }

    /// <summary>Combined runtime rules for Login/ChangePassword/AppPageModel — see <see cref="SecurityRules"/>.</summary>
    public async Task<SecurityRules> GetSecurityRulesAsync()
    {
        var row = await GetOrCreateAsync();
        return new SecurityRules(row.MinimumPasswordLength, row.PasswordComplexityRequired,
            row.MaxLoginAttempts, row.AccountLockoutMinutes, row.PasswordExpiryDays,
            row.RememberMeDurationDays, row.SessionTimeoutMinutes, row.EnableAuditLogging,
            string.IsNullOrWhiteSpace(row.DefaultUserPassword) ? DefaultSeedPassword : row.DefaultUserPassword);
    }

    // ==================================================================== Dashboard
    public async Task<DashboardSettings> GetDashboardSettingsAsync()
    {
        var row = await GetOrCreateAsync();
        return new DashboardSettings(row.WelcomeMessage, row.AnnouncementBanner);
    }

    public async Task SaveDashboardSettingsAsync(DashboardSettings input, string updatedBy)
    {
        var row = await GetOrCreateAsync();
        row.WelcomeMessage = Trimmed(input.WelcomeMessage);
        row.AnnouncementBanner = Trimmed(input.AnnouncementBanner);
        Touch(row, updatedBy);
        await _db.SaveChangesAsync();
    }

    // ==================================================================== Branding
    public async Task<BrandingSettings> GetBrandingSettingsAsync()
    {
        var row = await GetOrCreateAsync();
        return new BrandingSettings(row.CompanyLogoPath, row.PrimaryColorHex, row.SecondaryColorHex, row.FooterText);
    }

    public async Task SaveBrandingSettingsAsync(BrandingSettings input, string updatedBy)
    {
        var row = await GetOrCreateAsync();
        row.PrimaryColorHex = NormalizeHex(input.PrimaryColorHex);
        row.SecondaryColorHex = NormalizeHex(input.SecondaryColorHex);
        row.FooterText = Trimmed(input.FooterText);
        if (input.CompanyLogoPath is not null) row.CompanyLogoPath = input.CompanyLogoPath;
        Touch(row, updatedBy);
        await _db.SaveChangesAsync();
    }

    private static string? Trimmed(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? NormalizeHex(string? hex)
    {
        hex = Trimmed(hex);
        if (hex is null) return null;
        if (!hex.StartsWith('#')) hex = "#" + hex;
        return System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9a-fA-F]{6}$") ? hex : null;
    }
}

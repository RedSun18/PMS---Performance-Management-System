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

/// <summary>
/// Database-backed application settings (singleton row, Id = 1). Started with the
/// Email/SMTP category; on first run with no row present, seeds itself once from
/// IConfiguration (appsettings/User Secrets "Email:*" section) so local dev works
/// without ever visiting the Settings page. After that the DB row is authoritative.
/// </summary>
public class SettingsService
{
    private readonly PmDbContext _db;
    private readonly IConfiguration _config;
    private readonly IDataProtector _protector;

    public SettingsService(PmDbContext db, IConfiguration config, IDataProtectionProvider dataProtection)
    {
        _db = db;
        _config = config;
        _protector = dataProtection.CreateProtector("PerformanceManagement.Settings.Smtp");
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
            ApplicationBaseUrl = _config["General:ApplicationBaseUrl"],
            SmtpHost = section["SmtpHost"],
            SmtpPort = int.TryParse(section["SmtpPort"], out var p) ? p : 587,
            SmtpUsername = section["SmtpUsername"],
            SmtpPasswordProtected = string.IsNullOrEmpty(section["SmtpPassword"])
                ? null : _protector.Protect(section["SmtpPassword"]!),
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

    /// <summary>Public origin used to build absolute links in outgoing email. Falls back to the local dev URL if unset.</summary>
    public async Task<string> GetApplicationBaseUrlAsync()
    {
        var row = await GetOrCreateAsync();
        return string.IsNullOrWhiteSpace(row.ApplicationBaseUrl)
            ? "http://localhost:5273" : row.ApplicationBaseUrl.Trim().TrimEnd('/');
    }

    public async Task SaveApplicationBaseUrlAsync(string? baseUrl, string updatedBy)
    {
        var row = await GetOrCreateAsync();
        row.ApplicationBaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim().TrimEnd('/');
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = updatedBy;
        await _db.SaveChangesAsync();
    }

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
                ? "" : _protector.Unprotect(row.SmtpPasswordProtected);
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
            row.SmtpPasswordProtected = _protector.Protect(input.NewPassword);
        row.SenderName = input.SenderName?.Trim();
        row.SenderEmail = input.SenderEmail?.Trim();
        row.SmtpEnableSsl = input.EnableSsl;
        row.EnableEmailNotifications = input.EnableEmailNotifications;
        row.DevelopmentRedirectEmail = string.IsNullOrWhiteSpace(input.DevelopmentRedirectEmail)
            ? null : input.DevelopmentRedirectEmail.Trim();
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = updatedBy;
        await _db.SaveChangesAsync();
    }
}

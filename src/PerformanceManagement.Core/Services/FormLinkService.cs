using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PerformanceManagement.Core.Services;

/// <summary>Decoded contents of a form deep-link token.</summary>
public record FormLinkPayload(string EmpCode, int EvalYear, string IntendedUserName, DateTime ExpiresAtUtc);

/// <summary>
/// Builds and resolves signed, expiring deep links to a specific employee's PM form
/// (used by outgoing workflow email action buttons) — e.g.
/// https://pms.company.com/OpenForm?token=... instead of exposing empcd/year directly
/// in the URL. The token is opaque and tamper-evident (ASP.NET Core Data Protection);
/// it is not itself an authorization decision — <c>Pages/OpenForm</c> still re-checks
/// the intended recipient against the caller's real permissions before redirecting.
/// </summary>
public class FormLinkService
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromDays(30);

    private readonly IDataProtector _protector;
    private readonly SettingsService _settings;
    private readonly IClock _clock;

    public FormLinkService(IDataProtectionProvider dataProtection, SettingsService settings, IClock clock)
    {
        _protector = dataProtection.CreateProtector("PerformanceManagement.FormLink");
        _settings = settings;
        _clock = clock;
    }

    /// <summary>Builds a full, absolute /OpenForm?token=... URL using the configured Application Base URL.</summary>
    public async Task<string> BuildFormUrlAsync(string empCode, int evalYear, string intendedUserName)
    {
        var baseUrl = await _settings.GetApplicationBaseUrlAsync();
        var token = CreateToken(empCode, evalYear, intendedUserName);
        return $"{baseUrl}/OpenForm?token={token}";
    }

    private string CreateToken(string empCode, int evalYear, string intendedUserName)
    {
        var payload = new FormLinkPayload(empCode.Trim(), evalYear, intendedUserName.Trim(), _clock.Now.Add(DefaultLifetime));
        var json = JsonSerializer.Serialize(payload);
        var protectedBytes = _protector.Protect(Encoding.UTF8.GetBytes(json));
        return Base64UrlEncode(protectedBytes);
    }

    /// <summary>Decodes and validates a token; returns null for anything malformed, tampered, or expired
    /// — deliberately collapsing every failure mode into one outcome so a caller never has to (and
    /// can't accidentally) branch on *why* a link was rejected.</summary>
    public FormLinkPayload? TryDecode(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var protectedBytes = Base64UrlDecode(token);
            var json = Encoding.UTF8.GetString(_protector.Unprotect(protectedBytes));
            var payload = JsonSerializer.Deserialize<FormLinkPayload>(json);
            if (payload is null || string.IsNullOrWhiteSpace(payload.EmpCode)) return null;
            return payload.ExpiresAtUtc < _clock.Now ? null : payload;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] Base64UrlDecode(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }
}

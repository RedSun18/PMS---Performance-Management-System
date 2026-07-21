using System.Globalization;
using System.Resources;

namespace PerformanceManagement.Web.Validation;

/// <summary>Small shared input checks used by more than one page (Users/Edit, Employees/Edit).</summary>
public static class InputValidation
{
    // Plain ResourceManager rather than IStringLocalizer<T> since these are static helper
    // methods called from several pages' code-behind, none of which is a natural "owner" to
    // inject a page-scoped localizer for — same reasoning as PmFormStatus.DisplayName and
    // Core's ValidationResource.
    private static readonly ResourceManager Resources = new(
        "PerformanceManagement.Web.Resources.InputValidationResource", typeof(InputValidation).Assembly);

    private static string Get(string key, params object[] args)
    {
        var format = Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        return args.Length == 0 ? format : string.Format(format, args);
    }

    /// <summary>Floor used only before the Security Rules row is available (e.g. seeding) — everywhere
    /// a password is actually set, prefer <see cref="ValidatePassword"/> with the live admin-configured rule.</summary>
    public const int MinPasswordLength = 6;

    /// <summary>Minimal format check — not a deliverability guarantee, just catches obviously malformed input
    /// before it's saved and silently fails to receive mail later with no feedback to the person who typed it.</summary>
    public static bool IsValidEmail(string email)
    {
        try { return new System.Net.Mail.MailAddress(email).Address == email; }
        catch (FormatException) { return false; }
    }

    /// <summary>Enforced everywhere a password is set — self-service change, admin-created account, and
    /// admin reset — against the administrator-configured Authentication settings, so none of the three
    /// can bypass the currently active rule. Returns null when the password is acceptable.</summary>
    public static string? ValidatePassword(string password, int minLength, bool requireComplexity)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < minLength)
            return Get("PasswordMinLength", minLength);
        if (requireComplexity && !(password.Any(char.IsLetter) && password.Any(char.IsDigit)))
            return Get("PasswordNeedsLetterAndNumber");
        return null;
    }
}

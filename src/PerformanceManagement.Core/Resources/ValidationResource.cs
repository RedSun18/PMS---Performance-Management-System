using System.Globalization;
using System.Resources;

namespace PerformanceManagement.Core.Resources;

/// <summary>
/// Culture-aware lookup for form-validation messages (ValidationResource.resx / .ar.resx),
/// used by the static <see cref="Services.FormValidationService"/>. A plain ResourceManager
/// rather than <c>IStringLocalizer&lt;T&gt;</c> since FormValidationService's methods are static
/// (called from many places across both the Web and Core projects) and Core has no ASP.NET Core
/// localization dependency of its own — this mirrors how PmFormStatus.DisplayName reads
/// CultureInfo.CurrentUICulture directly, one level up from a full DI-based localizer.
/// </summary>
internal static class ValidationResource
{
    private static readonly ResourceManager Manager = new(typeof(ValidationResource));

    public static string Get(string key, params object[] args)
    {
        var format = Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        return args.Length == 0 ? format : string.Format(format, args);
    }
}

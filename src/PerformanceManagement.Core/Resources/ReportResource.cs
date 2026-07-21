using System.Globalization;
using System.Resources;

namespace PerformanceManagement.Core.Resources;

/// <summary>Same pattern as <see cref="EmailResource"/>, for PDF report headings/labels built in
/// ReportExportService — plain ResourceManager lookup, no ASP.NET Core dependency.</summary>
public static class ReportResource
{
    private static readonly ResourceManager Manager = new(typeof(ReportResource));

    public static string Get(string key, CultureInfo? culture = null) =>
        Manager.GetString(key, culture ?? CultureInfo.CurrentUICulture) ?? key;
}

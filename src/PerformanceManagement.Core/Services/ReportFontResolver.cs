using System.Reflection;
using PdfSharp.Fonts;

namespace PerformanceManagement.Core.Services;

/// <summary>
/// PdfSharp/MigraDoc has no system font enumeration on Linux (no GDI), so PDF report
/// generation would otherwise fail wherever the app is actually deployed — this resolver
/// maps every requested font family (Verdana, Segoe UI, etc.) to one bundled, freely
/// redistributable typeface (DejaVu Sans — Bitstream Vera licensed, see
/// Assets/Fonts/LICENSE.txt) so reports render identically regardless of host OS.
/// Registered once via <see cref="Register"/> at application startup.
/// </summary>
public class ReportFontResolver : IFontResolver
{
    private const string FamilyName = "ReportFont";
    private static readonly object RegisterLock = new();
    private static bool _registered;

    public static void Register()
    {
        lock (RegisterLock)
        {
            if (_registered) return;
            GlobalFontSettings.FontResolver = new ReportFontResolver();
            _registered = true;
        }
    }

    public byte[] GetFont(string faceName)
    {
        var resourceName = faceName switch
        {
            "ReportFont#Bold" => "DejaVuSans-Bold.ttf",
            "ReportFont#Italic" => "DejaVuSans-Oblique.ttf",
            "ReportFont#BoldItalic" => "DejaVuSans-BoldOblique.ttf",
            _ => "DejaVuSans.ttf"
        };
        var fullResourceName = $"PerformanceManagement.Core.Assets.Fonts.{resourceName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(fullResourceName)
            ?? throw new InvalidOperationException($"Embedded font resource '{fullResourceName}' not found.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var face = (isBold, isItalic) switch
        {
            (true, true) => "ReportFont#BoldItalic",
            (true, false) => "ReportFont#Bold",
            (false, true) => "ReportFont#Italic",
            _ => "ReportFont#"
        };
        return new FontResolverInfo(face);
    }
}

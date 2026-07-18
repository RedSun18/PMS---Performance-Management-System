namespace PerformanceManagement.Web;

/// <summary>
/// Marker type for shared localized strings (Resources/SharedResource.resx / .ar.resx) —
/// common terms reused across many pages (Save, Cancel, Sign in, …) so each page's own
/// resource file only needs to hold text that's actually specific to it. Never instantiated;
/// exists purely so <c>IStringLocalizer&lt;SharedResource&gt;</c> has a type to bind the
/// resource files to. Deliberately placed at the project root (not inside Resources/) —
/// ASP.NET Core derives the expected resx manifest name from the type's namespace relative
/// to the assembly name, then prepends ResourcesPath; a marker class living inside the
/// Resources folder itself doubles that segment (looks for Resources.Resources.SharedResource).
/// </summary>
public class SharedResource
{
}

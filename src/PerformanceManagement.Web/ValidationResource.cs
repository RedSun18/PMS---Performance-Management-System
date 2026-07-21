namespace PerformanceManagement.Web;

/// <summary>
/// Marker type for validation-message resources (Resources/ValidationResource.resx / .ar.resx) —
/// kept separate from SharedResource so validation wording can be found/reviewed as one group,
/// and separate from per-page resources since the same messages are reused by InputValidation.cs
/// across many pages. Same project-root placement as SharedResource — see its comment for why
/// (a marker class living inside the Resources folder itself doubles the resource path segment
/// ASP.NET Core derives from the type's namespace).
/// </summary>
public class ValidationResource
{
}

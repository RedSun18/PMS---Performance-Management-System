using Aic.Pm.Core.Domain;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Aic.Pm.Web.Pages;

/// <summary>Shared page plumbing: current-user identity claims + nav role flag.</summary>
public abstract class AppPageModel : PageModel
{
    public string CurrentUserName => User.Identity?.Name ?? "";
    public string CurrentEmpCode => User.FindFirst("EmpCode")?.Value?.Trim() ?? "";
    public string CurrentDisplayName => User.FindFirst("DisplayName")?.Value ?? CurrentUserName;
    public bool IsHrAdmin => User.IsInRole(Roles.HrAdmin);

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        ViewData["IsHrAdmin"] = IsHrAdmin;
        await next();
    }
}

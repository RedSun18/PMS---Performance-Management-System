using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace PerformanceManagement.Web.Pages.WorkflowAdmin;

/// <summary>
/// Workflow Administration detail/recovery page — HR Admin only. Shows the appraisal's
/// employee info, current stage/owner (with a visual progress tracker), the full workflow
/// timeline (<c>PmFormStatusHistory</c>) and audit history (<c>AuditLog</c>, filtered to this
/// form), plus the six administrative override actions. Every action requires a typed reason
/// (enforced both by the confirmation dialog's required textarea and, defensively, server-side
/// here) and is confirmed via a native &lt;dialog&gt; before it submits — see the inline script
/// in Details.cshtml. All the actual transition/validation/audit logic lives in
/// <see cref="WorkflowAdminService"/>; this page only wires HTTP verbs to it.
/// </summary>
[Authorize(Roles = Roles.HrAdmin)]
public class DetailsModel : AppPageModel
{
    private readonly WorkflowAdminService _admin;
    private readonly JobFamilyService _jobFamilies;
    private readonly PermissionService _permissions;
    private readonly IStringLocalizer<DetailsModel> _localizer;

    public DetailsModel(WorkflowAdminService admin, JobFamilyService jobFamilies,
        PermissionService permissions, IStringLocalizer<DetailsModel> localizer)
    {
        _admin = admin; _jobFamilies = jobFamilies; _permissions = permissions; _localizer = localizer;
    }

    [BindProperty(SupportsGet = true)] public string Empcd { get; set; } = "";
    [BindProperty(SupportsGet = true)] public int Year { get; set; }

    public WorkflowAdminDetails? Details { get; set; }

    // Proactively disables actions the current status/lock-state can never legally accept,
    // rather than only reporting the same validation as an error toast after the fact — the
    // page model owns this because it mirrors the exact guard each WorkflowService.Admin*
    // method itself enforces (see WorkflowAdminService), never a separate copy of the rule.
    public bool CanReturnToEmployee => Details is { } d && d.Form.Status is not (PmFormStatus.Draft or PmFormStatus.Ready);
    public bool CanReturnToManager => Details is { } d && d.Form.Status is PmFormStatus.SubmittedToHr or PmFormStatus.HrReview1Approved;
    public bool CanReopenReview => Details is { } d && d.Form.Status == PmFormStatus.Approved;
    public bool CanResendNotification => Details is { } d && d.Form.Status is not (PmFormStatus.Draft or PmFormStatus.Ready);
    public bool CanCompleteAdministratively => Details is { } d && d.Form.Status != PmFormStatus.Approved;
    public bool CanUnlock => Details is { } d && d.Form.IsLocked;

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    public async Task<IActionResult> OnGetAsync()
    {
        Details = await _admin.GetDetailsAsync(Empcd, Year);
        if (Details is null)
        {
            TempData["ErrorMessage"] = _localizer["FormNotFoundError"].Value;
            return RedirectToPage("/WorkflowAdmin/Index");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostReturnToEmployeeAsync(string reason)
    {
        var denied = RequireReason(reason);
        if (denied is not null) return denied;
        var result = await _admin.ReturnToEmployeeAsync(CurrentUserName, Empcd, Year, reason, ClientIp);
        return AfterAction(result);
    }

    public async Task<IActionResult> OnPostReturnToManagerAsync(string reason)
    {
        var denied = RequireReason(reason);
        if (denied is not null) return denied;
        var result = await _admin.ReturnToManagerAsync(CurrentUserName, CurrentEmpCode, Empcd, Year, reason, ClientIp);
        return AfterAction(result);
    }

    public async Task<IActionResult> OnPostReopenReviewAsync(string reason)
    {
        var denied = RequireReason(reason);
        if (denied is not null) return denied;
        var result = await _admin.ReopenReviewAsync(CurrentUserName, Empcd, Year, reason, ClientIp);
        return AfterAction(result);
    }

    public async Task<IActionResult> OnPostResendNotificationAsync(string reason)
    {
        var denied = RequireReason(reason);
        if (denied is not null) return denied;
        var result = await _admin.ResendNotificationAsync(CurrentUserName, Empcd, Year, reason, ClientIp);
        return AfterAction(result, successMessage: result.Success ? _localizer["NotificationResentMessage"].Value : null);
    }

    public async Task<IActionResult> OnPostAdministrativeCompletionAsync(string reason)
    {
        var denied = RequireReason(reason);
        if (denied is not null) return denied;

        var current = await _admin.GetDetailsAsync(Empcd, Year);
        if (current is null) return RedirectToPage("/WorkflowAdmin/Index");
        var jobFamily = await _jobFamilies.ResolveAsync(Empcd, current.Form.GradeSnapshot);
        var exempt = await _permissions.HasExceptionAsync(Empcd, ExceptionRule.PerspectiveMinExempt);

        var result = await _admin.AdministrativeCompletionAsync(CurrentUserName, Empcd, Year, reason,
            jobFamily.Configured, exempt, ClientIp);
        return AfterAction(result);
    }

    public async Task<IActionResult> OnPostUnlockAsync(string reason)
    {
        var denied = RequireReason(reason);
        if (denied is not null) return denied;
        var result = await _admin.UnlockAsync(CurrentUserName, Empcd, Year, reason, ClientIp);
        return AfterAction(result);
    }

    /// <summary>Defensive server-side backstop for the dialog's own `required minlength` — a
    /// direct POST (bypassing the UI) must not be able to skip the mandatory reason either.</summary>
    private IActionResult? RequireReason(string? reason)
    {
        if (!string.IsNullOrWhiteSpace(reason) && reason.Trim().Length >= 10) return null;
        TempData["ErrorMessage"] = _localizer["ReasonRequiredError"].Value;
        return RedirectToPage(new { empcd = Empcd, year = Year });
    }

    private IActionResult AfterAction(WorkflowResult result, string? successMessage = null)
    {
        TempData[result.Success ? "Message" : "ErrorMessage"] =
            result.Success ? (successMessage ?? _localizer["ActionSucceededMessage"].Value) : result.ErrorText;
        return RedirectToPage(new { empcd = Empcd, year = Year });
    }
}

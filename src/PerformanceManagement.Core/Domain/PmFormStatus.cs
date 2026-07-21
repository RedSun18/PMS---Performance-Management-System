using System.Globalization;

namespace PerformanceManagement.Core.Domain;

/// <summary>
/// Workflow status codes. These are the exact strings stored by the legacy system
/// (see docs/workflow-state-machine.md). Status is machine state — never derive it
/// from a display label.
/// </summary>
public static class PmFormStatus
{
    public const string Draft = "DRAFT";
    /// <summary>Legacy vestige: accepted on read, never written by the new app.</summary>
    public const string Ready = "READY";
    public const string PendingEmployeeAck = "PENDING_EMPLOYEE_ACK";
    /// <summary>Legacy constant value (not "...ACKNOWLEDGED").</summary>
    public const string EmployeeAcknowledged = "EMPLOYEE_ACKNOWLEDGE";
    public const string SubmittedToHr = "SUBMITTED_TO_HR";
    public const string HrReview1Approved = "HR_REVIEW_1_APPROVED";
    public const string Approved = "APPROVED";

    public static readonly string[] All =
    {
        Draft, Ready, PendingEmployeeAck, EmployeeAcknowledged,
        SubmittedToHr, HrReview1Approved, Approved
    };

    /// <summary>
    /// Single source of truth for the human-readable status label, in whichever language is
    /// requested — used by Razor views, PDF reports, Excel exports, and workflow emails alike,
    /// so all four surfaces always agree on wording instead of each carrying its own copy of the
    /// same translation (and risking drift). Defaults to the ambient request culture (set by the
    /// localization middleware for the page/PDF/Excel path) when none is passed explicitly, which
    /// covers the email path where the recipient's own preferred culture is passed in.
    /// </summary>
    public static string DisplayName(string? status, CultureInfo? culture = null)
    {
        var isArabic = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName == "ar";
        return (status ?? "").Trim() switch
        {
            Ready => isArabic ? "جاهز / لم يبدأ" : "Ready / Not Started",
            Draft => isArabic ? "مسودة" : "Draft",
            PendingEmployeeAck => isArabic ? "بانتظار إقرار الموظف" : "Pending Employee Acknowledgment",
            EmployeeAcknowledged => isArabic ? "تم إقرار الموظف" : "Employee Acknowledged",
            SubmittedToHr => isArabic ? "تم الإرسال إلى الموارد البشرية" : "Submitted to HR",
            HrReview1Approved => isArabic ? "اعتماد المراجعة الأولى" : "HR Review 1 Approved",
            Approved => isArabic ? "معتمد" : "Approved",
            "" => isArabic ? "غير متاح" : "N/A",
            var other => other
        };
    }

    /// <summary>Statuses in which the form content may be edited by the direct manager.</summary>
    public static bool AllowsEdit(string status) =>
        status is Draft or Ready or EmployeeAcknowledged;

    /// <summary>Statuses in which Cancel &amp; Delete is permitted (legacy rule).</summary>
    public static bool AllowsDelete(string status) => status is Draft or Ready;

    /// <summary>form_locked flag as the legacy system maintained it.</summary>
    public static bool IsLockedStatus(string status) =>
        status is PendingEmployeeAck or SubmittedToHr or Approved;
}

public static class ExceptionRule
{
    /// <summary>Employee is exempt from the "at least 3 distinct KPI perspectives" rule.</summary>
    public const string PerspectiveMinExempt = "PERSPECTIVE_MIN_EXEMPT";
    /// <summary>Employee gets a 50/50 KPI/competency split regardless of grade.</summary>
    public const string Kpi5050 = "KPI_50_50";
    /// <summary>Employee may act as their own direct manager (temporary legacy arrangement).</summary>
    public const string SelfManager = "SELF_MANAGER";
    /// <summary>User has view-only access to branch (PRO/BR) employee forms.</summary>
    public const string BranchViewer = "BRANCH_VIEWER";
}

public static class PromotionRecommendation
{
    public const string Yes = "YES";
    public const string Borderline = "BORDERLINE";
    public const string No = "NO";
}

public static class Roles
{
    /// <summary>
    /// PM Form HR administrative access ("Administrator" user type in User Management).
    /// Explicit role membership only — never derived from username pattern or department.
    /// </summary>
    public const string HrAdmin = "HR_ADMIN";

    /// <summary>Read-only access across HR-facing pages ("Viewer" user type). Never grants mutation.</summary>
    public const string Viewer = "VIEWER";

    /// <summary>Roles.HrAdmin and Roles.Viewer together — used to gate read-only pages to either.</summary>
    public const string HrAdminOrViewer = HrAdmin + "," + Viewer;
}

/// <summary>User Management "user type" selector — maps to role assignment, not a stored column.</summary>
public static class UserType
{
    public const string Administrator = "ADMINISTRATOR";
    public const string Employee = "EMPLOYEE";
    public const string Viewer = "VIEWER";

    public static readonly string[] All = { Administrator, Employee, Viewer };
}

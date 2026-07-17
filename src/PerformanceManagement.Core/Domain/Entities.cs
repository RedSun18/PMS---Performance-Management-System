namespace PerformanceManagement.Core.Domain;

/// <summary>
/// PM-relevant subset of the legacy empmaster (see docs/legacy-mapping.md §5).
/// </summary>
public class Employee
{
    public string EmpCode { get; set; } = "";
    public string LatinName { get; set; } = "";
    public string? ArabicName { get; set; }
    public string? DesignationCode { get; set; }
    public string? DeptCode { get; set; }
    public string? SectionCode { get; set; }
    public string? Grade { get; set; }
    public DateOnly? JoinDate { get; set; }
    public DateOnly? TermDate { get; set; }
    public string? Email { get; set; }
    /// <summary>HDR_SNAPSHOT for rows synthesized from pm_form_records; MANUAL for app-entered.</summary>
    public string Source { get; set; } = "MANUAL";

    public bool IsActive => TermDate is null;
}

public class Department
{
    public string Code { get; set; } = "";
    public string NameEn { get; set; } = "";
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    /// <summary>Disabled departments stay visible on existing employee records but cannot be
    /// assigned to new employees or on department change — see Employees/Edit. Departments are
    /// never hard-deleted (employees may already reference them).</summary>
    public bool IsActive { get; set; } = true;
}

public class Designation
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string? DescriptionAr { get; set; }
}

public class Section
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string? DescriptionAr { get; set; }
}

/// <summary>Legacy reference rows: rf_codetype ADM / rf_moduleno KPI / rf_subtype J.</summary>
public class JobFamily
{
    public string Code { get; set; } = "";          // JF001..
    public string NameEn { get; set; } = "";
    public string? NameAr { get; set; }
    /// <summary>Comma-separated grade list from rf_lastsrl, e.g. "6,7,8".</summary>
    public string GradesCsv { get; set; } = "";
    public int KpiWeight { get; set; }              // rf_frac
    public int CompWeight { get; set; }             // rf_toac
    public string Status { get; set; } = "A";

    public IEnumerable<string> Grades =>
        GradesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Legacy reference rows: rf_codetype ADM / rf_moduleno KPI / rf_subtype R.</summary>
public class RatingScale
{
    public string Code { get; set; } = "";          // "1".."6"
    public string NameEn { get; set; } = "";
    public string? NameAr { get; set; }
    public int MinScore { get; set; }               // rf_frac
    public int MaxScore { get; set; }               // rf_toac
    public string? Remarks { get; set; }
    public string Status { get; set; } = "A";
}

public class KpiMaster
{
    public string KpiId { get; set; } = "";         // KPI001..
    public string Name { get; set; } = "";
    public string? NameAr { get; set; }
    public string Perspective { get; set; } = "";   // F / C / I / L  (legacy kpi_type)
    public string? PerspectiveDesc { get; set; }
    public string? PerspectiveDescAr { get; set; }
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public string? Formula { get; set; }
    public string? FormulaAr { get; set; }
    /// <summary>Comma-separated dept codes or "*" for all.</summary>
    public string DeptCsv { get; set; } = "*";
    public string? DeptDesc { get; set; }
    public string? DeptDescAr { get; set; }
    public string? WeightRange { get; set; }
    public int MinWeight { get; set; } = 10;
    public int MaxWeight { get; set; } = 25;
    public string Status { get; set; } = "A";
    public string? Remarks { get; set; }
    public string? CreatedBy { get; set; }
    public DateOnly? CreatedDate { get; set; }
    public string? ModifiedBy { get; set; }
    public DateOnly? ModifiedDate { get; set; }
    public string? ModifiedTime { get; set; }

    public bool AppliesToDept(string deptCode) =>
        DeptCsv.Trim() == "*" ||
        DeptCsv.Split(',', StringSplitOptions.TrimEntries).Contains(deptCode, StringComparer.OrdinalIgnoreCase);
}

public class CompetencyMaster
{
    public string CompId { get; set; } = "";        // COM001..
    public string Name { get; set; } = "";
    public string? NameAr { get; set; }
    public string CompType { get; set; } = "B";     // B behavioural / T technical
    public string? TypeDesc { get; set; }
    public string? TypeDescAr { get; set; }
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public string DeptCsv { get; set; } = "*";
    public string? DeptDesc { get; set; }
    public string? DeptDescAr { get; set; }
    public string? WeightRange { get; set; }
    public int MinWeight { get; set; } = 10;
    public int MaxWeight { get; set; } = 20;
    public string Status { get; set; } = "A";
    public string? Remarks { get; set; }
    public string? CreatedBy { get; set; }
    public DateOnly? CreatedDate { get; set; }
    public string? ModifiedBy { get; set; }
    public DateOnly? ModifiedDate { get; set; }
    public string? ModifiedTime { get; set; }
}

/// <summary>Form-level authoritative record (legacy pm_form_records HDR row).</summary>
public class PmForm
{
    public int Id { get; set; }
    /// <summary>Legacy ref_no, preserved verbatim (may be unpadded historic format).</summary>
    public string LegacyRefNo { get; set; } = "";
    public string EmpCode { get; set; } = "";
    public int EvalYear { get; set; }

    // Snapshots captured at save time (legacy behaviour)
    public string EmpNameSnapshot { get; set; } = "";
    public string? DesignationSnapshot { get; set; }
    public string? DeptCode { get; set; }
    public string? SectionCode { get; set; }
    public string? ManagerEmpCode { get; set; }     // legacy app_by
    public string? GradeSnapshot { get; set; }
    public DateOnly? JoinDateSnapshot { get; set; }
    public DateOnly? LastReviewDate { get; set; }
    public string? JobFamily { get; set; }

    public int KpiWeightTotal { get; set; }
    public int CompWeightTotal { get; set; }
    public decimal KpiScore { get; set; }
    public decimal CompScore { get; set; }
    public decimal PerformanceScore { get; set; }
    public string? OverallRatingCode { get; set; }

    public string Status { get; set; } = PmFormStatus.Draft;
    public string? PreviousStatus { get; set; }
    public DateOnly? StatusChangeDate { get; set; }

    public string? SelfAssessment { get; set; }
    public string? DevelopmentPlan { get; set; }
    public string? EmployeeSign { get; set; }
    public string? ManagerSign { get; set; }

    public string? EmpAckBy { get; set; }
    public DateOnly? EmpAckDate { get; set; }
    public string? EmpAckSign { get; set; }
    public string? EmpAckComments { get; set; }

    public string? Hr1ReviewerName { get; set; }
    public DateOnly? Hr1ReviewDate { get; set; }
    public string? Hr1Sign { get; set; }
    public string? Hr1Remarks { get; set; }

    public string? Hr2ReviewerName { get; set; }
    public DateOnly? Hr2ReviewDate { get; set; }
    public string? Hr2Sign { get; set; }
    public string? Hr2Remarks { get; set; }

    public string? PromotionRecommendationValue { get; set; }  // YES / BORDERLINE / NO
    public string? PromotionComments { get; set; }

    public bool IsLocked { get; set; }
    public bool IsActive { get; set; } = true;
    public DateOnly? LastRemindedDate { get; set; }

    public string? CreatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Optimistic-concurrency token, incremented on every state transition.</summary>
    public int Version { get; set; }

    public List<PmFormKpi> Kpis { get; set; } = new();
    public List<PmFormCompetency> Competencies { get; set; } = new();
    public List<PmFormStatusHistory> History { get; set; } = new();
}

public class PmFormKpi
{
    public int Id { get; set; }
    public int PmFormId { get; set; }
    public PmForm? PmForm { get; set; }
    public int RecordSeq { get; set; }
    public string? LegacyRefNo { get; set; }
    public string Perspective { get; set; } = "";   // F / C / I / L
    public string KpiCode { get; set; } = "";
    public string KpiName { get; set; } = "";
    public string? KpiDefinition { get; set; }
    public string? FormulaMetric { get; set; }
    public string? Target { get; set; }
    public int ItemWeight { get; set; }
    public int AchievementScore { get; set; }
    public decimal WeightedCalculation { get; set; }
    public string? Comments { get; set; }
}

public class PmFormCompetency
{
    public int Id { get; set; }
    public int PmFormId { get; set; }
    public PmForm? PmForm { get; set; }
    public int RecordSeq { get; set; }
    public string? LegacyRefNo { get; set; }
    public string CompType { get; set; } = "B";     // B / T
    public string CompCode { get; set; } = "";
    public string CompName { get; set; } = "";
    /// <summary>Legacy stored the competency description in kpi_definition.</summary>
    public string? Description { get; set; }
    public int ItemWeight { get; set; }
    public int AchievementScore { get; set; }
    public decimal WeightedCalculation { get; set; }
    public string? Comments { get; set; }
}

public class PmFormStatusHistory
{
    public int Id { get; set; }
    public int PmFormId { get; set; }
    public PmForm? PmForm { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = "";
    public string ChangedBy { get; set; } = "";
    public DateTime ChangedAt { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// HR-designated direct manager for PM Form purposes. Some designated managers are
/// not formal org-chart managers (legacy note in KPIForm.aspx.vb).
/// </summary>
public class ManagerAssignment
{
    public string EmpCode { get; set; } = "";
    public string ManagerEmpCode { get; set; } = "";
    public string Source { get; set; } = "HR_LIST";
    public string? Note { get; set; }
}

/// <summary>Data-driven business-rule exceptions (see ExceptionRule).</summary>
public class EmployeeException
{
    public int Id { get; set; }
    public string EmpCode { get; set; } = "";
    public string RuleCode { get; set; } = "";
    public string? Reason { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    public bool IsEffective(DateOnly today) =>
        (EffectiveFrom is null || EffectiveFrom <= today) &&
        (EffectiveTo is null || EffectiveTo >= today);
}

public class AppUser
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? EmpCode { get; set; }
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public bool MustChangePassword { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Reset to 0 on any successful login; incremented on each failed attempt.</summary>
    public int FailedLoginAttempts { get; set; }
    /// <summary>Set when failed attempts reach Security:MaxLoginAttempts; login is blocked until this passes.</summary>
    public DateTime? LockedOutUntil { get; set; }
    /// <summary>Drives Security:PasswordExpiryDays — null (never set) never expires.</summary>
    public DateTime? PasswordChangedAt { get; set; }

    public List<UserRole> RolesList { get; set; } = new();
}

public class UserRole
{
    public int Id { get; set; }
    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }
    public string Role { get; set; } = "";
}

public class EmailLog
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string TemplateKey { get; set; } = "";
    public string? FormLegacyRefNo { get; set; }
    public string ToRecipients { get; set; } = "";
    public string CcRecipients { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>LOGGED (no SMTP configured), DISABLED (notifications off), SENT, FAILED, SKIPPED_NO_RECIPIENT, SKIPPED_DUPLICATE.</summary>
    public string Status { get; set; } = "LOGGED";
    public string? Note { get; set; }
    /// <summary>Idempotency key: templateKey + form + transition version.</summary>
    public string? IdempotencyKey { get; set; }
}

/// <summary>
/// Singleton application configuration (Id = 1), editable via the admin System Settings
/// page. Started with the Email/SMTP category (Phase 7); further categories (General,
/// Performance Review, Authentication, Dashboard) are added as new nullable columns in
/// later migrations rather than a separate table per category.
/// </summary>
public class SystemSettings
{
    public int Id { get; set; } = 1;

    // ---- General --------------------------------------------------------
    public string? CompanyName { get; set; }
    public string? ApplicationName { get; set; }
    /// <summary>Relative path under wwwroot (e.g. "/uploads/branding/logo.png"), set by the Branding tab's upload.</summary>
    public string? CompanyLogoPath { get; set; }
    public string? CompanyAddress { get; set; }
    public string? ContactEmail { get; set; }
    /// <summary>Public origin used to build absolute links in outgoing email, e.g. "https://pms.company.com".</summary>
    public string? ApplicationBaseUrl { get; set; }

    // ---- Performance Review ----------------------------------------------
    /// <summary>Informational default only — workflow gating (AchievementGate etc.) still uses the calendar year.</summary>
    public int? CurrentReviewYear { get; set; }
    public DateOnly? MidYearStart { get; set; }
    public DateOnly? MidYearEnd { get; set; }
    public DateOnly? EndYearStart { get; set; }
    public DateOnly? EndYearEnd { get; set; }

    // ---- Email -------------------------------------------------------------
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpUsername { get; set; }
    /// <summary>Encrypted at rest via ASP.NET Core Data Protection — never stored or shown in plaintext.</summary>
    public string? SmtpPasswordProtected { get; set; }
    public string? SenderName { get; set; }
    public string? SenderEmail { get; set; }
    public bool SmtpEnableSsl { get; set; } = true;
    public bool EnableEmailNotifications { get; set; } = true;
    /// <summary>Safety redirect: every outgoing email is sent here instead of the real recipient. Empty ⇒ send to real recipients.</summary>
    public string? DevelopmentRedirectEmail { get; set; }

    // ---- Authentication ----------------------------------------------------
    public string? DefaultUserPassword { get; set; }
    public bool PasswordComplexityRequired { get; set; }
    public int MinimumPasswordLength { get; set; } = 6;
    public int SessionTimeoutMinutes { get; set; } = 480;
    /// <summary>Encrypted at rest, like the SMTP password. Falls back to a hardcoded dev default when unset.</summary>
    public string? LoginAsVerificationPasswordProtected { get; set; }
    /// <summary>0 = lockout disabled.</summary>
    public int MaxLoginAttempts { get; set; } = 5;

    // ---- Security ------------------------------------------------------------
    public bool EnableAuditLogging { get; set; } = true;
    public int AccountLockoutMinutes { get; set; } = 15;
    /// <summary>0 = password never expires.</summary>
    public int PasswordExpiryDays { get; set; }
    public int RememberMeDurationDays { get; set; } = 30;

    // ---- Dashboard -----------------------------------------------------------
    public string? WelcomeMessage { get; set; }
    public string? AnnouncementBanner { get; set; }

    // ---- Branding ------------------------------------------------------------
    public string? PrimaryColorHex { get; set; }
    public string? SecondaryColorHex { get; set; }
    public string? FooterText { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Audit trail for the admin "Login As" impersonation feature. One row per impersonation
/// session, opened when impersonation starts and closed (EndedAt set) on "Return to
/// Administrator" or logout. Never deleted — this is the accountability record.
/// </summary>
public class ImpersonationLog
{
    public int Id { get; set; }
    public string AdminUserName { get; set; } = "";
    public string AdminDisplayName { get; set; } = "";
    public string ImpersonatedUserName { get; set; } = "";
    public string ImpersonatedDisplayName { get; set; } = "";
    public string? ImpersonatedEmpCode { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? IpAddress { get; set; }
    /// <summary>Correlates the running session's claim back to this row so Return can close it.</summary>
    public Guid SessionId { get; set; }
}

/// <summary>
/// Append-only record of significant administrator/system actions (user management,
/// department changes, impersonation, settings changes, report generation, …) — the source
/// of truth for the Audit Viewer. Writes are gated by SystemSettings.EnableAuditLogging via
/// AuditService, but existing rows are never deleted even if logging is later disabled.
/// </summary>
public class AuditLog
{
    public int Id { get; set; }
    public DateTime OccurredAt { get; set; }
    /// <summary>Short verb phrase, e.g. "User Created", "Department Disabled", "Password Reset".</summary>
    public string Action { get; set; } = "";
    public string PerformedBy { get; set; } = "";
    /// <summary>Employee the action concerns, if any — drives the Employee filter on the Audit Viewer.</summary>
    public string? EmpCode { get; set; }
    /// <summary>Department the action concerns, if any — drives the Department filter on the Audit Viewer.</summary>
    public string? DeptCode { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Details { get; set; }
}

/// <summary>
/// One row per execution of a Quartz scheduled job — Quartz's own in-memory RAMJobStore
/// doesn't retain run history across restarts, so this is the durable record the Job
/// Management page reads for Previous Run/Duration/Status/Result. Written by JobHistoryListener.
/// </summary>
public class ScheduledJobRun
{
    public int Id { get; set; }
    public string JobName { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    /// <summary>RUNNING, SUCCEEDED, FAILED.</summary>
    public string Status { get; set; } = "RUNNING";
    public string? ResultSummary { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>In-app notification for the bell-icon Notification Centre. UserName is the recipient's
/// login username (AppUser.UserName) — every notification is addressed to exactly one account;
/// events that concern several people (e.g. a department update) are fanned out into one row per
/// recipient at creation time rather than modeled as a broadcast.</summary>
public class Notification
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Message { get; set; }
    /// <summary>Short machine type — ReviewAssigned, EmployeeAcknowledged, SubmittedToHr,
    /// HrReview1Approved, Finalized, HrReturned, PasswordReset, UserCreated, DepartmentUpdated,
    /// ReportGenerated. Informational only (icon/grouping), never branched on for authorization.</summary>
    public string Type { get; set; } = "";
    /// <summary>URL to open when the notification is clicked, if any — usually the same signed
    /// deep link used in the corresponding email (absolute), occasionally a relative in-app path.</summary>
    public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

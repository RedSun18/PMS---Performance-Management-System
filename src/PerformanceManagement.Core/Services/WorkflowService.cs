using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace PerformanceManagement.Core.Services;

/// <summary>
/// Database-authoritative state transitions. Every action re-reads the current HDR row
/// inside a transaction, validates the expected source status (stale-page protection),
/// writes status + previous_status + status_change_date + audit fields, appends one
/// status-history row, commits, then dispatches exactly one email.
/// See docs/workflow-state-machine.md.
/// </summary>
public class WorkflowService
{
    private readonly PmDbContext _db;
    private readonly IClock _clock;
    private readonly AchievementGate _gate;
    private readonly PermissionService _permissions;
    private readonly EmailService _email;
    private readonly RatingService _rating;
    private readonly FormLinkService _links;
    private readonly NotificationService _notifications;
    private readonly SettingsService _settings;

    public WorkflowService(PmDbContext db, IClock clock, AchievementGate gate,
        PermissionService permissions, EmailService email, RatingService rating, FormLinkService links,
        NotificationService notifications, SettingsService settings)
    {
        _db = db; _clock = clock; _gate = gate;
        _permissions = permissions; _email = email; _rating = rating; _links = links;
        _notifications = notifications; _settings = settings;
    }

    /// <summary>Email header brand text — the environment's own company name if set
    /// (e.g. a Demo/customer deployment), else the generic product name.</summary>
    private async Task<string> GetBrandNameAsync()
    {
        var general = await _settings.GetGeneralSettingsAsync();
        return string.IsNullOrWhiteSpace(general.CompanyName)
            ? await _settings.GetApplicationNameAsync() : general.CompanyName;
    }

    public async Task<PmForm?> FindFormAsync(string empCode, int evalYear, bool track = false)
    {
        IQueryable<PmForm> q = _db.PmForms
            .Include(f => f.Kpis.OrderBy(k => k.RecordSeq))
            .Include(f => f.Competencies.OrderBy(c => c.RecordSeq));
        if (!track) q = q.AsNoTracking();
        return await q.FirstOrDefaultAsync(f => f.EmpCode == empCode.Trim() && f.EvalYear == evalYear);
    }

    // ---------------------------------------------------------------- T1 / T1b: Save
    /// <summary>
    /// Saves form content. New/DRAFT forms stay DRAFT. Saving while EMPLOYEE_ACKNOWLEDGE
    /// keeps the status (deliberate deviation from a legacy defect — legacy-mapping §6).
    /// </summary>
    public async Task<WorkflowResult> SaveDraftAsync(string actor, FormPermissions perms, PmFormContent content)
    {
        if (!perms.CanActAsManager)
            return WorkflowResult.Fail("Only the direct manager of this employee can save the form.");

        return await ExecuteAsync(content.EmpCode, content.EvalYear, allowCreate: true,
            expectedStatuses: new[] { PmFormStatus.Draft, PmFormStatus.Ready, PmFormStatus.EmployeeAcknowledged },
            action: async form =>
            {
                if (form.IsLocked && form.Status != PmFormStatus.EmployeeAcknowledged)
                    return WorkflowResult.Fail("This form is locked and cannot be edited.");

                await ApplyContentAsync(form, content, actor);

                if (form.Status is PmFormStatus.Draft or PmFormStatus.Ready)
                {
                    TransitionTo(form, PmFormStatus.Draft, actor, "Save Draft");
                    form.IsLocked = false;
                }
                else
                {
                    // EMPLOYEE_ACKNOWLEDGE: content-only save, no status change
                    Audit(form, actor);
                    AddHistory(form, form.Status, form.Status, actor, "Content saved (status unchanged)");
                }
                await Task.CompletedTask;
                return WorkflowResult.Ok();
            });
    }

    // --------------------------------------------------------------- T2: Send to Employee
    public async Task<WorkflowResult> SendToEmployeeAsync(string actor, FormPermissions perms, PmFormContent content)
    {
        if (!perms.CanActAsManager)
            return WorkflowResult.Fail("Only the direct manager of this employee can send the form.");

        var exempt = await _permissions.HasExceptionAsync(content.EmpCode, ExceptionRule.PerspectiveMinExempt);

        return await ExecuteAsync(content.EmpCode, content.EvalYear, allowCreate: true,
            expectedStatuses: new[] { PmFormStatus.Draft, PmFormStatus.Ready },
            duplicateStatusMessage: (s) => s == PmFormStatus.PendingEmployeeAck
                ? "This form has already been sent to the employee." : null,
            action: async form =>
            {
                await ApplyContentAsync(form, content, actor);

                var errors = FormValidationService.ValidateForSendToEmployee(form, exempt);
                if (errors.Count > 0) return WorkflowResult.Fail(errors);

                TransitionTo(form, PmFormStatus.PendingEmployeeAck, actor, "Send to Employee");
                form.IsLocked = true;
                await Task.CompletedTask;
                return WorkflowResult.Ok();
            },
            email: async form =>
            {
                var managerName = await EmployeeNameAsync(form.ManagerEmpCode);
                var recipientUserName = await UserNameForEmpCodeAsync(form.EmpCode);
                var actionUrl = await _links.BuildFormUrlAsync(form.EmpCode, form.EvalYear, recipientUserName);
                var culture = await CultureForEmpCodeAsync(form.EmpCode);
                var brandName = await GetBrandNameAsync();
                var (subject, body) = EmailTemplates.AcknowledgementRequest(form, managerName, actionUrl, _clock.Now, culture, brandName);
                await _email.DispatchAsync(new EmailSpec("ACK_REQUEST",
                    To: await EmailsAsync(form.EmpCode), Cc: await EmailsAsync(form.ManagerEmpCode),
                    subject, body, form.LegacyRefNo, IdemKey(form, "ACK_REQUEST")));
                await _notifications.CreateAsync(recipientUserName, "Performance Objectives Ready for Review",
                    $"{form.EvalYear} review — please review and acknowledge your objectives.", "ReviewAssigned", actionUrl);
            });
    }

    // --------------------------------------------------------------- T3: Acknowledge
    public async Task<WorkflowResult> AcknowledgeAsync(string actor, string actorEmpCode, string empCode, int evalYear, string? comments)
    {
        if ((actorEmpCode ?? "").Trim() != empCode.Trim())
            return WorkflowResult.Fail("You can only acknowledge your own performance objectives.");

        return await ExecuteAsync(empCode, evalYear, allowCreate: false,
            expectedStatuses: new[] { PmFormStatus.PendingEmployeeAck },
            statusMismatchMessage: "This form is not in a state that requires employee acknowledgement.",
            action: async form =>
            {
                TransitionTo(form, PmFormStatus.EmployeeAcknowledged, actor, "Employee acknowledged");
                form.EmpAckBy = actor;
                form.EmpAckDate = _clock.Today;
                form.EmpAckSign = (actorEmpCode ?? "").Trim();
                form.EmpAckComments = comments;
                form.IsLocked = false;
                await Task.CompletedTask;
                return WorkflowResult.Ok();
            },
            email: async form =>
            {
                var managerName = await EmployeeNameAsync(form.ManagerEmpCode);
                var managerUserName = await UserNameForEmpCodeAsync(form.ManagerEmpCode);
                var actionUrl = await _links.BuildFormUrlAsync(form.EmpCode, form.EvalYear, managerUserName);
                var achievementOpenDate = await _gate.AchievementOpenDateAsync(form.EvalYear);
                var culture = await CultureForEmpCodeAsync(form.ManagerEmpCode);
                var brandName = await GetBrandNameAsync();
                var (subject, body) = EmailTemplates.EmployeeAcknowledged(form, managerName, actionUrl, _clock.Now, achievementOpenDate, culture, brandName);
                await _email.DispatchAsync(new EmailSpec("EMP_ACKNOWLEDGED",
                    To: await EmailsAsync(form.ManagerEmpCode), Cc: await EmailsAsync(form.EmpCode),
                    subject, body, form.LegacyRefNo, IdemKey(form, "EMP_ACKNOWLEDGED")));
                await _notifications.CreateAsync(managerUserName, "Employee Acknowledged Objectives",
                    $"{form.EmpNameSnapshot} acknowledged their {form.EvalYear} objectives.", "EmployeeAcknowledged", actionUrl);
            });
    }

    // --------------------------------------------------------------- T4: Submit to HR
    public async Task<WorkflowResult> SubmitToHrAsync(string actor, FormPermissions perms, PmFormContent content, bool jobFamilyConfigured)
    {
        if (!perms.CanActAsManager)
            return WorkflowResult.Fail("Only the direct manager of this employee can submit to HR.");
        if (!await _gate.IsSubmitToHrOpenAsync(content.EvalYear))
        {
            var opensOn = await _gate.SubmitToHrOpenDateAsync(content.EvalYear);
            return WorkflowResult.Fail($"Submit to HR is available from {opensOn:dd/MM/yyyy} after achievement scores are entered.");
        }

        var exempt = await _permissions.HasExceptionAsync(content.EmpCode, ExceptionRule.PerspectiveMinExempt);

        return await ExecuteAsync(content.EmpCode, content.EvalYear, allowCreate: false,
            expectedStatuses: new[] { PmFormStatus.EmployeeAcknowledged },
            action: async form =>
            {
                await ApplyContentAsync(form, content, actor);

                var errors = FormValidationService.ValidateForSubmitToHr(form, jobFamilyConfigured, exempt);
                if (errors.Count > 0) return WorkflowResult.Fail(errors);

                ScoringService.Recalculate(form);
                var rating = await _rating.GetRatingAsync((int)Math.Round(form.PerformanceScore, MidpointRounding.AwayFromZero));
                form.OverallRatingCode = rating?.Code;

                TransitionTo(form, PmFormStatus.SubmittedToHr, actor, "Submitted to HR");
                form.IsLocked = true;
                return WorkflowResult.Ok();
            },
            email: async form =>
            {
                var managerName = await EmployeeNameAsync(form.ManagerEmpCode);
                var rating = await _rating.GetRatingAsync((int)Math.Round(form.PerformanceScore, MidpointRounding.AwayFromZero));
                // Role-based recipient (any current HR admin) — no single intended username to bind to.
                var actionUrl = await _links.BuildFormUrlAsync(form.EmpCode, form.EvalYear, "");
                var brandName = await GetBrandNameAsync();
                var (subject, body) = EmailTemplates.SubmittedToHr(form, managerName, RatingService.RatingName(rating), actionUrl, _clock.Now, appName: brandName);
                await _email.DispatchAsync(new EmailSpec("SUBMIT_TO_HR",
                    To: await HrAdminEmailsAsync(), Cc: await EmailsAsync(form.ManagerEmpCode),
                    subject, body, form.LegacyRefNo, IdemKey(form, "SUBMIT_TO_HR")));
                await NotifyAllAsync(await HrAdminUserNamesAsync(), "PM Form Ready for HR Review",
                    $"{form.EmpNameSnapshot} ({form.EvalYear}) is ready for first HR review.", "SubmittedToHr", actionUrl);
            });
    }

    // --------------------------------------------------------------- T5: HR Review 1
    public async Task<WorkflowResult> HrApprove1Async(string actor, FormPermissions perms, string empCode, int evalYear,
        string reviewerName, string reviewerSign, string? remarks)
    {
        if (!perms.CanActAsHr)
            return WorkflowResult.Fail("Only an approved HR administrator can perform HR review.");
        if (string.IsNullOrWhiteSpace(reviewerName))
            return WorkflowResult.Fail("Please enter HR Reviewer 1 name.");

        return await ExecuteAsync(empCode, evalYear, allowCreate: false,
            expectedStatuses: new[] { PmFormStatus.SubmittedToHr },
            action: async form =>
            {
                TransitionTo(form, PmFormStatus.HrReview1Approved, actor, "HR review 1 approved");
                form.Hr1ReviewerName = reviewerName.Trim();
                form.Hr1ReviewDate = _clock.Today;
                form.Hr1Sign = reviewerSign.Trim();
                form.Hr1Remarks = remarks;
                await Task.CompletedTask;
                return WorkflowResult.Ok();
            },
            email: async form =>
            {
                var actionUrl = await _links.BuildFormUrlAsync(form.EmpCode, form.EvalYear, "");
                var brandName = await GetBrandNameAsync();
                var (subject, body) = EmailTemplates.Hr1Approved(form, actionUrl, _clock.Now, appName: brandName);
                await _email.DispatchAsync(new EmailSpec("HR1_APPROVED",
                    To: await HrAdminEmailsAsync(exceptEmpCode: form.Hr1Sign), Cc: Array.Empty<string>(),
                    subject, body, form.LegacyRefNo, IdemKey(form, "HR1_APPROVED")));
                await NotifyAllAsync(await HrAdminUserNamesAsync(exceptEmpCode: form.Hr1Sign), "Final HR Review Required",
                    $"{form.EmpNameSnapshot} ({form.EvalYear}) needs final HR review and approval.", "HrReview1Approved", actionUrl);
            });
    }

    // --------------------------------------------------------------- T6: HR Final Approval
    public async Task<WorkflowResult> HrFinalApproveAsync(string actor, FormPermissions perms, string actorEmpCode,
        string empCode, int evalYear, string reviewerName, string reviewerSign, string? remarks)
    {
        if (!perms.CanActAsHr)
            return WorkflowResult.Fail("Only an approved HR administrator can perform HR review.");
        if (string.IsNullOrWhiteSpace(reviewerName))
            return WorkflowResult.Fail("Please enter HR Reviewer 2 name (Final Reviewer).");

        return await ExecuteAsync(empCode, evalYear, allowCreate: false,
            expectedStatuses: new[] { PmFormStatus.HrReview1Approved },
            action: async form =>
            {
                // Segregation of duties: the second reviewer must differ from the first
                var hr1 = (form.Hr1Sign ?? "").Trim();
                if (hr1.Length > 0 && hr1.Equals(actorEmpCode.Trim(), StringComparison.OrdinalIgnoreCase))
                    return WorkflowResult.Fail("You cannot perform the second HR review because you were the first HR reviewer. Another HR representative must complete the final review.");

                TransitionTo(form, PmFormStatus.Approved, actor, "Final HR approval");
                form.Hr2ReviewerName = reviewerName.Trim();
                form.Hr2ReviewDate = _clock.Today;
                form.Hr2Sign = reviewerSign.Trim();
                form.Hr2Remarks = remarks;
                form.IsLocked = true;

                var rating = await _rating.GetRatingAsync((int)Math.Round(form.PerformanceScore, MidpointRounding.AwayFromZero));
                form.OverallRatingCode = rating?.Code;
                return WorkflowResult.Ok();
            },
            email: async form =>
            {
                var rating = await _rating.GetRatingAsync((int)Math.Round(form.PerformanceScore, MidpointRounding.AwayFromZero));
                var empUserName = await UserNameForEmpCodeAsync(form.EmpCode);
                var actionUrl = await _links.BuildFormUrlAsync(form.EmpCode, form.EvalYear, empUserName);
                var culture = await CultureForEmpCodeAsync(form.EmpCode);
                var brandName = await GetBrandNameAsync();
                var (subject, body) = EmailTemplates.FinalApproved(form, RatingService.RatingName(rating), actionUrl, _clock.Now, culture, brandName);
                await _email.DispatchAsync(new EmailSpec("FINAL_APPROVED",
                    To: await EmailsAsync(form.EmpCode), Cc: await EmailsAsync(form.ManagerEmpCode),
                    subject, body, form.LegacyRefNo, IdemKey(form, "FINAL_APPROVED")));

                var title = $"Performance Review Finalized ({form.EvalYear})";
                var message = $"{form.EmpNameSnapshot}'s {form.EvalYear} performance review is finalized.";
                await _notifications.CreateAsync(empUserName, title, message, "Finalized", actionUrl);
                await _notifications.CreateAsync(await UserNameForEmpCodeAsync(form.ManagerEmpCode), title, message, "Finalized", actionUrl);
            });
    }

    // --------------------------------------------------------------- T7: HR Revert
    public async Task<WorkflowResult> HrRevertAsync(string actor, FormPermissions perms, string empCode, int evalYear, string? hrComments)
    {
        if (!perms.CanActAsHr)
            return WorkflowResult.Fail("Only an approved HR administrator can revert a form.");

        return await ExecuteAsync(empCode, evalYear, allowCreate: false,
            expectedStatuses: new[] { PmFormStatus.SubmittedToHr, PmFormStatus.HrReview1Approved },
            action: async form =>
            {
                TransitionTo(form, PmFormStatus.EmployeeAcknowledged, actor, "HR reverted to manager");
                form.IsLocked = false;
                await Task.CompletedTask;
                return WorkflowResult.Ok();
            },
            email: async form =>
            {
                var managerName = await EmployeeNameAsync(form.ManagerEmpCode);
                var managerUserName = await UserNameForEmpCodeAsync(form.ManagerEmpCode);
                var actionUrl = await _links.BuildFormUrlAsync(form.EmpCode, form.EvalYear, managerUserName);
                var culture = await CultureForEmpCodeAsync(form.ManagerEmpCode);
                var brandName = await GetBrandNameAsync();
                var (subject, body) = EmailTemplates.Reverted(form, managerName, hrComments ?? form.Hr1Remarks ?? "", actionUrl, _clock.Now, culture, brandName);
                await _email.DispatchAsync(new EmailSpec("HR_REVERTED",
                    To: await EmailsAsync(form.ManagerEmpCode), Cc: await HrAdminEmailsAsync(),
                    subject, body, form.LegacyRefNo, IdemKey(form, "HR_REVERTED")));
                await _notifications.CreateAsync(managerUserName, "Performance Form Requires Revision",
                    $"HR returned {form.EmpNameSnapshot}'s {form.EvalYear} review for revision.", "HrReturned", actionUrl);
            });
    }

    // --------------------------------------------------------------- T8: Cancel & Delete
    public async Task<WorkflowResult> CancelDeleteAsync(string actor, FormPermissions perms, string empCode, int evalYear)
    {
        if (!perms.CanActAsManager)
            return WorkflowResult.Fail("Only the direct manager of this employee can delete a draft form.");

        await using var tx = await _db.Database.BeginTransactionAsync();
        var form = await _db.PmForms.Include(f => f.Kpis).Include(f => f.Competencies)
            .FirstOrDefaultAsync(f => f.EmpCode == empCode.Trim() && f.EvalYear == evalYear);
        if (form is null)
            return WorkflowResult.Fail("No evaluation records found for this employee and year.");
        // Re-read status inside the transaction (legacy stale-state fix)
        if (!PmFormStatus.AllowsDelete(form.Status))
            return WorkflowResult.Fail($"Cannot delete a submitted or approved form. Current status: {PmFormStatus.DisplayName(form.Status)}.");

        _db.PmForms.Remove(form);
        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return WorkflowResult.Fail("This form was changed by another user. Please retry.");
        }
        return WorkflowResult.Ok();
    }

    // --------------------------------------------------------------- T9: Admin — Return to Employee
    /// <summary>
    /// Workflow Administration override (see <c>WorkflowAdminService</c>): forces the form back to
    /// PENDING_EMPLOYEE_ACK so the employee must acknowledge again, regardless of how far past that
    /// stage it has gone. Not reachable through the normal employee/manager/HR flows — only via the
    /// HR-Admin-only Workflow Administration console, which supplies its own reason/audit trail on
    /// top of this transition.
    /// </summary>
    public async Task<WorkflowResult> AdminReturnToEmployeeAsync(string actor, string empCode, int evalYear, string reason)
    {
        return await ExecuteAsync(empCode, evalYear, allowCreate: false,
            expectedStatuses: new[] { PmFormStatus.PendingEmployeeAck, PmFormStatus.EmployeeAcknowledged,
                PmFormStatus.SubmittedToHr, PmFormStatus.HrReview1Approved, PmFormStatus.Approved },
            statusMismatchMessage: "Cannot return to the employee — this form has not yet been sent for acknowledgement.",
            action: async form =>
            {
                TransitionTo(form, PmFormStatus.PendingEmployeeAck, actor, $"Admin: returned to employee — {reason}");
                form.EmpAckBy = null;
                form.EmpAckDate = null;
                form.EmpAckSign = null;
                form.EmpAckComments = null;
                form.IsLocked = true;
                await Task.CompletedTask;
                return WorkflowResult.Ok();
            },
            email: async form =>
            {
                var managerName = await EmployeeNameAsync(form.ManagerEmpCode);
                var recipientUserName = await UserNameForEmpCodeAsync(form.EmpCode);
                var actionUrl = await _links.BuildFormUrlAsync(form.EmpCode, form.EvalYear, recipientUserName);
                var culture = await CultureForEmpCodeAsync(form.EmpCode);
                var brandName = await GetBrandNameAsync();
                var (subject, body) = EmailTemplates.AcknowledgementRequest(form, managerName, actionUrl, _clock.Now, culture, brandName);
                await _email.DispatchAsync(new EmailSpec("ACK_REQUEST",
                    To: await EmailsAsync(form.EmpCode), Cc: await EmailsAsync(form.ManagerEmpCode),
                    subject, body, form.LegacyRefNo, IdempotencyKey: null));
                await _notifications.CreateAsync(recipientUserName, "Performance Objectives Ready for Review",
                    $"{form.EvalYear} review — please review and acknowledge your objectives.", "ReviewAssigned", actionUrl);
            });
    }

    // --------------------------------------------------------------- T10: Admin — Reopen Review
    /// <summary>
    /// Workflow Administration override: restores a completed (APPROVED) review to the manager's
    /// editable stage. Clears both HR reviewers' sign-off fields since they applied to a form that
    /// is now being reopened for changes — a stale HR signature next to a since-modified form would
    /// misrepresent who actually reviewed the final content.
    /// </summary>
    public async Task<WorkflowResult> AdminReopenReviewAsync(string actor, string empCode, int evalYear, string reason)
    {
        return await ExecuteAsync(empCode, evalYear, allowCreate: false,
            expectedStatuses: new[] { PmFormStatus.Approved },
            statusMismatchMessage: "Only a completed review can be reopened.",
            action: async form =>
            {
                TransitionTo(form, PmFormStatus.EmployeeAcknowledged, actor, $"Admin: reopened review — {reason}");
                form.IsLocked = false;
                form.Hr1ReviewerName = null; form.Hr1ReviewDate = null; form.Hr1Sign = null; form.Hr1Remarks = null;
                form.Hr2ReviewerName = null; form.Hr2ReviewDate = null; form.Hr2Sign = null; form.Hr2Remarks = null;
                await Task.CompletedTask;
                return WorkflowResult.Ok();
            },
            email: async form =>
            {
                var managerUserName = await UserNameForEmpCodeAsync(form.ManagerEmpCode);
                var actionUrl = await _links.BuildFormUrlAsync(form.EmpCode, form.EvalYear, managerUserName);
                await _notifications.CreateAsync(managerUserName, "Performance Review Reopened",
                    $"HR reopened {form.EmpNameSnapshot}'s {form.EvalYear} review for further editing.", "AdminReopened", actionUrl);
            });
    }

    // --------------------------------------------------------------- T11: Admin — Resend Notification
    /// <summary>
    /// Workflow Administration override: re-dispatches whichever workflow email corresponds to the
    /// form's CURRENT status, reusing the exact same <see cref="EmailTemplates"/> call and recipient
    /// resolution the matching transition already uses — no new templates, no status change. Passes
    /// a null idempotency key deliberately: the normal key (<see cref="IdemKey"/>) is scoped to the
    /// form's current Version, so a same-version resend would otherwise collide with the original
    /// send and get silently marked SKIPPED_DUPLICATE by <see cref="EmailService"/>'s dedup guard —
    /// exactly wrong for an intentional resend.
    /// </summary>
    public async Task<WorkflowResult> AdminResendNotificationAsync(string actor, string empCode, int evalYear)
    {
        var form = await FindFormAsync(empCode, evalYear);
        if (form is null) return WorkflowResult.Fail("No PM form exists for this employee and year.");

        var brandName = await GetBrandNameAsync();
        EmailLog log;
        switch (form.Status)
        {
            case PmFormStatus.PendingEmployeeAck:
            {
                var managerName = await EmployeeNameAsync(form.ManagerEmpCode);
                var recipientUserName = await UserNameForEmpCodeAsync(form.EmpCode);
                var actionUrl = await _links.BuildFormUrlAsync(form.EmpCode, form.EvalYear, recipientUserName);
                var culture = await CultureForEmpCodeAsync(form.EmpCode);
                var (subject, body) = EmailTemplates.AcknowledgementRequest(form, managerName, actionUrl, _clock.Now, culture, brandName);
                log = await _email.DispatchAsync(new EmailSpec("ACK_REQUEST",
                    To: await EmailsAsync(form.EmpCode), Cc: await EmailsAsync(form.ManagerEmpCode),
                    subject, body, form.LegacyRefNo, IdempotencyKey: null));
                break;
            }
            case PmFormStatus.EmployeeAcknowledged:
            {
                var managerName = await EmployeeNameAsync(form.ManagerEmpCode);
                var managerUserName = await UserNameForEmpCodeAsync(form.ManagerEmpCode);
                var actionUrl = await _links.BuildFormUrlAsync(form.EmpCode, form.EvalYear, managerUserName);
                var achievementOpenDate = await _gate.AchievementOpenDateAsync(form.EvalYear);
                var culture = await CultureForEmpCodeAsync(form.ManagerEmpCode);
                var (subject, body) = EmailTemplates.EmployeeAcknowledged(form, managerName, actionUrl, _clock.Now, achievementOpenDate, culture, brandName);
                log = await _email.DispatchAsync(new EmailSpec("EMP_ACKNOWLEDGED",
                    To: await EmailsAsync(form.ManagerEmpCode), Cc: await EmailsAsync(form.EmpCode),
                    subject, body, form.LegacyRefNo, IdempotencyKey: null));
                break;
            }
            case PmFormStatus.SubmittedToHr:
            {
                var managerName = await EmployeeNameAsync(form.ManagerEmpCode);
                var rating = await _rating.GetRatingAsync((int)Math.Round(form.PerformanceScore, MidpointRounding.AwayFromZero));
                var actionUrl = await _links.BuildFormUrlAsync(form.EmpCode, form.EvalYear, "");
                var (subject, body) = EmailTemplates.SubmittedToHr(form, managerName, RatingService.RatingName(rating), actionUrl, _clock.Now, appName: brandName);
                log = await _email.DispatchAsync(new EmailSpec("SUBMIT_TO_HR",
                    To: await HrAdminEmailsAsync(), Cc: await EmailsAsync(form.ManagerEmpCode),
                    subject, body, form.LegacyRefNo, IdempotencyKey: null));
                break;
            }
            case PmFormStatus.HrReview1Approved:
            {
                var actionUrl = await _links.BuildFormUrlAsync(form.EmpCode, form.EvalYear, "");
                var (subject, body) = EmailTemplates.Hr1Approved(form, actionUrl, _clock.Now, appName: brandName);
                log = await _email.DispatchAsync(new EmailSpec("HR1_APPROVED",
                    To: await HrAdminEmailsAsync(exceptEmpCode: form.Hr1Sign), Cc: Array.Empty<string>(),
                    subject, body, form.LegacyRefNo, IdempotencyKey: null));
                break;
            }
            case PmFormStatus.Approved:
            {
                var rating = await _rating.GetRatingAsync((int)Math.Round(form.PerformanceScore, MidpointRounding.AwayFromZero));
                var empUserName = await UserNameForEmpCodeAsync(form.EmpCode);
                var actionUrl = await _links.BuildFormUrlAsync(form.EmpCode, form.EvalYear, empUserName);
                var culture = await CultureForEmpCodeAsync(form.EmpCode);
                var (subject, body) = EmailTemplates.FinalApproved(form, RatingService.RatingName(rating), actionUrl, _clock.Now, culture, brandName);
                log = await _email.DispatchAsync(new EmailSpec("FINAL_APPROVED",
                    To: await EmailsAsync(form.EmpCode), Cc: await EmailsAsync(form.ManagerEmpCode),
                    subject, body, form.LegacyRefNo, IdempotencyKey: null));
                break;
            }
            default:
                return WorkflowResult.Fail("No notification exists for the current stage.");
        }

        return log.Status == "FAILED"
            ? WorkflowResult.Fail($"The notification could not be sent (status: {log.Status}).")
            : WorkflowResult.Ok();
    }

    // --------------------------------------------------------------- T12: Admin — Administrative Completion
    /// <summary>
    /// Workflow Administration override, surfaced in the UI as "Administrative Completion" (internal
    /// name is more literal — this really does force-finalize a stuck workflow). Bypasses the normal
    /// HR_REVIEW_1_APPROVED → APPROVED sequence requirement, but still runs the same completeness
    /// rules <see cref="SubmitToHrAsync"/> enforces: "force" only means skipping the status-sequence
    /// gate, never skipping data-quality validation.
    /// </summary>
    public async Task<WorkflowResult> AdminForceFinalizeAsync(string actor, string empCode, int evalYear,
        string reason, bool jobFamilyConfigured, bool perspectiveExempt)
    {
        return await ExecuteAsync(empCode, evalYear, allowCreate: false,
            expectedStatuses: PmFormStatus.All.Where(s => s != PmFormStatus.Approved).ToArray(),
            statusMismatchMessage: "This review is already completed.",
            action: async form =>
            {
                var errors = FormValidationService.ValidateForSubmitToHr(form, jobFamilyConfigured, perspectiveExempt);
                if (errors.Count > 0) return WorkflowResult.Fail(errors);

                ScoringService.Recalculate(form);
                var rating = await _rating.GetRatingAsync((int)Math.Round(form.PerformanceScore, MidpointRounding.AwayFromZero));
                form.OverallRatingCode = rating?.Code;

                TransitionTo(form, PmFormStatus.Approved, actor, $"Admin: administrative completion — {reason}");
                form.IsLocked = true;
                return WorkflowResult.Ok();
            },
            email: async form =>
            {
                var rating = await _rating.GetRatingAsync((int)Math.Round(form.PerformanceScore, MidpointRounding.AwayFromZero));
                var empUserName = await UserNameForEmpCodeAsync(form.EmpCode);
                var actionUrl = await _links.BuildFormUrlAsync(form.EmpCode, form.EvalYear, empUserName);
                var culture = await CultureForEmpCodeAsync(form.EmpCode);
                var brandName = await GetBrandNameAsync();
                var (subject, body) = EmailTemplates.FinalApproved(form, RatingService.RatingName(rating), actionUrl, _clock.Now, culture, brandName);
                await _email.DispatchAsync(new EmailSpec("FINAL_APPROVED",
                    To: await EmailsAsync(form.EmpCode), Cc: await EmailsAsync(form.ManagerEmpCode),
                    subject, body, form.LegacyRefNo, IdempotencyKey: null));

                var title = $"Performance Review Finalized ({form.EvalYear})";
                var message = $"{form.EmpNameSnapshot}'s {form.EvalYear} performance review is finalized.";
                await _notifications.CreateAsync(empUserName, title, message, "Finalized", actionUrl);
                await _notifications.CreateAsync(await UserNameForEmpCodeAsync(form.ManagerEmpCode), title, message, "Finalized", actionUrl);
            });
    }

    // --------------------------------------------------------------- T13: Admin — Unlock Review
    /// <summary>
    /// Workflow Administration override: unlocks the form for editing without rewinding its stage —
    /// for when HR just needs the manager to fix one field, not move the whole workflow backward.
    /// Records a same-status history row, the identical idiom <see cref="SaveDraftAsync"/> already
    /// uses for EMPLOYEE_ACKNOWLEDGE-stage content-only saves. No email: nothing about the stage
    /// changed, so none of the existing stage-transition templates apply.
    /// </summary>
    public async Task<WorkflowResult> AdminUnlockAsync(string actor, string empCode, int evalYear, string reason)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var form = await _db.PmForms.FirstOrDefaultAsync(f => f.EmpCode == empCode.Trim() && f.EvalYear == evalYear);
        if (form is null) return WorkflowResult.Fail("No PM form exists for this employee and year.");
        if (!form.IsLocked) return WorkflowResult.Fail("This form is already unlocked.");

        form.IsLocked = false;
        Audit(form, actor);
        AddHistory(form, form.Status, form.Status, actor, $"Admin: unlocked — {reason}");
        form.Version++;

        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return WorkflowResult.Fail("This form was changed by another user. Please retry.");
        }
        return WorkflowResult.Ok();
    }

    // ================================================================ internals

    /// <summary>Content payload from the PM Form page.</summary>
    public record PmFormContent(
        string EmpCode, int EvalYear,
        string EmpName, string? DesignationCode, string? DeptCode, string? SectionCode,
        string? ManagerEmpCode, string? Grade, DateOnly? JoinDate, string? JobFamily,
        int KpiWeightTotal, int CompWeightTotal,
        string? SelfAssessment, string? DevelopmentPlan,
        string? EmployeeSign, string? ManagerSign,
        string? PromotionRecommendation, string? PromotionComments,
        IReadOnlyList<PmFormKpi> Kpis, IReadOnlyList<PmFormCompetency> Competencies);

    private async Task ApplyContentAsync(PmForm form, PmFormContent c, string actor)
    {
        // Computed once per save rather than per KPI/competency row — same result either way
        // since it only depends on form.EvalYear, not the individual item.
        var achievementOpen = await _gate.IsAchievementOpenAsync(form.EvalYear);

        form.EmpNameSnapshot = c.EmpName;
        form.DesignationSnapshot = c.DesignationCode;
        form.DeptCode = c.DeptCode;
        form.SectionCode = c.SectionCode;
        form.ManagerEmpCode = c.ManagerEmpCode;
        form.GradeSnapshot = c.Grade;
        form.JoinDateSnapshot = c.JoinDate;
        form.JobFamily = c.JobFamily;
        form.KpiWeightTotal = c.KpiWeightTotal;
        form.CompWeightTotal = c.CompWeightTotal;
        form.SelfAssessment = c.SelfAssessment;
        form.DevelopmentPlan = c.DevelopmentPlan;
        form.EmployeeSign = c.EmployeeSign;
        form.ManagerSign = c.ManagerSign;
        form.PromotionRecommendationValue = c.PromotionRecommendation;
        form.PromotionComments = c.PromotionComments;

        // Replace-set semantics (legacy deleted + reinserted KPI/COMP rows on every save)
        form.Kpis.Clear();
        var seq = 0;
        foreach (var k in c.Kpis.OrderBy(k => k.RecordSeq))
        {
            seq++;
            form.Kpis.Add(new PmFormKpi
            {
                RecordSeq = seq,
                LegacyRefNo = RefNoGenerator.For(form.EmpCode, form.EvalYear, "KPI", seq),
                Perspective = k.Perspective,
                KpiCode = k.KpiCode,
                KpiName = k.KpiName,
                KpiDefinition = k.KpiDefinition,
                FormulaMetric = k.FormulaMetric,
                Target = k.Target,
                ItemWeight = k.ItemWeight,
                AchievementScore = NormalizeAchievement(achievementOpen, k.AchievementScore),
                Comments = k.Comments
            });
        }

        form.Competencies.Clear();
        seq = 0;
        foreach (var cp in c.Competencies.OrderBy(x => x.RecordSeq))
        {
            seq++;
            form.Competencies.Add(new PmFormCompetency
            {
                RecordSeq = seq,
                LegacyRefNo = RefNoGenerator.For(form.EmpCode, form.EvalYear, "COMP", seq),
                CompType = cp.CompType,
                CompCode = cp.CompCode,
                CompName = cp.CompName,
                Description = cp.Description,
                ItemWeight = cp.ItemWeight,
                AchievementScore = NormalizeAchievement(achievementOpen, cp.AchievementScore),
                Comments = cp.Comments
            });
        }

        ScoringService.Recalculate(form);
        Audit(form, actor);
    }

    private static int NormalizeAchievement(bool achievementOpen, int? value)
    {
        if (!achievementOpen || value is null) return 0;
        return Math.Clamp(value.Value, 0, 100);
    }

    private async Task<WorkflowResult> ExecuteAsync(
        string empCode, int evalYear, bool allowCreate,
        string[] expectedStatuses,
        Func<PmForm, Task<WorkflowResult>> action,
        Func<PmForm, Task>? email = null,
        Func<string, string?>? duplicateStatusMessage = null,
        string? statusMismatchMessage = null)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();

        var form = await _db.PmForms
            .Include(f => f.Kpis).Include(f => f.Competencies)
            .FirstOrDefaultAsync(f => f.EmpCode == empCode.Trim() && f.EvalYear == evalYear);

        if (form is null)
        {
            if (!allowCreate)
                return WorkflowResult.Fail("No PM form exists for this employee and year.");
            form = new PmForm
            {
                EmpCode = empCode.Trim(),
                EvalYear = evalYear,
                // Existing forms keep their stored ref_no; only new records get padded numbers
                LegacyRefNo = RefNoGenerator.Header(empCode, evalYear),
                Status = PmFormStatus.Draft,
                CreatedAt = _clock.Now
            };
            _db.PmForms.Add(form);
        }
        else
        {
            var dup = duplicateStatusMessage?.Invoke(form.Status);
            if (dup is not null) return WorkflowResult.Fail(dup);

            if (!expectedStatuses.Contains(form.Status))
                return WorkflowResult.Fail(statusMismatchMessage ??
                    $"This action is not valid for the form's current status ({PmFormStatus.DisplayName(form.Status)}). The page may be stale — it has been refreshed.");
        }

        var result = await action(form);
        if (!result.Success) return result;

        form.Version++;

        try
        {
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return WorkflowResult.Fail("This form was changed by another user. The page has been refreshed with the current state; please review and retry.");
        }

        // Exactly one email, after commit (a mail failure never rolls back the transition)
        if (email is not null)
        {
            try { await email(form); }
            catch { /* logged via EmailLog FAILED by EmailService callers; never break the workflow */ }
        }

        return WorkflowResult.Ok();
    }

    private void TransitionTo(PmForm form, string newStatus, string actor, string note)
    {
        var from = form.Status;
        if (from != newStatus)
        {
            form.PreviousStatus = from;
            form.StatusChangeDate = _clock.Today;
        }
        form.Status = newStatus;
        Audit(form, actor);
        AddHistory(form, from, newStatus, actor, note);
    }

    private void Audit(PmForm form, string actor)
    {
        if (string.IsNullOrEmpty(form.CreatedBy)) form.CreatedBy = actor;
        form.CreatedAt ??= _clock.Now;
        form.UpdatedBy = actor;
        form.UpdatedAt = _clock.Now;
        form.IsActive = true;
    }

    private void AddHistory(PmForm form, string? from, string to, string actor, string note) =>
        form.History.Add(new PmFormStatusHistory
        {
            FromStatus = from,
            ToStatus = to,
            ChangedBy = actor,
            ChangedAt = _clock.Now,
            Note = note
        });

    private string IdemKey(PmForm form, string template) =>
        $"{template}:{form.LegacyRefNo}:{form.Version}";

    private async Task<string> EmployeeNameAsync(string? empCode)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return "";
        var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmpCode == empCode.Trim());
        return e?.LatinName ?? empCode;
    }

    /// <summary>Login username of the account linked to an employee code, for binding a form deep-link
    /// to its intended recipient. Empty when no account exists — the link then relies solely on the
    /// caller's normal permissions (see OpenFormModel) rather than a specific recipient match.</summary>
    private async Task<string> UserNameForEmpCodeAsync(string? empCode)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return "";
        var u = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.EmpCode == empCode.Trim() && x.IsActive);
        return u?.UserName ?? "";
    }

    /// <summary>The recipient's own saved language preference, for rendering a workflow email in
    /// the recipient's chosen language rather than whatever culture happens to be ambient on the
    /// request thread that triggered the transition. Falls back to English (null → CurrentUICulture,
    /// which is English outside a request) when there's no account or no preference saved yet.</summary>
    private async Task<CultureInfo?> CultureForEmpCodeAsync(string? empCode)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return null;
        var u = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.EmpCode == empCode.Trim() && x.IsActive);
        return string.IsNullOrWhiteSpace(u?.PreferredCulture) ? null : new CultureInfo(u!.PreferredCulture!);
    }

    private async Task<IReadOnlyList<string>> EmailsAsync(string? empCode)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return Array.Empty<string>();
        var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmpCode == empCode.Trim());
        return string.IsNullOrWhiteSpace(e?.Email) ? Array.Empty<string>() : new[] { e!.Email! };
    }

    private async Task<IReadOnlyList<string>> HrAdminEmailsAsync(string? exceptEmpCode = null)
    {
        var admins = await _db.UserRoles.AsNoTracking()
            .Where(r => r.Role == Roles.HrAdmin)
            .Include(r => r.AppUser)
            .Select(r => r.AppUser!)
            .ToListAsync();
        return admins
            .Where(u => u.IsActive &&
                        (exceptEmpCode is null || (u.EmpCode ?? "") != exceptEmpCode.Trim()) &&
                        !string.IsNullOrWhiteSpace(u.Email))
            .Select(u => u.Email!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Login usernames of every active HR admin — for fanning an in-app Notification out
    /// to each of them individually (a notification has exactly one recipient row per person).</summary>
    private async Task<IReadOnlyList<string>> HrAdminUserNamesAsync(string? exceptEmpCode = null) =>
        await _db.UserRoles.AsNoTracking().Where(r => r.Role == Roles.HrAdmin).Include(r => r.AppUser)
            .Select(r => r.AppUser!)
            .Where(u => u.IsActive && (exceptEmpCode == null || (u.EmpCode ?? "") != exceptEmpCode))
            .Select(u => u.UserName).Distinct().ToListAsync();

    private async Task NotifyAllAsync(IEnumerable<string> userNames, string title, string? message, string type, string? link)
    {
        foreach (var userName in userNames)
            await _notifications.CreateAsync(userName, title, message, type, link);
    }
}

using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using Microsoft.EntityFrameworkCore;

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

    public WorkflowService(PmDbContext db, IClock clock, AchievementGate gate,
        PermissionService permissions, EmailService email, RatingService rating)
    {
        _db = db; _clock = clock; _gate = gate;
        _permissions = permissions; _email = email; _rating = rating;
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

                ApplyContent(form, content, actor);

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
                ApplyContent(form, content, actor);

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
                var (subject, body) = EmailTemplates.AcknowledgementRequest(form, managerName, _clock.Now);
                await _email.DispatchAsync(new EmailSpec("ACK_REQUEST",
                    To: await EmailsAsync(form.EmpCode), Cc: await EmailsAsync(form.ManagerEmpCode),
                    subject, body, form.LegacyRefNo, IdemKey(form, "ACK_REQUEST")));
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
                form.EmpAckSign = actorEmpCode.Trim();
                form.EmpAckComments = comments;
                form.IsLocked = false;
                await Task.CompletedTask;
                return WorkflowResult.Ok();
            },
            email: async form =>
            {
                var managerName = await EmployeeNameAsync(form.ManagerEmpCode);
                var (subject, body) = EmailTemplates.EmployeeAcknowledged(form, managerName, _clock.Now);
                await _email.DispatchAsync(new EmailSpec("EMP_ACKNOWLEDGED",
                    To: await EmailsAsync(form.ManagerEmpCode), Cc: await EmailsAsync(form.EmpCode),
                    subject, body, form.LegacyRefNo, IdemKey(form, "EMP_ACKNOWLEDGED")));
            });
    }

    // --------------------------------------------------------------- T4: Submit to HR
    public async Task<WorkflowResult> SubmitToHrAsync(string actor, FormPermissions perms, PmFormContent content, bool jobFamilyConfigured)
    {
        if (!perms.CanActAsManager)
            return WorkflowResult.Fail("Only the direct manager of this employee can submit to HR.");
        if (!_gate.IsOpen(content.EvalYear))
            return WorkflowResult.Fail($"Submit to HR is available from 01/12/{content.EvalYear} after achievement scores are entered.");

        var exempt = await _permissions.HasExceptionAsync(content.EmpCode, ExceptionRule.PerspectiveMinExempt);

        return await ExecuteAsync(content.EmpCode, content.EvalYear, allowCreate: false,
            expectedStatuses: new[] { PmFormStatus.EmployeeAcknowledged },
            action: async form =>
            {
                ApplyContent(form, content, actor);

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
                var (subject, body) = EmailTemplates.SubmittedToHr(form, managerName, RatingService.RatingName(rating), _clock.Now);
                await _email.DispatchAsync(new EmailSpec("SUBMIT_TO_HR",
                    To: await HrAdminEmailsAsync(), Cc: await EmailsAsync(form.ManagerEmpCode),
                    subject, body, form.LegacyRefNo, IdemKey(form, "SUBMIT_TO_HR")));
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
                var (subject, body) = EmailTemplates.Hr1Approved(form, _clock.Now);
                await _email.DispatchAsync(new EmailSpec("HR1_APPROVED",
                    To: await HrAdminEmailsAsync(exceptEmpCode: form.Hr1Sign), Cc: Array.Empty<string>(),
                    subject, body, form.LegacyRefNo, IdemKey(form, "HR1_APPROVED")));
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
                var (subject, body) = EmailTemplates.FinalApproved(form, RatingService.RatingName(rating), _clock.Now);
                await _email.DispatchAsync(new EmailSpec("FINAL_APPROVED",
                    To: await EmailsAsync(form.EmpCode), Cc: await EmailsAsync(form.ManagerEmpCode),
                    subject, body, form.LegacyRefNo, IdemKey(form, "FINAL_APPROVED")));
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
                var (subject, body) = EmailTemplates.Reverted(form, hrComments ?? form.Hr1Remarks ?? "", _clock.Now);
                await _email.DispatchAsync(new EmailSpec("HR_REVERTED",
                    To: await EmailsAsync(form.ManagerEmpCode), Cc: await HrAdminEmailsAsync(),
                    subject, body, form.LegacyRefNo, IdemKey(form, "HR_REVERTED")));
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
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
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

    private void ApplyContent(PmForm form, PmFormContent c, string actor)
    {
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
                AchievementScore = _gate.NormalizeAchievement(form.EvalYear, k.AchievementScore),
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
                AchievementScore = _gate.NormalizeAchievement(form.EvalYear, cp.AchievementScore),
                Comments = cp.Comments
            });
        }

        ScoringService.Recalculate(form);
        Audit(form, actor);
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
}

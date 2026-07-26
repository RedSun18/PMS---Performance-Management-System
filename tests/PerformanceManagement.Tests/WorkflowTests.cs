using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace PerformanceManagement.Tests;

/// <summary>Acceptance tests §A (state machine) and §E (email dispatch).</summary>
public class WorkflowTests : IAsyncLifetime
{
    private readonly TestHost _h = new();
    private FormPermissions _mgr = null!;   // 854 acting on 1504
    private int Year => _h.Clock.Today.Year;

    public async Task InitializeAsync()
    {
        await _h.SeedAsync();
        _mgr = await _h.PermsAsync("854", "1504");
    }

    public Task DisposeAsync() { _h.Dispose(); return Task.CompletedTask; }

    private async Task<PerformanceManagement.Core.Domain.PmForm> FormAsync() =>
        (await _h.Workflow.FindFormAsync("1504", Year))!;

    // ---- A.1 -----------------------------------------------------------------
    [Fact]
    public async Task SaveDraft_creates_draft_with_history_and_audit()
    {
        var r = await _h.Workflow.SaveDraftAsync("u854", _mgr, _h.Content1504());
        Assert.True(r.Success, r.ErrorText);

        var f = await FormAsync();
        Assert.Equal(PmFormStatus.Draft, f.Status);
        Assert.Equal("PM20261504HDR01", f.LegacyRefNo);
        Assert.Equal("u854", f.UpdatedBy);
        Assert.Equal(4, f.Kpis.Count);
        Assert.Equal(3, f.Competencies.Count);
        var history = await _h.Db.PmFormStatusHistory.Where(x => x.PmFormId == f.Id).ToListAsync();
        Assert.Single(history);
        Assert.Equal(PmFormStatus.Draft, history[0].ToStatus);
    }

    [Fact]
    public async Task SaveDraft_by_non_manager_or_on_own_form_is_rejected()
    {
        var stranger = await _h.PermsAsync("548", "1504");   // not 1504's manager
        Assert.False((await _h.Workflow.SaveDraftAsync("u548", stranger, _h.Content1504())).Success);

        var self = await _h.PermsAsync("1504", "1504");      // own form
        Assert.False(self.CanActAsManager);
        Assert.False((await _h.Workflow.SaveDraftAsync("u1504", self, _h.Content1504())).Success);
    }

    // ---- A.2 / A.3 / E ---------------------------------------------------------
    [Fact]
    public async Task SendToEmployee_transitions_locks_and_emails_once()
    {
        var r = await _h.Workflow.SendToEmployeeAsync("u854", _mgr, _h.Content1504());
        Assert.True(r.Success, r.ErrorText);

        var f = await FormAsync();
        Assert.Equal(PmFormStatus.PendingEmployeeAck, f.Status);
        Assert.Equal(PmFormStatus.Draft, f.PreviousStatus);
        Assert.Equal(_h.Clock.Today, f.StatusChangeDate);
        Assert.True(f.IsLocked);

        var mails = await _h.Db.EmailLogs.ToListAsync();
        var sent = Assert.Single(mails);
        Assert.Equal("ACK_REQUEST", sent.TemplateKey);
        // No DevelopmentRedirectEmail configured ⇒ mail goes to the real intended recipients.
        Assert.Equal("1504@test.local", sent.ToRecipients);
        Assert.Equal("854@test.local", sent.CcRecipients);
        Assert.Contains("SMTP is not configured", sent.Note);

        // A.3 duplicate send (stale second browser)
        var dup = await _h.Workflow.SendToEmployeeAsync("u854", _mgr, _h.Content1504());
        Assert.False(dup.Success);
        Assert.Contains("already been sent", dup.ErrorText);
        Assert.Equal(1, await _h.Db.EmailLogs.CountAsync());   // no second email
    }

    [Fact]
    public async Task SendToEmployee_requires_complete_weights()
    {
        var content = _h.Content1504() with
        {
            Competencies = new List<PmFormCompetency>
            {
                new() { RecordSeq = 1, CompType = "B", CompCode = "COM001", CompName = "Analytical Thinking", ItemWeight = 50 }
            }
        };
        var r = await _h.Workflow.SendToEmployeeAsync("u854", _mgr, content);
        Assert.False(r.Success);
        Assert.Contains("Competency weights must total 100%", r.ErrorText);
    }

    // ---- A.4 / A.5 / A.6 ----------------------------------------------------------
    [Fact]
    public async Task Acknowledge_rules()
    {
        await _h.Workflow.SendToEmployeeAsync("u854", _mgr, _h.Content1504());

        // A.5 someone else cannot acknowledge
        var wrong = await _h.Workflow.AcknowledgeAsync("u854", "854", "1504", Year, null);
        Assert.False(wrong.Success);

        // A.4 the employee can
        var ok = await _h.Workflow.AcknowledgeAsync("u1504", "1504", "1504", Year, "Looks good");
        Assert.True(ok.Success, ok.ErrorText);

        var f = await FormAsync();
        Assert.Equal(PmFormStatus.EmployeeAcknowledged, f.Status);
        Assert.Equal(PmFormStatus.PendingEmployeeAck, f.PreviousStatus);
        Assert.Equal("1504", f.EmpAckSign);
        Assert.Equal("Looks good", f.EmpAckComments);
        Assert.False(f.IsLocked);
        Assert.Contains(await _h.Db.EmailLogs.ToListAsync(), m => m.TemplateKey == "EMP_ACKNOWLEDGED");

        // A.6 stale page: DB status is no longer PENDING_EMPLOYEE_ACK
        var stale = await _h.Workflow.AcknowledgeAsync("u1504", "1504", "1504", Year, null);
        Assert.False(stale.Success);
        Assert.Contains("not in a state", stale.ErrorText);
    }

    // ---- A.13 -----------------------------------------------------------------
    [Fact]
    public async Task Save_during_employee_acknowledge_keeps_status()
    {
        await _h.Workflow.SendToEmployeeAsync("u854", _mgr, _h.Content1504());
        await _h.Workflow.AcknowledgeAsync("u1504", "1504", "1504", Year, null);

        _h.Clock.Today = new DateOnly(Year, 12, 2);   // gate open so achievements persist
        var r = await _h.Workflow.SaveDraftAsync("u854", _mgr, _h.Content1504(achievement: 90));
        Assert.True(r.Success, r.ErrorText);

        var f = await FormAsync();
        Assert.Equal(PmFormStatus.EmployeeAcknowledged, f.Status);   // deliberate deviation, legacy-mapping §6
        Assert.All(f.Kpis, k => Assert.Equal(90, k.AchievementScore));
    }

    // ---- A.7 / A.8 ---------------------------------------------------------------
    [Fact]
    public async Task SubmitToHr_gated_by_december_first_and_achievements()
    {
        await _h.Workflow.SendToEmployeeAsync("u854", _mgr, _h.Content1504());
        await _h.Workflow.AcknowledgeAsync("u1504", "1504", "1504", Year, null);

        // Before 1 Dec of the evaluation year
        _h.Clock.Today = new DateOnly(Year, 11, 30);
        var early = await _h.Workflow.SubmitToHrAsync("u854", _mgr, _h.Content1504(achievement: 90), true);
        Assert.False(early.Success);
        Assert.Contains("01/12", early.ErrorText);

        // After 1 Dec but missing achievements
        _h.Clock.Today = new DateOnly(Year, 12, 1);
        var missing = await _h.Workflow.SubmitToHrAsync("u854", _mgr, _h.Content1504(achievement: 0), true);
        Assert.False(missing.Success);
        Assert.Contains("achievement scores missing", missing.ErrorText);

        // Complete
        var ok = await _h.Workflow.SubmitToHrAsync("u854", _mgr, _h.Content1504(achievement: 90), true);
        Assert.True(ok.Success, ok.ErrorText);

        var f = await FormAsync();
        Assert.Equal(PmFormStatus.SubmittedToHr, f.Status);
        Assert.True(f.IsLocked);
        // Scores recomputed server-side: Σweighted KPI = 27+27+18+18 = 90 → ×60% = 54;
        // COMP = 36+27+27 = 90 → ×40% = 36; overall 90 → rating "Exceed Expectations" (code 4)
        Assert.Equal(54.00m, f.KpiScore);
        Assert.Equal(36.00m, f.CompScore);
        Assert.Equal(90.00m, f.PerformanceScore);
        Assert.Equal("4", f.OverallRatingCode);
    }

    // ---- A.9 / A.10 / A.11 ----------------------------------------------------------
    private async Task DriveToSubmittedAsync()
    {
        await _h.Workflow.SendToEmployeeAsync("u854", _mgr, _h.Content1504());
        await _h.Workflow.AcknowledgeAsync("u1504", "1504", "1504", Year, null);
        _h.Clock.Today = new DateOnly(Year, 12, 1);
        var r = await _h.Workflow.SubmitToHrAsync("u854", _mgr, _h.Content1504(achievement: 90), true);
        Assert.True(r.Success, r.ErrorText);
    }

    [Fact]
    public async Task Hr_two_stage_review_with_segregation_of_duties()
    {
        await DriveToSubmittedAsync();
        await _h.AddHrAdminAsync("hr1");
        await _h.AddHrAdminAsync("hr2");

        var hr1 = await _h.PermsAsync("hr1", "1504", userName: "hr1");
        Assert.True(hr1.IsHrAdmin);
        var r1 = await _h.Workflow.HrApprove1Async("hr1", hr1, "1504", Year, "HR Reviewer 1", "hr1", "ok");
        Assert.True(r1.Success, r1.ErrorText);

        var f = await FormAsync();
        Assert.Equal(PmFormStatus.HrReview1Approved, f.Status);
        Assert.Equal("hr1", f.Hr1Sign);

        // A.9 double-click guard: action no longer valid for current status
        var again = await _h.Workflow.HrApprove1Async("hr1", hr1, "1504", Year, "x", "hr1", null);
        Assert.False(again.Success);

        // A.10 same admin cannot do the final review
        var same = await _h.Workflow.HrFinalApproveAsync("hr1", hr1, "hr1", "1504", Year, "x", "hr1", null);
        Assert.False(same.Success);
        Assert.Contains("you were the first HR reviewer", same.ErrorText);

        // A different HR admin can
        var hr2 = await _h.PermsAsync("hr2", "1504", userName: "hr2");
        var r2 = await _h.Workflow.HrFinalApproveAsync("hr2", hr2, "hr2", "1504", Year, "HR Reviewer 2", "hr2", "final");
        Assert.True(r2.Success, r2.ErrorText);

        f = await FormAsync();
        Assert.Equal(PmFormStatus.Approved, f.Status);
        Assert.True(f.IsLocked);
        Assert.Equal("hr2", f.Hr2Sign);
    }

    [Fact]
    public async Task Hr_revert_returns_to_employee_acknowledge()
    {
        await DriveToSubmittedAsync();
        await _h.AddHrAdminAsync("hr1");
        var hr1 = await _h.PermsAsync("hr1", "1504", userName: "hr1");
        var r = await _h.Workflow.HrRevertAsync("hr1", hr1, "1504", Year, "please revise");
        Assert.True(r.Success, r.ErrorText);

        var f = await FormAsync();
        Assert.Equal(PmFormStatus.EmployeeAcknowledged, f.Status);
        Assert.False(f.IsLocked);
        Assert.Contains(await _h.Db.EmailLogs.ToListAsync(), m => m.TemplateKey == "HR_REVERTED");
    }

    [Fact]
    public async Task Non_admin_cannot_use_hr_actions()
    {
        await DriveToSubmittedAsync();
        var notHr = await _h.PermsAsync("854", "1504");
        Assert.False((await _h.Workflow.HrApprove1Async("u854", notHr, "1504", Year, "x", "854", null)).Success);
    }

    // ---- A.12 ---------------------------------------------------------------------
    [Fact]
    public async Task CancelDelete_only_in_draft()
    {
        await _h.Workflow.SaveDraftAsync("u854", _mgr, _h.Content1504());
        var ok = await _h.Workflow.CancelDeleteAsync("u854", _mgr, "1504", Year);
        Assert.True(ok.Success, ok.ErrorText);
        Assert.Null(await _h.Workflow.FindFormAsync("1504", Year));

        await _h.Workflow.SendToEmployeeAsync("u854", _mgr, _h.Content1504());
        var locked = await _h.Workflow.CancelDeleteAsync("u854", _mgr, "1504", Year);
        Assert.False(locked.Success);
        Assert.Contains("Cannot delete", locked.ErrorText);
    }

    // ---- A.14 history on every transition -------------------------------------------
    [Fact]
    public async Task Every_transition_appends_history()
    {
        await DriveToSubmittedAsync();
        var f = await FormAsync();
        var history = await _h.Db.PmFormStatusHistory.Where(x => x.PmFormId == f.Id)
            .OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(new[] { PmFormStatus.PendingEmployeeAck, PmFormStatus.EmployeeAcknowledged, PmFormStatus.SubmittedToHr },
            history.Select(x => x.ToStatus).ToArray());
        Assert.Equal(new[] { PmFormStatus.Draft, PmFormStatus.PendingEmployeeAck, PmFormStatus.EmployeeAcknowledged },
            history.Select(x => x.FromStatus).ToArray());
    }

    // ---- E.1 / E.2 email dispatch --------------------------------------------------
    [Fact]
    public async Task Email_dedupes_intended_recipients_and_sends_to_them_when_no_redirect_is_configured()
    {
        var dedup = await _h.Email.DispatchAsync(new EmailSpec("T", new[] { "a@x", "A@X", "b@x" },
            new[] { "a@x", "c@x" }, "s", "b", null, null));
        // No DevelopmentRedirectEmail configured (TestHost's Settings has no SMTP host at all,
        // so GetSmtpCredentialsAsync returns null) ⇒ mail addresses the real deduped recipients.
        Assert.Equal("a@x;b@x", dedup.ToRecipients);
        Assert.Equal("c@x", dedup.CcRecipients);
        Assert.Equal("LOGGED", dedup.Status);
        Assert.Contains("SMTP is not configured", dedup.Note);

        var empty = await _h.Email.DispatchAsync(new EmailSpec("T", Array.Empty<string>(),
            Array.Empty<string>(), "s", "b", null, null));
        Assert.Equal("SKIPPED_NO_RECIPIENT", empty.Status);
        Assert.Equal("", empty.ToRecipients);
    }

    [Fact]
    public async Task Email_redirects_to_the_configured_dev_address_and_never_the_real_recipients()
    {
        // Opt-in safety guardrail: only once an admin explicitly sets DevelopmentRedirectEmail
        // (e.g. UAT against real imported employee data) does dispatch stop addressing real
        // recipients — EnableEmailNotifications=false keeps this test from attempting a real
        // SMTP connection while still exercising the redirect/log logic.
        await _h.Settings.SaveEmailSettingsAsync(new EmailSettingsInput(
            "smtp.test.local", 587, "user", "pw", "PMS", "pms@test.local", true, false, "safe@test.local"), "admin");

        var dedup = await _h.Email.DispatchAsync(new EmailSpec("T", new[] { "a@x", "A@X", "b@x" },
            new[] { "a@x", "c@x" }, "s", "b", null, null));
        Assert.Equal("safe@test.local", dedup.ToRecipients);
        Assert.Equal("", dedup.CcRecipients);
        Assert.Equal("DISABLED", dedup.Status);
        Assert.Contains("a@x;b@x", dedup.Note);   // deduped intended To preserved for traceability
        Assert.Contains("c@x", dedup.Note);       // deduped intended Cc preserved for traceability
    }

    [Fact]
    public void Email_headings_render_on_an_explicit_background_colour()
    {
        // Light-mode Outlook rule: heading text colour must never depend on an inherited/
        // transparent background — the white heading text sits on an explicit navy
        // background-color (with a gradient fallback), so it is always legible.
        var f = new PerformanceManagement.Core.Domain.PmForm { EmpNameSnapshot = "X", LegacyRefNo = "PM20260907HDR01", EvalYear = 2026 };
        var (_, body) = EmailTemplates.AcknowledgementRequest(f, "Mgr", "https://pms.example.com/OpenForm?token=abc", new DateTime(2026, 9, 7, 10, 0, 0));
        Assert.Contains("background-color:#0f2b5c", body);
        Assert.Contains("color:#fff", body);
    }

    // ==================================================================================
    // Workflow Administration (Phase 13)
    // ==================================================================================

    private async Task<AuditLog> SingleAuditRowAsync() =>
        Assert.Single(await _h.Db.AuditLogs.Where(a => a.EmpCode == "1504").ToListAsync());

    [Fact]
    public async Task Admin_return_to_employee_resets_ack_and_relocks_form()
    {
        await DriveToSubmittedAsync();
        await _h.AddHrAdminAsync("hr1");

        var r = await _h.WorkflowAdmin.ReturnToEmployeeAsync("hr1", "1504", Year, "manager resigned mid-cycle", "10.0.0.1");
        Assert.True(r.Success, r.ErrorText);

        var f = await FormAsync();
        Assert.Equal(PmFormStatus.PendingEmployeeAck, f.Status);
        Assert.True(f.IsLocked);
        Assert.Null(f.EmpAckBy);
        Assert.Null(f.EmpAckDate);
        Assert.Null(f.EmpAckSign);
        Assert.Null(f.EmpAckComments);

        var history = await _h.Db.PmFormStatusHistory.Where(x => x.PmFormId == f.Id).OrderByDescending(x => x.Id).FirstAsync();
        Assert.Equal(PmFormStatus.PendingEmployeeAck, history.ToStatus);

        var audit = await SingleAuditRowAsync();
        Assert.Equal("Workflow Administration: Return to Employee", audit.Action);
        Assert.Equal("hr1", audit.PerformedBy);
        Assert.Equal("PmForm", audit.EntityType);
        Assert.Equal(f.Id.ToString(), audit.EntityId);
        Assert.Contains("manager resigned mid-cycle", audit.Details);
        Assert.Contains("IP: 10.0.0.1", audit.Details);
    }

    [Fact]
    public async Task Admin_return_to_employee_rejected_before_form_ever_sent()
    {
        await _h.Workflow.SaveDraftAsync("u854", _mgr, _h.Content1504());
        await _h.AddHrAdminAsync("hr1");

        var r = await _h.WorkflowAdmin.ReturnToEmployeeAsync("hr1", "1504", Year, "not reachable yet", null);
        Assert.False(r.Success);
        Assert.Equal(PmFormStatus.Draft, (await FormAsync()).Status);
        Assert.Empty(await _h.Db.AuditLogs.Where(a => a.EmpCode == "1504").ToListAsync());
    }

    [Fact]
    public async Task Admin_return_to_manager_delegates_to_hr_revert_and_logs_audit()
    {
        await DriveToSubmittedAsync();
        await _h.AddHrAdminAsync("hr1");

        var r = await _h.WorkflowAdmin.ReturnToManagerAsync("hr1", "hr1", "1504", Year, "wrong ratings entered", null);
        Assert.True(r.Success, r.ErrorText);

        var f = await FormAsync();
        Assert.Equal(PmFormStatus.EmployeeAcknowledged, f.Status);
        Assert.False(f.IsLocked);
        Assert.Contains(await _h.Db.EmailLogs.ToListAsync(), m => m.TemplateKey == "HR_REVERTED");

        var audit = await SingleAuditRowAsync();
        Assert.Equal("Workflow Administration: Return to Manager", audit.Action);
    }

    [Fact]
    public async Task Admin_reopen_review_only_valid_from_approved_and_clears_hr_signatures()
    {
        await DriveToSubmittedAsync();
        await _h.AddHrAdminAsync("hr1");
        await _h.AddHrAdminAsync("hr2");
        var hr1 = await _h.PermsAsync("hr1", "1504", userName: "hr1");
        var hr2 = await _h.PermsAsync("hr2", "1504", userName: "hr2");
        await _h.Workflow.HrApprove1Async("hr1", hr1, "1504", Year, "HR Reviewer 1", "hr1", "ok");
        await _h.Workflow.HrFinalApproveAsync("hr2", hr2, "hr2", "1504", Year, "HR Reviewer 2", "hr2", "final");
        Assert.Equal(PmFormStatus.Approved, (await FormAsync()).Status);

        var reopen = await _h.WorkflowAdmin.ReopenReviewAsync("hr1", "1504", Year, "employee disputes final rating", null);
        Assert.True(reopen.Success, reopen.ErrorText);

        var f = await FormAsync();
        Assert.Equal(PmFormStatus.EmployeeAcknowledged, f.Status);
        Assert.False(f.IsLocked);
        Assert.Null(f.Hr1ReviewerName);
        Assert.Null(f.Hr1ReviewDate);
        Assert.Null(f.Hr1Sign);
        Assert.Null(f.Hr1Remarks);
        Assert.Null(f.Hr2ReviewerName);
        Assert.Null(f.Hr2ReviewDate);
        Assert.Null(f.Hr2Sign);
        Assert.Null(f.Hr2Remarks);

        var audit = await SingleAuditRowAsync();
        Assert.Equal("Workflow Administration: Reopen Review", audit.Action);

        // The form is no longer Approved (it's EmployeeAcknowledged again) — reopening again must be rejected.
        var again = await _h.WorkflowAdmin.ReopenReviewAsync("hr1", "1504", Year, "cannot reopen an already open workflow", null);
        Assert.False(again.Success);
    }

    [Fact]
    public async Task Admin_reopen_review_rejected_when_not_yet_completed()
    {
        await DriveToSubmittedAsync();
        await _h.AddHrAdminAsync("hr1");

        var r = await _h.WorkflowAdmin.ReopenReviewAsync("hr1", "1504", Year, "trying to reopen before completion", null);
        Assert.False(r.Success);
        Assert.Equal(PmFormStatus.SubmittedToHr, (await FormAsync()).Status);
        Assert.Empty(await _h.Db.AuditLogs.Where(a => a.EmpCode == "1504").ToListAsync());
    }

    [Fact]
    public async Task Admin_resend_notification_dispatches_matching_template_per_status_bypassing_dedup()
    {
        await _h.Workflow.SendToEmployeeAsync("u854", _mgr, _h.Content1504());
        await _h.AddHrAdminAsync("hr1");

        var before = await _h.Db.EmailLogs.CountAsync();
        var r = await _h.WorkflowAdmin.ResendNotificationAsync("hr1", "1504", Year, "employee says email never arrived", null);
        Assert.True(r.Success, r.ErrorText);

        var logs = await _h.Db.EmailLogs.Where(m => m.TemplateKey == "ACK_REQUEST").ToListAsync();
        Assert.Equal(2, logs.Count);   // original SendToEmployee dispatch + the resend
        Assert.DoesNotContain(logs, m => m.Status == "SKIPPED_DUPLICATE");

        var audit = await SingleAuditRowAsync();
        Assert.Equal("Workflow Administration: Resend Notification", audit.Action);
    }

    [Fact]
    public async Task Admin_resend_notification_fails_gracefully_when_no_template_exists_for_stage()
    {
        await _h.Workflow.SaveDraftAsync("u854", _mgr, _h.Content1504());
        await _h.AddHrAdminAsync("hr1");

        var before = await _h.Db.EmailLogs.CountAsync();
        var r = await _h.WorkflowAdmin.ResendNotificationAsync("hr1", "1504", Year, "checking on draft", null);
        Assert.False(r.Success);
        Assert.Contains("No notification exists for the current stage", r.ErrorText);
        Assert.Equal(before, await _h.Db.EmailLogs.CountAsync());
        Assert.Empty(await _h.Db.AuditLogs.Where(a => a.EmpCode == "1504").ToListAsync());
    }

    [Fact]
    public async Task Admin_administrative_completion_succeeds_from_multiple_statuses_and_rejects_when_already_approved()
    {
        await _h.Workflow.SendToEmployeeAsync("u854", _mgr, _h.Content1504());
        await _h.Workflow.AcknowledgeAsync("u1504", "1504", "1504", Year, null);
        _h.Clock.Today = new DateOnly(Year, 12, 1);   // opens the achievement-entry window
        var save = await _h.Workflow.SaveDraftAsync("u854", _mgr, _h.Content1504(achievement: 90));
        Assert.True(save.Success, save.ErrorText);
        Assert.Equal(PmFormStatus.EmployeeAcknowledged, (await FormAsync()).Status);   // content-only save, no transition
        await _h.AddHrAdminAsync("hr1");

        var r = await _h.WorkflowAdmin.AdministrativeCompletionAsync("hr1", "1504", Year,
            "manager left the company, HR completing on their behalf", jobFamilyConfigured: true, perspectiveExempt: false, ip: null);
        Assert.True(r.Success, r.ErrorText);

        var f = await FormAsync();
        Assert.Equal(PmFormStatus.Approved, f.Status);
        Assert.True(f.IsLocked);
        Assert.NotNull(f.OverallRatingCode);

        var audit = await SingleAuditRowAsync();
        Assert.Equal("Workflow Administration: Administrative Completion", audit.Action);

        var again = await _h.WorkflowAdmin.AdministrativeCompletionAsync("hr1", "1504", Year,
            "double click", jobFamilyConfigured: true, perspectiveExempt: false, ip: null);
        Assert.False(again.Success);
    }

    [Fact]
    public async Task Admin_administrative_completion_rejects_incomplete_workflow()
    {
        // achievement: 0 ⇒ FormValidationService.ValidateForSubmitToHr flags missing achievement scores.
        await _h.Workflow.SaveDraftAsync("u854", _mgr, _h.Content1504(achievement: 0));
        await _h.AddHrAdminAsync("hr1");

        var r = await _h.WorkflowAdmin.AdministrativeCompletionAsync("hr1", "1504", Year,
            "trying to skip validation", jobFamilyConfigured: true, perspectiveExempt: false, ip: null);
        Assert.False(r.Success);
        Assert.Equal(PmFormStatus.Draft, (await FormAsync()).Status);
        Assert.Empty(await _h.Db.AuditLogs.Where(a => a.EmpCode == "1504").ToListAsync());
    }

    [Fact]
    public async Task Admin_unlock_flips_lock_flag_without_changing_status_and_rejects_if_already_unlocked()
    {
        await _h.Workflow.SendToEmployeeAsync("u854", _mgr, _h.Content1504());
        await _h.AddHrAdminAsync("hr1");
        Assert.True((await FormAsync()).IsLocked);

        var r = await _h.WorkflowAdmin.UnlockAsync("hr1", "1504", Year, "employee needs to fix one typo", null);
        Assert.True(r.Success, r.ErrorText);

        var f = await FormAsync();
        Assert.False(f.IsLocked);
        Assert.Equal(PmFormStatus.PendingEmployeeAck, f.Status);   // status unchanged by Unlock

        var audit = await SingleAuditRowAsync();
        Assert.Equal("Workflow Administration: Unlock Review", audit.Action);

        var again = await _h.WorkflowAdmin.UnlockAsync("hr1", "1504", Year, "double click", null);
        Assert.False(again.Success);
        Assert.Contains("already unlocked", again.ErrorText);
    }

    [Fact]
    public async Task Admin_actions_are_rejected_for_a_form_that_does_not_exist()
    {
        await _h.AddHrAdminAsync("hr1");
        var r = await _h.WorkflowAdmin.ReturnToEmployeeAsync("hr1", "9999", Year, "no such employee form", null);
        Assert.False(r.Success);
    }
}

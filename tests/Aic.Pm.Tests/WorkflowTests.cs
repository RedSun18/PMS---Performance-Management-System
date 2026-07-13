using Aic.Pm.Core.Domain;
using Aic.Pm.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Aic.Pm.Tests;

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

    private async Task<Aic.Pm.Core.Domain.PmForm> FormAsync() =>
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
        // Safety guardrail: never address the real employee inbox from empmaster —
        // every dispatch is redirected to the fixed safe recipient.
        Assert.Equal(EmailService.SafeRecipient, sent.ToRecipients);
        Assert.Equal("", sent.CcRecipients);
        Assert.Contains("1504@test.local", sent.Note);
        Assert.Contains("854@test.local", sent.Note);

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
    public async Task Email_dedupes_intended_recipients_but_always_sends_to_safe_address()
    {
        var dedup = await _h.Email.DispatchAsync(new EmailSpec("T", new[] { "a@x", "A@X", "b@x" },
            new[] { "a@x", "c@x" }, "s", "b", null, null));
        // Never the legacy addresses — always the fixed safety recipient
        Assert.Equal(EmailService.SafeRecipient, dedup.ToRecipients);
        Assert.Equal("", dedup.CcRecipients);
        Assert.Equal("LOGGED", dedup.Status);
        Assert.Contains("a@x;b@x", dedup.Note);   // deduped intended To preserved for traceability
        Assert.Contains("c@x", dedup.Note);       // deduped intended Cc preserved for traceability

        var empty = await _h.Email.DispatchAsync(new EmailSpec("T", Array.Empty<string>(),
            Array.Empty<string>(), "s", "b", null, null));
        Assert.Equal("SKIPPED_NO_RECIPIENT", empty.Status);
        Assert.Equal("", empty.ToRecipients);
    }

    [Fact]
    public void Email_headings_use_explicit_dark_colour()
    {
        // Light-mode Outlook rule: never white/inherited heading colours
        var f = new Aic.Pm.Core.Domain.PmForm { EmpNameSnapshot = "X", LegacyRefNo = "PM20260907HDR01", EvalYear = 2026 };
        var (_, body) = EmailTemplates.AcknowledgementRequest(f, "Mgr");
        Assert.Contains($"color:{EmailTemplates.HeadingColor}", body);
    }
}

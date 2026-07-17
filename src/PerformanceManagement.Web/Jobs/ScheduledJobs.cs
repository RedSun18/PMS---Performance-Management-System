using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Services;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace PerformanceManagement.Web.Jobs;

/// <summary>Stable Quartz job keys/groups and cron schedules — the single place both Program.cs
/// (registration) and the Job Management page (Task #21, display/Run Now/pause/resume) refer to.</summary>
public static class JobRegistry
{
    public const string Group = "PMS";

    public static readonly (string Name, Type JobType, string Cron, string Description)[] All =
    {
        ("GenerateAnnualForms", typeof(GenerateAnnualFormsJob), "0 5 0 1 1 ?", "Generate Annual Performance Forms (1 January)"),
        ("OpenMidYearReview", typeof(OpenMidYearReviewJob), "0 5 0 1 6 ?", "Open Mid-Year Reviews (1 June)"),
        ("OpenEndYearReview", typeof(OpenEndYearReviewJob), "0 5 0 1 11 ?", "Open End-Year Reviews (1 November)"),
        ("DailyReminder", typeof(DailyReminderJob), "0 0 8 * * ?", "Daily reminder emails for actionable forms"),
        ("WeeklyEscalation", typeof(WeeklyEscalationJob), "0 0 8 ? * MON", "Weekly escalation reminders for overdue forms"),
        ("MonthlyCleanup", typeof(MonthlyCleanupJob), "0 0 2 1 * ?", "Monthly cleanup of stale operational data"),
    };
}

/// <summary>Persists ScheduledJobRun rows around every execution so the Job Management page has
/// durable Previous Run/Duration/Status/Result history (Quartz's own RAMJobStore does not survive
/// a restart). Uses its own DI scope since job listeners are singletons.</summary>
public class JobHistoryListener : IJobListener
{
    public string Name => nameof(JobHistoryListener);
    private readonly IServiceScopeFactory _scopeFactory;
    public JobHistoryListener(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task JobToBeExecuted(IJobExecutionContext context, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        db.ScheduledJobRuns.Add(new ScheduledJobRun
        {
            JobName = context.JobDetail.Key.Name, StartedAt = clock.Now, Status = "RUNNING"
        });
        await db.SaveChangesAsync(ct);
    }

    public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken ct = default) => Task.CompletedTask;

    public async Task JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        // [DisallowConcurrentExecution] on every job guarantees at most one RUNNING row per
        // JobName at a time, so the latest one is unambiguously the run this callback belongs to.
        var run = await db.ScheduledJobRuns
            .Where(r => r.JobName == context.JobDetail.Key.Name && r.Status == "RUNNING")
            .OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(ct);
        if (run is null) return;

        run.CompletedAt = clock.Now;
        run.Status = jobException is null ? "SUCCEEDED" : "FAILED";
        run.ResultSummary = context.Result?.ToString();
        run.ErrorMessage = jobException?.Message;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>1 January: creates a Draft PM Form for every active employee who doesn't already
/// have one for the new evaluation year. KPI/Competency weight defaults are intentionally left
/// at 0 — the PM Form edit page backfills them from the employee's job family the first time
/// it's opened, exactly as it does for any other freshly-created Draft record.</summary>
[DisallowConcurrentExecution]
public class GenerateAnnualFormsJob : IJob
{
    private readonly PmDbContext _db;
    private readonly IClock _clock;
    private readonly PermissionService _permissions;
    private readonly AuditService _audit;

    public GenerateAnnualFormsJob(PmDbContext db, IClock clock, PermissionService permissions, AuditService audit)
    {
        _db = db; _clock = clock; _permissions = permissions; _audit = audit;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var year = _clock.Today.Year;
        var employees = await _db.Employees.AsNoTracking().Where(e => e.TermDate == null).ToListAsync();
        var existing = (await _db.PmForms.AsNoTracking().Where(f => f.EvalYear == year)
            .Select(f => f.EmpCode).ToListAsync()).ToHashSet();

        var created = 0;
        foreach (var emp in employees)
        {
            if (existing.Contains(emp.EmpCode)) continue;
            var managerEmpCode = await _permissions.GetManagerOfAsync(emp.EmpCode);
            _db.PmForms.Add(new PmForm
            {
                LegacyRefNo = RefNoGenerator.Header(emp.EmpCode, year),
                EmpCode = emp.EmpCode,
                EvalYear = year,
                EmpNameSnapshot = emp.LatinName,
                DeptCode = emp.DeptCode,
                ManagerEmpCode = managerEmpCode,
                GradeSnapshot = emp.Grade,
                JoinDateSnapshot = emp.JoinDate,
                Status = PmFormStatus.Draft,
                CreatedAt = _clock.Now,
                CreatedBy = "System (Scheduled Job)"
            });
            created++;
        }
        await _db.SaveChangesAsync();

        var summary = $"{created} form(s) created for {year} ({employees.Count - created} already existed).";
        await _audit.LogAsync("Annual Forms Generated", "System", details: summary);
        context.Result = summary;
    }
}

/// <summary>1 June: no workflow status exists for "mid-year" in this system (achievement scoring
/// is date-gated, not stage-gated — see AchievementGate), so this job is informational only: it
/// records that the configured mid-year window has opened. Kept as its own job (rather than folded
/// into the daily reminder) so the Job Management page can show it as a distinct, independently
/// runnable/disableable item, matching the spec.</summary>
[DisallowConcurrentExecution]
public class OpenMidYearReviewJob : IJob
{
    private readonly SettingsService _settings;
    private readonly AuditService _audit;
    public OpenMidYearReviewJob(SettingsService settings, AuditService audit) { _settings = settings; _audit = audit; }

    public async Task Execute(IJobExecutionContext context)
    {
        var review = await _settings.GetPerformanceReviewSettingsAsync();
        var summary = $"Mid-year review window opened" +
            (review.MidYearStart is { } s && review.MidYearEnd is { } e ? $" ({s:dd MMM} – {e:dd MMM})." : ".");
        await _audit.LogAsync("Mid-Year Review Period Opened", "System", details: summary);
        context.Result = summary;
    }
}

/// <summary>1 November — see OpenMidYearReviewJob remarks; same informational role for the end-year window.</summary>
[DisallowConcurrentExecution]
public class OpenEndYearReviewJob : IJob
{
    private readonly SettingsService _settings;
    private readonly AuditService _audit;
    public OpenEndYearReviewJob(SettingsService settings, AuditService audit) { _settings = settings; _audit = audit; }

    public async Task Execute(IJobExecutionContext context)
    {
        var review = await _settings.GetPerformanceReviewSettingsAsync();
        var summary = $"End-year review window opened" +
            (review.EndYearStart is { } s && review.EndYearEnd is { } e ? $" ({s:dd MMM} – {e:dd MMM})." : ".");
        await _audit.LogAsync("End-Year Review Period Opened", "System", details: summary);
        context.Result = summary;
    }
}

/// <summary>Daily: nudges whoever owns the next action on a form that's been sitting in the same
/// actionable status for 3+ days, at most once per calendar day per form (PmForm.LastRemindedDate).</summary>
[DisallowConcurrentExecution]
public class DailyReminderJob : IJob
{
    private const int ReminderThresholdDays = 3;
    private static readonly string[] ActionableStatuses =
    {
        PmFormStatus.PendingEmployeeAck, PmFormStatus.EmployeeAcknowledged,
        PmFormStatus.SubmittedToHr, PmFormStatus.HrReview1Approved
    };

    private readonly PmDbContext _db;
    private readonly IClock _clock;
    private readonly EmailService _email;
    private readonly FormLinkService _links;

    public DailyReminderJob(PmDbContext db, IClock clock, EmailService email, FormLinkService links)
    {
        _db = db; _clock = clock; _email = email; _links = links;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var today = _clock.Today;
        var cutoff = today.AddDays(-ReminderThresholdDays);

        var candidates = await _db.PmForms
            .Where(f => ActionableStatuses.Contains(f.Status) &&
                        f.StatusChangeDate != null && f.StatusChangeDate <= cutoff &&
                        (f.LastRemindedDate == null || f.LastRemindedDate < today))
            .ToListAsync();

        var sent = 0;
        foreach (var form in candidates)
        {
            var (empCode, requiredAction, recipientLabel) = form.Status switch
            {
                PmFormStatus.PendingEmployeeAck => (form.EmpCode, "Review and acknowledge your objectives", "Employee"),
                PmFormStatus.EmployeeAcknowledged => (form.ManagerEmpCode, "Enter achievement scores and submit to HR", "Manager"),
                _ => ((string?)null, "Complete HR review", "HR Team")
            };

            var days = (today.ToDateTime(TimeOnly.MinValue) - form.StatusChangeDate!.Value.ToDateTime(TimeOnly.MinValue)).Days;
            var recipients = form.Status is PmFormStatus.SubmittedToHr or PmFormStatus.HrReview1Approved
                ? await HrAdminEmailsAsync()
                : await EmailsAsync(empCode);
            if (recipients.Count == 0) continue;

            var userName = string.IsNullOrEmpty(empCode) ? "" : await UserNameForEmpCodeAsync(empCode);
            var actionUrl = await _links.BuildFormUrlAsync(form.EmpCode, form.EvalYear, userName);
            var (subject, body) = EmailTemplates.Reminder(form, recipientLabel, requiredAction, days, actionUrl, _clock.Now);
            await _email.DispatchAsync(new EmailSpec("REMINDER", recipients, Array.Empty<string>(),
                subject, body, form.LegacyRefNo, $"REMINDER-{form.LegacyRefNo}-{today:yyyyMMdd}"));

            form.LastRemindedDate = today;
            sent++;
        }
        await _db.SaveChangesAsync();
        context.Result = $"{sent} reminder(s) sent for {candidates.Count} eligible form(s).";
    }

    private async Task<IReadOnlyList<string>> EmailsAsync(string? empCode)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return Array.Empty<string>();
        var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.EmpCode == empCode.Trim());
        return string.IsNullOrWhiteSpace(e?.Email) ? Array.Empty<string>() : new[] { e!.Email! };
    }

    private async Task<string> UserNameForEmpCodeAsync(string? empCode)
    {
        if (string.IsNullOrWhiteSpace(empCode)) return "";
        var u = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.EmpCode == empCode.Trim() && x.IsActive);
        return u?.UserName ?? "";
    }

    private async Task<IReadOnlyList<string>> HrAdminEmailsAsync() =>
        await _db.UserRoles.AsNoTracking().Where(r => r.Role == Roles.HrAdmin).Include(r => r.AppUser)
            .Select(r => r.AppUser!.Email).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e!).ToListAsync();
}

/// <summary>Weekly (Monday): escalates any form that's been outstanding 14+ days to HR, regardless
/// of stage — deliberately re-sent every week a form stays overdue rather than tracked with its own
/// "already escalated" flag, since a recurring weekly nag is exactly the intended behaviour here.</summary>
[DisallowConcurrentExecution]
public class WeeklyEscalationJob : IJob
{
    private const int EscalationThresholdDays = 14;
    private static readonly string[] ActionableStatuses =
    {
        PmFormStatus.PendingEmployeeAck, PmFormStatus.EmployeeAcknowledged,
        PmFormStatus.SubmittedToHr, PmFormStatus.HrReview1Approved
    };

    private readonly PmDbContext _db;
    private readonly IClock _clock;
    private readonly EmailService _email;
    private readonly FormLinkService _links;

    public WeeklyEscalationJob(PmDbContext db, IClock clock, EmailService email, FormLinkService links)
    {
        _db = db; _clock = clock; _email = email; _links = links;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var today = _clock.Today;
        var cutoff = today.AddDays(-EscalationThresholdDays);

        var overdue = await _db.PmForms.AsNoTracking()
            .Where(f => ActionableStatuses.Contains(f.Status) && f.StatusChangeDate != null && f.StatusChangeDate <= cutoff)
            .ToListAsync();

        var hrEmails = await _db.UserRoles.AsNoTracking().Where(r => r.Role == Roles.HrAdmin).Include(r => r.AppUser)
            .Select(r => r.AppUser!.Email).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e!).ToListAsync();

        if (hrEmails.Count > 0 && overdue.Count > 0)
        {
            var rows = new List<EmailTemplates.EscalationRow>();
            foreach (var form in overdue)
            {
                var requiredAction = form.Status switch
                {
                    PmFormStatus.PendingEmployeeAck => "Employee acknowledgement",
                    PmFormStatus.EmployeeAcknowledged => "Manager achievement scoring / submission to HR",
                    PmFormStatus.SubmittedToHr => "First HR review",
                    _ => "Final HR review"
                };
                var owner = form.Status is PmFormStatus.PendingEmployeeAck ? form.EmpNameSnapshot
                    : form.Status is PmFormStatus.EmployeeAcknowledged ? "Manager" : "HR";
                var days = (today.ToDateTime(TimeOnly.MinValue) - form.StatusChangeDate!.Value.ToDateTime(TimeOnly.MinValue)).Days;
                var actionUrl = await _links.BuildFormUrlAsync(form.EmpCode, form.EvalYear, "");
                rows.Add(new EmailTemplates.EscalationRow(form.LegacyRefNo, form.EmpNameSnapshot, form.EvalYear.ToString(),
                    PmFormStatus.DisplayName(form.Status), requiredAction, owner, days, actionUrl));
            }

            // One digest email per run (not one per form) — a 14-day-overdue backlog can be sizeable,
            // and HR needs one triaging list, not dozens of separate messages.
            var (subject, body) = EmailTemplates.EscalationDigest(rows, _clock.Now);
            await _email.DispatchAsync(new EmailSpec("ESCALATION_DIGEST", hrEmails, Array.Empty<string>(),
                subject, body, null, $"ESCALATION_DIGEST-{today:yyyyMMdd}"));
        }
        context.Result = $"{overdue.Count} overdue form(s) escalated" + (overdue.Count > 0 ? " in 1 digest email." : ".");
    }
}

/// <summary>Monthly (1st, 02:00): purges operational EmailLog rows older than 180 days. Never touches
/// AuditLog (the compliance/accountability record — see AuditLog remarks) or ImpersonationLog.</summary>
[DisallowConcurrentExecution]
public class MonthlyCleanupJob : IJob
{
    private const int EmailLogRetentionDays = 180;

    private readonly PmDbContext _db;
    private readonly IClock _clock;
    private readonly AuditService _audit;
    public MonthlyCleanupJob(PmDbContext db, IClock clock, AuditService audit) { _db = db; _clock = clock; _audit = audit; }

    public async Task Execute(IJobExecutionContext context)
    {
        var cutoff = _clock.Now.AddDays(-EmailLogRetentionDays);
        var deleted = await _db.EmailLogs.Where(e => e.CreatedAt < cutoff).ExecuteDeleteAsync();

        var summary = $"{deleted} email log(s) older than {EmailLogRetentionDays} days purged.";
        await _audit.LogAsync("System Cleanup", "System", details: summary);
        context.Result = summary;
    }
}

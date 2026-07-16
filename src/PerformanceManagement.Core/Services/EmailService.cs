using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;

namespace PerformanceManagement.Core.Services;

public record EmailSpec(
    string TemplateKey,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    string Subject,
    string Body,
    string? FormLegacyRefNo,
    string? IdempotencyKey);

/// <summary>
/// Centralized mail dispatch: one caller, one send per transition, deduplicated
/// recipients, empty To ⇒ graceful skip (never an SMTP call), delivery log with
/// idempotency key. Sends real mail via SMTP using <see cref="SettingsService"/>
/// (database-backed, admin-editable) — see docs on the System Settings page.
///
/// SAFETY GUARDRAIL: while a development redirect address is configured, this build
/// must never address a real employee inbox imported from the legacy empmaster/
/// pm_form_records export. Every dispatch that would have had a recipient is
/// redirected there; the originally intended recipients are preserved in the log's
/// Note field for traceability only, never used as an actual send target.
/// </summary>
public class EmailService
{
    /// <summary>Fallback redirect used only if no DevelopmentRedirectEmail is configured in Settings.</summary>
    public const string SafeRecipient = "aryanbhandary@gmail.com";

    private readonly PmDbContext _db;
    private readonly IClock _clock;
    private readonly SettingsService _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(PmDbContext db, IClock clock, SettingsService settings, ILogger<EmailService> logger)
    {
        _db = db; _clock = clock; _settings = settings; _logger = logger;
    }

    /// <summary>Dedupe (To wins over CC), skip empties, redirect to the dev address, send via SMTP, write the log row.</summary>
    public async Task<EmailLog> DispatchAsync(EmailSpec spec)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var intendedTo = spec.To.Select(a => a.Trim()).Where(a => a.Length > 0 && seen.Add(a)).ToList();
        var intendedCc = spec.Cc.Select(a => a.Trim()).Where(a => a.Length > 0 && seen.Add(a)).ToList();

        var credentials = await _settings.GetSmtpCredentialsAsync();
        var redirectTo = credentials?.DevelopmentRedirectEmail?.Trim();
        if (string.IsNullOrEmpty(redirectTo)) redirectTo = SafeRecipient;

        // Redirect: never send to a legacy empmaster address while a dev redirect is
        // configured. Empty intended-To still skips entirely (no SMTP call at all).
        var to = intendedTo.Count == 0 ? new List<string>() : new List<string> { redirectTo };

        var log = new EmailLog
        {
            CreatedAt = _clock.Now,
            TemplateKey = spec.TemplateKey,
            FormLegacyRefNo = spec.FormLegacyRefNo,
            ToRecipients = string.Join(";", to),
            CcRecipients = "",
            Subject = spec.Subject,
            Body = spec.Body,
            IdempotencyKey = spec.IdempotencyKey,
            Note = (intendedTo.Count == 0 && intendedCc.Count == 0)
                ? null
                : $"Redirected to {redirectTo}. Intended To=[{string.Join(";", intendedTo)}] Cc=[{string.Join(";", intendedCc)}].",
            Status = to.Count == 0 ? "SKIPPED_NO_RECIPIENT" : "PENDING"
        };

        if (log.Status == "PENDING" && spec.IdempotencyKey is not null &&
            await _db.EmailLogs.AnyAsync(e => e.IdempotencyKey == spec.IdempotencyKey && e.Status != "FAILED"))
        {
            log.Status = "SKIPPED_DUPLICATE";
        }

        if (log.Status == "PENDING")
        {
            if (credentials is null)
            {
                // Distinct from FAILED: nothing was attempted because there is no SMTP
                // relay configured yet (fresh install / dev machine before Settings is set up).
                log.Status = "LOGGED";
                log.Note = (log.Note is null ? "" : log.Note + " ") +
                    "SMTP is not configured — set it up on the System Settings page.";
                _logger.LogWarning("Email {TemplateKey} for {RefNo} not sent: SMTP is not configured.",
                    spec.TemplateKey, spec.FormLegacyRefNo);
            }
            else if (!credentials.EnableEmailNotifications)
            {
                log.Status = "DISABLED";
            }
            else
            {
                try
                {
                    await SendAsync(credentials, to, spec.Subject, spec.Body);
                    log.Status = "SENT";
                }
                catch (Exception ex)
                {
                    log.Status = "FAILED";
                    log.Note = (log.Note is null ? "" : log.Note + " ") + $"Send failed: {ex.Message}";
                    _logger.LogError(ex, "Email {TemplateKey} for {RefNo} failed to send via {Host}:{Port}.",
                        spec.TemplateKey, spec.FormLegacyRefNo, credentials.Host, credentials.Port);
                }
            }
        }

        _db.EmailLogs.Add(log);
        await _db.SaveChangesAsync();
        return log;
    }

    private static async Task SendAsync(SmtpCredentials creds, IReadOnlyList<string> to, string subject, string body)
    {
        using var client = new SmtpClient(creds.Host, creds.Port)
        {
            EnableSsl = creds.EnableSsl,
            Credentials = new NetworkCredential(creds.Username, creds.Password)
        };
        using var message = new MailMessage
        {
            From = new MailAddress(creds.SenderEmail, creds.SenderName),
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };
        foreach (var addr in to) message.To.Add(addr);
        await client.SendMailAsync(message);
    }

    /// <summary>
    /// Sends an immediate connectivity test using credentials that may not yet be saved
    /// (the System Settings page tests before committing). Bypasses the log/redirect
    /// pipeline entirely — this is a direct admin-initiated check, not a workflow event.
    /// </summary>
    public static async Task SendTestEmailAsync(SmtpCredentials creds, string toAddress, string appName)
    {
        var now = DateTime.Now;
        var body = $"""
            <html><body style="margin:0;padding:0;background:#eef1f6;font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;">
            <div style="max-width:600px;margin:0 auto;padding:24px 16px;">
              <div style="background-color:#0f2b5c;background:linear-gradient(135deg,#0f2b5c 0%,#1e3a8a 100%);border-radius:12px 12px 0 0;padding:20px 28px;">
                <div style="color:#fff;font-size:13px;letter-spacing:.06em;text-transform:uppercase;opacity:.75;">{appName}</div>
                <div style="color:#fff;font-size:20px;font-weight:700;margin-top:4px;">SMTP Test Email</div>
              </div>
              <div style="background:#fff;border:1px solid #e3e7ee;border-top:none;border-radius:0 0 12px 12px;padding:28px;">
                <p style="margin-top:0;color:#15803d;font-weight:600;">✓ SMTP connection succeeded.</p>
                <table style="width:100%;border-collapse:collapse;margin:15px 0;">
                  <tr><td style="padding:8px;border-bottom:1px solid #e0e0e0;font-weight:bold;width:160px;color:#666;">Time Sent</td><td style="padding:8px;border-bottom:1px solid #e0e0e0;">{now:dddd, dd MMMM yyyy HH:mm:ss}</td></tr>
                  <tr><td style="padding:8px;border-bottom:1px solid #e0e0e0;font-weight:bold;color:#666;">Application</td><td style="padding:8px;border-bottom:1px solid #e0e0e0;">{appName}</td></tr>
                  <tr><td style="padding:8px;border-bottom:1px solid #e0e0e0;font-weight:bold;color:#666;">SMTP Server</td><td style="padding:8px;border-bottom:1px solid #e0e0e0;">{creds.Host}:{creds.Port}</td></tr>
                </table>
                <p style="color:#666;">If you received this message, outgoing email notifications are working correctly.</p>
              </div>
            </div>
            </body></html>
            """;
        await SendAsync(creds, new[] { toAddress }, "PMS — SMTP Test Email", body);
    }
}

/// <summary>
/// Branded, actionable HTML email bodies. Headings always use an explicit dark colour —
/// white headings become invisible in light-mode Outlook (legacy incident). Every template
/// carries a primary action button (deep-links to <c>/OpenForm?token=...</c> — see
/// <see cref="FormLinkService"/> — never a raw empcd/year query string) plus a plain-text
/// fallback link, and states current status / required action / who acted previously / who
/// must act next, so the email is actionable on its own without opening the app first.
/// </summary>
public static class EmailTemplates
{
    /// <summary>Progressive-enhancement hover state for the action button — ignored gracefully
    /// by email clients that strip &lt;style&gt; blocks, applied by those that don't (kept out of
    /// the interpolated template body so its literal braces don't need raw-string escaping).</summary>
    private const string ButtonHoverStyle = ".pms-btn:hover { background: #16336b !important; }";

    private static string Wrap(string appHeading, string title, string inner, DateTime sentAt) => $"""
        <html>
        <head>
          <style>
            {ButtonHoverStyle}
          </style>
        </head>
        <body style="margin:0;padding:0;background:#eef1f6;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
        <div style="max-width:640px;margin:0 auto;padding:24px 16px;">
          <div style="background-color:#0f2b5c;background:linear-gradient(135deg,#0f2b5c 0%,#1e3a8a 100%);border-radius:12px 12px 0 0;padding:20px 28px;">
            <div style="color:#fff;font-size:13px;letter-spacing:.06em;text-transform:uppercase;opacity:.75;">{appHeading}</div>
            <div style="color:#fff;font-size:20px;font-weight:700;margin-top:4px;">{title}</div>
          </div>
          <div style="background:#ffffff;border:1px solid #e3e7ee;border-top:none;border-radius:0 0 12px 12px;padding:28px;box-shadow:0 1px 3px rgba(15,23,42,.07);color:#333;font-size:14px;line-height:1.6;">
            {inner}
          </div>
          <div style="text-align:center;padding:18px 8px 0;color:#8a93a6;font-size:11px;">
            Sent {sentAt:dddd, dd MMMM yyyy} at {sentAt:HH:mm} &middot; This is an automated message, please do not reply.<br/>
            For assistance, contact the HR Department.
          </div>
        </div>
        </body></html>
        """;

    private static string InfoTable(params (string Label, string Value)[] rows)
    {
        var trs = string.Join("", rows.Select(r =>
            $"<tr><td style='padding:8px;border-bottom:1px solid #e0e0e0;font-weight:bold;width:180px;color:#666;'>{r.Label}</td>" +
            $"<td style='padding:8px;border-bottom:1px solid #e0e0e0;'>{r.Value}</td></tr>"));
        return $"<table style='width:100%;border-collapse:collapse;margin:15px 0;'>{trs}</table>";
    }

    /// <summary>Prominent, centered, mobile-friendly call-to-action button plus a plain-text fallback link.</summary>
    private static string ActionButton(string url, string label) => $"""
        <div style="text-align:center;margin:30px 0 14px;">
          <a href="{url}" class="pms-btn"
             style="display:inline-block;background-color:#0f2b5c;color:#ffffff !important;text-decoration:none;
                    font-weight:700;font-size:16px;padding:16px 36px;border-radius:8px;min-width:240px;
                    text-align:center;line-height:1.2;">{label}</a>
        </div>
        <p style="text-align:center;color:#8a93a6;font-size:11.5px;margin:0 0 22px;">
          If the button does not work, copy and paste this link into your browser:<br/>
          <a href="{url}" style="color:#2f5fd6;word-break:break-all;">{url}</a>
        </p>
        """;

    public static (string Subject, string Body) AcknowledgementRequest(
        PmForm f, string managerName, string actionUrl, DateTime sentAt) =>
        ($"ACTION REQUIRED: Review Your Performance Objectives | {f.EmpNameSnapshot} | {f.EvalYear}",
         Wrap("Performance Management System", "Performance Objectives Ready for Review",
              $"<p>Dear <strong>{f.EmpNameSnapshot}</strong>,</p>" +
              "<p>Your manager has set your performance objectives. Please review and acknowledge them within 7 days.</p>" +
              InfoTable(("Reference", f.LegacyRefNo), ("Employee", $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        ("Review Year", f.EvalYear.ToString()),
                        ("Current Status", PmFormStatus.DisplayName(f.Status)),
                        ("Required Action", "Review and acknowledge your objectives"),
                        ("Previous Action By", $"{managerName} (Manager)"),
                        ("Next Action Required By", $"{f.EmpNameSnapshot} (You)"),
                        ("KPIs Set", f.Kpis.Count.ToString()), ("Competencies", f.Competencies.Count.ToString())) +
              ActionButton(actionUrl, "View Performance Form"), sentAt));

    public static (string Subject, string Body) EmployeeAcknowledged(
        PmForm f, string managerName, string actionUrl, DateTime sentAt) =>
        ($"Employee Acknowledged Objectives: {f.EmpNameSnapshot} | {f.EvalYear}",
         Wrap("Performance Management System", "Employee Submitted Performance Review",
              $"<p>Dear <strong>{managerName}</strong>,</p>" +
              $"<p>{f.EmpNameSnapshot} has acknowledged their objectives for {f.EvalYear}.</p>" +
              InfoTable(("Reference", f.LegacyRefNo), ("Employee", $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        ("Review Year", f.EvalYear.ToString()),
                        ("Current Status", PmFormStatus.DisplayName(f.Status)),
                        ("Required Action", "Review the acknowledged objectives; achievement scoring opens 1 December"),
                        ("Previous Action By", $"{f.EmpNameSnapshot} (Employee)"),
                        ("Next Action Required By", $"{managerName} (You, Manager)"),
                        ("Acknowledged On", f.EmpAckDate?.ToString("dd/MM/yyyy") ?? "")) +
              (string.IsNullOrWhiteSpace(f.EmpAckComments) ? "" :
               $"<p><strong>Employee comments:</strong> {f.EmpAckComments}</p>") +
              ActionButton(actionUrl, "Review Employee Submission"), sentAt));

    public static (string Subject, string Body) SubmittedToHr(
        PmForm f, string managerName, string rating, string actionUrl, DateTime sentAt) =>
        ($"PM Form Ready for HR Review | {f.EmpNameSnapshot} | {f.EvalYear}",
         Wrap("Performance Management System", "Performance Review Ready for HR",
              "<p>Dear HR Team,</p><p>A Performance Management form has been completed by the manager and is ready for your review:</p>" +
              InfoTable(("Reference", f.LegacyRefNo), ("Employee", $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        ("Review Year", f.EvalYear.ToString()),
                        ("Current Status", PmFormStatus.DisplayName(f.Status)),
                        ("Required Action", "Complete first HR review"),
                        ("Previous Action By", $"{managerName} (Manager)"),
                        ("Next Action Required By", "HR Reviewer 1"),
                        ("KPI Score", f.KpiScore.ToString("F2")), ("Competency Score", f.CompScore.ToString("F2")),
                        ("Overall Score", f.PerformanceScore.ToString("F2")), ("Rating", rating)) +
              ActionButton(actionUrl, "Open HR Review"), sentAt));

    public static (string Subject, string Body) Hr1Approved(PmForm f, string actionUrl, DateTime sentAt) =>
        ($"PM Form - Ready for Final HR Review (HR Rep 2) | {f.EmpNameSnapshot} | {f.EvalYear}",
         Wrap("Performance Management System", "Final HR Review Required",
              "<p>Dear HR Team,</p>" +
              InfoTable(("Reference", f.LegacyRefNo), ("Employee", $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        ("Review Year", f.EvalYear.ToString()),
                        ("Current Status", PmFormStatus.DisplayName(f.Status)),
                        ("Required Action", "Complete final HR review and approval"),
                        ("Previous Action By", $"{f.Hr1ReviewerName} (HR Reviewer 1)"),
                        ("Next Action Required By", "HR Reviewer 2 (Final Approval)")) +
              (string.IsNullOrWhiteSpace(f.Hr1Remarks) ? "" : $"<p><strong>HR remarks:</strong> {f.Hr1Remarks}</p>") +
              ActionButton(actionUrl, "Open HR Review"), sentAt));

    public static (string Subject, string Body) FinalApproved(
        PmForm f, string rating, string actionUrl, DateTime sentAt) =>
        ($"PM Form - APPROVED (Final) | {f.EmpNameSnapshot} | {f.EvalYear}",
         Wrap("Performance Management System", "Performance Review Finalized",
              "<p>Dear Team,</p><p>This Performance Management form is now finalized and archived.</p>" +
              InfoTable(("Reference", f.LegacyRefNo), ("Employee", $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        ("Review Year", f.EvalYear.ToString()),
                        ("Current Status", PmFormStatus.DisplayName(f.Status)),
                        ("Required Action", "None — this review is complete"),
                        ("Previous Action By", $"{f.Hr2ReviewerName} (HR Reviewer 2)"),
                        ("Next Action Required By", "None"),
                        ("Reviewed By", f.Hr1ReviewerName ?? ""), ("Approved By", f.Hr2ReviewerName ?? ""),
                        ("Score", f.PerformanceScore.ToString("F2")), ("Rating", rating)) +
              (string.IsNullOrWhiteSpace(f.Hr2Remarks) ? "" : $"<p><strong>HR remarks:</strong> {f.Hr2Remarks}</p>") +
              ActionButton(actionUrl, "View Final Performance Review"), sentAt));

    public static (string Subject, string Body) Reverted(
        PmForm f, string managerName, string hrComments, string actionUrl, DateTime sentAt) =>
        ($"PM Form Requires Revision | {f.EmpNameSnapshot} | {f.EvalYear}",
         Wrap("Performance Management System", "Performance Form Requires Revision",
              $"<p>Dear <strong>{managerName}</strong>,</p><p>The Performance Management form below has been reviewed by HR and requires revisions:</p>" +
              InfoTable(("Reference", f.LegacyRefNo), ("Employee", $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        ("Review Year", f.EvalYear.ToString()),
                        ("Current Status", PmFormStatus.DisplayName(f.Status)),
                        ("Required Action", "Revise the form per HR's comments and resubmit"),
                        ("Previous Action By", string.IsNullOrWhiteSpace(f.Hr1ReviewerName) ? "HR Team" : $"{f.Hr1ReviewerName} (HR)"),
                        ("Next Action Required By", $"{managerName} (You, Manager)"),
                        ("HR Comments", string.IsNullOrWhiteSpace(hrComments) ? "No specific comments provided." : hrComments)) +
              ActionButton(actionUrl, "Revise Performance Form"), sentAt));
}

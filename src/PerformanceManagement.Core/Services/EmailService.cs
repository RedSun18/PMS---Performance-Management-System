using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using PerformanceManagement.Core.Resources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Globalization;
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
    private const string AppName = "Performance Management System";

    /// <summary>Progressive-enhancement hover state for the action button — ignored gracefully
    /// by email clients that strip &lt;style&gt; blocks, applied by those that don't (kept out of
    /// the interpolated template body so its literal braces don't need raw-string escaping).</summary>
    private const string ButtonHoverStyle = ".pms-btn:hover { background: #16336b !important; }";
    private const string ResponsiveStyle =
        "@media (max-width: 620px) { .pms-container { width: 100% !important; } .pms-card { padding: 20px !important; } }";

    /// <summary>Outer skeleton for every workflow email. Deliberately mixes a `max-width` div
    /// (for modern clients: Gmail, Apple Mail, Outlook.com, mobile) with an outer `role="presentation"`
    /// table pinned to 600px (the "bulletproof centering" pattern) — legacy desktop Outlook
    /// (2007–2019, Windows) uses Word's rendering engine and ignores `max-width` on a div
    /// entirely, rendering it full-width instead; the wrapping table forces the same constrained
    /// width there too. 600px (not 640px) is the conventional safe email width that comfortably
    /// clears Gmail's and Outlook's own chrome without triggering their horizontal scrollbars.
    ///
    /// `dir`/`lang` on &lt;html&gt; drive bidi reordering and default text-align (cells below rely
    /// on the UA default of `text-align: start`, never a hardcoded `left`, so they flip correctly
    /// for Arabic without any extra CSS). The font stack includes Tahoma — one of the few fonts
    /// with reliable Arabic glyph coverage baked into virtually every OS/email client, unlike the
    /// self-hosted Noto Sans Arabic used on-screen (email clients routinely strip @font-face).</summary>
    private static string Wrap(string appHeading, string title, string inner, DateTime sentAt, CultureInfo culture)
    {
        var isRtl = culture.TwoLetterISOLanguageName == "ar";
        var dir = isRtl ? "rtl" : "ltr";
        var footer = string.Format(EmailResource.Get("FooterSentOn", culture),
            sentAt.ToString("dddd, dd MMMM yyyy", culture), sentAt.ToString("HH:mm", culture));
        return $"""
            <html dir="{dir}" lang="{culture.TwoLetterISOLanguageName}">
            <head>
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <meta name="color-scheme" content="light dark" />
              <meta name="supported-color-schemes" content="light dark" />
              <style>
                {ButtonHoverStyle}
                {ResponsiveStyle}
              </style>
            </head>
            <body dir="{dir}" style="margin:0;padding:0;background:#eef1f6;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Tahoma,Roboto,Helvetica,Arial,sans-serif;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#eef1f6;">
              <tr><td align="center" style="padding:24px 16px;">
            <table role="presentation" width="600" cellpadding="0" cellspacing="0" class="pms-container" style="width:600px;max-width:600px;">
              <tr><td>
              <div style="background-color:#0f2b5c;background:linear-gradient(135deg,#0f2b5c 0%,#1e3a8a 100%);border-radius:12px 12px 0 0;padding:20px 28px;">
                <div style="color:#fff;font-size:13px;letter-spacing:.06em;text-transform:uppercase;opacity:.75;">{appHeading}</div>
                <div style="color:#fff;font-size:20px;font-weight:700;margin-top:4px;">{title}</div>
              </div>
              <div class="pms-card" style="background:#ffffff;border:1px solid #e3e7ee;border-top:none;border-radius:0 0 12px 12px;padding:28px;box-shadow:0 1px 3px rgba(15,23,42,.07);color:#333;font-size:14px;line-height:1.6;">
                {inner}
              </div>
              <div style="text-align:center;padding:18px 8px 0;color:#8a93a6;font-size:11px;">
                {footer}<br/>
                {EmailResource.Get("FooterContactHr", culture)}
              </div>
              </td></tr>
            </table>
              </td></tr>
            </table>
            </body></html>
            """;
    }

    private static string InfoTable(CultureInfo culture, params (string Label, string Value)[] rows)
    {
        _ = culture; // rows already carry pre-localized label/value text; kept for call-site symmetry
        // Every text-bearing cell sets its own `color` explicitly rather than inheriting from
        // the surrounding card — some clients/browsers apply an automatic dark-mode heuristic
        // once <meta name="color-scheme"> opts a page in (see Wrap), turning any *unstyled* text
        // white while explicit backgrounds stay as authored; the result is invisible white-on-
        // white text. Explicit color on every cell sidesteps that regardless of client behavior.
        // No hardcoded text-align — cells inherit the UA default of `text-align: start`, which
        // flips correctly for RTL along with the rest of the document.
        var trs = string.Join("", rows.Select(r =>
            $"<tr><td style='padding:8px;border-bottom:1px solid #e0e0e0;font-weight:bold;width:38%;color:#666666;word-break:break-word;'>{r.Label}</td>" +
            $"<td style='padding:8px;border-bottom:1px solid #e0e0e0;width:62%;color:#333333;word-break:break-word;'>{r.Value}</td></tr>"));
        return $"<table style='width:100%;table-layout:fixed;border-collapse:collapse;margin:15px 0;'>{trs}</table>";
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
        PmForm f, string managerName, string actionUrl, DateTime sentAt, CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.CurrentUICulture;
        string G(string k, params object?[] a) => EmailResource.Get(k, c, a);
        return (G("AckReqSubject", f.EmpNameSnapshot, f.EvalYear),
         Wrap(AppName, G("AckReqTitle"),
              $"<p>{G("DearName", f.EmpNameSnapshot)}</p>" +
              $"<p>{G("AckReqIntro")}</p>" +
              InfoTable(c, (G("Reference"), f.LegacyRefNo), (G("Employee"), $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        (G("ReviewYear"), f.EvalYear.ToString()),
                        (G("CurrentStatus"), PmFormStatus.DisplayName(f.Status, c)),
                        (G("RequiredAction"), G("AckReqRequiredAction")),
                        (G("PreviousActionBy"), G("RoleManager", managerName)),
                        (G("NextActionRequiredBy"), G("RoleYou", f.EmpNameSnapshot)),
                        (G("KpisSet"), f.Kpis.Count.ToString()), (G("Competencies"), f.Competencies.Count.ToString())) +
              ActionButton(actionUrl, G("ViewPerformanceForm")), sentAt, c));
    }

    public static (string Subject, string Body) EmployeeAcknowledged(
        PmForm f, string managerName, string actionUrl, DateTime sentAt, DateOnly achievementOpenDate, CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.CurrentUICulture;
        string G(string k, params object?[] a) => EmailResource.Get(k, c, a);
        return (G("EmpAckSubject", f.EmpNameSnapshot, f.EvalYear),
         Wrap(AppName, G("EmpAckTitle"),
              $"<p>{G("DearName", managerName)}</p>" +
              $"<p>{G("EmpAckIntro", f.EmpNameSnapshot, f.EvalYear)}</p>" +
              InfoTable(c, (G("Reference"), f.LegacyRefNo), (G("Employee"), $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        (G("ReviewYear"), f.EvalYear.ToString()),
                        (G("CurrentStatus"), PmFormStatus.DisplayName(f.Status, c)),
                        (G("RequiredAction"), G("EmpAckRequiredAction", achievementOpenDate.ToDateTime(TimeOnly.MinValue).ToString("dd MMMM yyyy", c))),
                        (G("PreviousActionBy"), G("RoleEmployee", f.EmpNameSnapshot)),
                        (G("NextActionRequiredBy"), G("RoleYouManager", managerName)),
                        (G("AcknowledgedOn"), f.EmpAckDate?.ToString("dd/MM/yyyy") ?? "")) +
              (string.IsNullOrWhiteSpace(f.EmpAckComments) ? "" :
               $"<p><strong>{G("EmployeeComments")}</strong> {f.EmpAckComments}</p>") +
              ActionButton(actionUrl, G("ReviewEmployeeSubmission")), sentAt, c));
    }

    public static (string Subject, string Body) SubmittedToHr(
        PmForm f, string managerName, string rating, string actionUrl, DateTime sentAt, CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.CurrentUICulture;
        string G(string k, params object?[] a) => EmailResource.Get(k, c, a);
        return (G("SubToHrSubject", f.EmpNameSnapshot, f.EvalYear),
         Wrap(AppName, G("SubToHrTitle"),
              $"<p>{G("DearHrTeam")}</p><p>{G("SubToHrIntro")}</p>" +
              InfoTable(c, (G("Reference"), f.LegacyRefNo), (G("Employee"), $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        (G("ReviewYear"), f.EvalYear.ToString()),
                        (G("CurrentStatus"), PmFormStatus.DisplayName(f.Status, c)),
                        (G("RequiredAction"), G("SubToHrRequiredAction")),
                        (G("PreviousActionBy"), G("RoleManager", managerName)),
                        (G("NextActionRequiredBy"), G("HrReviewer1Plain")),
                        (G("KpiScore"), f.KpiScore.ToString("F2")), (G("CompetencyScore"), f.CompScore.ToString("F2")),
                        (G("OverallScore"), f.PerformanceScore.ToString("F2")), (G("Rating"), rating)) +
              ActionButton(actionUrl, G("OpenHrReview")), sentAt, c));
    }

    public static (string Subject, string Body) Hr1Approved(PmForm f, string actionUrl, DateTime sentAt, CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.CurrentUICulture;
        string G(string k, params object?[] a) => EmailResource.Get(k, c, a);
        return (G("Hr1ApprovedSubject", f.EmpNameSnapshot, f.EvalYear),
         Wrap(AppName, G("Hr1ApprovedTitle"),
              $"<p>{G("DearHrTeam")}</p>" +
              InfoTable(c, (G("Reference"), f.LegacyRefNo), (G("Employee"), $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        (G("ReviewYear"), f.EvalYear.ToString()),
                        (G("CurrentStatus"), PmFormStatus.DisplayName(f.Status, c)),
                        (G("RequiredAction"), G("Hr1ApprovedRequiredAction")),
                        (G("PreviousActionBy"), G("RoleHrReviewer1", f.Hr1ReviewerName)),
                        (G("NextActionRequiredBy"), G("HrReviewer2FinalPlain"))) +
              (string.IsNullOrWhiteSpace(f.Hr1Remarks) ? "" : $"<p><strong>{G("HrRemarks")}</strong> {f.Hr1Remarks}</p>") +
              ActionButton(actionUrl, G("OpenHrReview")), sentAt, c));
    }

    public static (string Subject, string Body) FinalApproved(
        PmForm f, string rating, string actionUrl, DateTime sentAt, CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.CurrentUICulture;
        string G(string k, params object?[] a) => EmailResource.Get(k, c, a);
        return (G("FinalApprovedSubject", f.EmpNameSnapshot, f.EvalYear),
         Wrap(AppName, G("FinalApprovedTitle"),
              $"<p>{G("DearTeam")}</p><p>{G("FinalApprovedIntro")}</p>" +
              InfoTable(c, (G("Reference"), f.LegacyRefNo), (G("Employee"), $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        (G("ReviewYear"), f.EvalYear.ToString()),
                        (G("CurrentStatus"), PmFormStatus.DisplayName(f.Status, c)),
                        (G("RequiredAction"), G("FinalApprovedRequiredAction")),
                        (G("PreviousActionBy"), G("RoleHrReviewer2", f.Hr2ReviewerName)),
                        (G("NextActionRequiredBy"), G("NonePlain")),
                        (G("ReviewedBy"), f.Hr1ReviewerName ?? ""), (G("ApprovedBy"), f.Hr2ReviewerName ?? ""),
                        (G("Score"), f.PerformanceScore.ToString("F2")), (G("Rating"), rating)) +
              (string.IsNullOrWhiteSpace(f.Hr2Remarks) ? "" : $"<p><strong>{G("HrRemarks")}</strong> {f.Hr2Remarks}</p>") +
              ActionButton(actionUrl, G("ViewFinalPerformanceReview")), sentAt, c));
    }

    public static (string Subject, string Body) Reverted(
        PmForm f, string managerName, string hrComments, string actionUrl, DateTime sentAt, CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.CurrentUICulture;
        string G(string k, params object?[] a) => EmailResource.Get(k, c, a);
        return (G("RevertedSubject", f.EmpNameSnapshot, f.EvalYear),
         Wrap(AppName, G("RevertedTitle"),
              $"<p>{G("DearName", managerName)}</p><p>{G("RevertedIntro")}</p>" +
              InfoTable(c, (G("Reference"), f.LegacyRefNo), (G("Employee"), $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        (G("ReviewYear"), f.EvalYear.ToString()),
                        (G("CurrentStatus"), PmFormStatus.DisplayName(f.Status, c)),
                        (G("RequiredAction"), G("RevertedRequiredAction")),
                        (G("PreviousActionBy"), string.IsNullOrWhiteSpace(f.Hr1ReviewerName) ? G("HrTeamPlain") : $"{f.Hr1ReviewerName} ({G("HrTeamPlain")})"),
                        (G("NextActionRequiredBy"), G("RoleYouManager", managerName)),
                        (G("HrComments"), string.IsNullOrWhiteSpace(hrComments) ? G("NoCommentsProvided") : hrComments)) +
              ActionButton(actionUrl, G("RevisePerformanceForm")), sentAt, c));
    }

    /// <summary>Daily scheduled nudge for a form that has been sitting in the same actionable status too long.</summary>
    public static (string Subject, string Body) Reminder(
        PmForm f, string recipientLabel, string requiredAction, int daysWaiting, string actionUrl, DateTime sentAt, CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.CurrentUICulture;
        string G(string k, params object?[] a) => EmailResource.Get(k, c, a);
        return (G("ReminderSubject", f.EmpNameSnapshot, f.EvalYear),
         Wrap(AppName, G("ReminderTitle"),
              $"<p>{G("DearPlain", recipientLabel)}</p>" +
              $"<p>{G("ReminderIntro", daysWaiting)}</p>" +
              InfoTable(c, (G("Reference"), f.LegacyRefNo), (G("Employee"), $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        (G("ReviewYear"), f.EvalYear.ToString()),
                        (G("CurrentStatus"), PmFormStatus.DisplayName(f.Status, c)),
                        (G("RequiredAction"), requiredAction),
                        (G("DaysWaiting"), daysWaiting.ToString())) +
              ActionButton(actionUrl, G("ViewPerformanceForm")), sentAt, c));
    }

    /// <summary>One row in the weekly escalation digest — a single HR-facing summary email rather
    /// than one email per overdue form, so a large backlog can't turn into dozens of separate sends.</summary>
    public record EscalationRow(string LegacyRefNo, string EmpNameSnapshot, string EvalYear,
        string Status, string RequiredAction, string Owner, int DaysWaiting, string ActionUrl);

    /// <summary>Weekly digest for HR: every form that has been outstanding beyond the escalation
    /// threshold, one row each, in a single email rather than one send per form.
    /// Deliberately narrow (4 columns, fixed %-width, forced word-break) rather than the 7-column
    /// version this once was — a data-dense table with auto-sized columns and no wrap guarantee
    /// is exactly the kind of thing that overflows its container in Outlook's Word rendering
    /// engine (which doesn't support horizontal scrolling the way a browser does). Year and
    /// Reference fold into the Employee cell instead of getting their own columns.</summary>
    public static (string Subject, string Body) EscalationDigest(IReadOnlyList<EscalationRow> rows, DateTime sentAt, CultureInfo? culture = null)
    {
        // Broadcast to the whole HR team rather than one named recipient, so there's no single
        // PreferredCulture to honor — defaults to the ambient culture (English unless a caller
        // explicitly passes one), same as every other static default in this file.
        var c = culture ?? CultureInfo.CurrentUICulture;
        string G(string k, params object?[] a) => EmailResource.Get(k, c, a);

        // Explicit color on every cell — see InfoTable's remark on why nothing here can rely on
        // inherited text color. No hardcoded text-align on the header row — start-aligned by
        // default, which flips correctly for RTL along with the rest of the document.
        const string cell = "padding:8px;border-bottom:1px solid #e0e0e0;word-break:break-word;color:#333333;";
        var trs = string.Join("", rows.Select(r => $"""
            <tr>
              <td style='{cell}width:34%;'>
                <a href="{r.ActionUrl}" style="color:#2f5fd6;text-decoration:none;font-weight:600;">{r.EmpNameSnapshot}</a><br/>
                <span style="color:#8a93a6;font-size:11px;">{r.LegacyRefNo} &middot; {r.EvalYear}</span>
              </td>
              <td style='{cell}width:24%;'>{r.RequiredAction}</td>
              <td style='{cell}width:22%;'>{r.Owner}</td>
              <td style='{cell}width:20%;text-align:center;'>{G("EscalationDaysSuffix", r.DaysWaiting)}<br/><span style="color:#8a93a6;font-size:11px;">{r.Status}</span></td>
            </tr>
            """));
        const string th = "padding:8px;border-bottom:2px solid #ccc;color:#666666;";
        var table = $"""
            <table style='width:100%;table-layout:fixed;border-collapse:collapse;margin:15px 0;font-size:13px;'>
              <thead><tr>
                <th style='{th}width:34%;'>{G("Employee")}</th>
                <th style='{th}width:24%;'>{G("RequiredAction")}</th>
                <th style='{th}width:22%;'>{G("EscalationColAwaiting")}</th>
                <th style='{th}width:20%;text-align:center;'>{G("EscalationColWaitingStatus")}</th>
              </tr></thead>
              <tbody>{trs}</tbody>
            </table>
            """;
        return (G("EscalationSubject", rows.Count),
            Wrap(AppName, G("EscalationTitle"),
                 $"<p>{G("DearHrTeam")}</p><p>{G("EscalationIntro", rows.Count)}</p>" +
                 table, sentAt, c));
    }
}

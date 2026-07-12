using Aic.Pm.Core.Data;
using Aic.Pm.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Aic.Pm.Core.Services;

public record EmailSpec(
    string TemplateKey,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    string Subject,
    string Body,
    string? FormLegacyRefNo,
    string? IdempotencyKey);

/// <summary>
/// Centralized mail dispatch (handoff mail design): one caller, one send per transition,
/// deduplicated recipients, empty To ⇒ graceful skip (never an SMTP call), delivery log
/// with idempotency key. Development default is log-only (no SMTP configured).
/// </summary>
public class EmailService
{
    private readonly PmDbContext _db;
    private readonly IClock _clock;
    public EmailService(PmDbContext db, IClock clock) { _db = db; _clock = clock; }

    /// <summary>Dedupe (To wins over CC), skip empties, write the log row. Returns the log entry.</summary>
    public async Task<EmailLog> DispatchAsync(EmailSpec spec)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var to = spec.To.Select(a => a.Trim()).Where(a => a.Length > 0 && seen.Add(a)).ToList();
        var cc = spec.Cc.Select(a => a.Trim()).Where(a => a.Length > 0 && seen.Add(a)).ToList();

        var log = new EmailLog
        {
            CreatedAt = _clock.Now,
            TemplateKey = spec.TemplateKey,
            FormLegacyRefNo = spec.FormLegacyRefNo,
            ToRecipients = string.Join(";", to),
            CcRecipients = string.Join(";", cc),
            Subject = spec.Subject,
            Body = spec.Body,
            IdempotencyKey = spec.IdempotencyKey,
            Status = to.Count == 0 ? "SKIPPED_NO_RECIPIENT" : "LOGGED"
        };

        if (spec.IdempotencyKey is not null &&
            await _db.EmailLogs.AnyAsync(e => e.IdempotencyKey == spec.IdempotencyKey && e.Status != "FAILED"))
        {
            log.Status = "SKIPPED_DUPLICATE";
        }

        _db.EmailLogs.Add(log);
        await _db.SaveChangesAsync();
        return log;
    }
}

/// <summary>
/// Email bodies. Headings always use an explicit dark colour — white headings become
/// invisible in light-mode Outlook (legacy incident).
/// </summary>
public static class EmailTemplates
{
    public const string HeadingColor = "#1e293b";

    private static string Wrap(string heading, string inner) => $"""
        <html><body style="font-family:Arial,sans-serif;color:#333;">
        <div style="max-width:700px;margin:0 auto;padding:20px;">
        <h2 style="color:{HeadingColor};margin-top:0;">{heading}</h2>
        {inner}
        <hr style="border:none;border-top:1px solid #ddd;margin:30px 0;"/>
        <p style="color:#999;font-size:12px;"><i>*** This is an automated message. Please do not reply to this email. For assistance, contact the HR Department.</i></p>
        </div></body></html>
        """;

    private static string InfoTable(params (string Label, string Value)[] rows)
    {
        var trs = string.Join("", rows.Select(r =>
            $"<tr><td style='padding:8px;border-bottom:1px solid #e0e0e0;font-weight:bold;width:180px;color:#666;'>{r.Label}</td>" +
            $"<td style='padding:8px;border-bottom:1px solid #e0e0e0;'>{r.Value}</td></tr>"));
        return $"<table style='width:100%;border-collapse:collapse;margin:15px 0;'>{trs}</table>";
    }

    public static (string Subject, string Body) AcknowledgementRequest(PmForm f, string managerName) =>
        ($"ACTION REQUIRED: Review Your Performance Objectives | {f.EmpNameSnapshot} | {f.EvalYear}",
         Wrap("Performance Objectives Review Required",
              $"<p>Dear <strong>{f.EmpNameSnapshot}</strong>,</p>" +
              "<p><strong>Action required:</strong> your manager has set your performance objectives. Please review and acknowledge them.</p>" +
              InfoTable(("Reference", f.LegacyRefNo), ("Employee", $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        ("Manager", managerName), ("Year", f.EvalYear.ToString()),
                        ("KPIs Set", f.Kpis.Count.ToString()), ("Competencies", f.Competencies.Count.ToString())) +
              "<p><strong>Note:</strong> please acknowledge within 7 days.</p>"));

    public static (string Subject, string Body) EmployeeAcknowledged(PmForm f, string managerName) =>
        ($"Employee Acknowledged Objectives: {f.EmpNameSnapshot} | {f.EvalYear}",
         Wrap("Objectives Acknowledged",
              $"<p>Dear <strong>{managerName}</strong>,</p>" +
              $"<p><strong>Acknowledgement received:</strong> {f.EmpNameSnapshot} has acknowledged their objectives for {f.EvalYear}.</p>" +
              InfoTable(("Reference", f.LegacyRefNo), ("Employee", $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        ("Acknowledged On", f.EmpAckDate?.ToString("dd/MM/yyyy") ?? "")) +
              (string.IsNullOrWhiteSpace(f.EmpAckComments) ? "" :
               $"<p><strong>Employee comments:</strong> {f.EmpAckComments}</p>") +
              "<p>At year-end, add achievement scores and submit to HR.</p>"));

    public static (string Subject, string Body) SubmittedToHr(PmForm f, string managerName, string rating) =>
        ($"PM Form Ready for HR Review | {f.EmpNameSnapshot} | {f.EvalYear}",
         Wrap("Performance Management Form — Ready for HR Review",
              "<p>Dear HR Team,</p><p>A Performance Management form has been completed by the manager and is ready for your review:</p>" +
              InfoTable(("Reference", f.LegacyRefNo), ("Employee", $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        ("KPI Score", f.KpiScore.ToString("F2")), ("Competency Score", f.CompScore.ToString("F2")),
                        ("Overall Score", f.PerformanceScore.ToString("F2")), ("Rating", rating),
                        ("Reviewed by Manager", managerName))));

    public static (string Subject, string Body) Hr1Approved(PmForm f) =>
        ($"PM Form - Ready for Final HR Review (HR Rep 2) | {f.EmpNameSnapshot} | {f.EvalYear}",
         Wrap("First HR Review Complete — Ready for Final Review",
              "<p>Dear HR Team,</p>" +
              InfoTable(("Reference", f.LegacyRefNo), ("Employee", $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        ("First HR Reviewer", f.Hr1ReviewerName ?? "")) +
              "<p><strong>Awaiting final HR approval (HR Rep 2).</strong></p>"));

    public static (string Subject, string Body) FinalApproved(PmForm f, string rating) =>
        ($"PM Form - APPROVED (Final) | {f.EmpNameSnapshot} | {f.EvalYear}",
         Wrap("Performance Management Form — Final Approval",
              "<p>Dear Team,</p>" +
              InfoTable(("Reference", f.LegacyRefNo), ("Employee", $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        ("Reviewed By", f.Hr1ReviewerName ?? ""), ("Approved By", f.Hr2ReviewerName ?? ""),
                        ("Score", f.PerformanceScore.ToString("F2")), ("Rating", rating)) +
              "<p>The form is now locked and archived.</p>"));

    public static (string Subject, string Body) Reverted(PmForm f, string hrComments) =>
        ($"PM Form Requires Revision | {f.EmpNameSnapshot} | {f.EvalYear}",
         Wrap("PM Form Requires Revision",
              "<p>Dear Manager,</p><p>The Performance Management form below has been reviewed by HR and requires revisions:</p>" +
              InfoTable(("Reference", f.LegacyRefNo), ("Employee", $"{f.EmpNameSnapshot} ({f.EmpCode})"),
                        ("HR Comments", string.IsNullOrWhiteSpace(hrComments) ? "No specific comments provided." : hrComments)) +
              $"<p>The form has been returned to <strong>{PmFormStatus.DisplayName(PmFormStatus.EmployeeAcknowledged)}</strong>. Please make the changes and resubmit to HR.</p>"));
}

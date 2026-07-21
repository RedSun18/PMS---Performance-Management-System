using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using PerformanceManagement.Core.Resources;

namespace PerformanceManagement.Core.Services;

/// <summary>
/// Renders the report data models from <see cref="ReportDataService"/> to downloadable
/// PDF (HTML printed via headless Chromium — see <see cref="PdfRenderer"/>) and Excel
/// (ClosedXML) files. Pure rendering — no database access here, so the data-shaping and
/// export-formatting concerns stay independently testable/replaceable.
///
/// Every export is generated synchronously inside the request that asked for the download, so
/// <see cref="CultureInfo.CurrentUICulture"/> is already the viewer's own selected language (set by
/// RequestLocalizationMiddleware earlier in the pipeline) — labels are resolved via
/// <see cref="ReportResource"/> against that ambient culture, no explicit plumbing needed from the
/// calling page. "Performance Management System" is the product's brand name, kept as-is in both
/// languages for the same reason every workflow email leaves it untranslated.
/// </summary>
public static class ReportExportService
{
    private const string AppName = "Performance Management System";

    // ================================================================ PDF
    public static Task<byte[]> EmployeeReportToPdfAsync(EmployeePerformanceReport r)
    {
        var c = CultureInfo.CurrentUICulture;
        string G(string k) => ReportResource.Get(k, c);
        var h = new HtmlReportBuilder(c);
        h.AddBrandHeader(G("EmployeePerformanceReport"), r.GeneratedAt);

        h.AddKeyValueTable(G("EmployeeInformation"),
            (G("Employee"), $"{r.EmpName} ({r.EmpCode})"),
            (G("Department"), r.Department ?? "—"),
            (G("Designation"), r.Designation ?? "—"),
            (G("Grade"), r.Grade ?? "—"),
            (G("DirectManager"), r.ManagerName ?? "—"),
            (G("ReviewYear"), r.EvalYear.ToString()),
            (G("ReferenceNumber"), r.RefNo),
            (G("FinalStatus"), r.Status));

        h.AddKeyValueTable(G("OverallScoreHeading"),
            (G("KpiScore"), r.KpiScore.ToString("F2", c)),
            (G("CompetencyScore"), r.CompScore.ToString("F2", c)),
            (G("OverallScore"), r.OverallScore.ToString("F2", c)),
            (G("Rating"), r.Rating));

        if (r.Kpis.Count > 0)
        {
            h.AddDataTable(G("KpiBreakdown"),
                new[] { (G("Perspective"), false), (G("Kpi"), false), (G("Target"), true), (G("WeightPercent"), false),
                    (G("AchievementPercent"), false), (G("Weighted"), false), (G("Comments"), true) },
                r.Kpis.Select(k => new[] { k.Perspective, k.Name, k.Target ?? "", k.Weight.ToString(c),
                    k.Achievement.ToString(c), k.Weighted.ToString("F2", c), k.Comments ?? "" }));
        }

        if (r.Competencies.Count > 0)
        {
            h.AddDataTable(G("CompetencyBreakdown"),
                new[] { (G("Type"), false), (G("Competency"), false), (G("WeightPercent"), false),
                    (G("AchievementPercent"), false), (G("Weighted"), false), (G("Comments"), true) },
                r.Competencies.Select(comp => new[] { comp.Type == "T" ? G("Technical") : G("Behavioral"), comp.Name,
                    comp.Weight.ToString(c), comp.Achievement.ToString(c), comp.Weighted.ToString("F2", c), comp.Comments ?? "" }));
        }

        h.AddParagraphSection(G("SelfAssessment"), r.SelfAssessment);
        h.AddParagraphSection(G("DevelopmentPlan"), r.DevelopmentPlan);
        h.AddParagraphSection(G("EmployeeAcknowledgementComments"), r.EmployeeAckComments);

        h.AddKeyValueTable(G("ApprovalHistory"),
            (G("HrReviewer1"), r.Hr1ReviewerName ?? "—"),
            (G("HrReview1Date"), r.Hr1ReviewDate?.ToString("dd/MM/yyyy", c) ?? "—"),
            (G("HrReviewer1Remarks"), r.Hr1Remarks ?? "—"),
            (G("HrReviewer2Final"), r.Hr2ReviewerName ?? "—"),
            (G("HrReview2Date"), r.Hr2ReviewDate?.ToString("dd/MM/yyyy", c) ?? "—"),
            (G("HrReviewer2Remarks"), r.Hr2Remarks ?? "—"));

        if (r.History.Count > 0)
        {
            h.AddDataTable(G("WorkflowHistory"),
                new[] { (G("From"), false), (G("To"), false), (G("ChangedBy"), false), (G("ChangedAt"), false), (G("Note"), true) },
                r.History.Select(hi => new[] { hi.FromStatus is null ? "—" : Domain.PmFormStatus.DisplayName(hi.FromStatus, c),
                    Domain.PmFormStatus.DisplayName(hi.ToStatus, c), hi.ChangedBy, hi.ChangedAt.ToString("dd/MM/yyyy HH:mm", c), hi.Note ?? "" }));
        }

        return PdfRenderer.RenderAsync(h.Build());
    }

    public static Task<byte[]> DepartmentReportToPdfAsync(DepartmentSummaryReport r)
    {
        var c = CultureInfo.CurrentUICulture;
        string G(string k) => ReportResource.Get(k, c);
        var h = new HtmlReportBuilder(c);
        h.AddBrandHeader(G("DepartmentSummaryReport"), r.GeneratedAt);

        h.AddKeyValueTable(G("DepartmentInformation"),
            (G("Department"), $"{r.DeptName} ({r.DeptCode})"),
            (G("ReviewYear"), r.EvalYear.ToString()),
            (G("EmployeeCount"), r.TotalEmployees.ToString(c)),
            (G("FinalizedReviews"), r.FinalizedCount.ToString(c)),
            (G("AverageOverallScore"), r.AverageScore.ToString("F2", c)));

        h.AddEmployeeRowsTable(G("Employees"), r.Employees, c);
        return PdfRenderer.RenderAsync(h.Build());
    }

    public static Task<byte[]> ManagerReportToPdfAsync(ManagerSummaryReport r)
    {
        var c = CultureInfo.CurrentUICulture;
        string G(string k) => ReportResource.Get(k, c);
        var h = new HtmlReportBuilder(c);
        h.AddBrandHeader(G("ManagerSummaryReport"), r.GeneratedAt);

        h.AddKeyValueTable(G("ManagerInformation"),
            (G("Manager"), $"{r.ManagerName} ({r.ManagerEmpCode})"),
            (G("ReviewYear"), r.EvalYear.ToString()),
            (G("TeamSize"), r.TotalEmployees.ToString(c)),
            (G("FinalizedReviews"), r.FinalizedCount.ToString(c)),
            (G("AverageOverallScore"), r.AverageScore.ToString("F2", c)));

        h.AddEmployeeRowsTable(G("TeamMembers"), r.TeamMembers, c);
        return PdfRenderer.RenderAsync(h.Build());
    }

    public static Task<byte[]> OverallReportToPdfAsync(OverallOrganizationReport r)
    {
        var c = CultureInfo.CurrentUICulture;
        string G(string k) => ReportResource.Get(k, c);
        var h = new HtmlReportBuilder(c);
        h.AddBrandHeader(G("OverallOrganizationSummary"), r.GeneratedAt);

        h.AddKeyValueTable(G("OrganizationOverview"),
            (G("ReviewYear"), r.EvalYear.ToString()),
            (G("TotalEmployees"), r.TotalEmployees.ToString(c)),
            (G("FormsGenerated"), r.TotalForms.ToString(c)),
            (G("FinalizedReviews"), r.FinalizedCount.ToString(c)),
            (G("AverageOverallScore"), r.AverageScore.ToString("F2", c)));

        if (r.Departments.Count > 0)
        {
            h.AddDataTable(G("DepartmentBreakdown"),
                new[] { (G("Department"), false), (G("Employees"), false), (G("Finalized"), false),
                    (G("CompletionPercent"), false), (G("AverageScore"), false) },
                r.Departments.Select(d => new[] { d.DeptName, d.EmployeeCount.ToString(c), d.FinalizedCount.ToString(c),
                    $"{d.CompletionPercent}%", d.AverageScore.ToString("F2", c) }));
        }

        return PdfRenderer.RenderAsync(h.Build());
    }

    // ================================================================ Excel
    public static byte[] EmployeeReportToExcel(EmployeePerformanceReport r)
    {
        var c = CultureInfo.CurrentUICulture;
        string G(string k) => ReportResource.Get(k, c);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(G("EmployeePerformanceReport"));
        SetSheetDirection(ws, c);
        var row = WriteBrandHeader(ws, G("EmployeePerformanceReport"), r.GeneratedAt, c);

        row = WriteKeyValueBlock(ws, row, G("EmployeeInformation"),
            (G("Employee"), $"{r.EmpName} ({r.EmpCode})"), (G("Department"), r.Department ?? "—"),
            (G("Designation"), r.Designation ?? "—"), (G("Grade"), r.Grade ?? "—"),
            (G("DirectManager"), r.ManagerName ?? "—"), (G("ReviewYear"), r.EvalYear.ToString()),
            (G("ReferenceNumber"), r.RefNo), (G("FinalStatus"), r.Status));

        row = WriteKeyValueBlock(ws, row, G("OverallScoreHeading"),
            (G("KpiScore"), r.KpiScore.ToString("F2", c)), (G("CompetencyScore"), r.CompScore.ToString("F2", c)),
            (G("OverallScore"), r.OverallScore.ToString("F2", c)), (G("Rating"), r.Rating));

        if (r.Kpis.Count > 0)
        {
            row = WriteDataTable(ws, row, G("KpiBreakdown"),
                new[] { G("Perspective"), G("Kpi"), G("Target"), G("WeightPercent"), G("AchievementPercent"), G("Weighted"), G("Comments") },
                r.Kpis.Select(k => new object?[] { k.Perspective, k.Name, k.Target, k.Weight, k.Achievement, k.Weighted, k.Comments }));
        }

        if (r.Competencies.Count > 0)
        {
            row = WriteDataTable(ws, row, G("CompetencyBreakdown"),
                new[] { G("Type"), G("Competency"), G("WeightPercent"), G("AchievementPercent"), G("Weighted"), G("Comments") },
                r.Competencies.Select(comp => new object?[] { comp.Type == "T" ? G("Technical") : G("Behavioral"), comp.Name, comp.Weight, comp.Achievement, comp.Weighted, comp.Comments }));
        }

        row = WriteKeyValueBlock(ws, row, G("CommentsAndDevelopment"),
            (G("SelfAssessment"), r.SelfAssessment ?? "—"), (G("DevelopmentPlan"), r.DevelopmentPlan ?? "—"),
            (G("EmployeeAcknowledgementComments"), r.EmployeeAckComments ?? "—"));

        row = WriteKeyValueBlock(ws, row, G("ApprovalHistory"),
            (G("HrReviewer1"), r.Hr1ReviewerName ?? "—"), (G("HrReview1Date"), r.Hr1ReviewDate?.ToString("dd/MM/yyyy", c) ?? "—"),
            (G("HrReviewer1Remarks"), r.Hr1Remarks ?? "—"), (G("HrReviewer2Final"), r.Hr2ReviewerName ?? "—"),
            (G("HrReview2Date"), r.Hr2ReviewDate?.ToString("dd/MM/yyyy", c) ?? "—"), (G("HrReviewer2Remarks"), r.Hr2Remarks ?? "—"));

        if (r.History.Count > 0)
        {
            WriteDataTable(ws, row, G("WorkflowHistory"),
                new[] { G("From"), G("To"), G("ChangedBy"), G("ChangedAt"), G("Note") },
                r.History.Select(h => new object?[] { h.FromStatus is null ? "—" : Domain.PmFormStatus.DisplayName(h.FromStatus, c),
                    Domain.PmFormStatus.DisplayName(h.ToStatus, c), h.ChangedBy, h.ChangedAt.ToString("dd/MM/yyyy HH:mm", c), h.Note }));
        }

        ws.Columns().AdjustToContents();
        return ToBytes(wb);
    }

    public static byte[] DepartmentReportToExcel(DepartmentSummaryReport r)
    {
        var c = CultureInfo.CurrentUICulture;
        string G(string k) => ReportResource.Get(k, c);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(G("DepartmentSummaryReport"));
        SetSheetDirection(ws, c);
        var row = WriteBrandHeader(ws, G("DepartmentSummaryReport"), r.GeneratedAt, c);
        row = WriteKeyValueBlock(ws, row, G("DepartmentInformation"),
            (G("Department"), $"{r.DeptName} ({r.DeptCode})"), (G("ReviewYear"), r.EvalYear.ToString()),
            (G("EmployeeCount"), r.TotalEmployees.ToString(c)), (G("FinalizedReviews"), r.FinalizedCount.ToString(c)),
            (G("AverageOverallScore"), r.AverageScore.ToString("F2", c)));
        WriteEmployeeRowsTable(ws, row, G("Employees"), r.Employees, c);
        ws.Columns().AdjustToContents();
        return ToBytes(wb);
    }

    public static byte[] ManagerReportToExcel(ManagerSummaryReport r)
    {
        var c = CultureInfo.CurrentUICulture;
        string G(string k) => ReportResource.Get(k, c);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(G("ManagerSummaryReport"));
        SetSheetDirection(ws, c);
        var row = WriteBrandHeader(ws, G("ManagerSummaryReport"), r.GeneratedAt, c);
        row = WriteKeyValueBlock(ws, row, G("ManagerInformation"),
            (G("Manager"), $"{r.ManagerName} ({r.ManagerEmpCode})"), (G("ReviewYear"), r.EvalYear.ToString()),
            (G("TeamSize"), r.TotalEmployees.ToString(c)), (G("FinalizedReviews"), r.FinalizedCount.ToString(c)),
            (G("AverageOverallScore"), r.AverageScore.ToString("F2", c)));
        WriteEmployeeRowsTable(ws, row, G("TeamMembers"), r.TeamMembers, c);
        ws.Columns().AdjustToContents();
        return ToBytes(wb);
    }

    public static byte[] OverallReportToExcel(OverallOrganizationReport r)
    {
        var c = CultureInfo.CurrentUICulture;
        string G(string k) => ReportResource.Get(k, c);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(G("OverallOrganizationSummary"));
        SetSheetDirection(ws, c);
        var row = WriteBrandHeader(ws, G("OverallOrganizationSummary"), r.GeneratedAt, c);
        row = WriteKeyValueBlock(ws, row, G("OrganizationOverview"),
            (G("ReviewYear"), r.EvalYear.ToString()), (G("TotalEmployees"), r.TotalEmployees.ToString(c)),
            (G("FormsGenerated"), r.TotalForms.ToString(c)), (G("FinalizedReviews"), r.FinalizedCount.ToString(c)),
            (G("AverageOverallScore"), r.AverageScore.ToString("F2", c)));

        if (r.Departments.Count > 0)
        {
            WriteDataTable(ws, row, G("DepartmentBreakdown"),
                new[] { G("Department"), G("Employees"), G("Finalized"), G("CompletionPercent"), G("AverageScore") },
                r.Departments.Select(d => new object?[] { d.DeptName, d.EmployeeCount, d.FinalizedCount, $"{d.CompletionPercent}%", d.AverageScore }));
        }

        ws.Columns().AdjustToContents();
        return ToBytes(wb);
    }

    // ================================================================ ClosedXML helpers
    /// <summary>Arabic reads right-to-left, so the sheet's own column order and text direction flip
    /// too — otherwise every worksheet would look transposed against its own content in Excel.</summary>
    private static void SetSheetDirection(IXLWorksheet ws, CultureInfo c) =>
        ws.RightToLeft = c.TwoLetterISOLanguageName == "ar";

    private static int WriteBrandHeader(IXLWorksheet ws, string title, DateTime generatedAt, CultureInfo c)
    {
        ws.Cell(1, 1).Value = AppName;
        ws.Cell(1, 1).Style.Font.FontSize = 10;
        ws.Cell(2, 1).Value = title;
        ws.Cell(2, 1).Style.Font.FontSize = 16;
        ws.Cell(2, 1).Style.Font.Bold = true;
        ws.Cell(3, 1).Value = $"{ReportResource.Get("Generated", c)} {generatedAt.ToString("dd MMM yyyy HH:mm", c)}";
        ws.Cell(3, 1).Style.Font.FontSize = 8;
        ws.Cell(3, 1).Style.Font.FontColor = XLColor.Gray;
        return 5;
    }

    private static int WriteKeyValueBlock(IXLWorksheet ws, int startRow, string heading, params (string Label, string Value)[] rows)
    {
        ws.Cell(startRow, 1).Value = heading;
        ws.Cell(startRow, 1).Style.Font.Bold = true;
        ws.Cell(startRow, 1).Style.Font.FontColor = XLColor.FromArgb(15, 43, 92);
        var row = startRow + 1;
        foreach (var (label, value) in rows)
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 2).Value = value;
            row++;
        }
        return row + 1;
    }

    private static int WriteDataTable(IXLWorksheet ws, int startRow, string heading, string[] headers, IEnumerable<object?[]> rows)
    {
        ws.Cell(startRow, 1).Value = heading;
        ws.Cell(startRow, 1).Style.Font.Bold = true;
        ws.Cell(startRow, 1).Style.Font.FontColor = XLColor.FromArgb(15, 43, 92);
        var headerRow = startRow + 1;
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(headerRow, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(15, 43, 92);
        }

        var row = headerRow + 1;
        foreach (var cells in rows)
        {
            for (var i = 0; i < cells.Length; i++)
            {
                var value = cells[i];
                var cell = ws.Cell(row, i + 1);
                switch (value)
                {
                    case null: break;
                    case int iv: cell.Value = iv; break;
                    case decimal dv: cell.Value = dv; break;
                    default: cell.Value = value.ToString(); break;
                }
            }
            row++;
        }
        return row + 1;
    }

    private static void WriteEmployeeRowsTable(IXLWorksheet ws, int startRow, string heading, IReadOnlyList<ReportEmployeeRow> rows, CultureInfo c)
    {
        WriteDataTable(ws, startRow, heading,
            new[] { ReportResource.Get("EmployeeCode", c), ReportResource.Get("Name", c), ReportResource.Get("Designation", c),
                ReportResource.Get("OverallScore", c), ReportResource.Get("Rating", c), ReportResource.Get("Status", c) },
            rows.Select(r => new object?[] { r.EmpCode, r.Name, r.Designation, r.OverallScore, r.Rating, r.Status }));
    }

    private static byte[] ToBytes(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ================================================================ HTML report builder
    /// <summary>Builds one report's HTML in the same section-by-section shape the old MigraDoc
    /// helpers used (brand header, key-value table, data table, paragraph, employee rows) so the
    /// visual structure carries over unchanged — only the rendering engine underneath it does not
    /// (see <see cref="PdfRenderer"/> for why). Everything dynamic is HTML-encoded since these
    /// values come from free-text database fields.</summary>
    private sealed class HtmlReportBuilder
    {
        private readonly CultureInfo _culture;
        private readonly bool _isRtl;
        private readonly StringBuilder _body = new();

        public HtmlReportBuilder(CultureInfo culture)
        {
            _culture = culture;
            _isRtl = culture.TwoLetterISOLanguageName == "ar";
        }

        public void AddBrandHeader(string title, DateTime generatedAt)
        {
            _body.Append($"""
                <div class="brand">{Enc(AppName)}</div>
                <div class="report-title">{Enc(title)}</div>
                <div class="generated">{Enc(ReportResource.Get("Generated", _culture))} {Enc(generatedAt.ToString("dddd, dd MMMM yyyy", _culture))} {Enc(generatedAt.ToString("HH:mm", _culture))}</div>
                <hr class="rule" />
                """);
        }

        public void AddKeyValueTable(string heading, params (string Label, string Value)[] rows)
        {
            _body.Append($"<div class=\"section-heading\">{Enc(heading)}</div><table class=\"kv\">");
            foreach (var (label, value) in rows)
                _body.Append($"<tr><td class=\"kv-label\">{Enc(label)}</td><td>{Enc(value)}</td></tr>");
            _body.Append("</table>");
        }

        /// <summary>Column headers paired with whether that column carries long free text (comments/
        /// notes/remarks/targets) and so needs roughly double the width of a plain data column —
        /// left at equal widths, a comments column is exactly as cramped as a two-digit "Weight %"
        /// column, illegible the moment anyone types a few words. Driven by an explicit flag per
        /// column (rather than matching against the English header text) so it still works once
        /// headers are localized to Arabic.</summary>
        public void AddDataTable(string heading, (string Header, bool Wide)[] columns, IEnumerable<string[]> rows)
        {
            _body.Append($"<div class=\"section-heading\">{Enc(heading)}</div>");

            var weights = columns.Select(col => col.Wide ? 2.0 : 1.0).ToArray();
            var totalWeight = weights.Sum();

            _body.Append("<table class=\"data\"><colgroup>");
            foreach (var w in weights) _body.Append($"<col style=\"width:{w / totalWeight * 100:0.00}%\" />");
            _body.Append("</colgroup><thead><tr>");
            foreach (var col in columns) _body.Append($"<th>{Enc(col.Header)}</th>");
            _body.Append("</tr></thead><tbody>");
            foreach (var cells in rows)
            {
                _body.Append("<tr>");
                for (var i = 0; i < columns.Length; i++)
                    _body.Append($"<td>{Enc(i < cells.Length ? cells[i] : "")}</td>");
                _body.Append("</tr>");
            }
            _body.Append("</tbody></table>");
        }

        public void AddParagraphSection(string heading, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            _body.Append($"<div class=\"section-heading\">{Enc(heading)}</div><p class=\"para\">{Enc(text)}</p>");
        }

        public void AddEmployeeRowsTable(string heading, IReadOnlyList<ReportEmployeeRow> rows, CultureInfo c)
        {
            if (rows.Count == 0)
            {
                AddParagraphSection(heading, ReportResource.Get("NoEmployeesFound", c));
                return;
            }
            AddDataTable(heading,
                new[] { (ReportResource.Get("EmployeeCode", c), false), (ReportResource.Get("Name", c), false),
                    (ReportResource.Get("Designation", c), false), (ReportResource.Get("OverallScore", c), false),
                    (ReportResource.Get("Rating", c), false), (ReportResource.Get("Status", c), false) },
                rows.Select(r => new[] { r.EmpCode, r.Name, r.Designation ?? "", r.OverallScore.ToString("F2", c), r.Rating, r.Status }));
        }

        public string Build()
        {
            var latinFont = new Uri(Path.Combine(PdfRenderer.FontsDirectory, "NotoSans-latin.woff2")).AbsoluteUri;
            var arabicFont = new Uri(Path.Combine(PdfRenderer.FontsDirectory, "NotoSansArabic-arabic.woff2")).AbsoluteUri;

            // The CSS block is a plain (non-interpolated) string so its literal '{'/'}' don't
            // collide with C# interpolated raw-string escaping rules; the two font URLs are
            // substituted afterward via string.Format placeholders instead.
            var style = string.Format(StyleTemplate, latinFont, arabicFont);
            var dir = _isRtl ? "rtl" : "ltr";

            return $"""
                <!doctype html>
                <html dir="{dir}" lang="{_culture.TwoLetterISOLanguageName}">
                <head>
                <meta charset="utf-8" />
                <style>
                {style}
                </style>
                </head>
                <body dir="{dir}">
                {_body}
                </body>
                </html>
                """;
        }

        private const string StyleTemplate = """
            @font-face {{ font-family: 'Noto Sans'; font-weight: 400 700; src: url('{0}') format('woff2'); }}
            @font-face {{ font-family: 'Noto Sans Arabic'; font-weight: 400 700; src: url('{1}') format('woff2'); }}
            * {{ box-sizing: border-box; }}
            body {{
              font-family: 'Noto Sans', 'Noto Sans Arabic', sans-serif;
              font-size: 9pt; color: #1b2333; margin: 0;
            }}
            .brand {{ font-size: 10pt; color: #5a6474; margin-bottom: 2pt; }}
            .report-title {{ font-size: 18pt; font-weight: 700; color: #0f2b5c; margin-bottom: 4pt; }}
            .generated {{ font-size: 8pt; color: #5a6474; margin-bottom: 14pt; }}
            .rule {{ border: none; border-top: 1pt solid #0f2b5c; margin-bottom: 10pt; }}
            .section-heading {{ font-size: 12pt; font-weight: 700; color: #0f2b5c; margin: 10pt 0 6pt; page-break-after: avoid; }}
            .para {{ font-size: 9pt; margin: 0 0 8pt; white-space: pre-wrap; }}
            table {{ width: 100%; border-collapse: collapse; margin-bottom: 4pt; }}
            table.kv td {{ border: 0.4pt solid #dce0e6; padding: 4pt 6pt; font-size: 9pt; }}
            table.kv .kv-label {{ font-weight: 700; background: #f0f2f6; width: 26%; }}
            table.data {{ font-size: 8pt; }}
            table.data thead {{ display: table-header-group; }}
            table.data th {{
              background: #0f2b5c; color: #ffffff; font-weight: 700; font-size: 8pt;
              padding: 5pt 6pt; text-align: start; border: 0.4pt solid #0f2b5c;
            }}
            table.data td {{ padding: 5pt 6pt; border: 0.4pt solid #dce0e6; word-break: break-word; }}
            table.data tr {{ page-break-inside: avoid; }}
            """;

        private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
    }
}

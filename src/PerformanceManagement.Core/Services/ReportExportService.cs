using System.Net;
using System.Text;
using ClosedXML.Excel;

namespace PerformanceManagement.Core.Services;

/// <summary>
/// Renders the report data models from <see cref="ReportDataService"/> to downloadable
/// PDF (HTML printed via headless Chromium — see <see cref="PdfRenderer"/>) and Excel
/// (ClosedXML) files. Pure rendering — no database access here, so the data-shaping and
/// export-formatting concerns stay independently testable/replaceable.
/// </summary>
public static class ReportExportService
{
    private const string AppName = "Performance Management System";

    // ================================================================ PDF
    public static Task<byte[]> EmployeeReportToPdfAsync(EmployeePerformanceReport r)
    {
        var h = new HtmlReportBuilder($"Employee Performance Report — {r.EmpName}");
        h.AddBrandHeader("Employee Performance Report", r.GeneratedAt);

        h.AddKeyValueTable("Employee Information",
            ("Employee", $"{r.EmpName} ({r.EmpCode})"),
            ("Department", r.Department ?? "—"),
            ("Designation", r.Designation ?? "—"),
            ("Grade", r.Grade ?? "—"),
            ("Direct Manager", r.ManagerName ?? "—"),
            ("Review Year", r.EvalYear.ToString()),
            ("Reference Number", r.RefNo),
            ("Final Status", r.Status));

        h.AddKeyValueTable("Overall Score",
            ("KPI Score", r.KpiScore.ToString("F2")),
            ("Competency Score", r.CompScore.ToString("F2")),
            ("Overall Score", r.OverallScore.ToString("F2")),
            ("Rating", r.Rating));

        if (r.Kpis.Count > 0)
        {
            h.AddDataTable("KPI Breakdown",
                new[] { "Perspective", "KPI", "Target", "Weight %", "Achievement %", "Weighted", "Comments" },
                r.Kpis.Select(k => new[] { k.Perspective, k.Name, k.Target ?? "", k.Weight.ToString(),
                    k.Achievement.ToString(), k.Weighted.ToString("F2"), k.Comments ?? "" }));
        }

        if (r.Competencies.Count > 0)
        {
            h.AddDataTable("Competency Breakdown",
                new[] { "Type", "Competency", "Weight %", "Achievement %", "Weighted", "Comments" },
                r.Competencies.Select(c => new[] { c.Type == "T" ? "Technical" : "Behavioral", c.Name,
                    c.Weight.ToString(), c.Achievement.ToString(), c.Weighted.ToString("F2"), c.Comments ?? "" }));
        }

        h.AddParagraphSection("Self-Assessment", r.SelfAssessment);
        h.AddParagraphSection("Development Plan", r.DevelopmentPlan);
        h.AddParagraphSection("Employee Acknowledgement Comments", r.EmployeeAckComments);

        h.AddKeyValueTable("Approval History",
            ("HR Reviewer 1", r.Hr1ReviewerName ?? "—"),
            ("HR Review 1 Date", r.Hr1ReviewDate?.ToString("dd/MM/yyyy") ?? "—"),
            ("HR Reviewer 1 Remarks", r.Hr1Remarks ?? "—"),
            ("HR Reviewer 2 (Final)", r.Hr2ReviewerName ?? "—"),
            ("HR Review 2 Date", r.Hr2ReviewDate?.ToString("dd/MM/yyyy") ?? "—"),
            ("HR Reviewer 2 Remarks", r.Hr2Remarks ?? "—"));

        if (r.History.Count > 0)
        {
            h.AddDataTable("Workflow History",
                new[] { "From", "To", "Changed By", "Changed At", "Note" },
                r.History.Select(hi => new[] { hi.FromStatus is null ? "—" : Domain.PmFormStatus.DisplayName(hi.FromStatus),
                    Domain.PmFormStatus.DisplayName(hi.ToStatus), hi.ChangedBy, hi.ChangedAt.ToString("dd/MM/yyyy HH:mm"), hi.Note ?? "" }));
        }

        return PdfRenderer.RenderAsync(h.Build());
    }

    public static Task<byte[]> DepartmentReportToPdfAsync(DepartmentSummaryReport r)
    {
        var h = new HtmlReportBuilder($"Department Summary — {r.DeptName}");
        h.AddBrandHeader("Department Summary Report", r.GeneratedAt);

        h.AddKeyValueTable("Department Information",
            ("Department", $"{r.DeptName} ({r.DeptCode})"),
            ("Review Year", r.EvalYear.ToString()),
            ("Employee Count", r.TotalEmployees.ToString()),
            ("Finalized Reviews", r.FinalizedCount.ToString()),
            ("Average Overall Score", r.AverageScore.ToString("F2")));

        h.AddEmployeeRowsTable("Employees", r.Employees);
        return PdfRenderer.RenderAsync(h.Build());
    }

    public static Task<byte[]> ManagerReportToPdfAsync(ManagerSummaryReport r)
    {
        var h = new HtmlReportBuilder($"Manager Summary — {r.ManagerName}");
        h.AddBrandHeader("Manager Summary Report", r.GeneratedAt);

        h.AddKeyValueTable("Manager Information",
            ("Manager", $"{r.ManagerName} ({r.ManagerEmpCode})"),
            ("Review Year", r.EvalYear.ToString()),
            ("Team Size", r.TotalEmployees.ToString()),
            ("Finalized Reviews", r.FinalizedCount.ToString()),
            ("Average Overall Score", r.AverageScore.ToString("F2")));

        h.AddEmployeeRowsTable("Team Members", r.TeamMembers);
        return PdfRenderer.RenderAsync(h.Build());
    }

    public static Task<byte[]> OverallReportToPdfAsync(OverallOrganizationReport r)
    {
        var h = new HtmlReportBuilder($"Overall Organization Summary — {r.EvalYear}");
        h.AddBrandHeader("Overall Organization Summary", r.GeneratedAt);

        h.AddKeyValueTable("Organization Overview",
            ("Review Year", r.EvalYear.ToString()),
            ("Total Employees", r.TotalEmployees.ToString()),
            ("Forms Generated", r.TotalForms.ToString()),
            ("Finalized Reviews", r.FinalizedCount.ToString()),
            ("Average Overall Score", r.AverageScore.ToString("F2")));

        if (r.Departments.Count > 0)
        {
            h.AddDataTable("Department Breakdown",
                new[] { "Department", "Employees", "Finalized", "Completion %", "Average Score" },
                r.Departments.Select(d => new[] { d.DeptName, d.EmployeeCount.ToString(), d.FinalizedCount.ToString(),
                    $"{d.CompletionPercent}%", d.AverageScore.ToString("F2") }));
        }

        return PdfRenderer.RenderAsync(h.Build());
    }

    // ================================================================ Excel
    public static byte[] EmployeeReportToExcel(EmployeePerformanceReport r)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Employee Report");
        var row = WriteBrandHeader(ws, "Employee Performance Report", r.GeneratedAt);

        row = WriteKeyValueBlock(ws, row, "Employee Information",
            ("Employee", $"{r.EmpName} ({r.EmpCode})"), ("Department", r.Department ?? "—"),
            ("Designation", r.Designation ?? "—"), ("Grade", r.Grade ?? "—"),
            ("Direct Manager", r.ManagerName ?? "—"), ("Review Year", r.EvalYear.ToString()),
            ("Reference Number", r.RefNo), ("Final Status", r.Status));

        row = WriteKeyValueBlock(ws, row, "Overall Score",
            ("KPI Score", r.KpiScore.ToString("F2")), ("Competency Score", r.CompScore.ToString("F2")),
            ("Overall Score", r.OverallScore.ToString("F2")), ("Rating", r.Rating));

        if (r.Kpis.Count > 0)
        {
            row = WriteDataTable(ws, row, "KPI Breakdown",
                new[] { "Perspective", "KPI", "Target", "Weight %", "Achievement %", "Weighted", "Comments" },
                r.Kpis.Select(k => new object?[] { k.Perspective, k.Name, k.Target, k.Weight, k.Achievement, k.Weighted, k.Comments }));
        }

        if (r.Competencies.Count > 0)
        {
            row = WriteDataTable(ws, row, "Competency Breakdown",
                new[] { "Type", "Competency", "Weight %", "Achievement %", "Weighted", "Comments" },
                r.Competencies.Select(c => new object?[] { c.Type == "T" ? "Technical" : "Behavioral", c.Name, c.Weight, c.Achievement, c.Weighted, c.Comments }));
        }

        row = WriteKeyValueBlock(ws, row, "Comments & Development",
            ("Self-Assessment", r.SelfAssessment ?? "—"), ("Development Plan", r.DevelopmentPlan ?? "—"),
            ("Employee Acknowledgement Comments", r.EmployeeAckComments ?? "—"));

        row = WriteKeyValueBlock(ws, row, "Approval History",
            ("HR Reviewer 1", r.Hr1ReviewerName ?? "—"), ("HR Review 1 Date", r.Hr1ReviewDate?.ToString("dd/MM/yyyy") ?? "—"),
            ("HR Reviewer 1 Remarks", r.Hr1Remarks ?? "—"), ("HR Reviewer 2 (Final)", r.Hr2ReviewerName ?? "—"),
            ("HR Review 2 Date", r.Hr2ReviewDate?.ToString("dd/MM/yyyy") ?? "—"), ("HR Reviewer 2 Remarks", r.Hr2Remarks ?? "—"));

        if (r.History.Count > 0)
        {
            WriteDataTable(ws, row, "Workflow History",
                new[] { "From", "To", "Changed By", "Changed At", "Note" },
                r.History.Select(h => new object?[] { h.FromStatus is null ? "—" : Domain.PmFormStatus.DisplayName(h.FromStatus),
                    Domain.PmFormStatus.DisplayName(h.ToStatus), h.ChangedBy, h.ChangedAt.ToString("dd/MM/yyyy HH:mm"), h.Note }));
        }

        ws.Columns().AdjustToContents();
        return ToBytes(wb);
    }

    public static byte[] DepartmentReportToExcel(DepartmentSummaryReport r)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Department Summary");
        var row = WriteBrandHeader(ws, "Department Summary Report", r.GeneratedAt);
        row = WriteKeyValueBlock(ws, row, "Department Information",
            ("Department", $"{r.DeptName} ({r.DeptCode})"), ("Review Year", r.EvalYear.ToString()),
            ("Employee Count", r.TotalEmployees.ToString()), ("Finalized Reviews", r.FinalizedCount.ToString()),
            ("Average Overall Score", r.AverageScore.ToString("F2")));
        WriteEmployeeRowsTable(ws, row, "Employees", r.Employees);
        ws.Columns().AdjustToContents();
        return ToBytes(wb);
    }

    public static byte[] ManagerReportToExcel(ManagerSummaryReport r)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Manager Summary");
        var row = WriteBrandHeader(ws, "Manager Summary Report", r.GeneratedAt);
        row = WriteKeyValueBlock(ws, row, "Manager Information",
            ("Manager", $"{r.ManagerName} ({r.ManagerEmpCode})"), ("Review Year", r.EvalYear.ToString()),
            ("Team Size", r.TotalEmployees.ToString()), ("Finalized Reviews", r.FinalizedCount.ToString()),
            ("Average Overall Score", r.AverageScore.ToString("F2")));
        WriteEmployeeRowsTable(ws, row, "Team Members", r.TeamMembers);
        ws.Columns().AdjustToContents();
        return ToBytes(wb);
    }

    public static byte[] OverallReportToExcel(OverallOrganizationReport r)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Overall Summary");
        var row = WriteBrandHeader(ws, "Overall Organization Summary", r.GeneratedAt);
        row = WriteKeyValueBlock(ws, row, "Organization Overview",
            ("Review Year", r.EvalYear.ToString()), ("Total Employees", r.TotalEmployees.ToString()),
            ("Forms Generated", r.TotalForms.ToString()), ("Finalized Reviews", r.FinalizedCount.ToString()),
            ("Average Overall Score", r.AverageScore.ToString("F2")));

        if (r.Departments.Count > 0)
        {
            WriteDataTable(ws, row, "Department Breakdown",
                new[] { "Department", "Employees", "Finalized", "Completion %", "Average Score" },
                r.Departments.Select(d => new object?[] { d.DeptName, d.EmployeeCount, d.FinalizedCount, $"{d.CompletionPercent}%", d.AverageScore }));
        }

        ws.Columns().AdjustToContents();
        return ToBytes(wb);
    }

    // ================================================================ ClosedXML helpers
    private static int WriteBrandHeader(IXLWorksheet ws, string title, DateTime generatedAt)
    {
        ws.Cell(1, 1).Value = AppName;
        ws.Cell(1, 1).Style.Font.FontSize = 10;
        ws.Cell(2, 1).Value = title;
        ws.Cell(2, 1).Style.Font.FontSize = 16;
        ws.Cell(2, 1).Style.Font.Bold = true;
        ws.Cell(3, 1).Value = $"Generated {generatedAt:dd MMM yyyy HH:mm}";
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

    private static void WriteEmployeeRowsTable(IXLWorksheet ws, int startRow, string heading, IReadOnlyList<ReportEmployeeRow> rows)
    {
        WriteDataTable(ws, startRow, heading,
            new[] { "Employee Code", "Name", "Designation", "Overall Score", "Rating", "Status" },
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
        private static readonly string[] LongTextHeaders = { "Comments", "Note", "Remarks", "Target" };
        private readonly StringBuilder _body = new();

        public HtmlReportBuilder(string title)
        {
            _ = title; // reserved for a future <title> if reports gain a print-preview route
        }

        public void AddBrandHeader(string title, DateTime generatedAt)
        {
            _body.Append($"""
                <div class="brand">{Enc(AppName)}</div>
                <div class="report-title">{Enc(title)}</div>
                <div class="generated">Generated {generatedAt:dddd, dd MMMM yyyy} at {generatedAt:HH:mm}</div>
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

        public void AddDataTable(string heading, string[] headers, IEnumerable<string[]> rows)
        {
            _body.Append($"<div class=\"section-heading\">{Enc(heading)}</div>");

            // Long-text columns (Comments/Note/Remarks/Target) get roughly double the width of a
            // plain data column — left at equal widths, a "Comments" column is exactly as cramped
            // as a two-digit "Weight %" column, illegible the moment anyone types a few words.
            var weights = headers.Select(hd => LongTextHeaders.Contains(hd) ? 2.0 : 1.0).ToArray();
            var totalWeight = weights.Sum();

            _body.Append("<table class=\"data\"><colgroup>");
            foreach (var w in weights) _body.Append($"<col style=\"width:{w / totalWeight * 100:0.00}%\" />");
            _body.Append("</colgroup><thead><tr>");
            foreach (var hd in headers) _body.Append($"<th>{Enc(hd)}</th>");
            _body.Append("</tr></thead><tbody>");
            foreach (var cells in rows)
            {
                _body.Append("<tr>");
                for (var i = 0; i < headers.Length; i++)
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

        public void AddEmployeeRowsTable(string heading, IReadOnlyList<ReportEmployeeRow> rows)
        {
            if (rows.Count == 0)
            {
                AddParagraphSection(heading, "No employees found.");
                return;
            }
            AddDataTable(heading,
                new[] { "Employee Code", "Name", "Designation", "Overall Score", "Rating", "Status" },
                rows.Select(r => new[] { r.EmpCode, r.Name, r.Designation ?? "", r.OverallScore.ToString("F2"), r.Rating, r.Status }));
        }

        public string Build()
        {
            var latinFont = new Uri(Path.Combine(PdfRenderer.FontsDirectory, "NotoSans-latin.woff2")).AbsoluteUri;
            var arabicFont = new Uri(Path.Combine(PdfRenderer.FontsDirectory, "NotoSansArabic-arabic.woff2")).AbsoluteUri;

            // The CSS block is a plain (non-interpolated) string so its literal '{'/'}' don't
            // collide with C# interpolated raw-string escaping rules; the two font URLs are
            // substituted afterward via string.Format placeholders instead.
            var style = string.Format(StyleTemplate, latinFont, arabicFont);

            return $"""
                <!doctype html>
                <html lang="en">
                <head>
                <meta charset="utf-8" />
                <style>
                {style}
                </style>
                </head>
                <body>
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

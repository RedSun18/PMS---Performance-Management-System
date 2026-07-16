using ClosedXML.Excel;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;

namespace PerformanceManagement.Core.Services;

/// <summary>
/// Renders the report data models from <see cref="ReportDataService"/> to downloadable
/// PDF (MigraDoc/PdfSharp) and Excel (ClosedXML) files. Pure rendering — no database
/// access here, so the data-shaping and export-formatting concerns stay independently
/// testable/replaceable.
/// </summary>
public static class ReportExportService
{
    private const string AppName = "Performance Management System";
    private static readonly Color BrandNavy = new(15, 43, 92);
    private static readonly Color BrandGrey = new(90, 100, 116);

    // ================================================================ PDF
    public static byte[] EmployeeReportToPdf(EmployeePerformanceReport r)
    {
        var doc = NewDocument($"Employee Performance Report — {r.EmpName}");
        var section = doc.Sections[0];
        AddBrandHeader(section, "Employee Performance Report", r.GeneratedAt);

        AddKeyValueTable(section, "Employee Information",
            ("Employee", $"{r.EmpName} ({r.EmpCode})"),
            ("Department", r.Department ?? "—"),
            ("Designation", r.Designation ?? "—"),
            ("Grade", r.Grade ?? "—"),
            ("Direct Manager", r.ManagerName ?? "—"),
            ("Review Year", r.EvalYear.ToString()),
            ("Reference Number", r.RefNo),
            ("Final Status", r.Status));

        AddKeyValueTable(section, "Overall Score",
            ("KPI Score", r.KpiScore.ToString("F2")),
            ("Competency Score", r.CompScore.ToString("F2")),
            ("Overall Score", r.OverallScore.ToString("F2")),
            ("Rating", r.Rating));

        if (r.Kpis.Count > 0)
        {
            AddDataTable(section, "KPI Breakdown",
                new[] { "Perspective", "KPI", "Target", "Weight %", "Achievement %", "Weighted", "Comments" },
                r.Kpis.Select(k => new[] { k.Perspective, k.Name, k.Target ?? "", k.Weight.ToString(),
                    k.Achievement.ToString(), k.Weighted.ToString("F2"), k.Comments ?? "" }));
        }

        if (r.Competencies.Count > 0)
        {
            AddDataTable(section, "Competency Breakdown",
                new[] { "Type", "Competency", "Weight %", "Achievement %", "Weighted", "Comments" },
                r.Competencies.Select(c => new[] { c.Type == "T" ? "Technical" : "Behavioral", c.Name,
                    c.Weight.ToString(), c.Achievement.ToString(), c.Weighted.ToString("F2"), c.Comments ?? "" }));
        }

        AddParagraphSection(section, "Self-Assessment", r.SelfAssessment);
        AddParagraphSection(section, "Development Plan", r.DevelopmentPlan);
        AddParagraphSection(section, "Employee Acknowledgement Comments", r.EmployeeAckComments);

        AddKeyValueTable(section, "Approval History",
            ("HR Reviewer 1", r.Hr1ReviewerName ?? "—"),
            ("HR Review 1 Date", r.Hr1ReviewDate?.ToString("dd/MM/yyyy") ?? "—"),
            ("HR Reviewer 1 Remarks", r.Hr1Remarks ?? "—"),
            ("HR Reviewer 2 (Final)", r.Hr2ReviewerName ?? "—"),
            ("HR Review 2 Date", r.Hr2ReviewDate?.ToString("dd/MM/yyyy") ?? "—"),
            ("HR Reviewer 2 Remarks", r.Hr2Remarks ?? "—"));

        if (r.History.Count > 0)
        {
            AddDataTable(section, "Workflow History",
                new[] { "From", "To", "Changed By", "Changed At", "Note" },
                r.History.Select(h => new[] { h.FromStatus is null ? "—" : Domain.PmFormStatus.DisplayName(h.FromStatus),
                    Domain.PmFormStatus.DisplayName(h.ToStatus), h.ChangedBy, h.ChangedAt.ToString("dd/MM/yyyy HH:mm"), h.Note ?? "" }));
        }

        return Render(doc);
    }

    public static byte[] DepartmentReportToPdf(DepartmentSummaryReport r)
    {
        var doc = NewDocument($"Department Summary — {r.DeptName}");
        var section = doc.Sections[0];
        AddBrandHeader(section, "Department Summary Report", r.GeneratedAt);

        AddKeyValueTable(section, "Department Information",
            ("Department", $"{r.DeptName} ({r.DeptCode})"),
            ("Review Year", r.EvalYear.ToString()),
            ("Employee Count", r.TotalEmployees.ToString()),
            ("Finalized Reviews", r.FinalizedCount.ToString()),
            ("Average Overall Score", r.AverageScore.ToString("F2")));

        AddEmployeeRowsTable(section, "Employees", r.Employees);
        return Render(doc);
    }

    public static byte[] ManagerReportToPdf(ManagerSummaryReport r)
    {
        var doc = NewDocument($"Manager Summary — {r.ManagerName}");
        var section = doc.Sections[0];
        AddBrandHeader(section, "Manager Summary Report", r.GeneratedAt);

        AddKeyValueTable(section, "Manager Information",
            ("Manager", $"{r.ManagerName} ({r.ManagerEmpCode})"),
            ("Review Year", r.EvalYear.ToString()),
            ("Team Size", r.TotalEmployees.ToString()),
            ("Finalized Reviews", r.FinalizedCount.ToString()),
            ("Average Overall Score", r.AverageScore.ToString("F2")));

        AddEmployeeRowsTable(section, "Team Members", r.TeamMembers);
        return Render(doc);
    }

    public static byte[] OverallReportToPdf(OverallOrganizationReport r)
    {
        var doc = NewDocument($"Overall Organization Summary — {r.EvalYear}");
        var section = doc.Sections[0];
        AddBrandHeader(section, "Overall Organization Summary", r.GeneratedAt);

        AddKeyValueTable(section, "Organization Overview",
            ("Review Year", r.EvalYear.ToString()),
            ("Total Employees", r.TotalEmployees.ToString()),
            ("Forms Generated", r.TotalForms.ToString()),
            ("Finalized Reviews", r.FinalizedCount.ToString()),
            ("Average Overall Score", r.AverageScore.ToString("F2")));

        if (r.Departments.Count > 0)
        {
            AddDataTable(section, "Department Breakdown",
                new[] { "Department", "Employees", "Finalized", "Completion %", "Average Score" },
                r.Departments.Select(d => new[] { d.DeptName, d.EmployeeCount.ToString(), d.FinalizedCount.ToString(),
                    $"{d.CompletionPercent}%", d.AverageScore.ToString("F2") }));
        }

        return Render(doc);
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

    // ================================================================ MigraDoc helpers
    private static Document NewDocument(string title)
    {
        var doc = new Document();
        doc.Info.Title = title;
        doc.Info.Author = AppName;

        var style = doc.Styles["Normal"]!;
        style.Font.Name = "Verdana";
        style.Font.Size = 9;

        var section = doc.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = "1.5cm";
        section.PageSetup.BottomMargin = "1.5cm";
        section.PageSetup.LeftMargin = "1.5cm";
        section.PageSetup.RightMargin = "1.5cm";
        return doc;
    }

    private static void AddBrandHeader(Section section, string title, DateTime generatedAt)
    {
        var brand = section.AddParagraph(AppName);
        brand.Format.Font.Size = 10;
        brand.Format.Font.Color = BrandGrey;
        brand.Format.SpaceAfter = "2pt";

        var heading = section.AddParagraph(title);
        heading.Format.Font.Size = 18;
        heading.Format.Font.Bold = true;
        heading.Format.Font.Color = BrandNavy;
        heading.Format.SpaceAfter = "4pt";

        var generated = section.AddParagraph($"Generated {generatedAt:dddd, dd MMMM yyyy} at {generatedAt:HH:mm}");
        generated.Format.Font.Size = 8;
        generated.Format.Font.Color = BrandGrey;
        generated.Format.SpaceAfter = "14pt";

        var rule = section.AddParagraph();
        rule.Format.Borders.Bottom = new Border { Width = "1pt", Color = BrandNavy };
        rule.Format.SpaceAfter = "10pt";
    }

    private static void AddSectionHeading(Section section, string text)
    {
        var p = section.AddParagraph(text);
        p.Format.Font.Size = 12;
        p.Format.Font.Bold = true;
        p.Format.Font.Color = BrandNavy;
        p.Format.SpaceBefore = "10pt";
        p.Format.SpaceAfter = "6pt";
    }

    private static void AddKeyValueTable(Section section, string heading, params (string Label, string Value)[] rows)
    {
        AddSectionHeading(section, heading);
        var table = section.AddTable();
        table.Borders.Width = 0.4;
        table.Borders.Color = new Color(220, 224, 230);
        table.AddColumn("4.5cm");
        table.AddColumn("13cm");
        foreach (var (label, value) in rows)
        {
            var r = table.AddRow();
            r.Cells[0].AddParagraph(label);
            r.Cells[0].Format.Font.Bold = true;
            r.Cells[0].Shading.Color = new Color(240, 242, 246);
            r.Cells[1].AddParagraph(value);
            r.Format.Font.Size = 9;
        }
    }

    private static void AddDataTable(Section section, string heading, string[] headers, IEnumerable<string[]> rows)
    {
        AddSectionHeading(section, heading);
        var table = section.AddTable();
        table.Borders.Width = 0.4;
        table.Borders.Color = new Color(220, 224, 230);
        foreach (var _ in headers) table.AddColumn();

        var headerRow = table.AddRow();
        headerRow.Shading.Color = BrandNavy;
        for (var i = 0; i < headers.Length; i++)
        {
            var p = headerRow.Cells[i].AddParagraph(headers[i]);
            p.Format.Font.Color = Colors.White;
            p.Format.Font.Bold = true;
            p.Format.Font.Size = 8;
        }

        foreach (var cells in rows)
        {
            var r = table.AddRow();
            r.Format.Font.Size = 8;
            for (var i = 0; i < cells.Length && i < headers.Length; i++)
                r.Cells[i].AddParagraph(cells[i]);
        }
    }

    private static void AddParagraphSection(Section section, string heading, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        AddSectionHeading(section, heading);
        var p = section.AddParagraph(text);
        p.Format.Font.Size = 9;
    }

    private static void AddEmployeeRowsTable(Section section, string heading, IReadOnlyList<ReportEmployeeRow> rows)
    {
        if (rows.Count == 0)
        {
            AddParagraphSection(section, heading, "No employees found.");
            return;
        }
        AddDataTable(section, heading,
            new[] { "Employee Code", "Name", "Designation", "Overall Score", "Rating", "Status" },
            rows.Select(r => new[] { r.EmpCode, r.Name, r.Designation ?? "", r.OverallScore.ToString("F2"), r.Rating, r.Status }));
    }

    private static byte[] Render(Document doc)
    {
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var ms = new MemoryStream();
        renderer.PdfDocument.Save(ms, false);
        return ms.ToArray();
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
}

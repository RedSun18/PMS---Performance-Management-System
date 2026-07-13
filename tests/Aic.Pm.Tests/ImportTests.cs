using Aic.Pm.Core.Import;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aic.Pm.Tests;

/// <summary>
/// Acceptance tests §D.4/D.5: import reconciliation against the real legacy exports.
/// Runs only when References/Database is present in the working tree (it is kept out
/// of source control per the export checklist).
/// </summary>
public class ImportTests
{
    private static string? FindDataDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "References", "Database");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public async Task Import_reconciles_with_source_export_and_is_idempotent()
    {
        var dataDir = FindDataDir();
        if (dataDir is null) return;   // exports not present in this checkout — skip

        using var host = new TestHost();
        var importer = new LegacyImportService(host.Db, NullLogger<LegacyImportService>.Instance);

        var s = await importer.RunAsync(dataDir);

        Assert.Equal(185, s.Forms);
        Assert.Equal(248, s.KpiItems);
        Assert.Equal(888, s.CompItems);
        Assert.Equal(134, s.KpiMasters);
        Assert.Equal(77, s.CompetencyMasters);
        Assert.Equal(6, s.JobFamilies);
        Assert.Equal(6, s.RatingScales);

        var statuses = host.Db.PmForms.GroupBy(f => f.Status)
            .ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(160, statuses["EMPLOYEE_ACKNOWLEDGE"]);
        Assert.Equal(23, statuses["DRAFT"]);
        Assert.Equal(2, statuses["PENDING_EMPLOYEE_ACK"]);

        // Spot checks (docs/acceptance-tests.md §D.4)
        var sample = host.Db.PmForms.Single(f => f.LegacyRefNo == "PM20261022HDR01");
        Assert.Equal(2026, sample.EvalYear);                    // '2026  ' trimmed
        Assert.Equal("EMPLOYEE_ACKNOWLEDGE", sample.Status);
        Assert.True(sample.IsActive);                           // 'Y     ' → true
        Assert.Equal("Specialists & Professionals", sample.JobFamily);
        Assert.Equal(0, sample.KpiWeightTotal);
        Assert.Equal(100, sample.CompWeightTotal);
        Assert.Empty(host.Db.PmFormKpis.Where(k => k.PmFormId == sample.Id));
        Assert.Equal(5, host.Db.PmFormCompetencies.Count(c => c.PmFormId == sample.Id));

        // Perspective exceptions present (1058 / 1470)
        Assert.True(host.Db.EmployeeExceptions.Any(e => e.EmpCode == "1058" && e.RuleCode == "PERSPECTIVE_MIN_EXEMPT"));
        Assert.True(host.Db.EmployeeExceptions.Any(e => e.EmpCode == "1470" && e.RuleCode == "PERSPECTIVE_MIN_EXEMPT"));

        // D.5 idempotency: second run changes no counts
        var s2 = await importer.RunAsync(dataDir);
        Assert.Equal(s.Forms, s2.Forms);
        Assert.Equal(185, host.Db.PmForms.Count());
        Assert.Equal(248, host.Db.PmFormKpis.Count());
        Assert.Equal(888, host.Db.PmFormCompetencies.Count());
        Assert.Equal(0, s2.Users);   // no new accounts on re-run
    }

    [Fact]
    public void Csv_parser_handles_quotes_commas_and_newlines()
    {
        var rows = Csv.Read(new StringReader(
            "a,b,c\n" +
            "1,\"x, y\",\"line1\nline2\"\n" +
            "2,\"He said \"\"hi\"\"\",plain\n"));
        Assert.Equal(3, rows.Count);
        Assert.Equal("x, y", rows[1][1]);
        Assert.Equal("line1\nline2", rows[1][2]);
        Assert.Equal("He said \"hi\"", rows[2][1]);
    }
}

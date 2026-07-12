using Aic.Pm.Core.Data;
using Aic.Pm.Core.Import;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

// Usage:
//   dotnet run --project src/Aic.Pm.Importer -- --data "References/Database" [--connection "..."]
// Idempotent: safe to re-run; upserts by natural key. See docs/data-migration-plan.md.

var dataDir = GetArg(args, "--data") ?? "References/Database";
var connection = GetArg(args, "--connection")
    ?? Environment.GetEnvironmentVariable("PM_CONNECTION")
    ?? "Host=localhost;Port=5445;Database=aicpm;Username=aicpm;Password=aicpm_dev";

using var loggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true));
var log = loggerFactory.CreateLogger("Importer");

if (!Directory.Exists(dataDir))
{
    log.LogError("Data directory not found: {Dir}", Path.GetFullPath(dataDir));
    return 1;
}

var options = new DbContextOptionsBuilder<PmDbContext>().UseNpgsql(connection).Options;
await using var db = new PmDbContext(options);

log.LogInformation("Applying migrations to {Conn}", connection.Split(';')[0]);
await db.Database.MigrateAsync();

var importer = new LegacyImportService(db, loggerFactory.CreateLogger<LegacyImportService>());
var s = await importer.RunAsync(dataDir);

Console.WriteLine();
Console.WriteLine("=== Import summary ===");
Console.WriteLine($"Departments:         {s.Departments}");
Console.WriteLine($"Job families:        {s.JobFamilies}");
Console.WriteLine($"Rating scales:       {s.RatingScales}");
Console.WriteLine($"KPI masters:         {s.KpiMasters}");
Console.WriteLine($"Competency masters:  {s.CompetencyMasters}");
Console.WriteLine($"Employees (HDR):     {s.Employees}");
Console.WriteLine($"Manager assignments: {s.ManagerAssignments}");
Console.WriteLine($"Exceptions:          {s.Exceptions}");
Console.WriteLine($"New user accounts:   {s.Users}");
Console.WriteLine($"PM forms:            {s.Forms}");
Console.WriteLine($"KPI items:           {s.KpiItems}");
Console.WriteLine($"COMP items:          {s.CompItems}");

// Reconciliation against the source export (docs/acceptance-tests.md §D)
var statusCounts = await db.PmForms.GroupBy(f => f.Status)
    .Select(g => new { Status = g.Key, Count = g.Count() })
    .OrderBy(x => x.Status).ToListAsync();
Console.WriteLine();
Console.WriteLine("HDR status counts:");
foreach (var sc in statusCounts) Console.WriteLine($"  {sc.Status,-25} {sc.Count}");

if (s.Warnings.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Warnings:");
    foreach (var w in s.Warnings) Console.WriteLine($"  - {w}");
}

return 0;

static string? GetArg(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

using PerformanceManagement.Core.Data;
using PerformanceManagement.DemoSeeder;
using Microsoft.EntityFrameworkCore;

// Usage:
//   dotnet run --project src/PerformanceManagement.DemoSeeder                 (seed the Demo DB, fails if already populated)
//   dotnet run --project src/PerformanceManagement.DemoSeeder -- --reset      (wipe demo tables and reseed from scratch)
//   dotnet run --project src/PerformanceManagement.DemoSeeder -- --connection "..." --force-unsafe
//
// Fully deterministic — same seed (DemoSeeder.Seed), same fictional data, every run.
// NEVER touches the real Development database: refuses to run against the well-known dev
// connection string/port unless --force-unsafe is explicitly passed.

const string DefaultDemoConnection = "Host=localhost;Port=5446;Database=pms_demo;Username=pms_demo;Password=pms_demo_dev";
const string KnownRealDevConnection = "Host=localhost;Port=5445;Database=pms;Username=pms;Password=pms_dev";

var connection = GetArg(args, "--connection")
    ?? Environment.GetEnvironmentVariable("PM_DEMO_CONNECTION")
    ?? DefaultDemoConnection;
var reset = args.Contains("--reset");
var forceUnsafe = args.Contains("--force-unsafe");

if (!forceUnsafe && (connection == KnownRealDevConnection || connection.Contains("Port=5445")))
{
    Console.Error.WriteLine("Refusing to run: this connection string matches the real Development database " +
        "(port 5445). The Demo seeder must never write to that database. If you are certain this is not the " +
        "real Development database, pass --force-unsafe to override.");
    return 1;
}

var options = new DbContextOptionsBuilder<PmDbContext>().UseNpgsql(connection).Options;
await using var db = new PmDbContext(options);

Console.WriteLine($"Applying migrations to {connection.Split(';')[0]} ...");
await db.Database.MigrateAsync();

if (reset)
{
    Console.WriteLine("Resetting demo data (--reset): removing all rows from demo-populated tables ...");
    await ResetAsync(db);
}

var seeder = new PerformanceManagement.DemoSeeder.DemoSeeder(db);
var summary = await seeder.SeedAllAsync();

Console.WriteLine();
Console.WriteLine("=== Demo seed summary ===");
Console.WriteLine($"Departments:            {summary.Departments}");
Console.WriteLine($"Designations:           {summary.Designations}");
Console.WriteLine($"Sections:               {summary.Sections}");
Console.WriteLine($"Job families:           {summary.JobFamilies}");
Console.WriteLine($"Rating scales:          {summary.RatingScales}");
Console.WriteLine($"KPI masters:            {summary.Kpis}");
Console.WriteLine($"Competency masters:     {summary.Competencies}");
Console.WriteLine($"Employees:              {summary.Employees}");
Console.WriteLine($"  of which managers:    {summary.Managers}");
Console.WriteLine($"Manager assignments:    {summary.ManagerAssignments}");
Console.WriteLine($"Exceptions:             {summary.Exceptions}");
Console.WriteLine($"User accounts:          {summary.Users}");
Console.WriteLine($"PM forms created:       {summary.Forms}");
Console.WriteLine($"Workflow Admin actions: {summary.WorkflowAdminActions}");

var statusCounts = await db.PmForms.GroupBy(f => f.Status)
    .Select(g => new { Status = g.Key, Count = g.Count() }).OrderBy(x => x.Status).ToListAsync();
Console.WriteLine();
Console.WriteLine("PM form status counts:");
foreach (var sc in statusCounts) Console.WriteLine($"  {sc.Status,-25} {sc.Count}");

Console.WriteLine();
Console.WriteLine("=== Demo login credentials ===");
Console.WriteLine("HR Administrator : admin    / Admin@123");
Console.WriteLine("Manager          : manager  / Demo@123");
Console.WriteLine("Employee         : employee / Demo@123");
Console.WriteLine("(Every other seeded employee also has a login: 4-digit employee code / Demo@123.)");
Console.WriteLine();
Console.WriteLine("Run the web app against this database with ASPNETCORE_ENVIRONMENT=Demo — see docs/DEMO.md.");

return 0;

static async Task ResetAsync(PmDbContext db)
{
    // Order matters for FK dependencies. Truncate/cascade would be simpler in raw SQL, but
    // going through EF keeps this portable and honest about exactly what gets removed.
    db.PmFormStatusHistory.RemoveRange(db.PmFormStatusHistory);
    db.PmFormKpis.RemoveRange(db.PmFormKpis);
    db.PmFormCompetencies.RemoveRange(db.PmFormCompetencies);
    db.PmForms.RemoveRange(db.PmForms);
    db.EmailLogs.RemoveRange(db.EmailLogs);
    db.Notifications.RemoveRange(db.Notifications);
    db.AuditLogs.RemoveRange(db.AuditLogs);
    db.UserRoles.RemoveRange(db.UserRoles);
    db.AppUsers.RemoveRange(db.AppUsers);
    db.EmployeeExceptions.RemoveRange(db.EmployeeExceptions);
    db.ManagerAssignments.RemoveRange(db.ManagerAssignments);
    db.Employees.RemoveRange(db.Employees);
    db.CompetencyMasters.RemoveRange(db.CompetencyMasters);
    db.KpiMasters.RemoveRange(db.KpiMasters);
    db.JobFamilies.RemoveRange(db.JobFamilies);
    db.RatingScales.RemoveRange(db.RatingScales);
    db.Sections.RemoveRange(db.Sections);
    db.Designations.RemoveRange(db.Designations);
    db.Departments.RemoveRange(db.Departments);
    db.SystemSettings.RemoveRange(db.SystemSettings);
    await db.SaveChangesAsync();
}

static string? GetArg(string[] args, string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

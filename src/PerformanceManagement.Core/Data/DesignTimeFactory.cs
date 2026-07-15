using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PerformanceManagement.Core.Data;

/// <summary>Design-time factory for `dotnet ef` commands (migrations).</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<PmDbContext>
{
    public PmDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("PM_CONNECTION")
            ?? "Host=localhost;Port=5445;Database=pms;Username=pms;Password=pms_dev";
        var options = new DbContextOptionsBuilder<PmDbContext>().UseNpgsql(connection).Options;
        return new PmDbContext(options);
    }
}

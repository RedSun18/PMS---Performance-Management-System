using Aic.Pm.Core.Data;
using Aic.Pm.Core.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connection = builder.Configuration.GetConnectionString("Pm")
    ?? Environment.GetEnvironmentVariable("PM_CONNECTION")
    ?? "Host=localhost;Port=5445;Database=aicpm;Username=aicpm;Password=aicpm_dev";

builder.Services.AddDbContext<PmDbContext>(o => o.UseNpgsql(connection));

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<AchievementGate>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<RatingService>();
builder.Services.AddScoped<JobFamilyService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<WorkflowService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/Login";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(o =>
{
    // Everything requires login except pages marked [AllowAnonymous]
    o.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().Build();
});

// Per-employee working sets for the PM Form live in session, mirroring the legacy
// per-employee session DataTables (standalone single-node app: in-memory is fine).
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromHours(2);
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
});

builder.Services.AddRazorPages();

var app = builder.Build();

// Apply migrations and core seeds at startup (idempotent)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
    await db.Database.MigrateAsync();
    await DatabaseSeeder.SeedCoreAsync(db);
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();
app.MapRazorPages();

app.Run();

public partial class Program { }

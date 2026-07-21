using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Services;
using PerformanceManagement.Web.Jobs;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Quartz;
using System.Globalization;

// Downloads/launches the shared headless-Chromium instance used for PDF report rendering (see
// PdfRenderer) in the background so it's most likely already warm by the first report request,
// without delaying the rest of app startup — RenderAsync awaits this itself if it isn't done yet.
_ = PdfRenderer.WarmupAsync();

var builder = WebApplication.CreateBuilder(args);

var connection = builder.Configuration.GetConnectionString("Pm")
    ?? Environment.GetEnvironmentVariable("PM_CONNECTION")
    ?? "Host=localhost;Port=5445;Database=pms;Username=pms;Password=pms_dev";

builder.Services.AddDbContext<PmDbContext>(o => o.UseNpgsql(connection));

// Pinned application name so the Data Protection key ring (used to encrypt the SMTP
// password at rest) stays valid across restarts regardless of content-root path changes.
builder.Services.AddDataProtection().SetApplicationName("PerformanceManagement");

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<AchievementGate>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<RatingService>();
builder.Services.AddScoped<JobFamilyService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<FormLinkService>();
builder.Services.AddScoped<ReportDataService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<WorkflowService>();

// Scheduled jobs (Job Registry: GenerateAnnualForms, OpenMidYearReview, OpenEndYearReview,
// DailyReminder, WeeklyEscalation, MonthlyCleanup) — see Jobs/ScheduledJobs.cs. Job/trigger keys
// are stable strings in JobRegistry.Group/.All so the Job Management page can pause/resume/trigger
// them by name without depending on Quartz's in-memory scheduler surviving a restart.
builder.Services.AddQuartz(q =>
{
    foreach (var (name, jobType, cron, description) in JobRegistry.All)
    {
        var jobKey = new JobKey(name, JobRegistry.Group);
        q.AddJob(jobType, jobKey, j => j.WithDescription(description));
        q.AddTrigger(t => t.ForJob(jobKey)
            .WithIdentity($"{name}-trigger", JobRegistry.Group)
            .WithCronSchedule(cron));
    }
    q.AddJobListener<JobHistoryListener>();
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        // Role-based [Authorize] failures (e.g. non-admin hitting an admin page) land on
        // the same explicit Access Denied page used by PM Form's per-record authorization.
        o.AccessDeniedPath = "/AccessDenied";
        o.ExpireTimeSpan = TimeSpan.FromHours(8); // overridden below once Settings:Authentication is readable
        o.SlidingExpiration = true;
    });

// The admin-configured Session Timeout (Settings > Authentication) overrides the fallback
// above. This Configure delegate is resolved lazily on first use (after startup migrations
// have run), not at registration time, so the DB read here is safe — but it's still only
// read once per process, matching the "takes effect after the app restarts" note on the
// Settings page.
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<IServiceScopeFactory>((o, scopeFactory) =>
    {
        using var scope = scopeFactory.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        var minutes = settings.GetAuthenticationSettingsAsync().GetAwaiter().GetResult().SessionTimeoutMinutes;
        o.ExpireTimeSpan = TimeSpan.FromMinutes(minutes);
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

// Localization architecture (Phase 10 Part 8): English + Arabic supported, cookie-driven
// culture selection (see Pages/Culture/SetLanguage.cshtml.cs and the language switcher in
// _Layout.cshtml). Per-page .resx files live under Resources/Pages/{mirrors Pages/ folder
// structure}; shared cross-page strings live in Resources/SharedResource.resx — see
// Resources/SharedResource.cs. Deliberately not every page is translated yet (only Login, as
// a working proof of the pattern) — this wires up the architecture for the rest to follow.
builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");
builder.Services.AddRazorPages()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

var supportedCultures = new[] { new CultureInfo("en"), new CultureInfo("ar") };
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    o.DefaultRequestCulture = new RequestCulture("en");
    o.SupportedCultures = supportedCultures;
    o.SupportedUICultures = supportedCultures;
    // Replaces the plain cookie provider — see SettingsAwareCultureProvider for the System
    // Settings "Language Selection" toggle and per-user PreferredCulture fallback it adds.
    o.RequestCultureProviders = new List<IRequestCultureProvider>
    {
        new PerformanceManagement.Web.Culture.SettingsAwareCultureProvider()
    };
});

var app = builder.Build();

// Apply migrations and core seeds at startup (idempotent).
// Single dev admin account — override via appsettings "AdminAccount" section or the
// PM_ADMIN_USERNAME / PM_ADMIN_PASSWORD environment variables. Never hard-code real
// production credentials here (docs/data-migration-plan.md §2).
var adminUsername = builder.Configuration["AdminAccount:Username"]
    ?? Environment.GetEnvironmentVariable("PM_ADMIN_USERNAME")
    ?? DatabaseSeeder.DefaultAdminUsername;
var adminPassword = builder.Configuration["AdminAccount:Password"]
    ?? Environment.GetEnvironmentVariable("PM_ADMIN_PASSWORD")
    ?? DatabaseSeeder.DefaultAdminPassword;

// appsettings.json ships convenience defaults for local development (admin password,
// Login As verification password, default employee password). Refuse to start in
// Production with any of them still unchanged — a checked-in default credential is a
// real vulnerability the moment this app is deployed somewhere reachable.
if (app.Environment.IsProduction())
{
    var unchanged = new List<string>();
    if (adminPassword == DatabaseSeeder.DefaultAdminPassword) unchanged.Add("AdminAccount:Password");
    if ((builder.Configuration["Security:LoginAsVerificationPassword"] ?? "Password123") == "Password123")
        unchanged.Add("Security:LoginAsVerificationPassword");
    if ((builder.Configuration["Security:DefaultUserPassword"] ?? DatabaseSeeder.DevPassword) == DatabaseSeeder.DevPassword)
        unchanged.Add("Security:DefaultUserPassword");

    if (unchanged.Count > 0)
        throw new InvalidOperationException(
            "Refusing to start in Production with unchanged default credential(s): " +
            string.Join(", ", unchanged) +
            ". Override these via environment variables or a Production-specific configuration source before deploying.");
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PmDbContext>();
    await db.Database.MigrateAsync();
    await DatabaseSeeder.SeedCoreAsync(db, adminUsername, adminPassword);
}

app.UseStaticFiles();
app.UseRouting();
// Authentication before localization (not the more common order) so SettingsAwareCultureProvider
// can read HttpContext.User — it falls back to the signed-in user's saved PreferredCulture when
// no culture cookie is present yet (new browser/device), so language follows the account, not
// just the browser. HttpContext.User is only populated once UseAuthentication has run.
app.UseAuthentication();
app.UseRequestLocalization(app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestLocalizationOptions>>().Value);
app.UseAuthorization();
app.UseSession();
app.MapRazorPages();

app.Run();

public partial class Program { }

using PerformanceManagement.Core.Data;
using PerformanceManagement.Core.Domain;
using PerformanceManagement.Web.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace PerformanceManagement.Web.Pages.Jobs;

/// <summary>
/// Administrator view of the 6 Quartz scheduled jobs (see Web/Jobs/ScheduledJobs.cs and
/// JobRegistry). Next Run comes live from the Quartz scheduler; Previous Run/Duration/
/// Status/Result come from ScheduledJobRun, written by JobHistoryListener around every
/// execution — Quartz's own in-memory RAMJobStore doesn't retain that history across restarts.
/// </summary>
[Authorize(Roles = Roles.HrAdmin)]
public class IndexModel : AppPageModel
{
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly PmDbContext _db;
    public IndexModel(ISchedulerFactory schedulerFactory, PmDbContext db) { _schedulerFactory = schedulerFactory; _db = db; }

    public List<JobRow> Jobs { get; set; } = new();
    public List<ScheduledJobRun> LogRuns { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ViewLog { get; set; }
    [TempData] public string? Message { get; set; }
    [TempData] public string? ErrorMessage { get; set; }

    public record JobRow(string Name, string Description, bool IsPaused, DateTimeOffset? NextFireUtc,
        ScheduledJobRun? LastRun);

    public async Task OnGetAsync()
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        Jobs = new List<JobRow>();

        foreach (var (name, _, _, description) in JobRegistry.All)
        {
            var jobKey = new JobKey(name, JobRegistry.Group);
            var triggerKey = new TriggerKey($"{name}-trigger", JobRegistry.Group);
            var triggerState = await scheduler.GetTriggerState(triggerKey);
            var trigger = await scheduler.GetTrigger(triggerKey);
            var lastRun = await _db.ScheduledJobRuns.AsNoTracking()
                .Where(r => r.JobName == name).OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync();

            Jobs.Add(new JobRow(name, description, triggerState == TriggerState.Paused,
                triggerState == TriggerState.Paused ? null : trigger?.GetNextFireTimeUtc(), lastRun));
        }

        if (!string.IsNullOrWhiteSpace(ViewLog))
        {
            LogRuns = await _db.ScheduledJobRuns.AsNoTracking()
                .Where(r => r.JobName == ViewLog).OrderByDescending(r => r.StartedAt).Take(20).ToListAsync();
        }
    }

    public async Task<IActionResult> OnPostRunNowAsync(string name)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.TriggerJob(new JobKey(name, JobRegistry.Group));
        Message = $"'{name}' has been triggered. Refresh in a few seconds to see the result.";
        return RedirectToPage(new { ViewLog = name });
    }

    public async Task<IActionResult> OnPostPauseAsync(string name)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.PauseTrigger(new TriggerKey($"{name}-trigger", JobRegistry.Group));
        Message = $"'{name}' disabled — it will not fire on its schedule until re-enabled.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResumeAsync(string name)
    {
        var scheduler = await _schedulerFactory.GetScheduler();
        await scheduler.ResumeTrigger(new TriggerKey($"{name}-trigger", JobRegistry.Group));
        Message = $"'{name}' enabled.";
        return RedirectToPage();
    }
}

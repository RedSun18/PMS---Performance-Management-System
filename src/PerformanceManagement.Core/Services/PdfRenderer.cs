using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace PerformanceManagement.Core.Services;

/// <summary>
/// Renders report HTML to PDF using headless Chromium instead of MigraDoc/PdfSharp.
///
/// MigraDoc has no bidi (bidirectional text) or Arabic-shaping engine — Arabic text came out
/// with words in the wrong order and letters in isolated (unconnected) form, regardless of which
/// font was supplied. That's not fixable by swapping fonts; it needs a real text-layout engine.
/// Chromium already has one (the same engine that renders Arabic correctly on any website), so
/// reports are now built as HTML/CSS — the same approach already used for report-quality email
/// templates — and printed to PDF via <see cref="Page.PdfDataAsync()"/>.
///
/// The browser process is expensive to start (~1-2s) so one instance is launched lazily at
/// startup (<see cref="WarmupAsync"/>) and reused for every report; only a lightweight Page is
/// opened/closed per render.
/// </summary>
public static class PdfRenderer
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static IBrowser? _browser;

    /// <summary>Downloads the bundled Chromium revision (first run only) and launches the shared
    /// browser instance. Call once at application startup so the first real report request isn't
    /// the one paying the download/launch cost.</summary>
    public static async Task WarmupAsync()
    {
        if (_browser is not null) return;
        await InitLock.WaitAsync();
        try
        {
            if (_browser is not null) return;

            // Explicit cache directory instead of new BrowserFetcher()'s unconfigured default
            // (which resolves relative to the running assembly's own directory). In production
            // that default is a freshly published, timestamped release folder (see
            // deploy/publish.sh) owned by a different user than the one the app runs as (see
            // deploy/systemd/pms-demo.service) — every deploy would both force a full ~150MB
            // re-download AND fail outright with UnauthorizedAccessException, because the
            // unprivileged service account has no write access to its own release directory.
            // $HOME (set explicitly in pms-demo.service, also where the Data Protection key
            // ring already lives) is a stable, app-owned directory that survives every deploy —
            // anchoring the Chromium cache there makes it persist across releases and actually
            // writable by the account that needs to write it.
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "puppeteer");
            var fetcher = Puppeteer.CreateBrowserFetcher(new BrowserFetcherOptions { Path = cacheDir });
            var installedBrowser = await fetcher.DownloadAsync();
            _browser = await Puppeteer.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                ExecutablePath = installedBrowser.GetExecutablePath(),
                Args = new[] { "--no-sandbox" }
            });
        }
        finally
        {
            InitLock.Release();
        }
    }

    /// <summary>File-system directory containing the bundled report fonts (NotoSans-latin.woff2,
    /// NotoSansArabic-arabic.woff2) — resolved next to the running assembly since they're copied
    /// there as plain build output, not embedded resources.</summary>
    public static string FontsDirectory { get; } = AppContext.BaseDirectory;

    public static async Task<byte[]> RenderAsync(string html)
    {
        await WarmupAsync();
        var page = await _browser!.NewPageAsync();
        try
        {
            await page.SetContentAsync(html, new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.Networkidle0 } });
            return await page.PdfDataAsync(new PdfOptions
            {
                Format = PuppeteerSharp.Media.PaperFormat.A4,
                PrintBackground = true,
                MarginOptions = new MarginOptions { Top = "1.5cm", Bottom = "1.5cm", Left = "1.5cm", Right = "1.5cm" }
            });
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}

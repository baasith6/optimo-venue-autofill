using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using VenueAutofill.Api.Configuration;

namespace VenueAutofill.Api.Infrastructure.Browser;

public class PlaywrightHtmlPageFetcher : IAsyncDisposable
{
    private readonly BrowserFetchOptions _options;
    private readonly SemaphoreSlim _browserGate;
    private readonly ILogger<PlaywrightHtmlPageFetcher> _logger;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private static readonly string[] CookieDismissSelectors =
    [
        "button:has-text('Accept')",
        "button:has-text('Accept All')",
        "button:has-text('I Agree')",
        "button:has-text('Agree')",
        "#onetrust-accept-btn-handler",
        "[data-testid='accept-btn']"
    ];

    public PlaywrightHtmlPageFetcher(IOptions<BrowserFetchOptions> options, ILogger<PlaywrightHtmlPageFetcher> logger)
    {
        _options = options.Value;
        _logger = logger;
        _browserGate = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentBrowsers));
    }

    public async Task<HtmlFetchResult> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        var result = new HtmlFetchResult { FinalUrl = url };

        if (!_options.Enabled || !_options.UsePlaywrightFallback)
            return result;

        await _browserGate.WaitAsync(cancellationToken);
        try
        {
            await EnsureBrowserAsync(cancellationToken);
            if (_browser is null)
                return result;

            await using var context = await _browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36",
                Locale = "en-US",
                ViewportSize = new ViewportSize { Width = 1366, Height = 900 }
            });

            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(_options.PlaywrightTimeoutSeconds * 1000);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.PlaywrightTimeoutSeconds + 10));

            var response = await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = _options.PlaywrightTimeoutSeconds * 1000
            });

            await TryDismissCookieBannerAsync(page);

            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions
                {
                    Timeout = Math.Min(15000, _options.PlaywrightTimeoutSeconds * 1000)
                });
            }
            catch (TimeoutException)
            {
                // NetworkIdle often times out on hotel sites; continue with DOM content
            }

            await page.WaitForTimeoutAsync(1500);

            result.StatusCode = response?.Status ?? 0;
            result.FinalUrl = page.Url;
            result.Html = await page.ContentAsync();
            result.FetchedVia = "playwright";

            var title = await page.TitleAsync();
            result.Succeeded = !HttpHtmlPageFetcher.LooksBlocked(result.Html, title)
                               && result.Html.Length >= 500
                               && (response is null || response.Ok || result.Html.Length > 2000);
            result.LooksBlocked = !result.Succeeded && result.Html.Length > 0;
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Playwright fetch failed for {Url}", url);
            return result;
        }
        finally
        {
            _browserGate.Release();
        }
    }

    private static async Task TryDismissCookieBannerAsync(IPage page)
    {
        foreach (var selector in CookieDismissSelectors)
        {
            try
            {
                var button = page.Locator(selector).First;
                if (await button.IsVisibleAsync())
                {
                    await button.ClickAsync(new LocatorClickOptions { Timeout = 2000 });
                    await page.WaitForTimeoutAsync(500);
                    return;
                }
            }
            catch
            {
                // try next selector
            }
        }
    }

    private async Task EnsureBrowserAsync(CancellationToken cancellationToken)
    {
        if (_browser is not null)
            return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_browser is not null)
                return;

            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = ["--disable-blink-features=AutomationControlled"]
            });
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
        _playwright?.Dispose();
        _browserGate.Dispose();
        _initLock.Dispose();
    }
}

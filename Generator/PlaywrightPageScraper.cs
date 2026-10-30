using Microsoft.Playwright;

namespace Generator;

public sealed class PlaywrightPageScraper : IPageScraper, IAsyncDisposable
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;

    public async Task<string> GetContentAsync(
        Uri url,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = await GetContextAsync();
        var page = await context.NewPageAsync();

        try
        {
            var response = await page.GotoAsync(
                url.ToString(),
                new PageGotoOptions
                {
                    Timeout = 120_000,
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });

            if (response is null)
                throw new HttpRequestException(
                    $"Chrome returned no response for {url}.");

            if (response.Ok == false &&
                await WaitForSuccessfulDocumentAsync(page, url) == false)
            {
                throw new HttpRequestException(
                    $"Chrome returned {response.Status} for {url}.");
            }

            await WaitForRenderingAsync(page);
            cancellationToken.ThrowIfCancellationRequested();
            return await page.ContentAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
            await _context.CloseAsync();

        if (_browser is not null)
            await _browser.CloseAsync();

        _playwright?.Dispose();
    }

    private async Task<IBrowserContext> GetContextAsync()
    {
        if (_context is not null)
            return _context;

        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions
            {
                Channel = "chrome",
                Headless = true
            });
        var browserVersion = browser.Version;
        var context = await browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                AcceptDownloads = false,
                DeviceScaleFactor = 1,
                HasTouch = false,
                IsMobile = false,
                Locale = "en-US",
                ScreenSize = new ScreenSize
                {
                    Width = 1920,
                    Height = 1080
                },
                UserAgent =
                    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) " +
                    "AppleWebKit/537.36 (KHTML, like Gecko) " +
                    $"Chrome/{browserVersion} Safari/537.36",
                ViewportSize = new ViewportSize
                {
                    Width = 1920,
                    Height = 1080
                }
            });
        await context.AddInitScriptAsync(
            "Object.defineProperty(Navigator.prototype, 'webdriver', " +
            "{ get: () => undefined });");

        _playwright = playwright;
        _browser = browser;
        _context = context;
        return context;
    }

    private static async Task<bool> WaitForSuccessfulDocumentAsync(
        IPage page,
        Uri url)
    {
        try
        {
            await page.WaitForResponseAsync(
                response =>
                    response.Ok &&
                    response.Request.ResourceType == "document" &&
                    response.Url.StartsWith(
                        url.GetLeftPart(UriPartial.Path),
                        StringComparison.OrdinalIgnoreCase),
                new PageWaitForResponseOptions { Timeout = 25_000 });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private static async Task WaitForRenderingAsync(IPage page)
    {
        try
        {
            await page.WaitForLoadStateAsync(
                LoadState.Load,
                new PageWaitForLoadStateOptions { Timeout = 60_000 });
            await page.WaitForLoadStateAsync(
                LoadState.NetworkIdle,
                new PageWaitForLoadStateOptions { Timeout = 5_000 });
        }
        catch (TimeoutException)
        {
        }
    }
}

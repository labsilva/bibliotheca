using Bibliotheca.Backend.Options;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Bibliotheca.Backend.Fetchers;

public class PlaywrightFetcher(IOptions<PlaywrightOptions> options)
{
    private static readonly HttpClient DiscoveryClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    protected Task<(string Url, string Html)> ReadPageAsync(string url, CancellationToken cancellationToken) =>
        WithBrowserAsync(async browser =>
        {
            await using var context = await NewContextAsync(browser);
            var page = await context.NewPageAsync();
            var response = await page.GotoAsync(url);
            if (response is not null && response.Status >= 400)
                throw new PageNavigationException(response.Status, response.Url);
            return (page.Url, await page.ContentAsync());
        }, cancellationToken);

    protected static Task<IBrowserContext> NewContextAsync(IBrowser browser) =>
        browser.NewContextAsync();

    protected Task<T> WithBrowserAsync<T>(Func<IBrowser, Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return RunAsync().WaitAsync(cancellationToken);

        async Task<T> RunAsync()
        {
            using var playwright = await Playwright.CreateAsync();
            // Playwright operations have no token overload. Disconnect this operation's
            // driver to interrupt in-flight navigation, including on a remote browser.
            using var registration = cancellationToken.Register(playwright.Dispose);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var browser = options.Value.IsPlaywrightRemoteEnabled
                    ? await playwright.Chromium.ConnectAsync(await PlaywrightRemoteEndpoint.ResolveAsync(
                        options.Value.Remote, DiscoveryClient, cancellationToken))
                    : await playwright.Chromium.LaunchAsync(new()
                    {
                        Headless = options.Value.Headless,
                        Channel = string.IsNullOrWhiteSpace(options.Value.Channel) ? null : options.Value.Channel
                    });
                return await operation(browser);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
    }
}

public sealed class PageNavigationException(int statusCode, string? url = null) : Exception("Browser navigation failed.")
{
    public int StatusCode { get; } = statusCode;
    public string? Url { get; } = url;
}

using System.Text.Json;
using Bibliotheca.Backend.Fetchers;
using Microsoft.Playwright;

namespace Bibliotheca.Backend.Extractors;

internal sealed class ExtractorSession(IBrowserContext context, IPage page, HttpClient client,
    ExtractorManifest manifest, string operation, CancellationToken token, ILogger? logger)
{
    internal const string HostBindings = """
        goto: async url => parse(await call("goto", url, "")),
        fill: async (selector, value) => parse(await call("fill", selector, value)),
        click: async selector => parse(await call("click", selector, "")),
        waitForSelector: async selector => parse(await call("waitForSelector", selector, "")),
        text: async selector => parse(await call("text", selector, "")),
        attribute: async (selector, name) => parse(await call("attribute", selector, name)),
        downloadImage: async (url, mode = "") => parse(await call("downloadImage", url, mode))
        """;

    private const int MaxDocumentBytes = 2 * 1024 * 1024;
    private const int MaxRequests = 128;
    private readonly object hostCalls = new();
    private Task<string>? pending;
    private int calls;
    private int requests;
    private Uri? source;
    private Uri? coverRequest;
    private IPage? coverPage;
    private string? sourceRequestUrl;
    private bool sourceCommitted;

    public Uri? Source
    {
        get
        {
            if (!sourceCommitted || source is null ||
                !Uri.TryCreate(page.Url, UriKind.Absolute, out var current) ||
                !ExtractorDefinition.Allows(current, manifest.NavigationOrigins) ||
                current.GetLeftPart(UriPartial.Authority) != source.GetLeftPart(UriPartial.Authority))
                return null;
            return sourceRequestUrl == page.Url ? source : current;
        }
    }
    public FetchedCover? Cover { get; private set; }
    public System.Collections.Concurrent.ConcurrentDictionary<Uri, byte> Visited { get; } = new();

    public Task ConfigureBrowserAsync()
    {
        page.FrameNavigated += (_, frame) =>
        {
            if (frame == page.MainFrame && frame.Url == sourceRequestUrl)
            {
                sourceCommitted = true;
                if (operation == "authorBiography" && source is not null) Visited.TryAdd(source, 0);
            }
        };
        return context.RouteAsync("**/*", RelayAsync);
    }

    private async Task RelayAsync(IRoute route)
    {
        var request = route.Request;
        var isNavigation = request.IsNavigationRequest || request.ResourceType == "document";
        var mainNavigation = isNavigation && request.Frame == page.MainFrame;
        var coverNavigation = isNavigation && coverPage is not null && request.Frame == coverPage.MainFrame;
        var origins = isNavigation && !coverNavigation ? manifest.NavigationOrigins : manifest.ResourceOrigins;
        if (mainNavigation)
        {
            source = null;
            sourceRequestUrl = null;
            sourceCommitted = false;
            Cover = null;
        }

        try
        {
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var url) || !ExtractorDefinition.Allows(url, origins) ||
                (coverNavigation && url != coverRequest))
            {
                logger?.LogDebug("Extractor {ExtractorId} {Operation} blocked {ResourceType} origin {Host}",
                    manifest.Id, operation, request.ResourceType, url?.IdnHost);
                await route.AbortAsync();
                return;
            }

            if (Interlocked.Increment(ref requests) > MaxRequests ||
                request.Method is not ("GET" or "HEAD" or "POST" or "OPTIONS") ||
                request.PostDataBuffer is { Length: > 65_536 })
            {
                await route.AbortAsync();
                return;
            }

            // Playwright only routes the first URL in a redirect chain. The browser stays
            // offline and receives the final response from our origin-checking transport.
            var headers = (await request.AllHeadersAsync()).Where(pair => RequestHeaders.Contains(pair.Key))
                .ToDictionary();
            var downloadingCover = coverNavigation || request.ResourceType == "fetch" && url == coverRequest;
            var response = await ExtractorTransport.SendAsync(client, url, origins,
                downloadingCover ? CoverDownloader.MaxCoverBytes : MaxDocumentBytes,
                new HttpMethod(request.Method), request.PostDataBuffer, headers, token,
                sameOriginRedirects: true, rejectRedirects: downloadingCover);
            if (mainNavigation && response.Status >= 400)
            {
                logger?.Log(response.Status == 404 ? LogLevel.Debug : LogLevel.Warning,
                    "Extractor {ExtractorId} {Operation} navigation to {Host} returned HTTP {StatusCode}",
                    manifest.Id, operation, response.Url.IdnHost, response.Status);
            }
            if (coverNavigation && (response.Status is < 200 or >= 300 ||
                !IsCoverMediaType(response.MediaType?.ToLowerInvariant()) || response.Body.Length == 0))
            {
                await route.AbortAsync();
                return;
            }

            if (mainNavigation && response.Status is >= 200 and < 300 &&
                response.MediaType?.ToLowerInvariant() is "text/html" or "application/xhtml+xml")
            {
                source = response.Url;
                sourceRequestUrl = request.Url;
            }
            await route.FulfillAsync(new()
            {
                Status = response.Status,
                Headers = response.Headers,
                BodyBytes = response.Body
            });
        }
        catch (Exception exception) when (exception is InvalidDataException or HttpRequestException or
            IOException or PlaywrightException or OperationCanceledException)
        {
            if (!token.IsCancellationRequested)
                logger?.Log(mainNavigation || coverNavigation || request.ResourceType == "fetch" && coverRequest is not null
                        ? LogLevel.Warning : LogLevel.Debug,
                    "Extractor {ExtractorId} {Operation} request failed: {ResourceType} {FailureType}",
                    manifest.Id, operation, request.ResourceType, exception switch
                    {
                        InvalidDataException => exception.Message,
                        HttpRequestException requestError => requestError.HttpRequestError.ToString(),
                        _ => exception.GetType().Name
                    });
            try { await route.AbortAsync(); }
            catch (PlaywrightException) { /* The context may already be closed. */ }
        }
    }

    private static readonly HashSet<string> RequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "accept", "content-type", "cookie", "origin", "referer", "user-agent",
        "access-control-request-method", "access-control-request-headers"
    };

    public Task<string> CallAsync(string method, string input, string argument)
    {
        lock (hostCalls)
        {
            token.ThrowIfCancellationRequested();
            if (pending is { IsCompleted: false } || ++calls > 64 || input.Length > 2048 || argument.Length > (method == "fill" ? 2048 : 64))
                throw new InvalidDataException("Invalid or overlapping extractor host call.");
            return pending = ExecuteHostCallAsync(method, input, argument);
        }
    }

    private async Task<string> ExecuteHostCallAsync(string method, string input, string argument)
    {
        logger?.LogTrace("Extractor {ExtractorId} {Operation} starting host call {Method}", manifest.Id, operation, method);
        var result = await DispatchAsync(method, input, argument);
        logger?.LogTrace("Extractor {ExtractorId} {Operation} host call {Method} returned {Length} characters",
            manifest.Id, operation, method, result.Length);
        return result;
    }

    public async Task DrainAsync()
    {
        Task<string>? call;
        lock (hostCalls) call = pending;
        if (call is null) return;
        try { await call; }
        catch (Exception) when (token.IsCancellationRequested) { }
    }

    private async Task<string> DispatchAsync(string method, string input, string argument)
    {
        switch (method)
        {
            case "goto":
                var destination = Resolve(input);
                if (!ExtractorDefinition.Allows(destination, manifest.NavigationOrigins))
                {
                    logger?.LogWarning("Extractor {ExtractorId} {Operation} blocked navigation origin {Host}",
                        manifest.Id, operation, destination.IdnHost);
                    throw new InvalidDataException("Extractor navigation requires an approved HTTPS origin.");
                }
                var navigation = await page.GotoAsync(destination.AbsoluteUri,
                    new() { WaitUntil = WaitUntilState.DOMContentLoaded });
                return navigation?.Status == 404 ? "null" : JsonSerializer.Serialize(Source?.AbsoluteUri);

            case "fill" when Source is not null:
                await page.Locator(input).FillAsync(argument);
                return "true";

            case "click" when Source is not null:
                await page.Locator(input).ClickAsync();
                return "true";

            case "waitForSelector":
                await page.Locator(input).WaitForAsync();
                return "true";

            case "text" when Source is not null:
            case "attribute" when Source is not null:
                var values = await page.Locator(input).EvaluateAllAsync<string[]>(
                    "(elements, name) => elements.slice(0, 64).map(e => (name ? e.getAttribute(name) : e.textContent)?.slice(0, 32768) ?? '')",
                    method == "attribute" ? argument : "");
                return JsonSerializer.Serialize(values);

            case "downloadImage" when operation == "cover" && Source is not null:
                return await DownloadCoverAsync(Resolve(input), argument);

            default:
                throw new InvalidDataException("Extractor host operation is unavailable.");
        }
    }

    private async Task<string> DownloadCoverAsync(Uri image, string mode)
    {
        Cover = null;
        var product = Source;
        if (!ExtractorDefinition.Allows(image, manifest.ResourceOrigins))
        {
            logger?.LogWarning("Extractor {ExtractorId} {Operation} blocked cover origin {Host}",
                manifest.Id, operation, image.IdnHost);
            return "false";
        }

        if (mode is not ("" or "navigate"))
            throw new InvalidDataException("Unsupported extractor cover download mode.");
        coverRequest = image;
        try
        {
            var cover = mode == "navigate"
                ? await DownloadNavigatedCoverAsync(image)
                : await CoverDownloader.DownloadInPageAsync(page, image.AbsoluteUri);
            token.ThrowIfCancellationRequested();
            if (!cover.Success || Source != product || cover.Image is not { Length: > 0 and <= CoverDownloader.MaxCoverBytes } ||
                !IsCoverMediaType(cover.MimeType))
                return "false";

            Cover = cover;
            return "true";
        }
        catch (PlaywrightException)
        {
            token.ThrowIfCancellationRequested();
            return "false";
        }
        finally
        {
            if (coverPage is { } imagePage)
            {
                try { await imagePage.CloseAsync(); }
                catch (PlaywrightException) when (token.IsCancellationRequested) { }
                finally { coverPage = null; }
            }
            coverRequest = null;
        }
    }

    private async Task<FetchedCover> DownloadNavigatedCoverAsync(Uri image)
    {
        coverPage = await context.NewPageAsync();
        var response = await coverPage.GotoAsync(image.AbsoluteUri, new()
        {
            Referer = Source?.AbsoluteUri,
            WaitUntil = WaitUntilState.Commit
        });
        if (response is null || !response.Ok || !Uri.TryCreate(response.Url, UriKind.Absolute, out var actual) ||
            actual != image || !response.Headers.TryGetValue("content-type", out var contentType))
            return new() { Success = false };

        var type = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (!IsCoverMediaType(type))
            return new() { Success = false };

        // RelayAsync bounds this response before Playwright receives it.
        return new() { Success = true, Url = actual.AbsoluteUri, MimeType = type, Image = await response.BodyAsync() };
    }

    private static bool IsCoverMediaType(string? value) =>
        value is "image/jpeg" or "image/png" or "image/webp" or "image/gif" or "image/avif" or "image/bmp" or "image/tiff";

    private Uri Resolve(string input)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var absolute) && absolute.Scheme != Uri.UriSchemeFile)
            return absolute;
        if (Source is not null && Uri.TryCreate(Source, input, out var relative))
            return relative;
        throw new InvalidDataException("Invalid extractor URL.");
    }
}

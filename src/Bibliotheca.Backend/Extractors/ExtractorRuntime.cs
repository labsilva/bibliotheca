using System.Text.Json;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Options;
using Jint;
using Jint.Runtime;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Bibliotheca.Backend.Extractors;

internal sealed class ExtractorRuntime(IOptions<PlaywrightOptions> options, IHttpClientFactory clients, ILogger<ExtractorRuntime>? logger = null)
    : PlaywrightFetcher(options)
{
    public const string ClientName = "JavaScriptExtractors";
    private static readonly TimeSpan ExecutionLimit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CleanupLimit = TimeSpan.FromSeconds(2);
    private static readonly SemaphoreSlim EvaluationSlots = new(4);

    public Task<ExtractorResult?> RunAsync(ExtractorDefinition definition, string operation, string isbn,
        CancellationToken cancellationToken) =>
        RunAsync(definition, operation, isbn, (json, session) =>
            ExtractorResult.Parse(json, isbn, session.Source, session.Cover), cancellationToken);

    internal Task<AuthorBiographyResult?> RunAuthorAsync(ExtractorDefinition definition, AuthorBiographyInput input,
        CancellationToken cancellationToken) =>
        RunAsync(definition, "authorBiography", JsonSerializer.Serialize(input, AuthorBiographyResult.JsonOptions),
            (json, session) => AuthorBiographyResult.Parse(json, definition.Manifest, input, session.Source,
                session.Visited), cancellationToken);

    private async Task<T?> RunAsync<T>(ExtractorDefinition definition, string operation, string input,
        Func<string, ExtractorSession, T?> parse, CancellationToken cancellationToken) where T : class
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ExecutionLimit);
        using var hardStop = new CancellationTokenSource();
        using var registration = deadline.Token.Register(() => hardStop.CancelAfter(CleanupLimit));
        using var client = clients.CreateClient(ClientName);
        try
        {
            return await WithBrowserAsync(browser =>
                RunInBrowserAsync(browser, client, definition, operation, input, parse, deadline.Token, logger),
                hardStop.Token, waitForCleanup: true);
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested)
            {
                logger?.LogWarning("Extractor {ExtractorId} {Operation} timed out", definition.Manifest.Id, operation);
                throw new TimeoutException("JavaScript extractor exceeded its deadline.");
            }
            throw;
        }
    }

    internal static Task<ExtractorResult?> RunInBrowserAsync(IBrowser browser, HttpClient client,
        ExtractorDefinition definition, string operation, string isbn, CancellationToken token, ILogger? logger = null) =>
        RunInBrowserAsync(browser, client, definition, operation, isbn,
            (json, session) => ExtractorResult.Parse(json, isbn, session.Source, session.Cover), token, logger);

    private static async Task<T?> RunInBrowserAsync<T>(IBrowser browser, HttpClient client,
        ExtractorDefinition definition, string operation, string input, Func<string, ExtractorSession, T?> parse,
        CancellationToken token, ILogger? logger) where T : class
    {
        token.ThrowIfCancellationRequested();
        var context = await browser.NewContextAsync(new()
        {
            JavaScriptEnabled = true,
            ServiceWorkers = ServiceWorkerPolicy.Block,
            Offline = true,
            AcceptDownloads = false
        });
        using var sessionStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        ExtractorSession? session = null;
        try
        {
            token.ThrowIfCancellationRequested();
            await context.RouteWebSocketAsync("**/*", socket => _ = socket.CloseAsync());
            var page = await context.NewPageAsync();
            session = new ExtractorSession(context, page, client, definition.Manifest, operation, sessionStop.Token, logger);
            await session.ConfigureBrowserAsync();
            var json = await EvaluateAsync(definition.Script, operation, input, session.CallAsync, token,
                definition.Manifest.Id, logger);
            try
            {
                var result = parse(json, session);
                logger?.LogDebug("Extractor {ExtractorId} {Operation} result: {Outcome}",
                    definition.Manifest.Id, operation, result is null ? "no match" : "validated");
                return result;
            }
            catch (Exception exception) when (exception is InvalidDataException or JsonException)
            {
                logger?.LogWarning("Extractor {ExtractorId} {Operation} returned an invalid result: {FailureType}",
                    definition.Manifest.Id, operation, exception is InvalidDataException ? exception.Message : exception.GetType().Name);
                throw;
            }
        }
        finally
        {
            await sessionStop.CancelAsync();
            // Closing the context interrupts any outstanding Playwright call before we drain it.
            try { await context.CloseAsync(); }
            finally
            {
                if (session is not null) await session.DrainAsync();
            }
        }
    }

    internal static async Task<string> EvaluateAsync(string script, string operation, string isbn,
        Func<string, string, string, Task<string>> call, CancellationToken token,
        string? extractorId = null, ILogger? logger = null)
    {
        await EvaluationSlots.WaitAsync(token);
        try
        {
            // Jint's memory constraint counts allocations on its starting thread.
            // Keep the promise pump there and dispatch asynchronous host work off that thread.
            return await Task.Factory.StartNew(
                () => Evaluate(script, operation, isbn, call, token, extractorId, logger),
                token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        finally { EvaluationSlots.Release(); }
    }

    private static string Evaluate(string script, string operation, string isbn,
        Func<string, string, string, Task<string>> call, CancellationToken token, string? extractorId, ILogger? logger)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        logger?.LogDebug("Evaluating extractor {ExtractorId} {Operation} in Jint", extractorId, operation);
        var engine = new Engine(options =>
        {
            options.ExperimentalFeatures = ExperimentalFeature.TaskInterop;
            options.MaxStatements(100_000).LimitMemory(32 * 1024 * 1024).LimitRecursion(64).CancellationToken(token);
        });
        engine.SetValue("hostCall", (string method, string input, string argument) =>
            Task.Run(() => call(method, input, argument), token));
        engine.SetValue("operation", operation);
        engine.SetValue("isbn", isbn);
        Jint.Native.JsValue result;
        try
        {
            result = engine.Evaluate("""
                (async (call, operation, isbn, stringify, parse) => {
                    delete globalThis.hostCall;
                    const host = Object.freeze({
                        goto: async url => parse(await call("goto", url, "")),
                        fill: async (selector, value) => parse(await call("fill", selector, value)),
                        click: async selector => parse(await call("click", selector, "")),
                        waitForSelector: async selector => parse(await call("waitForSelector", selector, "")),
                        text: async selector => parse(await call("text", selector, "")),
                        attribute: async (selector, name) => parse(await call("attribute", selector, name)),
                        downloadImage: async (url, mode = "") => parse(await call("downloadImage", url, mode)),
                        hardcover: async action => parse(await call("hardcover", action, ""))
                    });
                """ + "\n" + script + "\n" + """
                    return stringify(await extract(host, operation, operation === "authorBiography" ? parse(isbn) : isbn));
                })(hostCall, operation, isbn, JSON.stringify, JSON.parse)
                """, source: extractorId).UnwrapIfPromise(token);
        }
        catch (ExecutionCanceledException) when (token.IsCancellationRequested)
        {
            throw new OperationCanceledException(token);
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            logger?.LogWarning("Jint evaluation for extractor {ExtractorId} {Operation} failed after {ElapsedMs} ms: {FailureType}",
                extractorId, operation, elapsed.ElapsedMilliseconds, exception.GetType().Name);
            throw;
        }
        logger?.LogDebug("Jint evaluation for extractor {ExtractorId} {Operation} completed in {ElapsedMs} ms",
            extractorId, operation, elapsed.ElapsedMilliseconds);
        if (!result.IsString() || result.AsString().Length > 32_768)
            throw new InvalidDataException("Extractor output is missing or exceeds its size limit.");
        return result.AsString();
    }
}

internal sealed class ExtractorSession(IBrowserContext context, IPage page, HttpClient client,
    ExtractorManifest manifest, string operation, CancellationToken token, ILogger? logger)
{
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

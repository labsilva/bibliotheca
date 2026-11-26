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
        json: async (selector, path) => parse(await call("json", selector, JSON.stringify(path))),
        authorEvidence: async (url, name) => parse(await call("authorEvidence", url, name)),
        rows: async (selector, columns) => parse(await call("rows", selector, JSON.stringify(columns))),
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
    private Uri? requestedNavigation;
    private ExtractorResponse? canonicalResponse;
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
    public HttpRequestException? RequestFailure { get; private set; }
    public System.Collections.Concurrent.ConcurrentDictionary<Uri, byte> Visited { get; } = new();
    public System.Collections.Concurrent.ConcurrentDictionary<(Uri Edition, Uri Profile, string Name), byte> AuthorEvidence { get; } = new();

    public Task ConfigureBrowserAsync()
    {
        page.FrameNavigated += (_, frame) =>
        {
            if (frame == page.MainFrame && frame.Url == sourceRequestUrl)
            {
                sourceCommitted = true;
                if (source is not null) Visited.TryAdd(source, 0);
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
            RequestFailure = null;
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
            var explicitNavigation = mainNavigation && request.Method == "GET" && requestedNavigation == url;
            var response = mainNavigation && canonicalResponse is { } cached && cached.Url == url
                ? cached
                : await ExtractorTransport.SendAsync(client, url, origins,
                    downloadingCover ? CoverDownloader.MaxCoverBytes : MaxDocumentBytes,
                    new HttpMethod(request.Method), request.PostDataBuffer, headers, token,
                    sameOriginRedirects: true, rejectRedirects: downloadingCover, canonicalNavigation: explicitNavigation);
            if (explicitNavigation && response.Url != url && response.Status is >= 200 and < 300 &&
                response.MediaType?.ToLowerInvariant() is ("text/html" or "application/xhtml+xml"))
            {
                // Commit an inert document first, then replay the validated response at its real URL.
                // The browser sees the correct base for scripts, relative resources and final cookies.
                canonicalResponse = response;
                await route.FulfillAsync(new() { Status = 200, ContentType = "text/html", Body = "<!doctype html>" });
                return;
            }
            if ((mainNavigation || downloadingCover) && response.Status >= 400 && response.Status is not (404 or 410))
                RequestFailure = new HttpRequestException("Extractor upstream request failed.", null,
                    (System.Net.HttpStatusCode)response.Status);
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
            if ((mainNavigation || coverNavigation) && exception is HttpRequestException requestFailure)
                RequestFailure = requestFailure;
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
            if (pending is { IsCompleted: false } || ++calls > 64 || input.Length > 2048 || argument.Length > (method is "fill" or "json" or "rows" ? 2048 : method == "authorEvidence" ? 512 : 64))
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
                return await NavigateAsync(destination);

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

            case "json" when Source is not null:
                var path = JsonSerializer.Deserialize<string[]>(argument);
                if (path is not { Length: > 0 and <= 12 } || path.Any(part => part is null || part.Length > 256))
                    throw new InvalidDataException("Invalid extractor JSON path.");
                return await page.Locator(input).First.EvaluateAsync<string>("""
                    (element, path) => {
                        if ((element.textContent?.length ?? 0) > 2 * 1024 * 1024) return 'null';
                        try {
                            let value = JSON.parse(element.textContent);
                            for (const key of path) value = value != null && Object.hasOwn(value, key) ? value[key] : null;
                            const result = JSON.stringify(value);
                            return result && result.length <= 32768 ? result : 'null';
                        } catch { return 'null'; }
                    }
                    """, path);

            case "rows" when Source is not null:
                var columns = JsonSerializer.Deserialize<string?[][]>(argument);
                if (columns is not { Length: > 0 and <= 12 } || columns.Any(column =>
                    column is not { Length: 2 } || column[0]?.Length > 256 || column[1]?.Length > 64))
                    throw new InvalidDataException("Invalid extractor DOM projection.");
                return await page.Locator(input).EvaluateAllAsync<string>("""
                    (elements, columns) => JSON.stringify(elements.slice(0, 64).map(row =>
                        columns.map(([selector, attribute]) => {
                            const element = selector ? row.querySelector(selector) : row;
                            return (attribute ? element?.getAttribute(attribute) : element?.textContent)?.trim().slice(0, 512) || null;
                        })))
                    """, columns);

            case "authorEvidence" when operation == "authorBiography" && Source is { } edition:
                return await RecordAuthorEvidenceAsync(edition, Resolve(input), argument) ? "true" : "false";

            case "downloadImage" when operation == "cover" && Source is not null:
                return await DownloadCoverAsync(Resolve(input), argument);

            default:
                throw new InvalidDataException("Extractor host operation is unavailable.");
        }
    }

    private async Task<string> NavigateAsync(Uri destination)
    {
        requestedNavigation = destination;
        try
        {
            var navigation = await page.GotoAsync(destination.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            if (canonicalResponse is { } canonical)
            {
                requestedNavigation = null;
                navigation = await page.GotoAsync(canonical.Url.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            }
            return navigation?.Status == 404 ? "null" : JsonSerializer.Serialize(Source?.AbsoluteUri);
        }
        finally
        {
            requestedNavigation = null;
            canonicalResponse = null;
        }
    }

    private async Task<bool> RecordAuthorEvidenceAsync(Uri edition, Uri profile, string name)
    {
        var expected = AuthorBiographyResult.Normalize(name);
        if (expected.Length == 0 || !ExtractorDefinition.Allows(profile, manifest.NavigationOrigins)) return false;

        var selector = manifest.Id switch
        {
            "wook" => "a[href*='/autor/'], table caption :is(.authors,.author) a[href^='#author-']",
            "goodreads" => ":is(.ContributorLinksList,.BookPageMetadataSection__contributor) a[href*='/author/show/']",
            "bertrand" => "a[href*='/autor/']",
            _ => "a[href]"
        };
        var links = await page.Locator(selector).EvaluateAllAsync<string[][]>("""
            elements => elements.slice(0, 512).map(element => [
                element.getAttribute('href') || '',
                (element.querySelector('.ContributorLink__name')?.textContent ?? element.textContent ?? '').trim().slice(0, 512)
            ])
            """);
        if (manifest.Id == "wook")
        {
            var profileId = profile.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(2);
            var credited = links.Any(link => link[0] == "#author-" + profileId &&
                AuthorBiographyResult.Normalize(link[1]) == expected);
            if (!credited) return false;
            links = links.Select(link => new[] { link[0], name }).ToArray();
        }
        if (manifest.Id == "bertrand")
        {
            var blocks = await page.Locator("script[type='application/ld+json']")
                .EvaluateAllAsync<string[]>("elements => elements.slice(0, 16).map(element => element.textContent.slice(0, 32768))");
            links = links.Concat(blocks.SelectMany(AuthorUrls)).ToArray();
        }
        if (!links.Any(link => AuthorBiographyResult.Normalize(link[1]) == expected &&
            Uri.TryCreate(edition, link[0], out var actual) &&
            AuthorBiographyResult.SameProfile(manifest.Id, profile, actual))) return false;
        AuthorEvidence.TryAdd((edition, profile, expected), 0);
        return true;
    }

    private static IReadOnlyList<string[]> AuthorUrls(string block)
    {
        var urls = new List<string[]>();
        try
        {
            using var document = JsonDocument.Parse(block);
            Read(document.RootElement);
        }
        catch (JsonException) { }
        return urls;

        void Read(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in node.EnumerateArray()) Read(child);
            }
            else if (node.ValueKind == JsonValueKind.Object)
            {
                if (node.TryGetProperty("author", out var author))
                {
                    if (author.ValueKind == JsonValueKind.Array)
                        foreach (var credit in author.EnumerateArray()) Add(credit);
                    else Add(author);
                }
                if (node.TryGetProperty("@graph", out var graph)) Read(graph);
            }
        }

        void Add(JsonElement credit)
        {
            if (credit.ValueKind == JsonValueKind.Object && credit.TryGetProperty("url", out var url) &&
                url.ValueKind == JsonValueKind.String && url.GetString() is { } value &&
                credit.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String &&
                name.GetString() is { } text)
                urls.Add([value, text]);
        }
    }

    private async Task<string> DownloadCoverAsync(Uri image, string mode)
    {
        Cover = null;
        RequestFailure = null;
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

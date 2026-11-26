using System.Text.Json;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Options;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace Bibliotheca.Backend.Extractors;

internal sealed class ExtractorRuntime(IOptions<PlaywrightOptions> options, IHttpClientFactory clients, ILogger<ExtractorRuntime>? logger = null)
    : PlaywrightFetcher(options)
{
    public const string ClientName = "JavaScriptExtractors";
    private static readonly TimeSpan ExecutionLimit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CleanupLimit = TimeSpan.FromSeconds(2);

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
            var json = await JintExtractorExecutor.EvaluateAsync(definition.Script, operation, input, session.CallAsync, token,
                definition.Manifest.Id, logger, structuredInput: operation == "authorBiography",
                hostBindings: ExtractorSession.HostBindings);
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
}

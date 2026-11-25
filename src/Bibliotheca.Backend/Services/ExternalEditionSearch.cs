using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Bibliotheca.Backend.Services;

internal sealed class ExternalEditionSearch(
    IEnumerable<IExternalEditionProvider> providers,
    IOptions<ExternalEditionSearchOptions> options,
    IConfiguration configuration,
    BibliothecaDbContext db,
    [FromKeyedServices(ExternalEditionSearchOptions.SectionName)] SemaphoreSlim concurrency,
    ILogger<ExternalEditionSearch> logger) : IExternalEditionSearch
{
    private IEnumerable<ProviderSettings> GetSettings() =>
        providers.Select(provider =>
        {
            var settings = options.Value.Providers.GetValueOrDefault(provider.Id) ?? new();
            var credential = string.IsNullOrWhiteSpace(settings.CredentialKey)
                ? null : configuration[settings.CredentialKey];
            var state = !settings.Enabled ? "disabled" :
                (provider.RequiresCredential || !string.IsNullOrWhiteSpace(settings.CredentialKey)) &&
                    string.IsNullOrWhiteSpace(credential)
                    ? "requiresConfiguration" : "active";
            return new ProviderSettings(provider, settings, credential, state);
        })
        .OrderBy(entry => entry.Options.Priority)
        .ThenBy(entry => entry.Provider.Id, StringComparer.Ordinal);

    public IReadOnlyList<ExternalProviderStatus> GetProviders() => GetSettings()
        .Select(entry => new ExternalProviderStatus(entry.Provider.Id, entry.Provider.Name,
            entry.State, entry.State == "active"))
        .ToArray();

    public async Task<ExternalEdition?> ResolveAsync(string providerId, string externalId, CancellationToken cancellationToken)
    {
        var entry = GetSettings().FirstOrDefault(entry => entry.State == "active" &&
            entry.Provider.Id.Equals(providerId, StringComparison.Ordinal));
        if (entry?.Provider is not IExternalEditionResolver resolver)
            return null;

        await concurrency.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(entry.Options.TimeoutSeconds));
            var edition = await resolver.GetEditionAsync(externalId, entry.Credential, timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            return edition?.ExternalId == externalId && !string.IsNullOrWhiteSpace(edition.Title) ? edition : null;
        }
        finally
        {
            concurrency.Release();
        }
    }

    public async Task<ExternalEditionSearchResponse> SearchAsync(
        string title, string? author, CancellationToken cancellationToken)
    {
        var searches = await Task.WhenAll(GetSettings()
            .Where(entry => entry.State == "active")
            .Select(entry => QueryProviderAsync(entry, title.Trim(),
                string.IsNullOrWhiteSpace(author) ? null : author.Trim(), cancellationToken)));

        var editions = searches.SelectMany(search => search.Editions)
            .GroupBy(result => result.Isbn is { } isbn
                ? (Isbn: isbn, ProviderId: "", ExternalId: "")
                : (Isbn: "", result.Source.ProviderId, result.Source.ExternalId));
        var results = editions.Select(group =>
        {
            // The most preferred provider supplies the display metadata.
            var first = group.First();
            var edition = first.Edition;
            return new ExternalEditionResult(edition.ExternalId, edition.Title, edition.Author, first.Isbn,
                edition.Language, edition.Publisher, edition.Year, edition.CoverUrl, null,
                group.Select(result => result.Source).DistinctBy(source => (source.ProviderId, source.ExternalId)).ToArray(),
                edition.Format);
        }).ToArray();

        var books = await FindCatalogueMatchesAsync(results, cancellationToken);
        var references = await FindReferenceMatchesAsync(results, cancellationToken);
        return new ExternalEditionSearchResponse(
            results.Select(result => result with
            {
                BookId = result.Isbn is { } isbn && books.TryGetValue(isbn, out var id) ? id :
                    result.Sources.Select(source => references.TryGetValue((source.ProviderId, source.ExternalId), out var bookId)
                        ? (int?)bookId : null).FirstOrDefault(bookId => bookId.HasValue)
            }).ToArray(),
            searches.Where(search => search.Failed).Select(search => search.ProviderId).ToArray());
    }

    private async Task<ProviderResult> QueryProviderAsync(
        ProviderSettings entry, string title, string? author, CancellationToken cancellationToken)
    {
        var provider = entry.Provider;
        await concurrency.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(entry.Options.TimeoutSeconds));
            var editions = await provider.SearchAsync(title, author, entry.Options.ResultLimit, entry.Credential, timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            return new ProviderResult(provider.Id, false, editions
                .Take(entry.Options.ResultLimit)
                .Where(edition => !string.IsNullOrWhiteSpace(edition.ExternalId) && !string.IsNullOrWhiteSpace(edition.Title))
                .Select(edition => new SourcedEdition(edition, EditionIsbn.Normalize(edition.Isbn),
                    new ExternalEditionSource(provider.Id, provider.Name, edition.ExternalId, edition.SourceUrl)))
                .ToArray());
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Provider exceptions can include request URLs or credentials.
            logger.LogWarning("External edition provider {ProviderId} failed ({FailureType}).",
                provider.Id, exception.GetType().Name);
            return new ProviderResult(provider.Id, true, []);
        }
        finally
        {
            concurrency.Release();
        }
    }

    private async Task<Dictionary<string, int>> FindCatalogueMatchesAsync(
        ExternalEditionResult[] results, CancellationToken cancellationToken)
    {
        var isbns = results.Select(result => result.Isbn).OfType<string>().Distinct()
            .SelectMany(EditionIsbn.CatalogueVariants).Distinct().ToArray();
        if (isbns.Length == 0)
            return [];

        var books = await db.Books
            .Where(book => isbns.Contains(book.Isbn.Replace("-", "").Replace(" ", "").ToUpper()))
            .OrderBy(book => book.BookId)
            .Select(book => new { book.BookId, book.Isbn })
            .ToListAsync(cancellationToken);
        return books.GroupBy(book => EditionIsbn.Normalize(book.Isbn)!)
            .ToDictionary(group => group.Key, group => group.First().BookId);
    }

    private async Task<Dictionary<(string ProviderId, string ExternalId), int>> FindReferenceMatchesAsync(
        ExternalEditionResult[] results, CancellationToken cancellationToken)
    {
        var sources = results.SelectMany(result => result.Sources).ToArray();
        if (sources.Length == 0)
            return [];

        var providerIds = sources.Select(source => source.ProviderId).Distinct().ToArray();
        var externalIds = sources.Select(source => source.ExternalId).Distinct().ToArray();
        return await db.BookEditionReferences.Where(reference =>
            providerIds.Contains(reference.ProviderId) && externalIds.Contains(reference.ExternalId))
            .ToDictionaryAsync(reference => (reference.ProviderId, reference.ExternalId),
                reference => reference.BookId, cancellationToken);
    }

    private sealed record ProviderSettings(
        IExternalEditionProvider Provider, ExternalEditionProviderOptions Options, string? Credential, string State);
    private sealed record SourcedEdition(ExternalEdition Edition, string? Isbn, ExternalEditionSource Source);
    private sealed record ProviderResult(string ProviderId, bool Failed, SourcedEdition[] Editions);
}

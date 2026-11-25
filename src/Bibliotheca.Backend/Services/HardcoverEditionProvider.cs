using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Bibliotheca.Backend.Fetchers;

namespace Bibliotheca.Backend.Services;

internal sealed class HardcoverEditionProvider(IHttpClientFactory clients)
    : IExternalEditionProvider, IExternalEditionResolver, IDisposable
{
    public const string ProviderId = "hardcover";
    private const string Endpoint = "https://api.hardcover.app/v1/graphql";
    private const string SearchQuery = """
        query SearchBooks($query: String!, $limit: Int!) {
          search(query: $query, query_type: "Book", per_page: $limit) { ids error }
        }
        """;
    private const string EditionFields = """
        id title isbn_13 isbn_10 edition_format release_year
        language { language }
        publisher { name }
        image { url }
        contributions { author { name } contribution contributor_role { name } }
        book { slug contributions { author { name } contribution contributor_role { name } } }
        """;
    private const string SearchEditionsQuery = """
        query SearchEditions($ids: [Int!]!, $limit: Int!) {
          books(where: {id: {_in: $ids}}) {
            id
            editions(limit: $limit, order_by: [{score: desc}, {id: asc}]) { ...EditionFields }
          }
        }
        fragment EditionFields on editions {
        """ + EditionFields + "}";
    private const string ResolveEditionQuery = """
        query ResolveEdition($id: Int!) {
          editions_by_pk(id: $id) { ...EditionFields }
        }
        fragment EditionFields on editions {
        """ + EditionFields + "}";

    private readonly TokenBucketRateLimiter limiter = new(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 1,
        TokensPerPeriod = 1,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        AutoReplenishment = true,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 32
    });

    public string Id => ProviderId;
    public string Name => "Hardcover";
    public bool RequiresCredential => true;

    public async Task<IReadOnlyList<ExternalEdition>> SearchAsync(
        string title, string? author, int limit, string? credential, CancellationToken cancellationToken)
    {
        var phrase = string.IsNullOrWhiteSpace(author) ? title : $"{title} {author}";
        var workLimit = Math.Min(5, limit);
        using var client = clients.CreateClient(ProviderId);
        var search = await QueryAsync<SearchData>(client, SearchQuery, new { query = phrase, limit = workLimit },
            credential, cancellationToken);
        if (!string.IsNullOrWhiteSpace(search.Search?.Error))
            throw new HttpRequestException("Hardcover could not perform the search.");

        var ids = (search.Search?.Ids ?? []).OfType<int>().Where(id => id > 0).Distinct().Take(workLimit).ToArray();
        if (ids.Length == 0)
            return [];

        var editionLimit = (limit + ids.Length - 1) / ids.Length;
        var result = await QueryAsync<BooksData>(client, SearchEditionsQuery, new { ids, limit = editionLimit },
            credential, cancellationToken);
        var workOrder = ids.Select((id, index) => (id, index)).ToDictionary(item => item.id, item => item.index);
        return (result.Books ?? []).Where(book => workOrder.ContainsKey(book.Id)).OrderBy(book => workOrder[book.Id])
            .SelectMany(book => book.Editions ?? []).Select(ToEdition).OfType<ExternalEdition>().Take(limit).ToArray();
    }

    public async Task<ExternalEdition?> GetEditionAsync(
        string externalId, string? credential, CancellationToken cancellationToken)
    {
        if (!int.TryParse(externalId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            return null;

        using var client = clients.CreateClient(ProviderId);
        var result = await QueryAsync<EditionData>(client, ResolveEditionQuery, new { id }, credential, cancellationToken);
        return result.Edition?.Id == id ? ToEdition(result.Edition) : null;
    }

    private async Task<T> QueryAsync<T>(HttpClient client, string query, object variables,
        string? credential, CancellationToken cancellationToken) where T : class
    {
        if (string.IsNullOrWhiteSpace(credential))
            throw new InvalidOperationException("Hardcover requires a personal access token.");

        using var lease = await limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
            throw new HttpRequestException("Hardcover rate limit queue is full.", null, HttpStatusCode.TooManyRequests);

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(new { query, variables })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        request.Headers.UserAgent.ParseAdd("Bibliotheca/1.0");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<Envelope<T>>(cancellationToken);
        if (envelope?.Data is null || envelope.Errors is { Length: > 0 })
            throw new HttpRequestException("Hardcover returned a GraphQL error.");

        return envelope.Data;
    }

    private static ExternalEdition? ToEdition(Edition row)
    {
        if (row.Id <= 0 || string.IsNullOrWhiteSpace(row.Title))
            return null;

        var authors = (row.Contributions ?? []).Concat(row.Book?.Contributions ?? [])
            .Where(item => ContributorRoles.IsAuthor(item.ContributorRole?.Name ?? item.ContributionName))
            .Select(item => item.Author?.Name).OfType<string>()
            .Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var isbn = EditionIsbn.Normalize(row.Isbn13) ?? EditionIsbn.Normalize(row.Isbn10);
        return new ExternalEdition(row.Id.ToString(CultureInfo.InvariantCulture), row.Title,
            authors.Length > 0 ? string.Join(", ", authors) : null,
            isbn, row.Language?.Language, row.Publisher?.Name, row.ReleaseYear, row.Image?.Url,
            row.Book?.Slug is { Length: > 0 } slug ? $"https://hardcover.app/books/{Uri.EscapeDataString(slug)}/editions" : null,
            row.EditionFormat, new ExternalEditionDetails(null, authors, null, null, null, null, null));
    }

    public void Dispose() => limiter.Dispose();

    private sealed record Envelope<T>(T? Data, JsonElement[]? Errors);
    private sealed record SearchData(SearchResult? Search);
    private sealed record SearchResult(int?[]? Ids, string? Error);
    private sealed record BooksData(Work[]? Books);
    private sealed record Work(int Id, Edition[]? Editions);
    private sealed record EditionData([property: JsonPropertyName("editions_by_pk")] Edition? Edition);
    private sealed record Edition(int Id, string? Title,
        [property: JsonPropertyName("isbn_13")] string? Isbn13,
        [property: JsonPropertyName("isbn_10")] string? Isbn10,
        [property: JsonPropertyName("edition_format")] string? EditionFormat,
        [property: JsonPropertyName("release_year")] int? ReleaseYear,
        LanguageInfo? Language, NamedRecord? Publisher, ImageInfo? Image, BookInfo? Book, Contribution[]? Contributions);
    private sealed record LanguageInfo(string? Language);
    private sealed record NamedRecord(string? Name);
    private sealed record ImageInfo(string? Url);
    private sealed record BookInfo(string? Slug, Contribution[]? Contributions);
    private sealed record Contribution(NamedRecord? Author,
        [property: JsonPropertyName("contribution")] string? ContributionName,
        [property: JsonPropertyName("contributor_role")] NamedRecord? ContributorRole);
}

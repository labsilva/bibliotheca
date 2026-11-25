using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace Bibliotheca.Backend.Services;

internal sealed partial class OpenLibraryEditionProvider
    : IExternalEditionProvider, IExternalEditionResolver, IAuthorPhotoLookup, IDisposable
{
    public const string ProviderId = "openLibrary";
    private const string Origin = "https://openlibrary.org";
    private readonly IHttpClientFactory clients;
    private readonly ILogger<OpenLibraryEditionProvider> logger;
    private readonly TokenBucketRateLimiter limiter;
    private readonly string? contact;

    public OpenLibraryEditionProvider(
        IHttpClientFactory clients, IConfiguration configuration, ILogger<OpenLibraryEditionProvider> logger)
    {
        this.clients = clients;
        this.logger = logger;
        var configuredContact = configuration["ExternalEditionSearch:Providers:openLibrary:ContactEmail"];
        if (MailAddress.TryCreate(configuredContact, out var address) && address.Address == configuredContact)
            contact = address.Address;

        limiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 1,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromMilliseconds(contact is null ? 1000 : 350),
            AutoReplenishment = true,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 32
        });
    }

    public string Id => ProviderId;
    public string Name => "Open Library";

    public async Task<IReadOnlyDictionary<string, string>> FindPhotosAsync(
        Fetchers.FetchedBook book, IReadOnlyList<string> authors, CancellationToken cancellationToken)
    {
        var profiles = await ReadProfilesAsync(book, authors, includeBiography: false, cancellationToken);
        return profiles.Where(pair => pair.Value.PhotoUrl is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.PhotoUrl!, StringComparer.Ordinal);
    }

    public Task<IReadOnlyDictionary<string, AuthorProfile>> FindProfilesAsync(
        Fetchers.FetchedBook book, IReadOnlyList<string> authors, CancellationToken cancellationToken) =>
        ReadProfilesAsync(book, authors, includeBiography: true, cancellationToken);

    public async Task<AuthorProfile?> FindProfileByKeyAsync(string sourceAuthorKey, string authorName, CancellationToken cancellationToken)
    {
        if (!AuthorKeyPattern().IsMatch(sourceAuthorKey))
            return null;

        using var client = clients.CreateClient(ProviderId);
        var person = await GetAsync<AuthorRecord>(client, sourceAuthorKey + ".json", cancellationToken);
        if (person is null || person.Key != sourceAuthorKey || !MatchesAuthor(person, authorName))
            return null;
        return await ToAuthorProfileAsync(client, person, includeBiography: true, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, AuthorProfile>> ReadProfilesAsync(
        Fetchers.FetchedBook book, IReadOnlyList<string> authors, bool includeBiography, CancellationToken cancellationToken)
    {
        var profiles = new Dictionary<string, AuthorProfile>(StringComparer.Ordinal);
        var editionKey = OpenLibraryEditionKey(book.Url);
        var isbn = EditionIsbn.Normalize(book.Isbn);
        if (editionKey is null && isbn is null)
            return profiles;

        using var client = clients.CreateClient(ProviderId);
        var record = await GetAsync<EditionRecord>(client, (editionKey ?? $"/isbn/{isbn}") + ".json", cancellationToken);
        if (record is null || !EditionPathPattern().IsMatch(record.Key ?? ""))
            return profiles;

        if (editionKey is not null)
        {
            if (record.Key != editionKey)
                return profiles;
        }
        else if (!(record.Isbn13 ?? []).Concat(record.Isbn10 ?? []).Any(value => EditionIsbn.Normalize(value) == isbn) ||
            !TitlesMatch(book.Title, book.Subtitle, record.Title))
        {
            return profiles;
        }

        var keys = (record.Authors ?? []).Select(author => author.Key).Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length == 0 || keys.Any(key => !AuthorKeyPattern().IsMatch(key ?? "")))
            return profiles;

        var people = new List<AuthorRecord>();
        foreach (var key in keys)
        {
            var person = await GetAsync<AuthorRecord>(client, key + ".json", cancellationToken);
            if (person?.Key != key || string.IsNullOrWhiteSpace(person?.Name))
                return profiles;
            people.Add(person);
        }

        foreach (var author in authors)
        {
            // A second matching record is ambiguous even when only one of the records has a photo.
            var matches = people.Where(person => MatchesAuthor(person, author)).Take(2).ToArray();
            if (matches.Length != 1)
                continue;
            profiles[author] = await ToAuthorProfileAsync(client, matches[0], includeBiography, cancellationToken);
        }

        return profiles;
    }

    private static bool MatchesAuthor(AuthorRecord person, string authorName) =>
        !string.IsNullOrWhiteSpace(person.Name) && (SameName(authorName, person.Name) ||
            (person.AlternateNames ?? []).Any(name => SameName(authorName, name)));

    private async Task<AuthorProfile> ToAuthorProfileAsync(HttpClient client, AuthorRecord person,
        bool includeBiography, CancellationToken cancellationToken)
    {
        var photo = person.Photos?.FirstOrDefault(id => id > 0) is > 0 and var photoId
            ? $"https://covers.openlibrary.org/a/id/{photoId.ToString(CultureInfo.InvariantCulture)}-M.jpg?default=false"
            : null;
        var biography = includeBiography ? await GetBiographyAsync(client, person, cancellationToken) : null;
        return new(person.Key, photo, biography?.Text, biography?.SourceUrl);
    }

    private static string? OpenLibraryEditionKey(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Host != "openlibrary.org" || !uri.IsDefaultPort)
            return null;

        var path = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return path is ["books", var id, ..] && EditionKeyPattern().IsMatch(id) ? $"/books/{id}" : null;
    }

    private static bool SameName(string name, string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate) &&
        string.Equals(NormalizeName(name), NormalizeName(candidate), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeName(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Normalize(NormalizationForm.FormC);

    private static bool TitlesMatch(string title, string? subtitle, string? candidate)
    {
        var expected = NormalizeTitle(title);
        var actual = NormalizeTitle(candidate);
        return expected.Length > 0 &&
            (actual == expected || actual == expected + NormalizeTitle(subtitle));
    }

    private static string NormalizeTitle(string? title) =>
        string.Concat((title ?? "").Normalize(NormalizationForm.FormC).Where(char.IsLetterOrDigit)).ToUpperInvariant();

    public async Task<IReadOnlyList<ExternalEdition>> SearchAsync(
        string title, string? author, int limit, string? credential, CancellationToken cancellationToken)
    {
        var path = QueryHelpers.AddQueryString("/search.json", new Dictionary<string, string?>
        {
            ["title"] = title,
            ["author"] = author,
            ["limit"] = Math.Min(3, limit).ToString(CultureInfo.InvariantCulture),
            ["fields"] = "key,author_name"
        });
        using var client = clients.CreateClient(ProviderId);
        var search = await GetAsync<WorkSearch>(client, path, cancellationToken);
        var works = (search?.Docs ?? []).Where(work => WorkKeyPattern().IsMatch(work.Key ?? ""))
            .Take(Math.Min(3, limit)).ToArray();
        if (works.Length == 0)
            return [];

        var editions = new List<ExternalEdition>();
        Exception? failure = null;
        for (var i = 0; i < works.Length; i++)
        {
            var work = works[i];
            var remaining = limit - editions.Count;
            if (remaining <= 0)
                break;

            var pageSize = (remaining + works.Length - i - 1) / (works.Length - i);
            try
            {
                var page = await GetAsync<EditionPage>(client, $"{work.Key}/editions.json?limit={pageSize}", cancellationToken);
                editions.AddRange((page?.Entries ?? []).Select(record => ToEdition(record, work.AuthorNames))
                    .OfType<ExternalEdition>().Take(pageSize));
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException)
            {
                failure ??= exception;
                logger.LogWarning("Open Library edition lookup failed ({FailureType}).", exception.GetType().Name);
                if (exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
                    break;
            }
        }

        if (editions.Count == 0 && failure is not null)
            throw new HttpRequestException("Open Library could not load editions.", failure);

        return editions;
    }

    public async Task<ExternalEdition?> GetEditionAsync(string externalId, string? credential, CancellationToken cancellationToken)
    {
        if (!EditionKeyPattern().IsMatch(externalId))
            return null;

        using var client = clients.CreateClient(ProviderId);
        var record = await GetAsync<EditionRecord>(client, $"/books/{externalId}.json", cancellationToken);
        if (record is null || record.Key != $"/books/{externalId}")
            return null;

        var names = new List<string>();
        foreach (var authorKey in (record.Authors ?? []).Select(author => author.Key)
            .Where(key => AuthorKeyPattern().IsMatch(key ?? "")).Distinct(StringComparer.Ordinal))
        {
            try
            {
                var details = await GetAsync<AuthorRecord>(client, authorKey + ".json", cancellationToken);
                if (!string.IsNullOrWhiteSpace(details?.Name))
                    names.Add(details.Name);
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException)
            {
                logger.LogWarning("Open Library author lookup failed ({FailureType}).", exception.GetType().Name);
                if (exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
                    break;
            }
        }

        return ToEdition(record, names.ToArray());
    }

    private async Task<T?> GetAsync<T>(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var lease = await limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
            throw new HttpRequestException("Open Library rate limit queue is full.", null, HttpStatusCode.TooManyRequests);

        using var request = new HttpRequestMessage(HttpMethod.Get, Origin + path);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Bibliotheca", "1.0"));
        if (contact is not null)
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue($"({contact})"));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private static ExternalEdition? ToEdition(EditionRecord record, string[]? authors)
    {
        const string prefix = "/books/";
        if (record.Key is not { } key || !key.StartsWith(prefix, StringComparison.Ordinal) ||
            !EditionKeyPattern().IsMatch(key[prefix.Length..]) || string.IsNullOrWhiteSpace(record.Title))
            return null;

        var isbn = (record.Isbn13 ?? []).Concat(record.Isbn10 ?? [])
            .Select(EditionIsbn.Normalize).FirstOrDefault(value => value is not null);
        var year = YearPattern().Match(record.PublishDate ?? "").Value;
        var language = record.Languages?.FirstOrDefault()?.Key?.Split('/').LastOrDefault();
        var cover = record.Covers?.FirstOrDefault(id => id > 0) ?? 0;
        return new ExternalEdition(key[prefix.Length..], record.Title,
            authors is { Length: > 0 } ? string.Join(", ", authors) : null,
            isbn, language, record.Publishers?.FirstOrDefault(),
            int.TryParse(year, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
            cover > 0 ? $"https://covers.openlibrary.org/b/id/{cover}-M.jpg" : null, Origin + key,
            Details: authors is { Length: > 0 } ? new(null, authors, null, null, null, null, null) : null);
    }

    public void Dispose() => limiter.Dispose();

    [GeneratedRegex(@"\A/works/OL[0-9]+W\z")]
    private static partial Regex WorkKeyPattern();

    [GeneratedRegex(@"\AOL[0-9]+M\z")]
    private static partial Regex EditionKeyPattern();

    [GeneratedRegex(@"\A/books/OL[0-9]+M\z")]
    private static partial Regex EditionPathPattern();

    [GeneratedRegex(@"\A/authors/OL[0-9]+A\z")]
    private static partial Regex AuthorKeyPattern();

    [GeneratedRegex(@"\b[0-9]{4}\b")]
    private static partial Regex YearPattern();

    private sealed record WorkSearch(Work[]? Docs);
    private sealed record Work(string? Key, [property: JsonPropertyName("author_name")] string[]? AuthorNames);
    private sealed record EditionPage(EditionRecord[]? Entries);
    private sealed record AuthorRecord(string? Key, string? Name, int[]? Photos,
        [property: JsonPropertyName("alternate_names")] string[]? AlternateNames,
        [property: JsonPropertyName("birth_date")] string? BirthDate,
        [property: JsonPropertyName("death_date")] string? DeathDate,
        [property: JsonPropertyName("birth_place")] string? BirthPlace,
        [property: JsonPropertyName("remote_ids")] RemoteIdentifiers? RemoteIds,
        Link[]? Links, string? Wikipedia);
    private sealed record RemoteIdentifiers(string? Wikidata);
    private sealed record Link(string? Url);
    private sealed record Reference(string? Key);
    private sealed record EditionRecord(string? Key, string? Title, string[]? Publishers, Reference[]? Languages,
        int[]? Covers, Reference[]? Authors, [property: JsonPropertyName("publish_date")] string? PublishDate,
        [property: JsonPropertyName("isbn_13")] string[]? Isbn13,
        [property: JsonPropertyName("isbn_10")] string[]? Isbn10);
}

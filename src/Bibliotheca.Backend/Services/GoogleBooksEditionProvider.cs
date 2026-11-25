using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;

namespace Bibliotheca.Backend.Services;

internal sealed partial class GoogleBooksEditionProvider(IHttpClientFactory clients)
    : IExternalEditionProvider, IExternalEditionResolver
{
    public const string ProviderId = "googleBooks";
    private const string BaseUrl = "https://www.googleapis.com/books/v1/volumes";

    public string Id => ProviderId;
    public string Name => "Google Books";
    public bool RequiresCredential => true;

    public async Task<IReadOnlyList<ExternalEdition>> SearchAsync(
        string title, string? author, int limit, string? credential, CancellationToken cancellationToken)
    {
        var query = $"intitle:\"{title.Replace('"', ' ')}\"";
        if (!string.IsNullOrWhiteSpace(author))
            query += $" inauthor:\"{author.Replace('"', ' ')}\"";

        var url = QueryHelpers.AddQueryString(BaseUrl, new Dictionary<string, string?>
        {
            ["q"] = query,
            ["maxResults"] = Math.Min(40, limit).ToString(CultureInfo.InvariantCulture),
            ["printType"] = "books",
            ["projection"] = "full"
        });
        using var client = clients.CreateClient(ProviderId);
        var result = await GetAsync<VolumeList>(client, url, credential, cancellationToken);
        return (result?.Items ?? []).Select(ToEdition).OfType<ExternalEdition>().Take(limit).ToArray();
    }

    public async Task<ExternalEdition?> GetEditionAsync(
        string externalId, string? credential, CancellationToken cancellationToken)
    {
        if (!IdPattern().IsMatch(externalId))
            return null;

        using var client = clients.CreateClient(ProviderId);
        var volume = await GetAsync<Volume>(client, $"{BaseUrl}/{externalId}", credential, cancellationToken);
        return volume?.Id == externalId ? ToEdition(volume) : null;
    }

    private static async Task<T?> GetAsync<T>(
        HttpClient client, string url, string? credential, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(credential))
            throw new InvalidOperationException("Google Books requires an API key.");

        using var response = await client.GetAsync(QueryHelpers.AddQueryString(url, "key", credential),
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private static ExternalEdition? ToEdition(Volume volume)
    {
        if (volume.Id is not { } id || !IdPattern().IsMatch(id) ||
            volume.VolumeInfo is not { } info || string.IsNullOrWhiteSpace(info.Title))
            return null;

        var isbn = (info.IndustryIdentifiers ?? [])
            .Where(identifier => identifier.Type is "ISBN_13" or "ISBN_10")
            .OrderBy(identifier => identifier.Type == "ISBN_13" ? 0 : 1)
            .Select(identifier => EditionIsbn.Normalize(identifier.Identifier))
            .FirstOrDefault(value => value is not null);
        var year = info.PublishedDate is { Length: >= 4 } date &&
            int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? (int?)value : null;
        var authors = (info.Authors ?? []).Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new ExternalEdition(id, info.Title, authors.Length > 0 ? string.Join(", ", authors) : null,
            isbn, info.Language, info.Publisher, year, info.ImageLinks?.Thumbnail ?? info.ImageLinks?.SmallThumbnail,
            $"https://books.google.com/books?id={id}", Details: new(null, authors, null, null, null, null, null));
    }

    [GeneratedRegex(@"\A[A-Za-z0-9_-]{1,128}\z")]
    private static partial Regex IdPattern();

    private sealed record VolumeList(Volume[]? Items);
    private sealed record Volume(string? Id, VolumeInfo? VolumeInfo);
    private sealed record VolumeInfo(string? Title, string[]? Authors, string? Publisher, string? PublishedDate,
        string? Language, IndustryIdentifier[]? IndustryIdentifiers, Images? ImageLinks);
    private sealed record IndustryIdentifier(string? Type, string? Identifier);
    private sealed record Images(string? Thumbnail, string? SmallThumbnail);
}

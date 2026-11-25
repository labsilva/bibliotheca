using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Bibliotheca.Backend.Services;

internal sealed partial class OpenLibraryEditionProvider
{
    private sealed record Biography(string Text, string SourceUrl);

    private async Task<Biography?> GetBiographyAsync(HttpClient client, AuthorRecord person, CancellationToken cancellationToken)
    {
        var fallback = CatalogueBiography(person);
        // A record can link to several articles. Only follow a single article which also
        // identifies the same Wikidata item as the edition's author record.
        if (person.RemoteIds?.Wikidata is not { Length: > 1 } item
            || item[0] != 'Q' || !item.Skip(1).All(char.IsAsciiDigit))
            return fallback;
        var titles = (person.Links ?? []).Select(link => link.Url).Append(person.Wikipedia)
            .Select(WikipediaTitle).OfType<string>().Distinct(StringComparer.Ordinal).Take(2).ToArray();
        if (titles.Length != 1)
            return fallback;

        var url = QueryHelpers.AddQueryString("https://en.wikipedia.org/w/api.php", new Dictionary<string, string?>
        {
            ["action"] = "query", ["format"] = "json", ["formatversion"] = "2", ["redirects"] = "1",
            ["prop"] = "extracts|pageprops", ["exintro"] = "1", ["explaintext"] = "1",
            ["exchars"] = "900", ["titles"] = titles[0],
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Bibliotheca", "1.0"));
        if (contact is not null)
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue($"({contact})"));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<WikipediaResult>(deadline.Token);
            if (result?.Query?.Pages is [var page] && page.Ns == 0 && page.PageId > 0 &&
                page.PageProps?.GetValueOrDefault("wikibase_item") == item && !page.PageProps.ContainsKey("disambiguation") &&
                !string.IsNullOrWhiteSpace(page.Extract))
                return new(page.Extract.Trim(), $"https://en.wikipedia.org/?curid={page.PageId}");
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
            exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            logger.LogWarning("Wikipedia biography unavailable ({FailureType}).", exception.GetType().Name);
        }
        return fallback;
    }

    private static string? WikipediaTitle(string? link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") || uri.Host != "en.wikipedia.org" ||
            !uri.IsDefaultPort || uri.UserInfo.Length > 0 || !uri.AbsolutePath.StartsWith("/wiki/", StringComparison.Ordinal))
            return null;
        var title = Uri.UnescapeDataString(uri.AbsolutePath[6..]).Replace('_', ' ').Trim();
        return title.Length is > 0 and <= 255 && !title.Contains('|') ? title : null;
    }

    private static Biography? CatalogueBiography(AuthorRecord person)
    {
        var born = person.BirthDate?.Trim();
        var place = person.BirthPlace?.Trim();
        var died = person.DeathDate?.Trim();
        var sentences = new List<string>();
        if (!string.IsNullOrEmpty(born) || !string.IsNullOrEmpty(place))
        {
            var location = string.IsNullOrEmpty(place) ? "" : $" in {place}";
            var date = DatePhrase(born);
            sentences.Add($"{person.Name} was born{date}{location}.");
        }
        if (!string.IsNullOrEmpty(died))
            sentences.Add($"{person.Name} died{DatePhrase(died)}.");
        return sentences.Count > 0 ? new(string.Join(" ", sentences), Origin + person.Key) : null;
    }

    private static string DatePhrase(string? date)
    {
        if (string.IsNullOrEmpty(date))
            return "";
        return date.Length == 4 && date.All(char.IsAsciiDigit) ? $" in {date}" : $" on {date}";
    }

    private sealed record WikipediaResult(WikipediaQuery? Query);
    private sealed record WikipediaQuery(WikipediaPage[]? Pages);
    private sealed record WikipediaPage(int Ns, long PageId, Dictionary<string, string?>? PageProps, string? Extract);
}

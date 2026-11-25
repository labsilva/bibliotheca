using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Bibliotheca.Backend.Services;

public sealed record WookAuthorEdition(string? Isbn, string? WookId);
public sealed record WookAuthorResult(string? PhotoUrl, string? Biography, string? SourceUrl, bool Ambiguous = false);
public interface IWookAuthorLookup
{
    Task<WookAuthorResult?> FindAsync(string name, IReadOnlyList<WookAuthorEdition> editions, CancellationToken cancellationToken);
}

public sealed class WookAuthorLookup(IWookPageReader reader, IOptions<ExternalEditionSearchOptions> options) : IWookAuthorLookup
{
    public async Task<WookAuthorResult?> FindAsync(string name, IReadOnlyList<WookAuthorEdition> editions, CancellationToken cancellationToken)
    {
        if (!options.Value.Providers.TryGetValue("wook", out var settings) || !settings.Enabled) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        var token = timeout.Token;
        var candidates = new Dictionary<string, Uri>();
        foreach (var edition in editions.Take(3))
        {
            var isbn = EditionIsbn.Normalize(edition.Isbn);
            Uri? address = edition.WookId is { Length: > 0 } id ? WookCatalogue.EditionAddress("/" + id.TrimStart('/'), WookCatalogue.Origin) : null;
            if (address is null && isbn is not null)
            {
                var search = new Uri(QueryHelpers.AddQueryString(new Uri(WookCatalogue.Origin, "pesquisa").AbsoluteUri, "keyword", isbn));
                var searchPage = await ReadAsync(search, token);
                if (WookCatalogue.EditionAddress(searchPage.Url, WookCatalogue.Origin) is { } direct) address = direct;
                else
                {
                    var results = WookCatalogue.ReadSearch(searchPage, Math.Min(settings.ResultLimit, 10), token);
                    var matches = results.Where(item => EditionIsbn.Normalize(item.Isbn) == isbn)
                        .Select(item => WookCatalogue.EditionAddress(item.SourceUrl, WookCatalogue.Origin)).OfType<Uri>().Distinct().Take(2).ToArray();
                    if (matches.Length == 1) address = matches[0];
                    else if (matches.Length == 0 && results.Count == 1)
                        address = WookCatalogue.EditionAddress(results[0].SourceUrl, WookCatalogue.Origin);
                }
            }
            if (address is null) continue;
            var page = await ReadAsync(address, token);
            var actual = WookCatalogue.EditionAddress(page.Url, WookCatalogue.Origin);
            if (actual is null || WookCatalogue.ProductKey(actual) != WookCatalogue.ProductKey(address)) continue;
            var product = WookCatalogue.ReadProduct(page.Html, actual);
            if (isbn is not null && EditionIsbn.Normalize(product.Isbn) != isbn) continue;
            var doc = new HtmlParser().ParseDocument(page.Html);
            var ids = doc.QuerySelectorAll("table caption .authors a[href^='#author-'], table caption .author a[href^='#author-']")
                .Where(link => Normalize(link.TextContent) == Normalize(name))
                .Select(link => link.GetAttribute("href")![8..]).Where(id => id.Length > 0 && id.All(char.IsAsciiDigit)).Distinct().ToArray();
            if (ids.Length > 1) return new(null, null, null, true);
            if (ids.Length == 0) continue;
            var idValue = ids[0];
            var links = doc.QuerySelectorAll("a[href*='/autor/']")
                .Select(link => Uri.TryCreate(WookCatalogue.Origin, link.GetAttribute("href"), out var uri) ? uri : null)
                .Where(uri => uri is not null && WookCatalogue.IsWookAddress(uri.AbsoluteUri) &&
                    Regex.IsMatch(uri.AbsolutePath, @"^/autor/[^/]+/" + Regex.Escape(idValue) + @"(?:/\d+)?/?$"))
                .Select(uri => new Uri(WookCatalogue.Origin, string.Join('/', uri!.AbsolutePath.Split('/').Take(4))))
                .Distinct().ToArray();
            if (links.Length == 1) candidates[idValue] = links[0];
        }
        if (candidates.Count > 1) return new(null, null, null, true);
        if (candidates.Count == 0) return null;
        var selected = candidates.Single();
        var authorPage = await ReadAsync(selected.Value, token);
        if (!Uri.TryCreate(authorPage.Url, UriKind.Absolute, out var authorUrl) || !WookCatalogue.IsWookAddress(authorPage.Url) ||
            !Regex.IsMatch(authorUrl.AbsolutePath, @"^/autor/[^/]+/" + Regex.Escape(selected.Key) + @"(?:/\d+)?/?$")) return null;
        var authorDoc = new HtmlParser().ParseDocument(authorPage.Html);
        var person = authorDoc.QuerySelector("[itemtype='https://schema.org/Person']");
        if (person is null || Normalize(person.QuerySelector("h1[itemprop='name']")?.TextContent) != Normalize(name)) return null;
        var bio = Regex.Replace(person.QuerySelector("[itemprop='description'] .bio-text")?.TextContent ?? "", @"\s+", " ").Trim();
        var image = person.QuerySelector("img[itemprop='image']")?.GetAttribute("src");
        if (!Uri.TryCreate(image, UriKind.Absolute, out var imageUrl) || imageUrl.Scheme != "https" || imageUrl.Host != "img.wook.pt") image = null;
        return new(image, bio.Length > 0 ? bio : null, selected.Value.AbsoluteUri);
    }

    private async Task<WookPage> ReadAsync(Uri url, CancellationToken token)
    {
        var page = await reader.ReadAsync(url, token);
        if (page.Status is < 200 or >= 300 || !WookCatalogue.IsWookAddress(page.Url)) throw new InvalidDataException("Wook page unavailable.");
        return page;
    }
    private static string Normalize(string? value) => new((value ?? "").Normalize(NormalizationForm.FormD)
        .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

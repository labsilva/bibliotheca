using AngleSharp.Html.Parser;
using Bibliotheca.Backend.Options;
using Bibliotheca.Backend.Services;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bibliotheca.Backend.Fetchers;

public record BertrandPage(string Url, int Status, string Html);
public interface IBertrandReader
{
    Task<BertrandPage> ReadAsync(Uri url, CancellationToken token);
    Task<FetchedCover> ReadCoverAsync(Uri product, string isbn, CancellationToken token);
}

public sealed class BertrandReader(IOptions<PlaywrightOptions> options) : PlaywrightFetcher(options), IBertrandReader
{
    public Task<BertrandPage> ReadAsync(Uri url, CancellationToken token) => WithBrowserAsync(async browser =>
    {
        if (!BertrandCatalogue.IsSite(url)) throw new InvalidDataException("Invalid Bertrand URL.");
        await using var context = await NewContextAsync(browser);
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(url.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (response is null || !Uri.TryCreate(page.Url, UriKind.Absolute, out var actual) || !BertrandCatalogue.IsSite(actual))
            throw new InvalidDataException("Bertrand navigation failed.");
        if (response.Status >= 400) throw new PageNavigationException(response.Status, actual.AbsoluteUri);
        return new BertrandPage(actual.AbsoluteUri, response.Status, await page.ContentAsync());
    }, token);

    public Task<FetchedCover> ReadCoverAsync(Uri product, string isbn, CancellationToken token) => WithBrowserAsync(async browser =>
    {
        await using var context = await NewContextAsync(browser);
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync(product.AbsoluteUri, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (response is null || response.Status >= 400 || page.Url != product.AbsoluteUri) return new FetchedCover { Success = false };
        var parsed = BertrandCatalogue.Parse(await page.ContentAsync(), product, isbn);
        if (parsed?.Image is not { } image || !BertrandCatalogue.IsSite(image)) return new FetchedCover { Success = false };
        return await CoverDownloader.DownloadInPageAsync(page, image.AbsoluteUri);
    }, token);
}

public sealed record BertrandProduct(FetchedBook Book, Uri? Image, IReadOnlyList<(string Name, Uri Url)> Authors);
public static class BertrandCatalogue
{
    private static readonly Uri Origin = new("https://www.bertrand.pt/");
    public static bool IsSite(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        (uri.Host.Equals("bertrand.pt", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".bertrand.pt", StringComparison.OrdinalIgnoreCase));
    public static bool IsProduct(Uri uri) => IsSite(uri) && Regex.IsMatch(uri.AbsolutePath, @"^/livro/[^/]+/\d+/?$", RegexOptions.IgnoreCase);
    public static IReadOnlyList<Uri> Candidates(BertrandPage page) => new HtmlParser().ParseDocument(page.Html).QuerySelectorAll("a[href]")
        .Select(a => Uri.TryCreate(new Uri(page.Url), a.GetAttribute("href"), out var u) ? u : null)
        .Where(u => u is not null && IsProduct(u)).Select(u => new Uri(u!.GetLeftPart(UriPartial.Path))).Distinct().Take(10).ToArray();
    public static BertrandProduct? Parse(string html, Uri url, string expectedIsbn)
    {
        if (!IsProduct(url) || EditionIsbn.Normalize(expectedIsbn) is not { } expected) return null;
        var doc = new HtmlParser().ParseDocument(html);
        foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                foreach (var node in Nodes(json.RootElement))
                {
                    var isbn = Value(node, "isbn") ?? Value(node, "gtin13") ?? Value(node, "gtin");
                    if (EditionIsbn.Normalize(isbn) != expected) continue;
                    var title = Value(node, "name"); if (string.IsNullOrWhiteSpace(title)) continue;
                    var authors = new List<(string, Uri)>(); var names = new List<string>();
                    if (node.TryGetProperty("author", out var author))
                        foreach (var item in author.ValueKind == JsonValueKind.Array ? author.EnumerateArray().ToArray() : [author])
                        {
                            var name = item.ValueKind == JsonValueKind.String ? item.GetString() : Value(item, "name");
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            names.Add(name.Trim());
                            if (Uri.TryCreate(url, Value(item, "url"), out var authorUrl) && IsSite(authorUrl) && authorUrl.AbsolutePath.StartsWith("/autor/", StringComparison.OrdinalIgnoreCase)) authors.Add((name.Trim(), authorUrl));
                        }
                    var book = new FetchedBook { Success = true, Url = url.AbsoluteUri, Isbn = expected, Title = title.Trim(), Authors = names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), HasStructuredAuthors = names.Count > 0, PublishedBy = ObjectName(node, "publisher") };
                    if (DateTime.TryParse(Value(node, "datePublished"), out var date)) book.PublishedOn = date;
                    book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataSynopsis, Value(node, "description"));
                    book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataLanguage, Value(node, "inLanguage"));
                    book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataTotalPages, Value(node, "numberOfPages"));
                    var imageText = ObjectName(node, "image");
                    var image = Uri.TryCreate(url, imageText, out var imageUrl) && IsSite(imageUrl) ? imageUrl : null;
                    return new BertrandProduct(book, image, authors);
                }
            }
            catch (JsonException) { }
        }
        return null;
    }
    private static IEnumerable<JsonElement> Nodes(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Array) { foreach (var child in node.EnumerateArray()) foreach (var item in Nodes(child)) yield return item; }
        else if (node.ValueKind == JsonValueKind.Object) { yield return node; if (node.TryGetProperty("@graph", out var graph)) foreach (var item in Nodes(graph)) yield return item; }
    }
    private static string? Value(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString() : null;
    private static string? ObjectName(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() : Value(v, "name") ?? Value(v, "url") : null;
}

public sealed class BertrandCatalogueLookup(IBertrandReader reader, ILogger<BertrandCatalogueLookup> logger)
{
    public async Task<BertrandProduct?> FindAsync(string isbn, CancellationToken token)
    {
        var expected = EditionIsbn.Normalize(isbn); if (expected is null) return null;
        var search = new Uri($"https://www.bertrand.pt/pesquisa/{expected}");
        logger.LogInformation("Searching Bertrand at {Url}", search);
        var page = await reader.ReadAsync(search, token);
        if (page.Status is < 200 or >= 300 || !Uri.TryCreate(page.Url, UriKind.Absolute, out var actual) || !BertrandCatalogue.IsSite(actual)) throw new InvalidDataException("Bertrand search unavailable.");
        if (BertrandCatalogue.IsProduct(actual)) return BertrandCatalogue.Parse(page.Html, actual, expected);
        foreach (var candidate in BertrandCatalogue.Candidates(page))
        {
            token.ThrowIfCancellationRequested();
            var detail = await reader.ReadAsync(candidate, token);
            if (detail.Status is < 200 or >= 300 || !Uri.TryCreate(detail.Url, UriKind.Absolute, out var final) || final.AbsoluteUri != candidate.AbsoluteUri) continue;
            var product = BertrandCatalogue.Parse(detail.Html, final, expected);
            if (product is not null) return product;
        }
        return null;
    }
}

[Bibliotheca.Backend.Extractors.ExtractorProvider("bertrand")]
public sealed class BertrandBookFetcher(BertrandCatalogueLookup lookup) : IBookFetcher, IFetcherPriority
{
    public int Priority => 6;
    public async Task<FetchedBook> FetchBookAsync(string isbn, CancellationToken cancellationToken) =>
        (await lookup.FindAsync(isbn, cancellationToken))?.Book ?? new FetchedBook { Success = false };
}
[Bibliotheca.Backend.Extractors.ExtractorProvider("bertrand")]
public sealed class BertrandCoverFetcher(BertrandCatalogueLookup lookup, IBertrandReader reader) : ICoverFetcher, IFetcherPriority
{
    public int Priority => 6;
    public async Task<FetchedCover> FetchCoverAsync(string isbn, CancellationToken cancellationToken)
    {
        var product = await lookup.FindAsync(isbn, cancellationToken);
        return product?.Image is null ? new FetchedCover { Success = false } : await reader.ReadCoverAsync(new Uri(product.Book.Url), product.Book.Isbn, cancellationToken);
    }
}

public sealed record BertrandAuthorResult(string? Biography, string? PhotoUrl, string SourceUrl);
public sealed class BertrandAuthorLookup(BertrandCatalogueLookup catalogue, IBertrandReader reader)
{
    public async Task<BertrandAuthorResult?> FindAsync(string name, IEnumerable<string?> isbns, CancellationToken token)
    {
        var candidates = new HashSet<Uri>();
        foreach (var isbn in isbns.Where(value => EditionIsbn.Normalize(value) is not null).Take(3))
        {
            var product = await catalogue.FindAsync(isbn!, token);
            if (product is null) continue;
            foreach (var author in product.Authors.Where(a => string.Equals(a.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)))
                candidates.Add(author.Url);
        }
        if (candidates.Count != 1) return null;
        var url = candidates.Single(); var page = await reader.ReadAsync(url, token);
        if (page.Status is < 200 or >= 300 || page.Url != url.AbsoluteUri) return null;
        var doc = new HtmlParser().ParseDocument(page.Html);
        foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                var person = json.RootElement;
                if (person.ValueKind != JsonValueKind.Object || !person.TryGetProperty("@type", out var type) ||
                    type.ValueKind != JsonValueKind.String || type.GetString() != "Person" ||
                    !person.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String ||
                    !string.Equals(n.GetString()?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                var bio = person.TryGetProperty("description", out var description) && description.ValueKind == JsonValueKind.String
                    ? description.GetString()?.Trim() : null;
                string? photo = null;
                if (person.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.String &&
                    Uri.TryCreate(url, image.GetString(), out var imageUrl) && BertrandCatalogue.IsSite(imageUrl)) photo = imageUrl.AbsoluteUri;
                if (!string.IsNullOrEmpty(bio) || photo is not null) return new BertrandAuthorResult(bio, photo, url.AbsoluteUri);
            }
            catch (JsonException) { }
        }
        return null;
    }
}

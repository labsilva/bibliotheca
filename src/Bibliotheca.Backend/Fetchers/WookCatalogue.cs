using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Bibliotheca.Backend.Services;

namespace Bibliotheca.Backend.Fetchers;

internal static partial class WookCatalogue
{
    internal static readonly Uri Origin = new("https://www.wook.pt/");
    private const string AuthorsSelector = ".authors, .author, .product-author, [itemprop='author'], [class*='product-author']";
    private const string IllustratorsSelector = "[itemprop='illustrator'], [data-role='illustrator' i], .illustrator";
    private const string TitleSelector = "[itemprop='name'], .product-title, .book-title, .title, h2, h3";
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-PT");

    internal static bool IsWookAddress(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps &&
        address.IsDefaultPort && address.UserInfo.Length == 0 && address.Host is "www.wook.pt" or "wook.pt";

    internal static Uri? EditionAddress(string? value, Uri origin)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(origin, value, out var uri) ||
            !IsWookAddress(uri.AbsoluteUri))
            return null;

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.StartsWith("/en/", StringComparison.Ordinal))
            path = path[3..];
        if (!ProductPathPattern().IsMatch(path))
            return null;

        return new Uri(Origin, path);
    }

    // The trailing Wook product number survives a change to the descriptive slug.
    internal static string ProductKey(Uri address) => address.AbsolutePath[(address.AbsolutePath.LastIndexOf('/') + 1)..];

    internal static IReadOnlyList<ExternalEdition> ReadSearch(WookPage page, int limit, CancellationToken cancellationToken)
    {
        var location = new Uri(page.Url);
        if (location.AbsolutePath.TrimEnd('/') is not ("/pesquisa" or "/en/pesquisa"))
            throw new InvalidDataException("Wook did not return a search or product page.");

        var document = new HtmlParser().ParseDocument(page.Html);
        var search = document.QuerySelector("#search-results, .search-results, #product-list, .product-list, .product-listing") ??
            document.QuerySelector("main") ?? document.Body;
        var result = new List<ExternalEdition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in search?.QuerySelectorAll("a[href]").AsEnumerable() ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (link.Closest("header, footer, nav, aside") is not null ||
                EditionAddress(link.GetAttribute("href"), location) is not { } product ||
                seen.Contains(ProductKey(product)))
                continue;

            var card = FindCard(link, product, location);
            var heading = card.QuerySelector(TitleSelector);
            var title = Clean(heading?.TextContent) ??
                Clean(heading?.GetAttribute("content")) ??
                card.QuerySelectorAll("a[href]").Where(candidate =>
                    EditionAddress(candidate.GetAttribute("href"), location) is { } address &&
                    ProductKey(address) == ProductKey(product))
                    .Select(candidate => Clean(candidate.TextContent) ?? Clean(candidate.GetAttribute("title")))
                    .FirstOrDefault(value => value is not null) ??
                Clean(link.TextContent) ?? Clean(link.GetAttribute("title")) ??
                Clean(link.QuerySelector("img")?.GetAttribute("alt"));
            if (title is null)
                continue;

            var (authors, _) = ReadAuthors(card);
            var isbn = EditionIsbn.Normalize(Clean(card.GetAttribute("data-isbn")) ??
                Value(card, "[itemprop='isbn'], [data-isbn], .isbn"));
            var publishedOn = ReadDate(Value(card, "[itemprop='datePublished'], .publication-date, .year"));
            result.Add(new ExternalEdition(product.AbsolutePath[1..], title,
                authors.Length > 0 ? string.Join(", ", authors) : null, isbn,
                Value(card, "[itemprop='inLanguage'], .language, .product-language"),
                Value(card, "[itemprop='publisher'], .publisher, .product-publisher"),
                publishedOn?.Year, CoverAddress(location, CoverImage(card)), product.AbsoluteUri,
                ProductFormat(product)));
            seen.Add(ProductKey(product));
            if (result.Count == limit)
                break;
        }

        if (result.Count == 0 && !EmptyResultsPattern().IsMatch(search?.TextContent ?? ""))
            throw new InvalidDataException("Wook search results could not be recognized.");
        return result;
    }

    private static IElement FindCard(IElement link, Uri product, Uri location)
    {
        var card = link;
        while (card.ParentElement is { } parent &&
            parent.LocalName is not ("main" or "body" or "html") &&
            parent.QuerySelectorAll("a[href]").All(candidate =>
                EditionAddress(candidate.GetAttribute("href"), location) is not { } address ||
                ProductKey(address) == ProductKey(product)))
        {
            card = parent;
            if (card.Matches("article, li, .product-item, .product-card, [data-product-id]"))
                break;
        }
        return card;
    }

    internal static WookProduct ReadProduct(string html, Uri location)
    {
        var document = new HtmlParser().ParseDocument(html);
        var table = document.QuerySelector("table:has(caption p.font-medium)") ??
            document.QuerySelector("table:has(caption p.authors)") ??
            document.QuerySelector("table:has(tr td:first-child:contains('ISBN'))");
        var caption = table?.QuerySelector("caption");
        var title = Value(caption, "p.font-medium, [itemprop='name']") ??
            Value(document, "h1.product-title, h1[itemprop='name'], h1");
        // A heading by itself also occurs on Wook challenge and error pages.
        if (title is null || (table is null && document.QuerySelector("#synopsis-toggle-collapse, [itemtype$='/Book']") is null))
            throw new InvalidDataException("Wook product metadata could not be recognized.");

        var classification = TableValue(table, "classificação temática", "classificação");
        var classificationParts = classification?.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var (authors, hasStructuredAuthors) = ReadAuthors(caption?.QuerySelector(AuthorsSelector) is null ? document : caption);
        return new WookProduct(
            title,
            Value(caption, ".subtitle") ?? Value(document, ".product-subtitle"),
            authors,
            TableValue(table, "isbn") ?? Value(document, "[itemprop='isbn']", "book:isbn"),
            TableValue(table, "idioma", "língua") ?? Value(document, "[itemprop='inLanguage']"),
            TableValue(table, "editor", "editora") ?? Value(document, "[itemprop='publisher']"),
            ReadDate(TableValue(table, "data de lançamento", "lançamento") ?? Value(document, "[itemprop='datePublished']")),
            CoverAddress(location, Value(document, "[itemprop='image']", "og:image") ?? CoverImage(document)),
            TableValue(table, "encadernação", "tipo de produto") ?? ProductFormat(location),
            TableValue(table, "coleção"),
            TableValue(table, "páginas"),
            Value(document, "#synopsis-toggle-collapse, [itemprop='description']"),
            classificationParts is { Length: > 0 } ? [classificationParts[^1]] : null, hasStructuredAuthors);
    }

    private static string? TableValue(IElement? table, params string[] labels)
    {
        foreach (var row in table?.QuerySelectorAll("tr").AsEnumerable() ?? [])
        {
            var cells = row.Children;
            if (cells.Length >= 2 && Clean(cells[0].TextContent)?.TrimEnd(':') is { } label &&
                labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                return Clean(cells[1].TextContent);
        }
        return null;
    }

    private static (string[] Authors, bool IsStructured) ReadAuthors(IParentNode root)
    {
        var bylines = root.QuerySelectorAll(AuthorsSelector)
            .Where(element => element.ParentElement?.Closest(AuthorsSelector) is null &&
                element.Closest(IllustratorsSelector) is null)
            .Select(ReadByline).ToArray();
        return (bylines.SelectMany(byline => byline.Authors).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            bylines.Length > 0 && bylines.All(byline => byline.IsStructured));
    }

    private static (IEnumerable<string> Authors, bool IsStructured) ReadByline(IElement element)
    {
        var segments = ReadBylineSegments(element).ToArray();
        var byline = string.Concat(segments.Select(segment => segment.Text));
        var annotation = IllustrationCredit().Match(byline);
        var endOfAuthors = annotation.Success ? annotation.Index : byline.Length;
        var offset = 0;
        var linkedAuthors = new List<string>();
        foreach (var (segmentText, isAuthor) in segments)
        {
            if (offset >= endOfAuthors)
                break;
            if (isAuthor && Clean(segmentText) is { } name)
                linkedAuthors.Add(name);
            offset += segmentText.Length;
        }
        if (linkedAuthors.Count > 0)
            return (linkedAuthors, true);
        var text = Clean(byline[..endOfAuthors]) ?? Clean(element.GetAttribute("content"));
        if (text is null)
            return ([], false);
        if (text.StartsWith("de ", StringComparison.OrdinalIgnoreCase))
            text = text[3..];
        var names = text.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return (names.SelectMany(name =>
        {
            var conjunction = name.Split(" e ", StringSplitOptions.TrimEntries);
            return conjunction.All(part => part.Contains(' ')) ? conjunction : [name];
        }), false);
    }

    private static IEnumerable<(string Text, bool IsAuthor)> ReadBylineSegments(INode node)
    {
        if (node is IElement element)
        {
            if (element.Matches(IllustratorsSelector))
                yield break;
            if (element.Matches("a[href*='/autor/']"))
            {
                yield return (element.TextContent, true);
                yield break;
            }
        }
        if (node is IText text)
            yield return (text.Data, false);
        foreach (var child in node.ChildNodes)
            foreach (var segment in ReadBylineSegments(child))
                yield return segment;
    }

    [GeneratedRegex(@"(?:^|[;]|\r?\n)\s*(?:Ilustra(?:ç(?:ão|ões)|dora?s?|dores)|Illustrat(?:ion|ors?))\s*:",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    internal static partial Regex IllustrationCredit();

    private static string? Value(IParentNode? parent, string selector, string? metaProperty = null)
    {
        if (metaProperty is not null && parent?.QuerySelector($"meta[property='{metaProperty}']") is { } meta &&
            Clean(meta.GetAttribute("content")) is { } metaValue)
            return metaValue;
        var element = parent?.QuerySelector(selector);
        return element is null ? null : Clean(element.GetAttribute("content")) ?? Clean(element.GetAttribute("datetime")) ??
            Clean(element.GetAttribute("data-isbn")) ?? Clean(element.GetAttribute("src")) ?? Clean(element.TextContent);
    }

    private static string? CoverImage(IParentNode parent)
    {
        var image = parent.QuerySelector("img[itemprop='image'], img[data-src], img[src]");
        var source = Clean(image?.GetAttribute("data-src")) ?? Clean(image?.GetAttribute("src"));
        // Wook's lazy image data-src can include a srcset width descriptor,
        // while the actual image URL ends before it (for example '/290x 265w').
        return source is null ? null : ImageDescriptorPattern().Replace(source, "");
    }

    private static string? CoverAddress(Uri location, string? value) =>
        !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(location, value, out var address) &&
        address.Scheme == Uri.UriSchemeHttps && address.UserInfo.Length == 0 ? address.AbsoluteUri : null;

    private static DateTime? ReadDate(string? value)
    {
        if (value is null)
            return null;
        value = value.Replace(" de ", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("marco", "março", StringComparison.OrdinalIgnoreCase).Trim();
        if (DateTime.TryParseExact(value, ["d MMMM yyyy", "MMMM yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "yyyy-MM"],
            Portuguese, DateTimeStyles.AllowWhiteSpaces, out var date))
            return date;
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) &&
            year is >= 1 and <= 9999 ? new DateTime(year, 1, 1) : null;
    }

    private static string? ProductFormat(Uri address) => address.Segments.ElementAtOrDefault(1) switch
    {
        "livro/" => "Livro",
        "ebook/" => "eBook",
        "audiolivro/" => "Audiolivro",
        _ => null
    };

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : WhitespacePattern().Replace(value.Replace("\\n", ""), " ").Trim();

    [GeneratedRegex(@"\A/(?:livro|ebook|audiolivro)/[a-zA-Z0-9][a-zA-Z0-9._~-]{0,159}/[0-9]{1,12}\z")]
    private static partial Regex ProductPathPattern();

    [GeneratedRegex(@"não\s+foram\s+encontrados\s+resultados|não\s+encontrámos\s+resultados|não\s+encontrou\s+o\s+que\s+procura\?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmptyResultsPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"\s+[0-9]+(?:\.[0-9]+)?[wx]$")]
    private static partial Regex ImageDescriptorPattern();
}

internal sealed record WookProduct(string Title, string? Subtitle, string[] Authors, string? Isbn, string? Language,
    string? Publisher, DateTime? PublishedOn, string? CoverUrl, string? Format, string? Collection, string? Pages,
    string? Synopsis, string[]? Genres, bool HasStructuredAuthors);

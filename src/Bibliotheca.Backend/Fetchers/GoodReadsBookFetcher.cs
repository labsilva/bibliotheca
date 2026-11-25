using AngleSharp.Html.Parser;
using AngleSharp.Dom;
using Bibliotheca.Backend.Options;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Bibliotheca.Backend.Fetchers;

public class GoodReadsBookFetcher : PlaywrightFetcher, IBookFetcher, IFetcherPriority
{
    private readonly IOptions<FetcherOptions> _options;
    private readonly ILogger<GoodReadsBookFetcher> _logger;

    public int Priority => 10;

    public GoodReadsBookFetcher(
        IOptions<FetcherOptions> fetcherOptions,
        IOptions<PlaywrightOptions> playwrightOptions,
        ILogger<GoodReadsBookFetcher> logger) : base(playwrightOptions)
    {
        _options = fetcherOptions;
        _logger = logger;
    }

    public async Task<FetchedBook> FetchBookAsync(
        string isbn,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching book metadata for isbn {isbn}", isbn);
        var (url, html) = await WithBrowserAsync(async browser =>
        {
            await Task.Delay(10000, cancellationToken);
            string bookUrl;
            await using (var search = await NewContextAsync(browser))
            {
                var page = await search.NewPageAsync();
                await page.GotoAsync($"https://www.goodreads.com/search?query={isbn}");
                bookUrl = page.Url;
            }

            await using var context = await NewContextAsync(browser);
            var details = await context.NewPageAsync();
            await details.GotoAsync(bookUrl);
            await details.ClickAsync("button[aria-label='Book details and editions']", new() { Timeout = 5000 });
            return (details.Url, await details.ContentAsync());
        }, cancellationToken);

        if (_options.Value.SaveTempFiles)
        {
            var path = string.Concat(_options.Value.TempFilesSaveLocation, isbn, ".html");
            await File.WriteAllTextAsync(path, $"<!--{url}-->\n{html}", cancellationToken);
        }

        return ParseResponse(url, ref html);
    }

    public FetchedBook ParseResponse(string url, ref string content)
    {
        var parser = new HtmlParser();
        var parsed = parser.ParseDocument(content);

        var title = parsed.QuerySelector(".BookPageTitleSection__title h1")!.TextContent.StripCharacters();
        var subtitle = parsed.QuerySelector("div.BookPageTitleSection__title h3.Text__title3 a")?.TextContent.StripCharacters();
        var authors = ReadAuthors(parsed, url);
        var synopsis = parsed.QuerySelector(".BookPageMetadataSection__description")!.TextContent.StripCharacters();
        var genres = parsed.QuerySelectorAll(".BookPageMetadataSection__genreButton")?.Select(s => s.TextContent).ToArray();

        const string DefinitionItemCssSelector = ".DescListItem:has(> dt:contains(\"{0}\")) > dd";

        var editionRegion = parsed.QuerySelector("div.EditionDetails")!;
        var isbn = editionRegion.QuerySelector(string.Format(DefinitionItemCssSelector, "ISBN"))!.TextContent.Split(' ', StringSplitOptions.RemoveEmptyEntries).First().StripCharacters();
        var language = editionRegion.QuerySelector(string.Format(DefinitionItemCssSelector, "Language"))?.TextContent.StripCharacters() ?? "English";
        var format = editionRegion.QuerySelector(string.Format(DefinitionItemCssSelector, "Format"))!.TextContent.StripCharacters();
        var published = editionRegion.QuerySelector(string.Format(DefinitionItemCssSelector, "Published"))!.TextContent.StripCharacters();
        var series = editionRegion.QuerySelector(string.Format(DefinitionItemCssSelector, "Series"))?.TextContent.StripCharacters();

        var pages = format.Split("pages", StringSplitOptions.RemoveEmptyEntries).First();
        var publishedBy = published.Split(" by ", StringSplitOptions.RemoveEmptyEntries).Last();
        var publishedOn = published.Split(" by ", StringSplitOptions.RemoveEmptyEntries).First();

        var book = new FetchedBook
        {
            Title = title,
            Subtitle = subtitle,
            Isbn = isbn,
            Authors = authors,
            HasStructuredAuthors = true,
            PublishedBy = publishedBy,
            PublishedOn = publishedOn.ToDateFromEnglishDate(),
            Url = url,
        };

        book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataCollection, series);
        book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataTotalPages, pages);
        book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataLanguage, language);
        book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataSynopsis, synopsis);
        book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataGenres, genres);
        book.Success = true;
        return book;
    }

    private static string[] ReadAuthors(IDocument document, string url)
    {
        if (ReadStructuredAuthors(document, url) is { } authors)
            return authors;

        var byline = document.QuerySelector(".ContributorLinksList, .BookPageMetadataSection__contributor") ?? (IParentNode)document;
        return byline.QuerySelectorAll(".ContributorLink__name")
            .Where(IsAuthor)
            .Select(name => name.TextContent.StripCharacters())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[]? ReadStructuredAuthors(IDocument document, string url)
    {
        if (document.QuerySelector("script#__NEXT_DATA__") is not { } data ||
            !Uri.TryCreate(url, UriKind.Absolute, out var address))
            return null;
        var bookId = string.Concat(address.AbsolutePath.Split('/').Last().TakeWhile(char.IsAsciiDigit));
        if (bookId.Length == 0)
            return null;

        try
        {
            using var metadata = JsonDocument.Parse(data.TextContent);
            var state = Property(Property(Property(metadata.RootElement, "props"), "pageProps"), "apolloState");
            var bookQuery = Property(Property(state, "ROOT_QUERY"), $"getBookByLegacyId({{\"legacyId\":\"{bookId}\"}})");
            if (TextProperty(bookQuery, "__ref") is not { } key)
                return null;

            var book = Property(state, key);
            var primary = Property(book, "primaryContributorEdge");
            var secondary = Property(book, "secondaryContributorEdges");
            if (primary.ValueKind != JsonValueKind.Object && secondary.ValueKind != JsonValueKind.Array)
                return null;

            var authors = new List<string>();
            AddAuthor(primary);
            if (secondary.ValueKind == JsonValueKind.Array)
                foreach (var edge in secondary.EnumerateArray())
                    AddAuthor(edge);
            return authors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            void AddAuthor(JsonElement edge)
            {
                if (!ContributorRoles.IsAuthor(TextProperty(edge, "role")))
                    return;
                var node = Property(edge, "node");
                if (TextProperty(node, "__ref") is { } reference)
                    node = Property(state, reference);
                if (TextProperty(node, "name") is { } name && !string.IsNullOrWhiteSpace(name))
                    authors.Add(name.Trim());
            }
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static string? TextProperty(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static bool IsAuthor(IElement name)
    {
        var credit = name.Closest(".ContributorLink") ?? name;
        const string roleSelector = ".ContributorLink__role, [data-contributor-role]";
        var roleElement = credit.QuerySelector(roleSelector) ??
            (credit.NextElementSibling?.Matches(roleSelector) == true ? credit.NextElementSibling : null);
        var role = credit.GetAttribute("data-contributor-role") ?? roleElement?.GetAttribute("data-contributor-role") ??
            roleElement?.TextContent;
        return ContributorRoles.IsAuthor(role);
    }
}

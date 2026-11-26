using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Services;

namespace Bibliotheca.Backend.Extractors;

internal sealed record ExtractorResult(string Isbn, string Title, string[]? Authors, string? Publisher,
    string? PublishedOn, string? Synopsis, string? Language, int? Pages, string? Subtitle,
    string? Collection, string[]? Genres, bool? HasStructuredAuthors)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };

    internal Uri Source { get; private init; } = null!;
    internal FetchedCover? Cover { get; private init; }

    public static ExtractorResult? Parse(string json, string expected, Uri? source, FetchedCover? cover)
    {
        var result = JsonSerializer.Deserialize<ExtractorResult>(json, JsonOptions);
        if (result is null) return null;
        if (EditionIsbn.Normalize(result.Isbn) != expected)
            throw new InvalidDataException("Extractor returned a mismatched ISBN.");
        if (source is null ||
            string.IsNullOrWhiteSpace(result.Title) || result.Title.Length > 512 ||
            result.Authors is { Length: > 64 } ||
            result.Authors?.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 256) == true ||
            result.Subtitle?.Length > 512 || result.Collection?.Length > 512 ||
            result.Genres is { Length: > 32 } || result.Genres?.Any(genre => string.IsNullOrWhiteSpace(genre) || genre.Length > 256) == true ||
            result.Publisher?.Length > 512 || result.Synopsis?.Length > 16_384 ||
            result.Language?.Length > 32 || result.Pages is <= 0 or > 100_000 ||
            (result.PublishedOn is not null && !DateTime.TryParseExact(result.PublishedOn, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
            throw new InvalidDataException("Extractor result does not match the ISBN or the version 1 contract.");
        return result with { Isbn = expected, Source = source, Cover = cover };
    }

    public FetchedBook ToBook()
    {
        var authors = Authors?.Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
        var book = new FetchedBook
        {
            Success = true,
            Isbn = Isbn,
            Title = Title.Trim(),
            Authors = authors,
            HasStructuredAuthors = authors.Length > 0 && HasStructuredAuthors != false,
            Subtitle = Subtitle!,
            PublishedBy = Publisher!,
            PublishedOn = PublishedOn is null ? null : DateTime.ParseExact(PublishedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            Url = Source.AbsoluteUri
        };
        if (Synopsis is not null) book.Metadata[FetchedBook.FetchedBookMetadataSynopsis] = Synopsis;
        if (Language is not null) book.Metadata[FetchedBook.FetchedBookMetadataLanguage] = Language;
        if (Pages is not null) book.Metadata[FetchedBook.FetchedBookMetadataTotalPages] = Pages.Value.ToString(CultureInfo.InvariantCulture);
        if (Collection is not null) book.Metadata[FetchedBook.FetchedBookMetadataCollection] = Collection;
        if (Genres is not null) book.Metadata[FetchedBook.FetchedBookMetadataGenres] = Genres;
        return book;
    }
}

internal sealed class JavaScriptExtractorFetcher(IExtractor extractor, int priority)
    : IBookFetcher, ICoverFetcher, IFetcherPriority
{
    internal JavaScriptExtractorFetcher(ExtractorDefinition definition, ExtractorRuntime runtime)
        : this(new JavaScriptExtractor(definition, runtime), definition.Priority) { }

    public int Priority => priority;
    internal string Id => extractor.Definition.Manifest.Id;

    public async Task<FetchedBook> FetchBookAsync(string isbn, CancellationToken token) =>
        (await RunAsync("metadata", isbn, token))?.ToBook() ?? new() { Success = false };

    public async Task<FetchedCover> FetchCoverAsync(string isbn, CancellationToken token) =>
        (await RunAsync("cover", isbn, token))?.Cover ?? new() { Success = false };

    private Task<ExtractorResult?> RunAsync(string operation, string isbn, CancellationToken token) =>
        EditionIsbn.Normalize(isbn) is { } normalized
            ? extractor.ExtractBookAsync(operation, normalized, token)
            : Task.FromResult<ExtractorResult?>(null);
}

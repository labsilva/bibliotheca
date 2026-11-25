using Bibliotheca.Backend.Fetchers;

namespace Bibliotheca.Backend.Services;

internal static class ExternalEditionImportExtensions
{
    public static FetchedBook ToFetchedBook(this ExternalEdition edition)
    {
        var details = edition.Details;
        var book = new FetchedBook
        {
            Isbn = EditionIsbn.Normalize(edition.Isbn) ?? "",
            Title = edition.Title,
            Authors = details?.Authors ?? (string.IsNullOrWhiteSpace(edition.Author) ? [] : [edition.Author]),
            HasStructuredAuthors = details?.HasStructuredAuthors == true,
            PublishedBy = edition.Publisher ?? "",
            PublishedOn = details?.PublishedOn ??
                (edition.Year is >= 1 and <= 9999 ? new DateTime(edition.Year.Value, 1, 1) : null),
            Url = edition.SourceUrl ?? "",
            Success = true
        };
        if (details?.Subtitle is { } subtitle)
            book.Subtitle = subtitle;
        if (!string.IsNullOrWhiteSpace(edition.Language))
            book.Metadata[FetchedBook.FetchedBookMetadataLanguage] = edition.Language;
        book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataCollection, details?.Collection);
        book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataTotalPages, details?.Pages);
        book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataSynopsis, details?.Synopsis);
        book.Metadata.AddNonNull(FetchedBook.FetchedBookMetadataGenres, details?.Genres);
        if (!string.IsNullOrWhiteSpace(edition.Format))
            book.Metadata["format"] = edition.Format;
        return book;
    }
}

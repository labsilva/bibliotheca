namespace Bibliotheca.Backend.Fetchers;

public interface IBookFetcher
{
    Task<FetchedBook> FetchBookAsync(
        string isbn, 
        CancellationToken cancellationToken);   
}

public record FetchedBook
{
    public const string FetchedBookMetadataLanguage = "language";
    public const string FetchedBookMetadataCollection = "colection";
    public const string FetchedBookMetadataTotalPages = "pages";
    public const string FetchedBookMetadataSynopsis = "synopsis";
    public const string FetchedBookMetadataGenres = "genres";

    public string Title { get; set; }
    public string Subtitle { get; set; }
    public string[] Authors { get; set; }
    public bool HasStructuredAuthors { get; init; }
    public string Isbn { get; set; }
    public string PublishedBy { get; set; }
    public DateTime? PublishedOn { get; set; }
    public Dictionary<string, object> Metadata { get; } = new Dictionary<string, object>();
    public string Url { get; set; }
    public bool Success { get; set; }
}


using System.Diagnostics;
using Bibliotheca.Backend.Fetchers;

namespace Bibliotheca.Backend.Services;

public class FetchBookMetadataService : IServiceExecutor<FetchBookMetadataServiceArgs, FetchBookMetadataServiceResult>
{
    private readonly FetchBookService _fetchBookService;
    private readonly FetchBookCoverService _fetchBookCoverService;
    private readonly ILogger<FetchBookMetadataService> _logger;

    public FetchBookMetadataService(
        FetchBookService fetchBookService,
        FetchBookCoverService fetchBookCoverService,
        ILogger<FetchBookMetadataService> logger)
    {
        _fetchBookService = fetchBookService;
        _fetchBookCoverService = fetchBookCoverService;
        _logger = logger;
    }

    public async Task<FetchBookMetadataServiceResult> ExecuteAsync(
        FetchBookMetadataServiceArgs args, 
        CancellationToken cancellationToken = default)
    {
        var start = Stopwatch.GetTimestamp();
        var book = await _fetchBookService.ExecuteAsync(new FetchBookServiceArgs
        {
            Isbn = args.Isbn,
            FailOnProviderErrors = args.FailOnProviderErrors
        }, cancellationToken);
        IsbnImportMetrics.Metadata.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        cancellationToken.ThrowIfCancellationRequested();
        if (!book.Success)
            return new FetchBookMetadataServiceResult
            {
                Isbn = args.Isbn,
                Book = book,
                Cover = new FetchedCover { Success = false }
            };

        start = Stopwatch.GetTimestamp();
        var cover = await _fetchBookCoverService.ExecuteAsync(new FetchBookCoverServiceArgs { Isbn = args.Isbn }, cancellationToken);

        IsbnImportMetrics.Cover.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);

        return new FetchBookMetadataServiceResult
        {
            Isbn = args.Isbn,
            Book = book,
            Cover = cover,
        };
    }
}

public record FetchBookMetadataServiceArgs
{
    public string Isbn { get; init; }
    public bool FailOnProviderErrors { get; init; }
}

public record FetchBookMetadataServiceResult
{
    public string Isbn { get; init; }
    public FetchedBook Book { get; init; }
    public FetchedCover Cover { get; init; }
    public bool Success
    {
        get
        {
            return Book.Success && Cover.Success;
        }
    }
}

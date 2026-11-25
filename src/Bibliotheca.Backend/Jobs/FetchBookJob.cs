using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Services;

namespace Bibliotheca.Backend.Jobs;

public class FetchBookJob 
{
    private readonly FetchBookMetadataService _fetchBookMetadataService;
    private readonly StoreFetchedBookService _storeFetchedBookService;
    private readonly ILogger<FetchBookJob> _logger;

    public FetchBookJob(
        FetchBookMetadataService fetchBookMetadataService,
        StoreFetchedBookService storeFetchedBookService,
        ILogger<FetchBookJob> logger)
    {
        _fetchBookMetadataService = fetchBookMetadataService;
        _storeFetchedBookService = storeFetchedBookService;
        _logger = logger;
    }

    public async Task<(int? BookId, string Title)> FetchBookAsync(
        string isbn,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing request for ISBN {isbn}", isbn);
        var fetchedData = await _fetchBookMetadataService.ExecuteAsync(new FetchBookMetadataServiceArgs
        {
            Isbn = isbn
        }, cancellationToken);
        int? bookId = null;
        if(fetchedData.Success)
        {
            var args = new StoreFetchedBookArgs
            {
                Book = fetchedData.Book,
                Cover = fetchedData.Cover,
            };
            var storeResult = await _storeFetchedBookService.ExecuteAsync(args, cancellationToken);
            if (!storeResult.Result)
                throw new InvalidOperationException("Could not save the imported book.");
            _logger.LogInformation("ISBN {isbn} stored successfully", isbn);
            bookId = storeResult.BookId;
        }
        else
        {
            _logger.LogWarning("ISBN {isbn} data fetch failed", isbn);
            throw new BookImportException(!fetchedData.Book.Success
                ? "No metadata was found for this ISBN."
                : "Could not retrieve the book cover.");
        }
        _logger.LogInformation("ISBN {isbn} request processed", isbn);

        return (bookId, fetchedData.Book.Title);
    }

    public ICoverFetcher[] PrioritizeFetchers(IEnumerable<ICoverFetcher> fetchers)
    {
        return fetchers
            .Select(s => new
            {
                Fetcher = s,
                HasPriority = s.GetType().IsAssignableTo(typeof(IFetcherPriority)),
            })
            .Select(s => new
            {
                s.Fetcher,
                s.HasPriority,
                Priority = s.HasPriority ? s.Fetcher.GetType().GetProperty("Priority").GetValue(s.Fetcher) : 100
            })
            .OrderBy(o => o.Priority)
            .Select(s => s.Fetcher)
            .ToArray();
    }
}

internal sealed class BookImportException(string message) : Exception(message);

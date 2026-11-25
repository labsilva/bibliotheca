using Bibliotheca.Backend.Fetchers;

namespace Bibliotheca.Backend.Services;

public class FetchBookService : FetcherBaseService, IServiceExecutor<FetchBookServiceArgs, FetchedBook>
{
    private readonly IEnumerable<IBookFetcher> _bookFetchers;
    private readonly ILogger<FetchBookService> _logger;

    public FetchBookService(
        IEnumerable<IBookFetcher> bookFetchers,
        ILogger<FetchBookService> logger)
    {
        _bookFetchers = bookFetchers;
        _logger = logger;
    }

    public async Task<FetchedBook> ExecuteAsync(
        FetchBookServiceArgs args,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Prioritizing book fetchers");
        var fetchers = PrioritizeFetchers(_bookFetchers);
        FetchedBook? book = null;
        var providerFailed = false;
        foreach (var bookFetcher in fetchers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fetcherName = bookFetcher.GetType().Name;
            _logger.LogInformation("Trying {fetcher}", fetcherName);
            try
            {
                book = await bookFetcher.FetchBookAsync(args.Isbn, cancellationToken);
                if (book.Success)
                {
                    _logger.LogInformation("Book fetched from {fetcher}", fetcherName);
                    break;
                }
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                providerFailed = true;
                _logger.LogError(ex, "Fetching book from {fetcher} failed", fetcherName);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (args.FailOnProviderErrors && providerFailed && book?.Success != true)
            throw new BookMetadataProvidersUnavailableException();

        if (book is null)
        {
            return new FetchedBook
            {
                Success = false
            };
        }
        return book;
    }
}

public record FetchBookServiceArgs
{
    public string Isbn { get; init; }
    public bool FailOnProviderErrors { get; init; }
}

internal sealed class BookMetadataProvidersUnavailableException()
    : InvalidOperationException("Book metadata providers could not be reached.");

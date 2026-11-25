using Bibliotheca.Backend.Fetchers;

namespace Bibliotheca.Backend.Services;

public class FetchBookCoverService : FetcherBaseService, IServiceExecutor<FetchBookCoverServiceArgs, FetchedCover>
{
    private readonly IEnumerable<ICoverFetcher> _coverFetchers;
    private readonly ILogger<FetchBookCoverService> _logger;

    public FetchBookCoverService(
        IEnumerable<ICoverFetcher> coverFetchers,
        ILogger<FetchBookCoverService> logger)
    {
        _coverFetchers = coverFetchers;
        _logger = logger;
    }

    public async Task<FetchedCover> ExecuteAsync(
        FetchBookCoverServiceArgs args, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Prioritizing cover fetchers");
        var fetchers = PrioritizeFetchers(_coverFetchers);
        FetchedCover? cover = null;
        foreach (var coverFetcher in fetchers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fetcherName = coverFetcher.GetType().Name;
            _logger.LogInformation("Trying {fetcher}", fetcherName);
            try
            {
                cover = await coverFetcher.FetchCoverAsync(args.Isbn, cancellationToken);
                if (cover.Success)
                {
                    _logger.LogInformation("Cover fetched from {fetcher}", fetcherName);
                    break;
                }
            }
            catch (Exception ex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _logger.LogError(ex, "Fetching cover from {fetcher} failed", fetcherName);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if(cover is null)
        {
            return new FetchedCover
            {
                Success = false
            };
        }
        return cover;
    }
}

public record FetchBookCoverServiceArgs 
{
    public string Isbn { get; init; }  

}
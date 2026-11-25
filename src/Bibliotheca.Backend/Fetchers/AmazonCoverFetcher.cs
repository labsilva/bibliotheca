using AngleSharp.Html.Parser;
using Bibliotheca.Backend.Options;
using Microsoft.Extensions.Options;

namespace Bibliotheca.Backend.Fetchers;

public class AmazonCoverFetcher : PlaywrightFetcher, ICoverFetcher, IFetcherPriority
{
    private readonly CoverDownloader _downloader;
    private readonly ILogger<AmazonCoverFetcher> _logger;

    public int Priority => 2;

    public AmazonCoverFetcher(
        CoverDownloader downloader,
        IOptions<PlaywrightOptions> playwrightOptions,
        ILogger<AmazonCoverFetcher> logger) : base(playwrightOptions)
    {
        _downloader = downloader;
        _logger = logger;
    }


    public async Task<FetchedCover> FetchCoverAsync(
        string isbn,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching cover for isbn {isbn}", isbn);
        var (_, html) = await ReadPageAsync($"https://www.amazon.com/s?k={isbn}", cancellationToken);
        var parser = new HtmlParser();
        var parsed = parser.ParseDocument(html);
        var imageElement = parsed.QuerySelector("img.s-image");
        var imageSource = imageElement?.GetAttribute("src");
        if (string.IsNullOrWhiteSpace(imageSource))
        {
            _logger.LogWarning("Could not get cover for {isbn}", isbn);
            return new FetchedCover
            {
                Success = false,
                Image = null,
                MimeType = null,
                Url = $"https://www.amazon.com/s?k={isbn}",
            };
        }
        return await _downloader.DownloadAsync(isbn, imageSource, cancellationToken);
    }
}

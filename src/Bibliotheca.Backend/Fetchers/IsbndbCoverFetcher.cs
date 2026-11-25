
using AngleSharp.Html.Parser;
using Bibliotheca.Backend.Options;
using Microsoft.Extensions.Options;

namespace Bibliotheca.Backend.Fetchers;

public class IsbndbCoverFetcher : PlaywrightFetcher, ICoverFetcher
{
    private readonly CoverDownloader _downloader;
    private readonly ILogger<IsbndbCoverFetcher> _logger;

    public IsbndbCoverFetcher(
        CoverDownloader downloader,
        IOptions<PlaywrightOptions> playwrightOptions,
        ILogger<IsbndbCoverFetcher> logger) : base(playwrightOptions)
    {
        _downloader = downloader;
        _logger = logger;
    }


    public async Task<FetchedCover> FetchCoverAsync(
        string isbn, 
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching cover for isbn {isbn}", isbn);
        var (_, html) = await ReadPageAsync($"https://isbndb.com/book/{isbn}", cancellationToken);
        var parser = new HtmlParser();
        var parsed = parser.ParseDocument(html);
        var imageElement = parsed.QuerySelector("div.artwork > object:nth-child(1)");
        var imageSource = imageElement?.GetAttribute("data");
        if (string.IsNullOrWhiteSpace(imageSource))
        {
            _logger.LogWarning("Could not get cover for {isbn}", isbn);
            return new FetchedCover
            {
                Success = false,
                Image = null,
                MimeType = null,
                Url = $"https://isbndb.com/book/{isbn}",
            };
        }
        return await _downloader.DownloadAsync(isbn, imageSource, cancellationToken);
    }
}

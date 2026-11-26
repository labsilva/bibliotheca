using AngleSharp.Html.Parser;
using Bibliotheca.Backend.Options;
using Microsoft.Extensions.Options;

namespace Bibliotheca.Backend.Fetchers;

[Bibliotheca.Backend.Extractors.ExtractorProvider("goodreads")]
public class GoodReadsCoverFetcher : PlaywrightFetcher, ICoverFetcher, IFetcherPriority
{
    private readonly CoverDownloader _downloader;
    private readonly ILogger<GoodReadsCoverFetcher> _logger;

    public int Priority => 1;

    public GoodReadsCoverFetcher(
        CoverDownloader downloader,
        IOptions<PlaywrightOptions> playwrightOptions,
        ILogger<GoodReadsCoverFetcher> logger) : base(playwrightOptions)
    {
        _downloader = downloader;
        _logger = logger;
    }

    public async Task<FetchedCover> FetchCoverAsync(
        string isbn,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching cover for isbn {isbn}", isbn);
        var (_, html) = await ReadPageAsync($"https://www.goodreads.com/search?utf8=✓&query={isbn}", cancellationToken);
        var parser = new HtmlParser();
        var parsed = parser.ParseDocument(html);
        var imageElement = parsed.QuerySelector("img[role='presentation']");
        var imageSource = imageElement?.GetAttribute("src");
        if (string.IsNullOrWhiteSpace(imageSource))
        {
            _logger.LogWarning("Could not get cover for {isbn}", isbn);
            return new FetchedCover
            {
                Success = false,
                Image = null,
                MimeType = null,
                Url = $"https://www.goodreads.com/search?utf8=✓&query={isbn}",
            };
        }
        return await _downloader.DownloadAsync(isbn, imageSource, cancellationToken);
    }
}

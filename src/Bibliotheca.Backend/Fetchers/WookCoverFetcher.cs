using Bibliotheca.Backend.Services;
using Bibliotheca.Backend.Options;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Bibliotheca.Backend.Fetchers;

public sealed class WookCoverFetcher(IWookPageReader pages, IWookCoverReader covers, IOptions<FetcherOptions> options) : ICoverFetcher, IFetcherPriority
{
    private const int CandidateLimit = 5;
    public int Priority => 0;

    public async Task<FetchedCover> FetchCoverAsync(string isbn, CancellationToken cancellationToken)
    {
        var expected = EditionIsbn.Normalize(isbn);
        if (expected is null)
            return new FetchedCover { Success = false };

        var searchAddress = new Uri(QueryHelpers.AddQueryString(new Uri(WookCatalogue.Origin, "pesquisa").AbsoluteUri,
            "keyword", expected));
        var search = await pages.ReadAsync(searchAddress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAvailable(search);

        if (WookCatalogue.EditionAddress(search.Url, WookCatalogue.Origin) is { } direct)
            return await ReadCoverAsync(search, direct, expected, cancellationToken);

        var candidates = WookCatalogue.ReadSearch(search, CandidateLimit, cancellationToken);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.Isbn is not null && EditionIsbn.Normalize(candidate.Isbn) != expected)
                continue;
            var address = WookCatalogue.EditionAddress(candidate.SourceUrl, WookCatalogue.Origin);
            if (address is null)
                continue;
            var page = await pages.ReadAsync(address, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (page.Status is < 200 or >= 300 || WookCatalogue.EditionAddress(page.Url, WookCatalogue.Origin) is not { } actual ||
                WookCatalogue.ProductKey(actual) != WookCatalogue.ProductKey(address))
                continue;
            var cover = await ReadCoverAsync(page, actual, expected, cancellationToken);
            if (cover.Success)
                return cover;
        }
        return new FetchedCover { Success = false };
    }

    private async Task<FetchedCover> ReadCoverAsync(WookPage page, Uri address, string expected, CancellationToken cancellationToken)
    {
        var product = WookCatalogue.ReadProduct(page.Html, address);
        if (EditionIsbn.Normalize(product.Isbn) != expected || !IsWookImage(product.CoverUrl))
            return new FetchedCover { Success = false };
        var cover = await covers.ReadCoverAsync(address, expected, cancellationToken);
        if (cover.Success && options.Value.SaveTempFiles)
        {
            var extension = cover.MimeType["image/".Length..];
            var path = Path.Combine(options.Value.TempFilesSaveLocation, $"{Path.GetFileName(expected)}.{extension}");
            await File.WriteAllBytesAsync(path, cover.Image, cancellationToken);
        }
        return cover;
    }

    internal static bool IsWookImage(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps &&
        address.IsDefaultPort && address.UserInfo.Length == 0 &&
        (address.Host == "wook.pt" || address.Host.EndsWith(".wook.pt", StringComparison.OrdinalIgnoreCase));

    private static void EnsureAvailable(WookPage page)
    {
        if (page.Status is < 200 or >= 300 || !WookCatalogue.IsWookAddress(page.Url))
            throw new InvalidDataException("Wook catalogue page is unavailable.");
    }
}

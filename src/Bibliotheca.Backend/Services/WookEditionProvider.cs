using Bibliotheca.Backend.Fetchers;
using Microsoft.AspNetCore.WebUtilities;

namespace Bibliotheca.Backend.Services;

internal sealed class WookEditionProvider(IWookPageReader pages) : IExternalEditionProvider, IExternalEditionResolver
{
    public string Id => "wook";
    public string Name => "Wook";

    public async Task<IReadOnlyList<ExternalEdition>> SearchAsync(string title, string? author, int limit,
        string? credential, CancellationToken cancellationToken)
    {
        if (limit <= 0)
            return [];
        var keyword = string.IsNullOrWhiteSpace(author) ? title.Trim() : $"{title.Trim()} {author.Trim()}";
        var address = new Uri(QueryHelpers.AddQueryString(new Uri(WookCatalogue.Origin, "pesquisa").AbsoluteUri,
            "keyword", keyword));
        var page = await pages.ReadAsync(address, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureAvailable(page);

        if (WookCatalogue.EditionAddress(page.Url, WookCatalogue.Origin) is { } product)
        {
            var detail = WookCatalogue.ReadProduct(page.Html, product);
            return [ToEdition(product, detail)];
        }

        return WookCatalogue.ReadSearch(page, limit, cancellationToken);
    }

    public async Task<ExternalEdition?> GetEditionAsync(string externalId, string? credential,
        CancellationToken cancellationToken)
    {
        var address = WookCatalogue.EditionAddress("/" + externalId, WookCatalogue.Origin);
        if (address is null || address.AbsolutePath[1..] != externalId)
            return null;

        var page = await pages.ReadAsync(address, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (page.Status is 404 or 410)
            return null;
        EnsureAvailable(page);
        var product = WookCatalogue.EditionAddress(page.Url, WookCatalogue.Origin);
        if (product is null || WookCatalogue.ProductKey(product) != WookCatalogue.ProductKey(address))
            return null;

        return ToEdition(product, WookCatalogue.ReadProduct(page.Html, product)) with { ExternalId = externalId };
    }

    private static void EnsureAvailable(WookPage page)
    {
        if (page.Status is < 200 or >= 300 || !WookCatalogue.IsWookAddress(page.Url))
            throw new InvalidDataException("Wook catalogue page is unavailable.");
    }

    private static ExternalEdition ToEdition(Uri product, WookProduct detail) =>
        new(product.AbsolutePath[1..], detail.Title,
            detail.Authors.Length > 0 ? string.Join(", ", detail.Authors) : null,
            EditionIsbn.Normalize(detail.Isbn), detail.Language, detail.Publisher, detail.PublishedOn?.Year,
            detail.CoverUrl, product.AbsoluteUri, detail.Format,
            new ExternalEditionDetails(detail.Subtitle, detail.Authors, detail.PublishedOn,
                detail.Collection, detail.Pages, detail.Synopsis, detail.Genres, detail.HasStructuredAuthors));
}

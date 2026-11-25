using Bibliotheca.Backend.Options;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using Bibliotheca.Backend.Services;
using System.Text.Json;

namespace Bibliotheca.Backend.Fetchers;

public interface IWookPageReader
{
    Task<WookPage> ReadAsync(Uri address, CancellationToken cancellationToken);
}

public interface IWookCoverReader
{
    Task<FetchedCover> ReadCoverAsync(Uri productAddress, string expectedIsbn, CancellationToken cancellationToken);
}

public sealed record WookPage(string Url, int Status, string Html);

internal sealed class WookPlaywrightPageReader(IOptions<PlaywrightOptions> options)
    : PlaywrightFetcher(options), IWookPageReader, IWookCoverReader, IDisposable
{
    private readonly SemaphoreSlim _navigation = new(1, 1);

    public async Task<WookPage> ReadAsync(Uri address, CancellationToken cancellationToken)
    {
        await _navigation.WaitAsync(cancellationToken);
        try
        {
            return await WithBrowserAsync(async browser =>
            {
                await using var context = await NewContextAsync(browser);
                var page = await context.NewPageAsync();
                var response = await page.GotoAsync(address.AbsoluteUri,
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                if (!WookCatalogue.IsWookAddress(page.Url) || response is null)
                    throw new InvalidDataException("Wook navigation did not return a catalogue page.");

                if (response.Status >= 400)
                    throw new PageNavigationException(response.Status, response.Url);

                return new WookPage(page.Url, response.Status, await page.ContentAsync());
            }, cancellationToken);
        }
        finally
        {
            _navigation.Release();
        }
    }

    public async Task<FetchedCover> ReadCoverAsync(Uri productAddress, string expectedIsbn, CancellationToken cancellationToken)
    {
        await _navigation.WaitAsync(cancellationToken);
        try
        {
            return await WithBrowserAsync(async browser =>
            {
                await using var context = await NewContextAsync(browser);
                var page = await context.NewPageAsync();
                var response = await page.GotoAsync(productAddress.AbsoluteUri,
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                if (response is null || WookCatalogue.EditionAddress(page.Url, WookCatalogue.Origin) is not { } actual ||
                    WookCatalogue.ProductKey(actual) != WookCatalogue.ProductKey(productAddress))
                    return new FetchedCover { Success = false };
                if (response.Status >= 400)
                    throw new PageNavigationException(response.Status, response.Url);
                var product = WookCatalogue.ReadProduct(await page.ContentAsync(), actual);
                if (EditionIsbn.Normalize(product.Isbn) != expectedIsbn || !WookCoverFetcher.IsWookImage(product.CoverUrl))
                    return new FetchedCover { Success = false };
                var image = await page.GotoAsync(product.CoverUrl!, new PageGotoOptions
                    { WaitUntil = WaitUntilState.Commit, Referer = actual.AbsoluteUri });
                if (image is null || !WookCoverFetcher.IsWookImage(image.Url))
                    return new FetchedCover { Success = false };
                if (image.Status >= 400)
                    throw new PageNavigationException(image.Status, image.Url);

                // Stream the response in the same browser context and cap the bytes returned to the API.
                var result = await page.EvaluateAsync<JsonElement>("""
                    async ({ url, maxBytes }) => {
                      const response = await fetch(url, { credentials: 'include', redirect: 'error' });
                      const type = (response.headers.get('content-type') || '').split(';')[0].toLowerCase();
                      const allowed = ['image/jpeg', 'image/png', 'image/webp', 'image/gif', 'image/avif', 'image/bmp', 'image/tiff'];
                      const length = Number(response.headers.get('content-length'));
                      if (!response.ok || !allowed.includes(type) || length > maxBytes || !response.body)
                        return { status: response.status, type, data: null };
                      const reader = response.body.getReader();
                      let total = 0, chunks = [];
                      while (true) {
                        const { value, done } = await reader.read();
                        if (done) break;
                        total += value.length;
                        if (total > maxBytes) { await reader.cancel(); return { status: response.status, type, data: null }; }
                        chunks.push(value);
                      }
                      if (!total) return { status: response.status, type, data: null };
                      let encoded = '';
                      for (const chunk of chunks) {
                        for (let i = 0; i < chunk.length; i += 8192)
                          encoded += String.fromCharCode(...chunk.subarray(i, i + 8192));
                      }
                      return { status: response.status, type, data: btoa(encoded) };
                    }
                    """, new { url = image.Url, maxBytes = CoverDownloader.MaxCoverBytes });
                var status = result.GetProperty("status").GetInt32();
                if (status >= 400)
                    throw new PageNavigationException(status, image.Url);
                var data = result.GetProperty("data");
                if (data.ValueKind != JsonValueKind.String)
                    return new FetchedCover { Success = false };
                return new FetchedCover { Success = true, Url = image.Url,
                    MimeType = result.GetProperty("type").GetString()!, Image = Convert.FromBase64String(data.GetString()!) };
            }, cancellationToken);
        }
        finally { _navigation.Release(); }
    }

    public void Dispose() => _navigation.Dispose();
}

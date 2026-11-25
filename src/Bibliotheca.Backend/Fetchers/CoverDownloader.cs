using Bibliotheca.Backend.Options;
using Microsoft.Extensions.Options;

namespace Bibliotheca.Backend.Fetchers;

public sealed class CoverDownloader(HttpClient client, IOptions<FetcherOptions> options)
{
    public const int MaxCoverBytes = 5 * 1024 * 1024;

    public async Task<FetchedCover> DownloadAsync(string isbn, string imageUrl, CancellationToken cancellationToken)
    {
        var unavailable = new FetchedCover { Url = imageUrl, Success = false };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(client.Timeout);
        using var response = await client.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        var mimeType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        if (!response.IsSuccessStatusCode ||
            mimeType is not ("image/jpeg" or "image/png" or "image/webp" or "image/gif" or "image/avif" or "image/bmp" or "image/tiff") ||
            response.Content.Headers.ContentLength is > MaxCoverBytes)
            return unavailable;

        using var source = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var image = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var remaining = MaxCoverBytes - (int)image.Length;
            var count = await source.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining + 1)), deadline.Token);
            if (count == 0)
                break;
            if (count > remaining)
                return unavailable;
            image.Write(buffer, 0, count);
        }

        if (image.Length == 0)
            return unavailable;

        var bytes = image.ToArray();
        if (options.Value.SaveTempFiles)
        {
            var extension = mimeType["image/".Length..];
            var path = Path.Combine(options.Value.TempFilesSaveLocation, $"{Path.GetFileName(isbn)}.{extension}");
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        }

        return new FetchedCover { Url = imageUrl, Success = true, Image = bytes, MimeType = mimeType };
    }
}

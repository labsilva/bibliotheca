using System.Net;
using System.Net.Http.Headers;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Options;
using Microsoft.Extensions.Options;

namespace Bibliotheca.Backend.Tests;

public class CoverDownloaderTests
{
    private const string Isbn = "9780140328721";
    private const string ImageUrl = "https://covers.example/cover";

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "image/png", null)]
    [InlineData(HttpStatusCode.OK, "text/html", null)]
    [InlineData(HttpStatusCode.OK, null, null)]
    [InlineData(HttpStatusCode.OK, "image/png", CoverDownloader.MaxCoverBytes + 1)]
    public async Task RejectsInvalidHeadersBeforeReadingBody(HttpStatusCode status, string? type, int? length)
    {
        var body = new ChunkedBody([1, 2, 3]);
        using var client = CreateClient(body, status, type, length);

        var cover = await DownloadAsync(client);

        Assert.False(cover.Success);
        Assert.Null(cover.Image);
        Assert.Equal(0, body.BytesRead);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public async Task StopsReadingAtFirstByteOverLimit(int? advertisedLength)
    {
        var body = new ChunkedBody(new byte[CoverDownloader.MaxCoverBytes + 1024]);
        using var client = CreateClient(body, HttpStatusCode.OK, "image/jpeg", advertisedLength);

        var cover = await DownloadAsync(client);

        Assert.False(cover.Success);
        Assert.Null(cover.Image);
        Assert.Equal(CoverDownloader.MaxCoverBytes + 1, body.BytesRead);
    }

    [Fact]
    public async Task ReturnsImageAndResponseMimeType()
    {
        var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==");
        using var client = CreateClient(new ChunkedBody(gif), HttpStatusCode.OK, "image/gif; name=cover.gif");

        var cover = await DownloadAsync(client);

        Assert.True(cover.Success);
        Assert.Equal(gif, cover.Image);
        Assert.Equal("image/gif", cover.MimeType);
        Assert.Equal(ImageUrl, cover.Url);
    }

    [Fact]
    public async Task AcceptsBodyExactlyAtLimit()
    {
        var bytes = new byte[CoverDownloader.MaxCoverBytes];
        using var client = CreateClient(new ChunkedBody(bytes), HttpStatusCode.OK, "image/webp");

        var cover = await DownloadAsync(client);

        Assert.True(cover.Success);
        Assert.Equal(bytes.Length, cover.Image.Length);
    }

    [Fact]
    public async Task PropagatesCallerCancellationDuringBodyRead()
    {
        using var cancellation = new CancellationTokenSource();
        var body = new ChunkedBody([1], cancellation.Cancel);
        using var client = CreateClient(body, HttpStatusCode.OK, "image/png");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DownloadAsync(client, cancellation.Token));
    }

    private static Task<FetchedCover> DownloadAsync(HttpClient client, CancellationToken token = default) =>
        new CoverDownloader(client, Microsoft.Extensions.Options.Options.Create(new FetcherOptions())).DownloadAsync(Isbn, ImageUrl, token);

    private static HttpClient CreateClient(Stream body, HttpStatusCode status, string? type, int? length = null)
    {
        var content = new StreamContent(body);
        if (type != null)
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(type);
        if (length.HasValue)
            content.Headers.ContentLength = length.Value;
        return new HttpClient(new ResponseHandler(new HttpResponseMessage(status) { Content = content }));
    }

    private sealed class ResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response);
    }

    private sealed class ChunkedBody(byte[] bytes, Action? beforeRead = null) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public int BytesRead { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            beforeRead?.Invoke();
            var count = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += count;
            return count;
        }
    }
}

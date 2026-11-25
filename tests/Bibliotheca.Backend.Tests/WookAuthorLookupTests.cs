using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Options;
using Bibliotheca.Backend.Services;
using Microsoft.Extensions.Options;

namespace Bibliotheca.Backend.Tests;

public class WookAuthorLookupTests
{
    private const string Edition = "livro/tormenta/23897332";
    private const string Product = """
        <h1>A Tormenta</h1><table><caption><p class="font-medium">A Tormenta</p><p class="authors">
        <a href="#author-22998">George R. R. Martin</a></p></caption>
        <tr><td>ISBN</td><td>9789897731921</td></tr></table>
        <div id="author-22998"></div><a href="/autor/george-r-r-martin/22998/122">George R. R. Martin</a>
        """;
    private const string Author = """
        <div itemtype="https://schema.org/Person"><h1 itemprop="name">George R. R. Martin</h1>
        <img itemprop="image" src="https://img.wook.pt/autor/imagem/example">
        <div itemprop="description"><div class="bio-text">A complete biography.</div></div></div>
        """;

    [Fact]
    public async Task FindsProfileThroughVerifiedEditionAndAuthorId()
    {
        var reader = new Reader((uri) => uri.AbsolutePath.StartsWith("/autor/") ? Author : Product);
        var result = await Create(reader).FindAsync("George R.R. Martin", [new("9789897731921", Edition)], default);
        Assert.NotNull(result);
        Assert.Equal("A complete biography.", result.Biography);
        Assert.Equal("https://img.wook.pt/autor/imagem/example", result.PhotoUrl);
        Assert.Equal("https://www.wook.pt/autor/george-r-r-martin/22998", result.SourceUrl);
    }

    [Fact]
    public async Task RejectsMismatchedIsbnAndAuthorName()
    {
        var reader = new Reader(uri => uri.AbsolutePath.StartsWith("/autor/") ? Author : Product);
        Assert.Null(await Create(reader).FindAsync("George R. R. Martin", [new("9780140328721", Edition)], default));
        Assert.Null(await Create(reader).FindAsync("Another author", [new("9789897731921", Edition)], default));
    }

    [Fact]
    public async Task RejectsMultipleMatchingAuthorIds()
    {
        var product = Product.Replace("</p></caption>", "<a href=\"#author-333\">George R. R. Martin</a></p></caption>");
        var result = await Create(new Reader(_ => product)).FindAsync("George R. R. Martin", [new("9789897731921", Edition)], default);
        Assert.True(result!.Ambiguous);
    }

    [Fact]
    public async Task AllowsAProfileWithOnlyAPhoto()
    {
        var incomplete = Author.Replace("<div itemprop=\"description\"><div class=\"bio-text\">A complete biography.</div></div>", "");
        var result = await Create(new Reader(uri => uri.AbsolutePath.StartsWith("/autor/") ? incomplete : Product))
            .FindAsync("George R. R. Martin", [new("9789897731921", Edition)], default);
        Assert.NotNull(result);
        Assert.Null(result.Biography);
        Assert.NotNull(result.PhotoUrl);
    }

    [Fact]
    public async Task RejectsBlockedPages()
    {
        var lookup = Create(new StatusReader());
        await Assert.ThrowsAsync<InvalidDataException>(() => lookup.FindAsync("George R. R. Martin", [new("9789897731921", Edition)], default));
    }

    private sealed class StatusReader : IWookPageReader
    {
        public Task<WookPage> ReadAsync(Uri address, CancellationToken token) => Task.FromResult(new WookPage(address.AbsoluteUri, 403, "blocked"));
    }

    [Fact]
    public async Task DisabledProviderMakesNoRequests()
    {
        var reader = new Reader(_ => throw new Exception());
        var service = new WookAuthorLookup(reader, Microsoft.Extensions.Options.Options.Create(new ExternalEditionSearchOptions()));
        Assert.Null(await service.FindAsync("George R. R. Martin", [new("9789897731921", Edition)], default));
    }

    private static WookAuthorLookup Create(IWookPageReader reader) => new(reader, Microsoft.Extensions.Options.Options.Create(new ExternalEditionSearchOptions
    { Providers = new(StringComparer.OrdinalIgnoreCase) { ["wook"] = new() { Enabled = true, TimeoutSeconds = 15 } } }));
    private sealed class Reader(Func<Uri, string> html) : IWookPageReader
    {
        public Task<WookPage> ReadAsync(Uri address, CancellationToken token) => Task.FromResult(new WookPage(address.AbsoluteUri, 200, html(address)));
    }
}

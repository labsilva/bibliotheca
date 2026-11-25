using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bibliotheca.Backend.Tests;

public class FetchBookTests
{
    [Fact]
    public async Task TriesProvidersInPriorityOrderAndStopsAfterSuccess()
    {
        const string isbn = "9780140328721";
        using var cancellation = new CancellationTokenSource();
        var calls = new List<string>();
        var successful = new FetchedBook { Isbn = isbn, Title = "Matilda", Success = true };
        var later = new StubFetcher(40, (_, _) => throw new InvalidOperationException("Unexpected provider"));
        var winner = new StubFetcher(30, (requestedIsbn, token) =>
        {
            calls.Add("winner");
            return requestedIsbn == isbn && token == cancellation.Token
                ? successful
                : new FetchedBook { Success = false };
        });
        var missing = new StubFetcher(20, (_, _) =>
        {
            calls.Add("missing");
            return new FetchedBook { Success = false };
        });
        var unavailable = new StubFetcher(10, (_, _) =>
        {
            calls.Add("unavailable");
            throw new HttpRequestException("Provider unavailable");
        });
        var service = new FetchBookService([later, winner, missing, unavailable], NullLogger<FetchBookService>.Instance);

        var result = await service.ExecuteAsync(new FetchBookServiceArgs { Isbn = isbn, FailOnProviderErrors = true }, cancellation.Token);

        Assert.Same(successful, result);
        Assert.Equal(["unavailable", "missing", "winner"], calls);
        Assert.Equal(0, later.Calls);
    }

    [Fact]
    public async Task ReturnsFailureWhenNoProvidersAreConfigured()
    {
        var service = new FetchBookService([], NullLogger<FetchBookService>.Instance);

        var result = await service.ExecuteAsync(new FetchBookServiceArgs { Isbn = "9780140328721" });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task StrictMetadataFetchDistinguishesProviderErrorsFromAResultThatWasNotFound()
    {
        var missing = new StubFetcher(20, (_, _) => new FetchedBook { Success = false });
        var unavailable = new StubFetcher(10, (_, _) => throw new HttpRequestException("Provider unavailable"));
        var covers = new FetchBookCoverService([], NullLogger<FetchBookCoverService>.Instance);
        var books = new FetchBookService([missing, unavailable], NullLogger<FetchBookService>.Instance);
        var metadata = new FetchBookMetadataService(books, covers, NullLogger<FetchBookMetadataService>.Instance);
        var request = new FetchBookMetadataServiceArgs { Isbn = "9780140328721" };

        Assert.False((await metadata.ExecuteAsync(request)).Book.Success);
        var strictRequest = request with { FailOnProviderErrors = true };
        var error = await Assert.ThrowsAnyAsync<InvalidOperationException>(() => metadata.ExecuteAsync(strictRequest));
        Assert.Equal("Book metadata providers could not be reached.", error.Message);

        var missingOnly = new FetchBookService([missing], NullLogger<FetchBookService>.Instance);
        var missingMetadata = new FetchBookMetadataService(missingOnly, covers, NullLogger<FetchBookMetadataService>.Instance);
        Assert.False((await missingMetadata.ExecuteAsync(strictRequest)).Book.Success);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CallerCancellationStopsMetadataFallback(bool throws)
    {
        using var cancellation = new CancellationTokenSource();
        var first = new StubFetcher(10, (_, token) =>
        {
            cancellation.Cancel();
            if (throws)
                token.ThrowIfCancellationRequested();
            return new FetchedBook { Success = false };
        });
        var next = new StubFetcher(20, (_, _) => new FetchedBook { Success = true });
        var service = new FetchBookService([first, next], NullLogger<FetchBookService>.Instance);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExecuteAsync(new() { Isbn = "9780140328721" }, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, first.Calls);
        Assert.Equal(0, next.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CallerCancellationStopsCoverFallback(bool throws)
    {
        using var cancellation = new CancellationTokenSource();
        var first = new StubCoverFetcher(10, () =>
        {
            cancellation.Cancel();
            if (throws)
                throw new HttpRequestException("Provider disconnected");
            return new FetchedCover { Success = false };
        });
        var next = new StubCoverFetcher(20, () => new FetchedCover { Success = true });
        var service = new FetchBookCoverService([first, next], NullLogger<FetchBookCoverService>.Instance);

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExecuteAsync(new() { Isbn = "9780140328721" }, cancellation.Token));

        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Single(first.Requests);
        Assert.Empty(next.Requests);
    }

    [Fact]
    public async Task ProviderTimeoutStillAllowsMetadataAndCoverFallback()
    {
        var book = new FetchedBook { Success = true };
        var cover = new FetchedCover { Success = true };
        var books = new FetchBookService([
            new StubFetcher(10, (_, _) => throw new TaskCanceledException("Provider timed out")),
            new StubFetcher(20, (_, _) => book)
        ], NullLogger<FetchBookService>.Instance);
        var covers = new FetchBookCoverService([
            new StubCoverFetcher(10, () => throw new TaskCanceledException("Provider timed out")),
            new StubCoverFetcher(20, () => cover)
        ], NullLogger<FetchBookCoverService>.Instance);
        var service = new FetchBookMetadataService(books, covers, NullLogger<FetchBookMetadataService>.Instance);

        var result = await service.ExecuteAsync(new() { Isbn = "9780140328721" });

        Assert.Same(book, result.Book);
        Assert.Same(cover, result.Cover);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task FetchesCoverOnlyWhenMetadataWasFound(bool hasBook, bool hasCover)
    {
        const string isbn = "9780140328721";
        var book = new FetchedBook { Isbn = isbn, Title = "Matilda", Success = hasBook };
        var cover = new FetchedCover { Success = hasCover, Image = [1], MimeType = "image/png" };
        var unavailable = new StubCoverFetcher(10, () => throw new HttpRequestException("Provider unavailable"));
        var fallback = new StubCoverFetcher(20, () => cover);
        var books = new FetchBookService([new StubFetcher(10, (_, _) => book)], NullLogger<FetchBookService>.Instance);
        var covers = new FetchBookCoverService([fallback, unavailable], NullLogger<FetchBookCoverService>.Instance);
        var metadata = new FetchBookMetadataService(books, covers, NullLogger<FetchBookMetadataService>.Instance);
        using var cancellation = new CancellationTokenSource();

        var result = await metadata.ExecuteAsync(new FetchBookMetadataServiceArgs { Isbn = isbn }, cancellation.Token);

        Assert.Equal(isbn, result.Isbn);
        Assert.Same(book, result.Book);
        Assert.Equal(hasBook && hasCover, result.Success);
        if (hasBook)
        {
            Assert.Same(cover, result.Cover);
            Assert.Equal((isbn, cancellation.Token), Assert.Single(unavailable.Requests));
            Assert.Equal((isbn, cancellation.Token), Assert.Single(fallback.Requests));
        }
        else
        {
            Assert.False(result.Cover.Success);
            Assert.Empty(unavailable.Requests);
            Assert.Empty(fallback.Requests);
        }
    }

    [Fact]
    public async Task ReturnsAnUnavailableCoverWhenAllProvidersThrow()
    {
        var unavailable = new StubCoverFetcher(10, () => throw new HttpRequestException("Provider unavailable"));
        var service = new FetchBookCoverService([unavailable], NullLogger<FetchBookCoverService>.Instance);

        var cover = await service.ExecuteAsync(new FetchBookCoverServiceArgs { Isbn = "9780140328721" });

        Assert.False(cover.Success);
        Assert.Single(unavailable.Requests);
    }

    private sealed class StubCoverFetcher(int priority, Func<FetchedCover> fetch) : ICoverFetcher, IFetcherPriority
    {
        public int Priority { get; } = priority;
        public List<(string Isbn, CancellationToken Token)> Requests { get; } = [];

        public Task<FetchedCover> FetchCoverAsync(string isbn, CancellationToken cancellationToken)
        {
            Requests.Add((isbn, cancellationToken));
            return Task.FromResult(fetch());
        }
    }

    private sealed class StubFetcher(int priority, Func<string, CancellationToken, FetchedBook> fetch)
        : IBookFetcher, IFetcherPriority
    {
        public int Priority { get; } = priority;
        public int Calls { get; private set; }

        public Task<FetchedBook> FetchBookAsync(string isbn, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(fetch(isbn, cancellationToken));
        }
    }
}

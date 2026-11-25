using Bibliotheca.Backend.Controllers;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Jobs;
using Bibliotheca.Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bibliotheca.Backend.Tests;

public class AuthorRefreshTests
{
    [Fact]
    public async Task IndividualRefreshQueuesOnlySelectedAuthor()
    {
        await using var db = await TestDatabase.CreateDatabaseAsync();
        db.Authors.AddRange(new Author { AuthorName = "First" }, new Author { AuthorName = "Second" });
        await db.SaveChangesAsync();
        var id = await db.Authors.Where(author => author.AuthorName == "Second").Select(author => author.AuthorId).SingleAsync();
        var controller = Controller(db);
        Assert.IsType<NotFoundResult>(await controller.StartAuthor(-1, default));
        var accepted = Assert.IsType<AuthorRefreshAccepted>(Assert.IsType<AcceptedResult>(await controller.StartAuthor(id, default)).Value);
        Assert.Equal(1, accepted.Total);
        Assert.Equal(id, (await db.AuthorRefreshItems.SingleAsync()).AuthorId);
        Assert.IsType<OkObjectResult>(await controller.GetBatch(accepted.Id, default));
        Assert.IsType<ConflictObjectResult>(await controller.StartAuthor(id, default));
        Assert.IsType<NotFoundResult>(await controller.GetBatch(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task RefreshResumesAndReportsOutcomesWithoutChangingBookAssociations()
    {
        await using var database = await TestDatabase.CreateDatabaseAsync();
        var existing = new Author
        {
            AuthorName = "Linked author", SourceAuthorKey = "/authors/OL1A", PhotoUrl = "keep.jpg",
            Biography = "Old biography", BiographySourceUrl = "https://openlibrary.org/authors/OL1A"
        };
        var failed = new Author { AuthorName = "Unavailable" };
        var conflicting = new Author { AuthorName = "Same name" };
        var legacy = new Author { AuthorName = "Legacy profile" };
        var noEvidence = new Author { AuthorName = "No edition" };
        database.Authors.AddRange(existing, failed, conflicting, legacy, noEvidence);
        await database.SaveChangesAsync();
        var edition = NewBook("Referenced edition", "", legacy);
        database.Books.AddRange(NewBook("Unavailable edition", "9780140328721", failed),
            NewBook("First edition", "9780140328738", conflicting), NewBook("Second edition", "9780140328745", conflicting), edition);
        database.BookEditionReferences.Add(new BookEditionReference { ProviderId = "openLibrary", ExternalId = "OL52M", Book = edition });
        await database.SaveChangesAsync();

        var controller = Controller(database);
        Assert.Equal(5, Assert.IsType<AuthorRefreshAccepted>(Assert.IsType<AcceptedResult>(await controller.Start(default)).Value).Total);
        var lookup = new ProfileLookup();
        await using var services = CreateServices(database, lookup);
        Assert.True(await Worker(services).ProcessNextAsync(default));
        var inProgress = Assert.IsType<AuthorRefreshOverview>(Assert.IsType<OkObjectResult>(await controller.Get(default)).Value);
        Assert.Equal(1, inProgress.Latest!.Updated);
        Assert.Null(inProgress.Latest.CompletedAtUtc);
        await database.AuthorRefreshes.ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseOwner, Guid.NewGuid())
            .SetProperty(row => row.LeaseUntilUtc, DateTime.UtcNow.AddMinutes(-1)));

        var restarted = Worker(services);
        for (var index = 0; index < 4; index++)
            Assert.True(await restarted.ProcessNextAsync(default));
        Assert.False(await restarted.ProcessNextAsync(default));
        Assert.Equal(1, lookup.KnownKeyCalls);

        var report = Assert.IsType<AuthorRefreshOverview>(Assert.IsType<OkObjectResult>(await controller.Get(default)).Value);
        Assert.Equal(5, report.AuthorCount);
        Assert.Equal(2, report.Latest!.Updated);
        Assert.Equal(2, report.Latest.Unchanged);
        Assert.Equal(1, report.Latest.Failed);
        Assert.NotNull(report.Latest.CompletedAtUtc);
        Assert.Equal(failed.AuthorId, Assert.Single(report.Errors).AuthorId);
        var stored = await database.Authors.AsNoTracking().ToDictionaryAsync(author => author.AuthorName);
        Assert.Equal("Updated biography", stored[existing.AuthorName].Biography);
        Assert.Equal("keep.jpg", stored[existing.AuthorName].PhotoUrl);
        Assert.Null(stored[conflicting.AuthorName].SourceAuthorKey);
        Assert.Null(stored[conflicting.AuthorName].Biography);
        Assert.Equal("/authors/OL7A", stored[legacy.AuthorName].SourceAuthorKey);
        Assert.Equal("new.jpg", stored[legacy.AuthorName].PhotoUrl);
        var members = await database.Books.AsNoTracking().OrderBy(book => book.BookId)
            .Select(book => book.Authors!.Select(author => author.AuthorId).ToArray()).ToArrayAsync();
        Assert.Equal([failed.AuthorId, conflicting.AuthorId, conflicting.AuthorId, legacy.AuthorId], members.Select(ids => Assert.Single(ids)));
    }

    [Fact]
    public async Task CurrentAuthorIsOnlyReportedWhileWorkerLeaseIsValid()
    {
        await using var database = await TestDatabase.CreateDatabaseAsync();
        var author = new Author { AuthorName = "Current author" };
        database.Authors.Add(author);
        await database.SaveChangesAsync();
        var controller = Controller(database);
        await controller.Start(default);
        async Task<AuthorRefreshReport> Report() =>
            Assert.IsType<AuthorRefreshOverview>(Assert.IsType<OkObjectResult>(await controller.Get(default)).Value).Latest!;
        Assert.Null((await Report()).Current);
        await database.AuthorRefreshes.ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseOwner, Guid.NewGuid())
            .SetProperty(row => row.LeaseUntilUtc, DateTime.UtcNow.AddMinutes(1)));
        Assert.Equal(author.AuthorId, (await Report()).Current!.AuthorId);
        await database.AuthorRefreshes.ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseUntilUtc, DateTime.UtcNow.AddMinutes(-1)));
        Assert.Null((await Report()).Current);
    }

    [Fact]
    public async Task CurrentBookIsOnlyReportedWhileWorkerLeaseIsValid()
    {
        await using var database = await TestDatabase.CreateDatabaseAsync();
        var book = NewBook("Current book", "", new Author { AuthorName = "An author" });
        database.Books.Add(book);
        await database.SaveChangesAsync();
        var controller = new CatalogueRefreshController(database) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        await controller.Start(default);
        async Task<CatalogueRefreshReport> Report() =>
            Assert.IsType<CatalogueRefreshOverview>(Assert.IsType<OkObjectResult>(await controller.Get(default)).Value).Latest!;
        Assert.Null((await Report()).Current);
        await database.CatalogueRefreshes.ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseOwner, Guid.NewGuid())
            .SetProperty(row => row.LeaseUntilUtc, DateTime.UtcNow.AddMinutes(1)));
        Assert.Equal(book.BookId, (await Report()).Current!.BookId);
        await database.CatalogueRefreshes.ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseUntilUtc, DateTime.UtcNow.AddMinutes(-1)));
        Assert.Null((await Report()).Current);
    }

    [Fact]
    public async Task StaleLeaseCannotSaveProfileOrProgress()
    {
        await using var database = await TestDatabase.CreateDatabaseAsync();
        database.Authors.Add(new Author { AuthorName = "Linked author", SourceAuthorKey = "/authors/OL1A" });
        await database.SaveChangesAsync();
        await Controller(database).Start(default);
        var replacement = Guid.NewGuid();
        var lookup = new ProfileLookup(async () => await database.AuthorRefreshes.ExecuteUpdateAsync(set => set
            .SetProperty(row => row.LeaseOwner, replacement).SetProperty(row => row.LeaseUntilUtc, DateTime.UtcNow.AddMinutes(2))));
        await using var services = CreateServices(database, lookup);
        Assert.False(await Worker(services).ProcessNextAsync(default));
        Assert.Equal(replacement, (await database.AuthorRefreshes.AsNoTracking().SingleAsync()).LeaseOwner);
        Assert.Equal("pending", (await database.AuthorRefreshItems.AsNoTracking().SingleAsync()).Status);
        Assert.Null((await database.Authors.AsNoTracking().SingleAsync()).Biography);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AuthorAndBookRefreshJobsBlockEachOther(bool authorFirst)
    {
        await using var database = await TestDatabase.CreateDatabaseAsync();
        database.Books.Add(NewBook("An edition", "", new Author { AuthorName = "An author" }));
        await database.SaveChangesAsync();
        var author = Controller(database);
        var catalogue = new CatalogueRefreshController(database)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        if (authorFirst)
        {
            Assert.IsType<AcceptedResult>(await author.Start(default));
            Assert.IsType<ConflictObjectResult>(await author.Start(default));
            Assert.IsType<ConflictObjectResult>(await catalogue.Start(default));
        }
        else
        {
            Assert.IsType<AcceptedResult>(await catalogue.Start(default));
            Assert.IsType<ConflictObjectResult>(await author.Start(default));
            var report = Assert.IsType<AuthorRefreshOverview>(Assert.IsType<OkObjectResult>(await author.Get(default)).Value);
            Assert.True(report.CatalogueRefreshActive);
            Assert.Null(report.Latest);
        }
    }

    [Fact]
    public async Task ConcurrentAuthorAndBookRequestsAcceptOneJob()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-refresh-lock-");
        try
        {
            var options = new DbContextOptionsBuilder<BibliothecaDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory.FullName, "refresh.db")};Pooling=False")
                .AddInterceptors(new DiacriticsInterceptor()).Options;
            await using var database = new BibliothecaDbContext(options);
            await database.Database.MigrateAsync();
            database.Books.Add(NewBook("An edition", "", new Author { AuthorName = "An author" }));
            await database.SaveChangesAsync();

            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var authorRequest = Task.Run(async () =>
            {
                await using var context = new BibliothecaDbContext(options);
                await ready.Task;
                return await Controller(context).Start(default);
            });
            var catalogueRequest = Task.Run(async () =>
            {
                await using var context = new BibliothecaDbContext(options);
                var controller = new CatalogueRefreshController(context)
                {
                    ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
                };
                await ready.Task;
                return await controller.Start(default);
            });
            ready.SetResult();
            var results = await Task.WhenAll(authorRequest, catalogueRequest);
            Assert.Single(results.OfType<AcceptedResult>());
            Assert.Single(results.OfType<ConflictObjectResult>());
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task WookEnrichesAWeakProfileWithoutChangingItsOpenLibraryKey()
    {
        await using var database = await TestDatabase.CreateDatabaseAsync();
        var author = new Author { AuthorName = "Linked author", SourceAuthorKey = "/authors/OL1A",
            Biography = "Old biography", BiographySourceUrl = "https://openlibrary.org/authors/OL1A" };
        database.Books.Add(NewBook("An edition", "9780140328721", author));
        await database.SaveChangesAsync();
        await Controller(database).Start(default);
        var options = new DbContextOptionsBuilder<BibliothecaDbContext>().UseSqlite(database.Database.GetDbConnection())
            .AddInterceptors(new DiacriticsInterceptor()).Options;
        await using var services = new ServiceCollection().AddScoped(_ => new BibliothecaDbContext(options))
            .AddSingleton<IAuthorProfileLookup>(new ProfileLookup()).AddSingleton<IWookAuthorLookup>(new WookProfile()).BuildServiceProvider();
        Assert.True(await Worker(services).ProcessNextAsync(default));
        var stored = await database.Authors.AsNoTracking().SingleAsync();
        Assert.Equal("/authors/OL1A", stored.SourceAuthorKey);
        Assert.Equal("new.jpg", stored.PhotoUrl);
        Assert.Equal("https://www.wook.pt/autor/linked-author/123", stored.BiographySourceUrl);
        Assert.True(stored.Biography!.Length >= 100);
    }

    private sealed class WookProfile : IWookAuthorLookup
    {
        public Task<WookAuthorResult?> FindAsync(string name, IReadOnlyList<WookAuthorEdition> editions, CancellationToken token) =>
            Task.FromResult<WookAuthorResult?>(new("https://img.wook.pt/author.jpg", new string('A', 150), "https://www.wook.pt/autor/linked-author/123"));
    }

    [Fact]
    public async Task WorkerLogsBatchItemOutcomeAndCompletion()
    {
        await using var database = await TestDatabase.CreateDatabaseAsync();
        database.Authors.Add(new Author { AuthorName = "No edition" });
        await database.SaveChangesAsync();
        await Controller(database).Start(default);
        await using var services = CreateServices(database, new ProfileLookup());
        var logger = new RecordingLogger<AuthorRefreshWorker>();
        var worker = new AuthorRefreshWorker(services.GetRequiredService<IServiceScopeFactory>(), logger);
        Assert.True(await worker.ProcessNextAsync(default));
        Assert.Contains(logger.Messages, message => message.Contains("started author"));
        Assert.Contains(logger.Messages, message => message.Contains("finished author") && message.Contains("unchanged"));
        Assert.Contains(logger.Messages, message => message.Contains("completed after"));
    }

    private sealed class RecordingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId id,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    private static AuthorRefreshController Controller(BibliothecaDbContext database) => new(database)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
    };

    private static Book NewBook(string title, string isbn, Author author) => new()
    {
        Title = title, Isbn = isbn, CreatedBy = "test", ModifiedBy = "test", Authors = [author]
    };

    private static ServiceProvider CreateServices(BibliothecaDbContext database, IAuthorProfileLookup lookup)
    {
        var options = new DbContextOptionsBuilder<BibliothecaDbContext>().UseSqlite(database.Database.GetDbConnection())
            .AddInterceptors(new DiacriticsInterceptor()).Options;
        return new ServiceCollection().AddScoped(_ => new BibliothecaDbContext(options))
            .AddSingleton(lookup).BuildServiceProvider();
    }

    private static AuthorRefreshWorker Worker(ServiceProvider services) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AuthorRefreshWorker>.Instance);

    private sealed class ProfileLookup(Func<Task>? beforeLookup = null) : IAuthorProfileLookup
    {
        public int KnownKeyCalls { get; private set; }

        public async Task<AuthorProfile?> FindProfileByKeyAsync(string sourceAuthorKey, string authorName, CancellationToken cancellationToken)
        {
            KnownKeyCalls++;
            Assert.Equal("Linked author", authorName);
            Assert.Equal("/authors/OL1A", sourceAuthorKey);
            if (beforeLookup is not null)
                await beforeLookup();
            return new(sourceAuthorKey, "new.jpg", "Updated biography", "https://openlibrary.org/authors/OL1A");
        }

        public Task<IReadOnlyDictionary<string, AuthorProfile>> FindProfilesAsync(FetchedBook book, IReadOnlyList<string> authors,
            CancellationToken cancellationToken)
        {
            var name = Assert.Single(authors);
            var key = book.Title switch
            {
                "Unavailable edition" => throw new HttpRequestException("Provider unavailable"),
                "First edition" => "/authors/OL2A",
                "Second edition" => "/authors/OL3A",
                "Referenced edition" when book.Url == "https://openlibrary.org/books/OL52M" => "/authors/OL7A",
                _ => throw new InvalidOperationException("Unexpected edition fixture")
            };
            return Task.FromResult<IReadOnlyDictionary<string, AuthorProfile>>(new Dictionary<string, AuthorProfile>
            {
                [name] = new(key, "new.jpg", "Found biography", $"https://openlibrary.org{key}")
            });
        }
    }
}

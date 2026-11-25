using Bibliotheca.Backend.Controllers;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Jobs;
using Bibliotheca.Backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bibliotheca.Backend.Tests;

public class CatalogueRefreshTests
{
    [Fact]
    public async Task SingleBookRefreshQueuesOnlyRequestedBookAndReportsItsStatus()
    {
        await using var db = await TestDatabase.CreateDatabaseAsync();
        db.Books.AddRange(new Book { Title = "First", Isbn = "1", CreatedBy = "test", ModifiedBy = "test" },
            new Book { Title = "Second", Isbn = "2", CreatedBy = "test", ModifiedBy = "test" });
        await db.SaveChangesAsync();
        var id = await db.Books.Where(book => book.Title == "Second").Select(book => book.BookId).SingleAsync();
        var controller = Controller(db);
        Assert.IsType<NotFoundResult>(await controller.StartBook(-1, default));
        var accepted = Assert.IsType<CatalogueRefreshAccepted>(Assert.IsType<AcceptedResult>(await controller.StartBook(id, default)).Value);
        Assert.Equal(1, accepted.Total);
        Assert.Equal(id, (await db.CatalogueRefreshItems.SingleAsync()).BookId);
        Assert.IsType<OkObjectResult>(await controller.GetBatch(accepted.Id, default));
        Assert.IsType<ConflictObjectResult>(await controller.StartBook(id, default));
        Assert.IsType<NotFoundResult>(await controller.GetBatch(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task ReclaimedLeaseFencesOldWorkersMetadataAndProgress()
    {
        await using var db = await TestDatabase.CreateDatabaseAsync();
        var book = new Book { Title = "Keep original", Isbn = "9780140328721", CreatedBy = "test", ModifiedBy = "test" };
        db.Books.Add(book);
        await db.SaveChangesAsync();
        await Controller(db).Start(default);
        var nextOwner = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<BibliothecaDbContext>()
            .UseSqlite(db.Database.GetDbConnection()).AddInterceptors(new DiacriticsInterceptor()).Options;
        var services = new ServiceCollection().AddLogging();
        services.AddScoped(_ => new BibliothecaDbContext(options));
        services.AddScoped<FetchBookService>();
        services.AddScoped<FetchBookCoverService>();
        services.AddScoped<FetchBookMetadataService>();
        services.AddScoped<StoreFetchedBookService>();
        services.AddSingleton<IAuthorProfileLookup>(NoAuthorPhotoLookup.Instance);
        services.AddSingleton<IBookFetcher>(new ReclaimingFetcher(async () =>
            await db.CatalogueRefreshes.ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseOwner, nextOwner)
                .SetProperty(row => row.LeaseUntilUtc, DateTime.UtcNow.AddMinutes(3)))));
        await using var provider = services.BuildServiceProvider();
        var worker = new CatalogueRefreshWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<CatalogueRefreshWorker>.Instance);
        Assert.False(await worker.ProcessNextAsync(default));
        db.ChangeTracker.Clear();
        Assert.Equal("Keep original", (await db.Books.SingleAsync()).Title);
        Assert.Equal("pending", (await db.CatalogueRefreshItems.SingleAsync()).Status);
        Assert.Equal(nextOwner, (await db.CatalogueRefreshes.SingleAsync()).LeaseOwner);
    }

    [Fact]
    public async Task DatabaseRejectsOverlappingBatchesAndAllowsCompletedHistory()
    {
        await using var db = await TestDatabase.CreateDatabaseAsync();
        db.CatalogueRefreshes.AddRange(new CatalogueRefresh { ActiveSlot = null, CompletedAtUtc = DateTime.UtcNow },
            new CatalogueRefresh { ActiveSlot = null, CompletedAtUtc = DateTime.UtcNow }, new CatalogueRefresh());
        await db.SaveChangesAsync();
        db.CatalogueRefreshes.Add(new CatalogueRefresh());
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task RefreshPreservesPersonalDataAndPartialMetadataAndRecoversBetweenWorkers()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-refresh-");
        try
        {
            var options = new DbContextOptionsBuilder<BibliothecaDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory.FullName, "test.db")};Pooling=False")
                .AddInterceptors(new DiacriticsInterceptor()).Options;
            var services = new ServiceCollection().AddLogging();
            services.AddScoped(_ => new BibliothecaDbContext(options));
            services.AddScoped<FetchBookService>();
            services.AddScoped<FetchBookCoverService>();
            services.AddScoped<FetchBookMetadataService>();
            services.AddScoped<StoreFetchedBookService>();
            services.AddSingleton<IBookFetcher, FixtureFetcher>();
            services.AddSingleton<IAuthorProfileLookup>(NoAuthorPhotoLookup.Instance);
            services.AddSingleton<IExternalEditionSearch, ReferenceSearch>();
            await using var provider = services.BuildServiceProvider();
            await using var db = new BibliothecaDbContext(options);
            await db.Database.MigrateAsync();
            var user = new User { Name = "alice", FullName = "Alice", Email = "alice@example.invalid", Password = "fixture", Settings = "{}" };
            var book = new Book { Title = "Original", Isbn = "9780140328721", Subtitle = "Keep subtitle", CreatedBy = "test", ModifiedBy = "test",
                AdditionalData = new() { ["synopsis"] = "Keep synopsis" } };
            var missing = new Book { Title = "No ISBN", Isbn = "", CreatedBy = "test", ModifiedBy = "test" };
            var mismatched = new Book { Title = "Different edition", Isbn = "9780140328738", CreatedBy = "test", ModifiedBy = "test" };
            var linked = new Book { Title = "Linked edition", Isbn = "", CreatedBy = "test", ModifiedBy = "test" };
            db.AddRange(book, missing, mismatched, linked);
            db.BookEditionReferences.Add(new BookEditionReference { Book = linked, ProviderId = "fixture", ExternalId = "stable-edition" });
            var shelf = new Shelve { User = user, ShelveId = 1, ShelveName = "Personal shelf" };
            var personal = new UserBook { User = user, Book = book, Shelve = shelf, ShelveId = 1, ReadingStatus = BookReadingStatus.Read,
                ReadingTimestamp = new DateTime(2020, 1, 2), Rating = 5, Comments = "Keep notes", Favorite = true };
            db.UsersBooks.Add(personal);
            await db.SaveChangesAsync();
            var controller = Controller(db);
            Assert.IsType<AcceptedResult>(await controller.Start(default));
            Assert.IsType<ConflictObjectResult>(await controller.Start(default));
            var first = new CatalogueRefreshWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<CatalogueRefreshWorker>.Instance);
            Assert.True(await first.ProcessNextAsync(default));
            await db.CatalogueRefreshes.ExecuteUpdateAsync(set => set
                .SetProperty(row => row.LeaseOwner, Guid.NewGuid())
                .SetProperty(row => row.LeaseUntilUtc, DateTime.UtcNow.AddMinutes(-1)));
            // A new worker resumes persisted progress without repeating the first item.
            var restarted = new CatalogueRefreshWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<CatalogueRefreshWorker>.Instance);
            Assert.True(await restarted.ProcessNextAsync(default));
            Assert.True(await restarted.ProcessNextAsync(default));
            Assert.True(await restarted.ProcessNextAsync(default));
            Assert.False(await restarted.ProcessNextAsync(default));
            db.ChangeTracker.Clear();
            var updated = await db.Books.SingleAsync(row => row.BookId == book.BookId);
            Assert.Equal("Updated title", updated.Title);
            Assert.Equal("Keep subtitle", updated.Subtitle);
            Assert.Equal("Keep synopsis", updated.AdditionalData!["synopsis"].ToString());
            var preserved = await db.UsersBooks.SingleAsync();
            Assert.Equal(new DateTime(2020, 1, 2), preserved.ReadingTimestamp);
            Assert.Equal((short)5, preserved.Rating);
            Assert.Equal("Keep notes", preserved.Comments);
            Assert.True(preserved.Favorite);
            Assert.Equal(1, preserved.ShelveId);
            Assert.Equal(BookReadingStatus.Read, preserved.ReadingStatus);
            Assert.Equal(4, await db.Books.CountAsync());
            Assert.Equal("Exact referenced edition", (await db.Books.SingleAsync(row => row.BookId == linked.BookId)).Title);
            var batch = await db.CatalogueRefreshes.SingleAsync();
            Assert.NotNull(batch.CompletedAtUtc);
            Assert.Null(batch.ActiveSlot);
            Assert.Equal(new[] { "updated", "unchanged", "failed", "updated" }, await db.CatalogueRefreshItems.OrderBy(row => row.Id).Select(row => row.Status).ToArrayAsync());
            Assert.Contains("different edition", (await db.CatalogueRefreshItems.SingleAsync(row => row.Status == "failed")).Error);
            var report = Assert.IsType<CatalogueRefreshOverview>(Assert.IsType<OkObjectResult>(await controller.Get(default)).Value);
            Assert.Equal(4, report.BookCount);
            Assert.Equal(2, report.Latest!.Updated);
            Assert.Equal(1, report.Latest.Unchanged);
            Assert.Equal(1, report.Latest.Failed);
            Assert.Equal(mismatched.BookId, Assert.Single(report.Errors).BookId);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task EmptyCatalogueCompletesImmediatelyAndActiveLeaseIsRespected()
    {
        await using var db = await TestDatabase.CreateDatabaseAsync();
        var controller = Controller(db);
        Assert.IsType<AcceptedResult>(await controller.Start(default));
        var batch = await db.CatalogueRefreshes.SingleAsync();
        Assert.NotNull(batch.CompletedAtUtc);
        Assert.Null(batch.ActiveSlot);
        db.CatalogueRefreshes.Add(new CatalogueRefresh { LeaseOwner = Guid.NewGuid(), LeaseUntilUtc = DateTime.UtcNow.AddMinutes(2) });
        await db.SaveChangesAsync();
        using var provider = new ServiceCollection().AddScoped(_ => db).BuildServiceProvider();
        var worker = new CatalogueRefreshWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<CatalogueRefreshWorker>.Instance);
        Assert.False(await worker.ProcessNextAsync(default));
    }

    private sealed class FixtureFetcher : IBookFetcher
    {
        public Task<FetchedBook> FetchBookAsync(string isbn, CancellationToken cancellationToken) => Task.FromResult(new FetchedBook
        {
            Success = true, Isbn = "9780140328721", Title = "Updated title", Authors = [], PublishedBy = "", Url = ""
        });
    }
    private sealed class ReclaimingFetcher(Func<Task> reclaim) : IBookFetcher
    {
        public async Task<FetchedBook> FetchBookAsync(string isbn, CancellationToken cancellationToken)
        {
            await reclaim();
            return new FetchedBook { Success = true, Isbn = isbn, Title = "Stale worker must not save", Authors = [], PublishedBy = "", Url = "" };
        }
    }
    private static CatalogueRefreshController Controller(BibliothecaDbContext db) => new(db)
    {
        ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }
    };
    private sealed class ReferenceSearch : IExternalEditionSearch
    {
        public Task<ExternalEdition?> ResolveAsync(string providerId, string externalId, CancellationToken cancellationToken)
        {
            Assert.Equal("fixture", providerId);
            Assert.Equal("stable-edition", externalId);
            return Task.FromResult<ExternalEdition?>(new(externalId, "Exact referenced edition"));
        }
        public IReadOnlyList<ExternalProviderStatus> GetProviders() => [];
        public Task<ExternalEditionSearchResponse> SearchAsync(string title, string? author, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Refresh must never search by title.");
    }
}

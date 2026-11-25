using System.Security.Claims;
using Bibliotheca.Backend.Controllers;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bibliotheca.Backend.Tests;

public class AuthorDuplicateTests
{
    [Fact]
    public async Task ScanResumesFromCheckpointAndDeferringOrRejectingSurvivesRescans()
    {
        await using var db = await TestDatabase.CreateDatabaseAsync();
        var first = new Author { AuthorName = "George R.R. Martin" };
        var second = new Author { AuthorName = "George R. R. Martin" };
        var unrelated = new Author { AuthorName = "George Martin" };
        var homonym = new Author { AuthorName = "George R. R. Martin", SourceAuthorKey = "/authors/OL3A" };
        db.Authors.AddRange(first, second, unrelated, homonym);
        await db.SaveChangesAsync();
        var controller = Controller(db);
        Assert.IsType<AcceptedResult>(await controller.Start(default));
        Assert.IsType<ConflictObjectResult>(await controller.Start(default));
        var scan = await db.AuthorDuplicateScans.SingleAsync();
        scan.LastAuthorId = first.AuthorId;
        scan.Processed = 1;
        scan.NewSuggestions = 2;
        db.AuthorDuplicateSuggestions.AddRange(
            new AuthorDuplicateSuggestion { FirstAuthorId = first.AuthorId, SecondAuthorId = second.AuthorId },
            new AuthorDuplicateSuggestion { FirstAuthorId = first.AuthorId, SecondAuthorId = homonym.AuthorId });
        await db.SaveChangesAsync();

        var interrupted = await Overview(controller);
        Assert.True(interrupted.Latest!.Active);
        Assert.Equal(1, interrupted.Latest.Processed);
        await using var services = Services(db);
        Assert.True(await Worker(services).ProcessNextAsync(default, batchSize: 1));
        Assert.False(await Worker(services).ProcessNextAsync(default));
        var completed = await Overview(controller);
        Assert.False(completed.Latest!.Active);
        Assert.Equal(4, completed.Latest.Processed);
        Assert.Equal(3, completed.PendingCount);
        Assert.Equal(3, completed.Latest.NewSuggestions);
        Assert.Equal("GEORGERRMARTIN", completed.Comparison!.NormalizedName);
        Assert.Equal(first.AuthorId, completed.Comparison.First.Id);
        Assert.Equal(second.AuthorId, completed.Comparison.Second.Id);
        Assert.False(await db.AuthorDuplicateSuggestions.AnyAsync(pair => pair.FirstAuthorId == unrelated.AuthorId || pair.SecondAuthorId == unrelated.AuthorId));

        Assert.IsType<NoContentResult>(await controller.Review(completed.Comparison.Id,
            new("defer", completed.Comparison.Version, null), default));
        var deferred = await Overview(controller, deferred: true);
        Assert.Equal(1, deferred.DeferredCount);
        Assert.Equal(2, deferred.PendingCount);
        Assert.Equal(completed.Comparison.Id, deferred.Comparison!.Id);
        Assert.IsType<ConflictObjectResult>(await controller.Review(deferred.Comparison.Id,
            new("reject", completed.Comparison.Version, null), default));
        Assert.IsType<NoContentResult>(await controller.Review(deferred.Comparison.Id,
            new("reject", deferred.Comparison.Version, null), default));
        await controller.Start(default);
        await Worker(services).ProcessNextAsync(default);
        var rescanned = await Overview(controller);
        Assert.Equal(0, rescanned.DeferredCount);
        Assert.Equal(0, rescanned.Latest!.NewSuggestions);
        Assert.Equal(2, rescanned.PendingCount);
        Assert.Equal(4, await db.Authors.CountAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MergePreservesBookCreditsAndReaderDataAndResolvesProfileConflicts(bool conflictingKeys)
    {
        await using var db = await TestDatabase.CreateDatabaseAsync();
        var keep = new Author
        {
            AuthorName = "Ada B. Writer", PhotoUrl = "keep.jpg", SourceAuthorKey = conflictingKeys ? "/authors/OL1A" : null
        };
        var remove = new Author
        {
            AuthorName = "Ada B Writer", PhotoUrl = "discard.jpg", SourceAuthorKey = "/authors/OL2A",
            Biography = "Biography to fill if safe", BiographySourceUrl = "https://example.invalid/bio"
        };
        var coauthor = new Author { AuthorName = "A different author" };
        db.Authors.AddRange(keep, remove, coauthor);
        var shared = Book("Shared", keep, remove);
        var transferred = Book("Transferred", remove, coauthor);
        var user = new User { Name = "reader", Email = "reader@example.invalid", FullName = "Reader", Password = "test", Settings = "{}" };
        var shelf = new Shelve { ShelveId = 1, User = user, ShelveName = "Owned" };
        db.UsersBooks.Add(new UserBook
        {
            User = user, Shelve = shelf, ShelveId = shelf.ShelveId, Book = transferred, ReadingStatus = BookReadingStatus.Read,
            Rating = 5, Comments = "Personal notes", Favorite = true
        });
        db.Books.Add(shared);
        await db.SaveChangesAsync();
        var controller = Controller(db);
        await using var services = Services(db);
        await controller.Start(default);
        await Worker(services).ProcessNextAsync(default);
        var comparison = (await Overview(controller)).Comparison!;
        Assert.Equal(1, comparison.SharedBookCount);
        Assert.Equal(2, comparison.Second.Books.Length);

        var decision = new ReviewAuthorDuplicateRequest("merge", comparison.Version, keep.AuthorId);
        await db.Books.Where(book => book.BookId == transferred.BookId)
            .ExecuteUpdateAsync(set => set.SetProperty(book => book.Title, "Edited title"));
        Assert.IsType<ConflictObjectResult>(await controller.Review(comparison.Id, decision, default));
        db.ChangeTracker.Clear();
        var current = (await Overview(controller)).Comparison!;
        decision = decision with { Version = current.Version };
        Assert.IsType<BadRequestObjectResult>(await controller.Review(current.Id, decision with { KeepAuthorId = coauthor.AuthorId }, default));
        db.AuthorRefreshes.Add(new AuthorRefresh());
        await db.SaveChangesAsync();
        Assert.IsType<ConflictObjectResult>(await controller.Review(current.Id, decision, default));
        await db.AuthorRefreshes.ExecuteDeleteAsync();
        Assert.IsType<NoContentResult>(await controller.Review(current.Id, decision, default));
        Assert.IsType<ConflictObjectResult>(await controller.Review(current.Id, decision, default));

        var authors = await db.Authors.AsNoTracking().OrderBy(author => author.AuthorId).ToArrayAsync();
        Assert.Equal(2, authors.Length);
        Assert.DoesNotContain(authors, author => author.AuthorId == remove.AuthorId);
        var retained = Assert.Single(authors, author => author.AuthorId == keep.AuthorId);
        Assert.Equal(keep.AuthorName, retained.AuthorName);
        Assert.Equal("keep.jpg", retained.PhotoUrl);
        Assert.Equal(keep.SourceAuthorKey, retained.SourceAuthorKey);
        Assert.Equal(conflictingKeys ? null : remove.Biography, retained.Biography);
        Assert.Equal(conflictingKeys ? null : remove.BiographySourceUrl, retained.BiographySourceUrl);
        var books = await db.Books.AsNoTracking()
            .Select(book => new { book.BookId, AuthorIds = book.Authors!.Select(author => author.AuthorId).ToArray() })
            .ToDictionaryAsync(book => book.BookId);
        Assert.Equal([keep.AuthorId], books[shared.BookId].AuthorIds);
        Assert.Equal([keep.AuthorId, coauthor.AuthorId], books[transferred.BookId].AuthorIds.Order());
        var personal = await db.UsersBooks.AsNoTracking().SingleAsync();
        Assert.Equal((user.Id, shelf.ShelveId, transferred.BookId, BookReadingStatus.Read, (short?)5, "Personal notes", true),
            (personal.UserId, personal.ShelveId, personal.BookId, personal.ReadingStatus, personal.Rating, personal.Comments, personal.Favorite));
        var audit = await db.AuthorDuplicateSuggestions.AsNoTracking().SingleAsync();
        Assert.Equal(AuthorDuplicateSuggestion.Merged, audit.Status);
        Assert.Equal(keep.AuthorId, audit.KeptAuthorId);
        Assert.Equal("manager", audit.ReviewedBy);
        Assert.NotNull(audit.ReviewedAtUtc);
    }

    [Fact]
    public async Task MergePreservesRejectionWhenTheRejectedProfileReceivesACanonicalId()
    {
        await using var db = await TestDatabase.CreateDatabaseAsync();
        db.Authors.AddRange(new Author { AuthorName = "A. Name" },
            new Author { AuthorName = "A Name" }, new Author { AuthorName = "A.Name" });
        await db.SaveChangesAsync();
        var controller = Controller(db);
        await using var services = Services(db);
        await controller.Start(default);
        await Worker(services).ProcessNextAsync(default);
        var first = (await Overview(controller)).Comparison!;
        var last = (await Overview(controller, after: first.Id + 1)).Comparison!;
        Assert.Equal(first.Second.Id, last.First.Id);
        Assert.IsType<NoContentResult>(await controller.Review(last.Id, new("reject", last.Version, null), default));
        Assert.IsType<NoContentResult>(await controller.Review(first.Id, new("merge", first.Version, first.First.Id), default));
        Assert.Equal(0, (await Overview(controller)).PendingCount);
        Assert.Equal(2, await db.AuthorDuplicateSuggestions.CountAsync(pair => pair.Status == AuthorDuplicateSuggestion.Rejected));
    }

    [Fact]
    public async Task ConcurrentStartsAndMergesHaveOneWinner()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-duplicate-concurrency-");
        try
        {
            var options = new DbContextOptionsBuilder<BibliothecaDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory.FullName, "test.db")};Pooling=False;Foreign Keys=True")
                .AddInterceptors(new DiacriticsInterceptor()).Options;
            await using var services = new ServiceCollection().AddScoped(_ => new BibliothecaDbContext(options)).BuildServiceProvider();
            await using var db = new BibliothecaDbContext(options);
            await db.Database.MigrateAsync();
            db.Authors.AddRange(new Author { AuthorName = "A. Writer" }, new Author { AuthorName = "A Writer" });
            await db.SaveChangesAsync();
            async Task<IActionResult> Start()
            {
                await using var context = new BibliothecaDbContext(options);
                return await Controller(context).Start(default);
            }
            var starts = await Task.WhenAll(Task.Run(Start), Task.Run(Start));
            Assert.Single(starts.OfType<AcceptedResult>());
            Assert.Single(starts.OfType<ConflictObjectResult>());
            await Task.WhenAll(Task.Run(() => Worker(services).ProcessNextAsync(default)),
                Task.Run(() => Worker(services).ProcessNextAsync(default)));
            var comparison = (await Overview(Controller(db))).Comparison!;
            Assert.Equal(1, await db.AuthorDuplicateSuggestions.CountAsync());

            async Task<IActionResult> Merge(int keep)
            {
                await using var context = new BibliothecaDbContext(options);
                return await Controller(context).Review(comparison.Id, new("merge", comparison.Version, keep), default);
            }
            var merges = await Task.WhenAll(Task.Run(() => Merge(comparison.First.Id)), Task.Run(() => Merge(comparison.Second.Id)));
            Assert.Single(merges.OfType<NoContentResult>());
            Assert.Single(merges.OfType<ConflictObjectResult>());
            Assert.Equal(1, await db.Authors.CountAsync());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static AuthorDuplicatesController Controller(BibliothecaDbContext db) =>
        new(db, NullLogger<AuthorDuplicatesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "manager")], "test"))
                }
            }
        };

    private static async Task<AuthorDuplicateOverview> Overview(AuthorDuplicatesController controller, bool deferred = false, int after = 0) =>
        Assert.IsType<AuthorDuplicateOverview>(Assert.IsType<OkObjectResult>((await controller.Get(default, deferred, after)).Result).Value);

    private static Book Book(string title, params Author[] authors) =>
        new() { Title = title, Isbn = "", CreatedBy = "test", ModifiedBy = "test", Authors = authors.ToList() };

    private static ServiceProvider Services(BibliothecaDbContext database) =>
        new ServiceCollection().AddScoped(_ =>
            new BibliothecaDbContext(new DbContextOptionsBuilder<BibliothecaDbContext>()
                .UseSqlite(database.Database.GetDbConnection()).Options)).BuildServiceProvider();

    private static AuthorDuplicateScanWorker Worker(ServiceProvider services) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AuthorDuplicateScanWorker>.Instance);
}

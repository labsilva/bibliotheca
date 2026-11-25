using System.Diagnostics;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Jobs;

public sealed class CatalogueRefreshWorker(IServiceScopeFactory scopes, ILogger<CatalogueRefreshWorker> logger) : BackgroundService
{
    private readonly Guid owner = Guid.NewGuid();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessNextAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Catalogue refresh failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
        var now = DateTime.UtcNow;
        var batch = await db.CatalogueRefreshes.AsNoTracking().SingleOrDefaultAsync(row => row.ActiveSlot == 1, cancellationToken);
        if (batch is null) return false;
        var claimed = await db.CatalogueRefreshes.Where(row => row.Id == batch.Id && row.ActiveSlot == 1 &&
            (row.LeaseOwner == owner || row.LeaseUntilUtc == null || row.LeaseUntilUtc < now))
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseOwner, owner)
                .SetProperty(row => row.LeaseUntilUtc, now.AddMinutes(3)), cancellationToken);
        if (claimed == 0) return false;
        if (batch.LeaseOwner is not null && batch.LeaseUntilUtc <= now)
            logger.LogInformation("Resuming catalogue refresh {BatchId} after an expired lease", batch.Id);
        try
        {
            var item = await db.CatalogueRefreshItems.AsTracking().Where(row => row.CatalogueRefreshId == batch.Id && row.Status == "pending")
                .OrderBy(row => row.Id).FirstOrDefaultAsync(cancellationToken);
            if (item is null) return false;
            var elapsed = Stopwatch.StartNew();
            logger.LogInformation("Catalogue refresh {BatchId} started book {BookId}", batch.Id, item.BookId);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                var book = await db.Books.AsTracking().AsSplitQuery().Include(row => row.Authors)
                    .Include(row => row.Publisher).Include(row => row.Collection).Include(row => row.Genres)
                    .Include(row => row.ExternalLinks).Include(row => row.BookCover)
                    .SingleOrDefaultAsync(row => row.BookId == item.BookId, timeout.Token);
                if (book is null) throw new CatalogueRefreshException("The book was deleted.");
                var isbn = EditionIsbn.Normalize(book.Isbn);
                if (isbn is null)
                {
                    var edition = await ResolveReferenceAsync(db, scope.ServiceProvider, book.BookId, timeout.Token);
                    if (edition is null) item.Status = "unchanged";
                    else
                    {
                        var metadata = edition.ToFetchedBook();
                        // The persisted reference identifies this edition; retain its catalogue ISBN.
                        metadata.Isbn = book.Isbn;
                        if (book.PublishedOn.HasValue && edition.Details?.PublishedOn is null)
                            metadata.PublishedOn = null;
                        var changed = await scope.ServiceProvider.GetRequiredService<StoreFetchedBookService>().RefreshExistingAsync(book,
                            new StoreFetchedBookArgs { Book = metadata }, timeout.Token);
                        item.Status = changed ? "updated" : "unchanged";
                    }
                }
                else
                {
                    var fetched = await scope.ServiceProvider.GetRequiredService<FetchBookMetadataService>()
                        .ExecuteAsync(new FetchBookMetadataServiceArgs { Isbn = isbn, FailOnProviderErrors = true }, timeout.Token).WaitAsync(timeout.Token);
                    if (!fetched.Book.Success) item.Status = "unchanged";
                    else if (EditionIsbn.Normalize(fetched.Book.Isbn) != isbn)
                        throw new CatalogueRefreshException("The provider returned a different edition ISBN.");
                    else
                    {
                        var changed = await scope.ServiceProvider.GetRequiredService<StoreFetchedBookService>().RefreshExistingAsync(book,
                            new StoreFetchedBookArgs { Book = fetched.Book, Cover = fetched.Cover.Success ? fetched.Cover : null }, timeout.Token);
                        item.Status = changed ? "updated" : "unchanged";
                    }
                }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Could not refresh book {BookId}", item.BookId);
                var id = item.Id;
                db.ChangeTracker.Clear();
                item = await db.CatalogueRefreshItems.AsTracking().SingleAsync(row => row.Id == id, cancellationToken);
                item.Status = "failed";
                item.Error = exception is OperationCanceledException ? "Provider request timed out."
                    : exception is CatalogueRefreshException or BookMetadataProvidersUnavailableException
                        ? exception.Message : "Could not refresh this book.";
            }
            // Fence the commit against an expired/reclaimed lease. Provider IO stays outside
            // this short transaction; a stalled old worker may never overwrite a new owner's work.
            await using (var transaction = await db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, cancellationToken))
            {
                var commitTime = DateTime.UtcNow;
                var stillOwned = await db.CatalogueRefreshes.Where(row => row.Id == batch.Id && row.ActiveSlot == 1 &&
                        row.LeaseOwner == owner && row.LeaseUntilUtc > commitTime)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseUntilUtc, commitTime.AddMinutes(1)), cancellationToken);
                if (stillOwned == 0)
                {
                    db.ChangeTracker.Clear();
                    return false;
                }
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            logger.LogInformation("Catalogue refresh {BatchId} finished book {BookId}: {Outcome} in {ElapsedMs} ms", batch.Id, item.BookId, item.Status, elapsed.ElapsedMilliseconds);
            return true;
        }
        finally
        {
            using var release = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var pending = await db.CatalogueRefreshItems.AnyAsync(row => row.CatalogueRefreshId == batch.Id && row.Status == "pending", release.Token);
            var query = db.CatalogueRefreshes.Where(row => row.Id == batch.Id && row.LeaseOwner == owner);
            if (!pending)
            {
                var released = await query.ExecuteUpdateAsync(set => set.SetProperty(row => row.ActiveSlot, (int?)null)
                    .SetProperty(row => row.CompletedAtUtc, DateTime.UtcNow).SetProperty(row => row.LeaseOwner, (Guid?)null)
                    .SetProperty(row => row.LeaseUntilUtc, (DateTime?)null), release.Token);
                if (released > 0)
                    logger.LogInformation("Catalogue refresh {BatchId} completed after {ElapsedMs} ms", batch.Id, (DateTime.UtcNow - batch.CreatedAtUtc).TotalMilliseconds);
            }
            else
                await query.ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseOwner, (Guid?)null)
                    .SetProperty(row => row.LeaseUntilUtc, (DateTime?)null), release.Token);
        }
    }

    private static async Task<ExternalEdition?> ResolveReferenceAsync(BibliothecaDbContext db,
        IServiceProvider services, int bookId, CancellationToken cancellationToken)
    {
        var references = await db.BookEditionReferences.AsNoTracking().Where(row => row.BookId == bookId)
            .OrderBy(row => row.ProviderId).Select(row => new { row.ProviderId, row.ExternalId }).ToArrayAsync(cancellationToken);
        if (references.Length == 0) return null;
        var search = services.GetRequiredService<IExternalEditionSearch>();
        Exception? failure = null;
        foreach (var reference in references)
        {
            try
            {
                var edition = await search.ResolveAsync(reference.ProviderId, reference.ExternalId, cancellationToken).WaitAsync(cancellationToken);
                if (edition?.ExternalId == reference.ExternalId) return edition;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested) { failure = exception; }
        }
        if (failure is not null) throw new CatalogueRefreshException("The linked edition providers could not be reached.");
        return null;
    }
}

internal sealed class CatalogueRefreshException(string message) : Exception(message);

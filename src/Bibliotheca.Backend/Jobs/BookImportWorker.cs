using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Jobs;

public sealed class BookImportWorker(
    BookImportSignal signal,
    IServiceScopeFactory scopeFactory,
    NotifyFetchedBookService notifications,
    ILogger<BookImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await ProcessNextAsync(stoppingToken))
                    await signal.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not update the import queue");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
        var import = await database.BookImports.AsTracking()
            .Where(import => import.CompletedAtUtc == null)
            .OrderBy(import => import.CreatedAtUtc).ThenBy(import => import.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (import is null)
            return false;

        if (!import.StartedAtUtc.HasValue)
        {
            import.StartedAtUtc = DateTime.UtcNow;
            await database.SaveChangesAsync(cancellationToken);
        }

        var edition = import.SelectedEdition;
        var isbn = edition is null ? import.Isbns[import.Succeeded + import.Failed] : EditionIsbn.Normalize(edition.Isbn);
        (int? BookId, string Title)? book = null;
        try
        {
            if (edition is null)
            {
                IsbnImportMetrics.Queue.Record((DateTime.UtcNow - import.CreatedAtUtc).TotalMilliseconds);
                await using var jobScope = scopeFactory.CreateAsyncScope();
                var job = jobScope.ServiceProvider.GetRequiredService<FetchBookJob>();
                var fetched = await job.FetchBookAsync(isbn!, cancellationToken);
                if (import.ShelfId is { } shelfId)
                {
                    var store = scope.ServiceProvider.GetRequiredService<StoreExternalEdition>();
                    var shelf = await store.FindShelfAsync(import.Owner, shelfId, cancellationToken)
                        ?? throw new BookImportException("The destination shelf no longer exists.");
                    var stored = await database.Books.SingleAsync(row => row.BookId == fetched.BookId, cancellationToken);
                    await store.AssignShelfAsync(stored, shelf, cancellationToken);
                }
                book = fetched;
            }
            else
            {
                var store = scope.ServiceProvider.GetRequiredService<StoreExternalEdition>();
                await using var transaction = await database.Database.BeginTransactionAsync(
                    System.Data.IsolationLevel.Serializable, cancellationToken);
                await database.Entry(import).ReloadAsync(cancellationToken);
                if (import.CompletedAtUtc.HasValue)
                    return true;
                var shelf = import.ShelfId is { } id ? await store.FindShelfAsync(import.Owner, id, cancellationToken) : null;
                if (import.ShelfId.HasValue && shelf is null)
                    throw new InvalidOperationException("The destination shelf no longer exists.");

                var stored = await store.StoreAsync(import.EditionProviderId!, edition, shelf, cancellationToken);
                await database.SaveChangesAsync(cancellationToken);
                import.ResultBookId = stored.BookId;
                import.Succeeded++;
                import.CompletedAtUtc = DateTime.UtcNow;
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                book = (stored.BookId, stored.Title);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (edition is null)
                import.Succeeded++;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Import {ImportId} failed", import.Id);
            var importId = import.Id;
            database.ChangeTracker.Clear();
            import = await database.BookImports.AsTracking().SingleAsync(row => row.Id == importId, cancellationToken);
            if (edition is null)
            {
                import.IsbnFailurePositions = [.. import.IsbnFailurePositions, import.Succeeded + import.Failed];
                import.IsbnFailureReasons = [.. import.IsbnFailureReasons, exception is BookImportException
                    ? exception.Message : "Could not import this ISBN."];
            }
            book = null;
            import.Failed++;
        }

        if (import.CompletedAtUtc is null && import.Succeeded + import.Failed == import.Total)
            import.CompletedAtUtc = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        try
        {
            await notifications.SendAsync(import.Owner,
                new BookImportNotification(isbn ?? "", book.HasValue, book?.Title, book?.BookId, BookImportStatus.From(import)),
                cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Could not send notification for import {ImportId}", import.Id);
        }

        return true;
    }
}

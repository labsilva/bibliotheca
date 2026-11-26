using System.Data;
using System.Diagnostics;
using Bibliotheca.Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Jobs;

public sealed class AuthorRefreshWorker(IServiceScopeFactory scopes, ILogger<AuthorRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessNextAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Author refresh failed"); }

            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
        var batch = await database.AuthorRefreshes.AsNoTracking().SingleOrDefaultAsync(row => row.ActiveSlot == 1, cancellationToken);
        if (batch is null)
            return false;

        var owner = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var claimed = await database.AuthorRefreshes.Where(row => row.Id == batch.Id && row.ActiveSlot == 1 &&
            (row.LeaseUntilUtc == null || row.LeaseUntilUtc <= now))
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseOwner, owner)
                .SetProperty(row => row.LeaseUntilUtc, now.AddMinutes(3)), cancellationToken);
        if (claimed == 0)
            return false;

        if (batch.LeaseOwner is not null && batch.LeaseUntilUtc <= now)
            logger.LogInformation("Resuming author refresh {BatchId} after an expired lease", batch.Id);
        try
        {
            var item = await database.AuthorRefreshItems.AsTracking()
                .Where(row => row.AuthorRefreshId == batch.Id && row.Status == "pending")
                .OrderBy(row => row.Id).FirstOrDefaultAsync(cancellationToken);
            if (item is null)
                return false;

            var elapsed = Stopwatch.StartNew();
            logger.LogInformation("Author refresh {BatchId} started author {AuthorId}", batch.Id, item.AuthorId);
            var author = await database.Authors.AsNoTracking().SingleOrDefaultAsync(row => row.AuthorId == item.AuthorId, cancellationToken);
            var resolution = await new AuthorRefreshProfiles(logger)
                .ResolveAsync(database, scope.ServiceProvider, author, cancellationToken);

            // Provider IO stays outside the transaction. Fence both the author update and progress against lease loss.
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var commitTime = DateTime.UtcNow;
            var stillOwned = await database.AuthorRefreshes.Where(row => row.Id == batch.Id && row.ActiveSlot == 1 &&
                row.LeaseOwner == owner && row.LeaseUntilUtc > commitTime)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseUntilUtc, commitTime.AddMinutes(1)), cancellationToken);
            if (stillOwned == 0)
                return false;

            var current = await database.Authors.AsTracking().SingleOrDefaultAsync(row => row.AuthorId == item.AuthorId, cancellationToken);
            item.Error = resolution.Error ?? (current is null ? "The author was deleted." : null);
            var changed = item.Error is null && current is not null && current.AuthorName == author?.AuthorName &&
                resolution.ApplyTo(current);
            if (resolution.IsAmbiguous) item.Error = "Wook author identity is ambiguous.";
            item.Status = item.Error is not null ? "failed" : changed ? "updated" : "unchanged";
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Author refresh {BatchId} finished author {AuthorId}: {Outcome} in {ElapsedMs} ms", batch.Id, item.AuthorId, item.Status, elapsed.ElapsedMilliseconds);
            return true;
        }
        finally
        {
            using var release = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var pending = await database.AuthorRefreshItems.AnyAsync(row => row.AuthorRefreshId == batch.Id && row.Status == "pending", release.Token);
            DateTime? completedAt = pending ? null : DateTime.UtcNow;
            int? activeSlot = pending ? 1 : null;
            var released = await database.AuthorRefreshes.Where(row => row.Id == batch.Id && row.LeaseOwner == owner)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(row => row.ActiveSlot, activeSlot)
                    .SetProperty(row => row.CompletedAtUtc, completedAt)
                    .SetProperty(row => row.LeaseOwner, (Guid?)null)
                    .SetProperty(row => row.LeaseUntilUtc, (DateTime?)null), release.Token);
            if (released > 0 && !pending)
                logger.LogInformation("Author refresh {BatchId} completed after {ElapsedMs} ms", batch.Id, (DateTime.UtcNow - batch.CreatedAtUtc).TotalMilliseconds);
        }
    }
}

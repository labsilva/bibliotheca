using System.Data;
using System.Text;
using Bibliotheca.Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Jobs;

public sealed class AuthorDuplicateScanWorker(IServiceScopeFactory scopes, ILogger<AuthorDuplicateScanWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ProcessNextAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Author duplicate scan failed"); }

            try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken, int batchSize = 100)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<BibliothecaDbContext>();
        var scan = await database.AuthorDuplicateScans.AsNoTracking()
            .SingleOrDefaultAsync(row => row.ActiveSlot == 1, cancellationToken);
        if (scan is null)
            return false;

        var names = await database.Authors.AsNoTracking().Where(author => author.AuthorId <= scan.MaxAuthorId)
            .OrderBy(author => author.AuthorId).Select(author => new { author.AuthorId, author.AuthorName })
            .ToArrayAsync(cancellationToken);
        var authors = names.Select(author => (Id: author.AuthorId, Key: Normalize(author.AuthorName))).ToArray();
        var groups = authors.Where(author => author.Key.Length > 0).ToLookup(author => author.Key, author => author.Id);
        var start = Array.FindIndex(authors, author => author.Id > scan.LastAuthorId);
        if (start < 0)
            start = authors.Length;

        // Each commit advances the cursor and saves its candidates together. A crashed transaction
        // rolls back, and another worker can continue from the last committed cursor.
        for (var index = start; ; )
        {
            cancellationToken.ThrowIfCancellationRequested();
            var end = Math.Min(index + batchSize, authors.Length);
            var batch = authors[index..end];
            var pairs = batch.SelectMany(author => groups[author.Key].Where(id => id > author.Id),
                (author, id) => (First: author.Id, Second: id)).ToArray();

            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            database.ChangeTracker.Clear();
            var current = await database.AuthorDuplicateScans.AsTracking()
                .SingleAsync(row => row.Id == scan.Id, cancellationToken);
            if (current.ActiveSlot is null || current.LastAuthorId != scan.LastAuthorId)
                return true;

            if (batch.Length > 0)
            {
                var first = batch[0].Id;
                var last = batch[^1].Id;
                var existing = await database.AuthorDuplicateSuggestions.AsNoTracking()
                    .Where(pair => pair.FirstAuthorId >= first && pair.FirstAuthorId <= last)
                    .Select(pair => new { pair.FirstAuthorId, pair.SecondAuthorId }).ToArrayAsync(cancellationToken);
                var seen = existing.Select(pair => (pair.FirstAuthorId, pair.SecondAuthorId)).ToHashSet();
                foreach (var (left, right) in pairs)
                {
                    if (seen.Contains((left, right)))
                        continue;
                    database.AuthorDuplicateSuggestions.Add(new AuthorDuplicateSuggestion { FirstAuthorId = left, SecondAuthorId = right });
                    current.NewSuggestions++;
                }
                current.LastAuthorId = last;
                current.Processed += batch.Length;
            }
            if (end == authors.Length)
            {
                current.ActiveSlot = null;
                current.CompletedAtUtc = DateTime.UtcNow;
            }
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            if (current.ActiveSlot is null)
                return true;

            scan.LastAuthorId = current.LastAuthorId;
            index = end;
        }
    }

    internal static string Normalize(string name)
    {
        var normalized = new StringBuilder(name.Length);
        foreach (var character in name.Normalize(NormalizationForm.FormC).EnumerateRunes())
        {
            if (!Rune.IsWhiteSpace(character) && !Rune.IsPunctuation(character))
                normalized.Append(Rune.ToUpperInvariant(character).ToString());
        }
        return normalized.ToString();
    }
}

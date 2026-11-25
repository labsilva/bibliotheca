using System.Data;
using System.Threading.Channels;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Jobs;

public sealed class BookImportQueue(BibliothecaDbContext database, BookImportSignal signal)
{
    public const int Capacity = 100;

    public async Task<IActionResult> SubmitAsync(string owner, string[] isbns, CancellationToken cancellationToken,
        int? shelfId = null, Guid? batchId = null)
    {
        if (batchId.HasValue)
        {
            if (isbns.Length != 1 || EditionIsbn.Normalize(isbns[0]) is not { } normalized)
                return new BadRequestObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest, Detail = "Provide one valid ISBN per scan."
                });
            isbns = [normalized];
        }

        if (isbns.Length is 0 or > Capacity)
            return new BadRequestObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Detail = $"Provide between 1 and {Capacity} books per import."
            });

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (shelfId is { } id && !await database.Shelves.AnyAsync(
            shelf => shelf.ShelveId == id && shelf.User.Name == owner, cancellationToken))
            return new NotFoundResult();

        if (batchId.HasValue)
        {
            var scans = database.BookImports.AsNoTracking().Where(row => row.Owner == owner && row.BatchId == batchId);
            if (await scans.AnyAsync(row => row.ShelfId != shelfId, cancellationToken))
                return new ConflictObjectResult(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict, Detail = "The destination shelf is fixed for this batch."
                });

            var isbn = isbns[0];
            var previous = await scans.FirstOrDefaultAsync(row => row.Isbns.Contains(isbn), cancellationToken);
            if (previous is not null)
                return Accepted(previous);
        }

        if (!await HasCapacityAsync(isbns.Length, cancellationToken))
            return QueueFull();

        var import = new BookImport { Owner = owner, Isbns = isbns, Total = isbns.Length, ShelfId = shelfId, BatchId = batchId };
        database.BookImports.Add(import);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        signal.Notify();
        return Accepted(import);
    }

    public async Task<IActionResult?> SubmitEditionAsync(string owner, string providerId, string externalId,
        ExternalEdition? edition, int? shelfId, StoreExternalEdition store, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var shelf = shelfId is { } id ? await store.FindShelfAsync(owner, id, cancellationToken) : null;
        if (shelfId.HasValue && shelf is null)
            return new NotFoundResult();

        var existing = await store.FindBookAsync(providerId, externalId, edition?.Isbn, cancellationToken);
        if (existing is not null)
        {
            await store.LinkAsync(providerId, externalId, existing, shelf, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new OkObjectResult(new ExistingExternalEditionResponse(existing.BookId));
        }

        var pending = await database.BookImports.AsNoTracking().FirstOrDefaultAsync(import =>
            import.CompletedAtUtc == null && import.Owner == owner && import.ShelfId == shelfId &&
            import.EditionProviderId == providerId && import.EditionExternalId == externalId, cancellationToken);
        if (pending is not null)
            return Accepted(pending);

        if (edition is null)
            return null;

        if (!await HasCapacityAsync(1, cancellationToken))
            return QueueFull();

        var queued = new BookImport
        {
            Owner = owner,
            Isbns = [],
            EditionProviderId = providerId,
            EditionExternalId = edition.ExternalId,
            SelectedEdition = edition,
            ShelfId = shelfId,
            Total = 1
        };
        database.BookImports.Add(queued);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        signal.Notify();
        return Accepted(queued);
    }

    private async Task<bool> HasCapacityAsync(int count, CancellationToken cancellationToken) =>
        count + await database.BookImports.Where(import => import.CompletedAtUtc == null)
            .SumAsync(import => import.Total - import.Succeeded - import.Failed, cancellationToken) <= Capacity;

    private static AcceptedResult Accepted(BookImport import) =>
        new($"/api/imports/{import.Id}", BookImportStatus.From(import));

    private static ObjectResult QueueFull() => new(new ProblemDetails
    {
        Status = StatusCodes.Status429TooManyRequests,
        Detail = "The import queue is full. Try again later."
    }) { StatusCode = StatusCodes.Status429TooManyRequests };
}

/// <summary>The catalogue entry for an edition that has already been imported.</summary>
public sealed record ExistingExternalEditionResponse(int BookId);

public sealed class BookImportSignal
{
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(1);

    public void Notify() => _wake.Writer.TryWrite(true);

    public async Task WaitAsync(CancellationToken cancellationToken) =>
        await _wake.Reader.ReadAsync(cancellationToken);
}

public sealed record BookImportStatus(Guid Id, string Status, DateTime CreatedAtUtc, int Total, int Succeeded, int Failed)
{
    public int? BookId { get; init; }
    public Guid? BatchId { get; init; }
    public int? ShelfId { get; init; }
    public IsbnImportFailure[] IsbnFailures { get; init; } = [];

    public static BookImportStatus From(BookImport import) => new(
        import.Id,
        import.CompletedAtUtc.HasValue ? (import.Failed == 0 ? "completed" : "completedWithErrors")
            : import.StartedAtUtc.HasValue ? "processing" : "queued",
        DateTime.SpecifyKind(import.CreatedAtUtc, DateTimeKind.Utc),
        import.Total, import.Succeeded, import.Failed)
    {
        BookId = import.ResultBookId,
        BatchId = import.BatchId,
        ShelfId = import.ShelfId,
        IsbnFailures = import.IsbnFailurePositions.Zip(import.IsbnFailureReasons,
            (position, reason) => new IsbnImportFailure(position, import.Isbns[position], reason)).ToArray()
    };
}

public sealed record IsbnImportFailure(int Position, string Isbn, string Reason);

using Bibliotheca.Backend.Authorization;
using Bibliotheca.Backend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Controllers;

[ApiController, Route("api/catalogue-refresh"), Authorize(Policy = CataloguePolicies.Manage)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CatalogueRefreshController(BibliothecaDbContext database) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var latest = await database.CatalogueRefreshes.AsNoTracking()
            .OrderByDescending(row => row.CreatedAtUtc).Select(row => new
            {
                row.Id, row.CreatedAtUtc, row.CompletedAtUtc,
                Current = row.ActiveSlot == 1 && row.LeaseOwner != null && row.LeaseUntilUtc > now
                    ? row.Items.Where(item => item.Status == "pending").OrderBy(item => item.Id)
                        .Select(item => new CatalogueRefreshCurrent(item.BookId, item.Title)).FirstOrDefault()
                    : null,
                Total = row.Items.Count,
                Updated = row.Items.Count(item => item.Status == "updated"),
                Unchanged = row.Items.Count(item => item.Status == "unchanged"),
                Failed = row.Items.Count(item => item.Status == "failed")
            }).FirstOrDefaultAsync(cancellationToken);
        var errors = latest is null ? [] : await database.CatalogueRefreshItems.AsNoTracking()
            .Where(item => item.CatalogueRefreshId == latest.Id && item.Status == "failed")
            .OrderBy(item => item.Id).Select(item => new RefreshError(item.BookId, item.Title, item.Error!))
            .ToArrayAsync(cancellationToken);
        var report = latest is null ? null : new CatalogueRefreshReport(latest.Id,
            new DateTimeOffset(DateTime.SpecifyKind(latest.CreatedAtUtc, DateTimeKind.Utc)),
            latest.CompletedAtUtc is { } completed ? new DateTimeOffset(DateTime.SpecifyKind(completed, DateTimeKind.Utc)) : null,
            latest.Total, latest.Updated, latest.Unchanged, latest.Failed, latest.Current);
        return Ok(new CatalogueRefreshOverview(await database.Books.CountAsync(cancellationToken), report, errors));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetBatch(Guid id, CancellationToken cancellationToken)
    {
        var batch = await database.CatalogueRefreshes.AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new { row.CompletedAtUtc, Items = row.Items.Select(item => new { item.Status, item.Error }).ToArray() })
            .SingleOrDefaultAsync(cancellationToken);
        return batch is null ? NotFound() : Ok(batch);
    }

    [HttpPost]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        return await StartCore(null, cancellationToken);
    }

    [HttpPost("books/{bookId:int}")]
    public Task<IActionResult> StartBook(int bookId, CancellationToken cancellationToken)
        => StartCore(bookId, cancellationToken);

    private async Task<IActionResult> StartCore(int? bookId, CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);
        if (await database.CatalogueRefreshes.AnyAsync(row => row.ActiveSlot == 1, cancellationToken))
            return Conflict(new ProblemDetails { Detail = "A catalogue refresh is already running." });
        if (await database.AuthorRefreshes.AnyAsync(row => row.ActiveSlot == 1, cancellationToken))
            return Conflict(new ProblemDetails { Detail = "An author refresh is already running." });
        var batch = new CatalogueRefresh { RequestedBy = User.GetUsername() };
        batch.Items = await database.Books.Where(book => !bookId.HasValue || book.BookId == bookId.Value).OrderBy(book => book.BookId)
            .Select(book => new CatalogueRefreshItem { BookId = book.BookId, Title = book.Title })
            .ToListAsync(cancellationToken);
        if (batch.Items.Count == 0 && bookId.HasValue) return NotFound();
        if (batch.Items.Count == 0)
        {
            batch.ActiveSlot = null;
            batch.CompletedAtUtc = DateTime.UtcNow;
        }
        database.CatalogueRefreshes.Add(batch);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Accepted("/api/catalogue-refresh", new CatalogueRefreshAccepted(batch.Id, batch.Items.Count));
    }
}

/// <summary>A catalogue entry that could not be refreshed.</summary>
public sealed record RefreshError(int BookId, string Title, string Error);

/// <summary>The latest catalogue refresh and the current shared catalogue size.</summary>
public sealed record CatalogueRefreshOverview(int BookCount, CatalogueRefreshReport? Latest, RefreshError[] Errors);
/// <summary>Persisted progress and outcomes for a catalogue refresh.</summary>
public sealed record CatalogueRefreshReport(Guid Id, DateTimeOffset CreatedAtUtc, DateTimeOffset? CompletedAtUtc,
    int Total, int Updated, int Unchanged, int Failed, CatalogueRefreshCurrent? Current);
/// <summary>The book currently held by the refresh worker lease.</summary>
public sealed record CatalogueRefreshCurrent(int BookId, string Title);
/// <summary>A newly queued catalogue refresh.</summary>
public sealed record CatalogueRefreshAccepted(Guid Id, int Total);

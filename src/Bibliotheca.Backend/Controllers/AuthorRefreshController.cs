using System.Data;
using Bibliotheca.Backend.Authorization;
using Bibliotheca.Backend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Controllers;

[ApiController, Route("api/author-refresh"), Authorize(Policy = CataloguePolicies.Manage)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthorRefreshController(BibliothecaDbContext database) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AuthorRefreshOverview>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var latest = await database.AuthorRefreshes.AsNoTracking().OrderByDescending(row => row.CreatedAtUtc)
            .Select(row => new
            {
                row.Id, row.CreatedAtUtc, row.CompletedAtUtc,
                Current = row.ActiveSlot == 1 && row.LeaseOwner != null && row.LeaseUntilUtc > now
                    ? row.Items.Where(item => item.Status == "pending").OrderBy(item => item.Id)
                        .Select(item => new AuthorRefreshCurrent(item.AuthorId, item.AuthorName)).FirstOrDefault()
                    : null,
                Total = row.Items.Count,
                Updated = row.Items.Count(item => item.Status == "updated"),
                Unchanged = row.Items.Count(item => item.Status == "unchanged"),
                Failed = row.Items.Count(item => item.Status == "failed"),
                Errors = row.Items.Where(item => item.Status == "failed").OrderBy(item => item.Id)
                    .Select(item => new AuthorRefreshError(item.AuthorId, item.AuthorName, item.Error!)).ToArray()
            }).FirstOrDefaultAsync(cancellationToken);
        var catalogueRefreshActive = await database.CatalogueRefreshes.AnyAsync(row => row.ActiveSlot == 1, cancellationToken);
        var authorCount = await database.Authors.CountAsync(cancellationToken);

        var report = latest is null ? null : new AuthorRefreshReport(latest.Id, ToUtcOffset(latest.CreatedAtUtc),
            latest.CompletedAtUtc is { } completed ? ToUtcOffset(completed) : null,
            latest.Total, latest.Updated, latest.Unchanged, latest.Failed, latest.Current);
        return Ok(new AuthorRefreshOverview(authorCount, report, latest?.Errors ?? [], catalogueRefreshActive));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetBatch(Guid id, CancellationToken cancellationToken)
    {
        var batch = await database.AuthorRefreshes.AsNoTracking().Where(row => row.Id == id)
            .Select(row => new { row.CompletedAtUtc, Items = row.Items.Select(item => new { item.Status, item.Error }).ToArray() })
            .SingleOrDefaultAsync(cancellationToken);
        return batch is null ? NotFound() : Ok(batch);
    }

    [HttpPost("authors/{authorId:int}")]
    public Task<IActionResult> StartAuthor(int authorId, CancellationToken cancellationToken)
        => StartCore(authorId, cancellationToken);

    [HttpPost]
    [ProducesResponseType<AuthorRefreshAccepted>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        return await StartCore(null, cancellationToken);
    }

    private async Task<IActionResult> StartCore(int? authorId, CancellationToken cancellationToken)
    {
        // SQLite obtains the write lock for this transaction before either active slot is read.
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await database.AuthorRefreshes.AnyAsync(row => row.ActiveSlot == 1, cancellationToken))
            return Conflict(new ProblemDetails { Detail = "An author refresh is already running." });
        if (await database.CatalogueRefreshes.AnyAsync(row => row.ActiveSlot == 1, cancellationToken))
            return Conflict(new ProblemDetails { Detail = "A catalogue refresh is already running." });

        var batch = new AuthorRefresh { RequestedBy = User.GetUsername() };
        batch.Items = await database.Authors.Where(author => !authorId.HasValue || author.AuthorId == authorId.Value).OrderBy(author => author.AuthorId)
            .Select(author => new AuthorRefreshItem { AuthorId = author.AuthorId, AuthorName = author.AuthorName })
            .ToListAsync(cancellationToken);
        if (batch.Items.Count == 0 && authorId.HasValue) return NotFound();
        if (batch.Items.Count == 0)
        {
            batch.ActiveSlot = null;
            batch.CompletedAtUtc = DateTime.UtcNow;
        }
        database.AuthorRefreshes.Add(batch);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Accepted("/api/author-refresh", new AuthorRefreshAccepted(batch.Id, batch.Items.Count));
    }

    private static DateTimeOffset ToUtcOffset(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

/// <summary>An author whose profile could not be refreshed.</summary>
public sealed record AuthorRefreshError(int AuthorId, string AuthorName, string Error);

/// <summary>The latest author refresh and current author catalogue size.</summary>
public sealed record AuthorRefreshOverview(int AuthorCount, AuthorRefreshReport? Latest, AuthorRefreshError[] Errors, bool CatalogueRefreshActive);

/// <summary>Persisted progress and outcomes for a single author refresh.</summary>
public sealed record AuthorRefreshReport(Guid Id, DateTimeOffset CreatedAtUtc, DateTimeOffset? CompletedAtUtc,
    int Total, int Updated, int Unchanged, int Failed, AuthorRefreshCurrent? Current);

/// <summary>The author currently held by the refresh worker lease.</summary>
public sealed record AuthorRefreshCurrent(int AuthorId, string AuthorName);

/// <summary>A newly queued author refresh.</summary>
public sealed record AuthorRefreshAccepted(Guid Id, int Total);

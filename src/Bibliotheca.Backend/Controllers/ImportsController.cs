using Bibliotheca.Backend.Authorization;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Services;
using Bibliotheca.Backend.Jobs;
using Bibliotheca.Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Controllers;

[ApiController]
[Route("api/imports")]
[Authorize(Policy = CataloguePolicies.Import)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ImportsController : ControllerBase
{
    [HttpGet]
    public async Task<ImportBatchSummary[]> ListAsync(
        [FromServices] BibliothecaDbContext database, CancellationToken cancellationToken)
    {
        var owner = User.GetUsername();
        var batches = await database.BookImports.Where(import => import.Owner == owner)
            .GroupBy(import => import.BatchId ?? import.Id)
            .Select(group => new
            {
                Id = group.Key, CreatedAt = group.Max(import => import.CreatedAtUtc),
                ShelfId = group.Max(import => import.ShelfId), Total = group.Sum(import => import.Total),
                Succeeded = group.Sum(import => import.Succeeded), Failed = group.Sum(import => import.Failed),
                Started = group.Any(import => import.StartedAtUtc != null || import.CompletedAtUtc != null),
                IsbnFailureCount = group.Sum(import => import.IsbnFailurePositions.Length)
            })
            .OrderByDescending(batch => batch.CreatedAt).ThenBy(batch => batch.Id)
            .Take(20).ToArrayAsync(cancellationToken);
        return batches.Select(batch =>
        {
            var pending = batch.Total - batch.Succeeded - batch.Failed;
            var status = pending > 0 ? (batch.Started ? "processing" : "queued") :
                (batch.Failed > 0 ? "completedWithErrors" : "completed");
            return new ImportBatchSummary(batch.Id, DateTime.SpecifyKind(batch.CreatedAt, DateTimeKind.Utc),
                batch.ShelfId, batch.Total, batch.Succeeded, pending, batch.Failed, batch.IsbnFailureCount, status);
        }).ToArray();
    }

    [HttpGet("{id:guid}/failures")]
    public async Task<ActionResult<PaginatedResult<BatchIsbnFailure>>> FailuresAsync(Guid id, int? page, int? size,
        [FromServices] BibliothecaDbContext database, CancellationToken cancellationToken)
    {
        if (BookPage.From(page, size) is not { } pagination)
            return BadRequest("Page must be non-negative and size must be between 1 and 100.");

        var owner = User.GetUsername();
        var total = await database.BookImports.Where(import => import.Owner == owner && (import.BatchId ?? import.Id) == id)
            .GroupBy(import => import.Owner).Select(group => (int?)group.Sum(import => import.IsbnFailurePositions.Length))
            .SingleOrDefaultAsync(cancellationToken);
        if (total is null)
            return NotFound();

        // Positions index the original ISBN array. Reasons use the ordinal within the failure array.
        var items = await database.Database.SqlQuery<BatchIsbnFailure>($"""
            SELECT i.Id AS ImportId, f.value AS Position,
                   json_extract(i.Isbns, '$[' || f.value || ']') AS Isbn,
                   json_extract(i.IsbnFailureReasons, '$[' || f.key || ']') AS Reason
            FROM BookImports AS i, json_each(i.IsbnFailurePositions) AS f
            WHERE i.Owner = {owner} AND coalesce(i.BatchId, i.Id) = {id}
            ORDER BY i.CreatedAtUtc, i.Id, f.key
            LIMIT {pagination.Size} OFFSET {pagination.Offset}
            """).ToArrayAsync(cancellationToken);
        return Ok(new PaginatedResult<BatchIsbnFailure> { Page = pagination.Index, Total = total.Value, Items = items });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookImportStatus>> GetAsync(
        Guid id, [FromServices] BibliothecaDbContext database, CancellationToken cancellationToken)
    {
        var owner = User.GetUsername();
        var import = await database.BookImports.AsNoTracking().FirstOrDefaultAsync(
            import => import.Id == id && import.Owner == owner, cancellationToken);
        return import is null ? NotFound() : Ok(BookImportStatus.From(import));
    }

    /// <summary>Queues failed ISBNs from an import or a scan batch on its original shelf.</summary>
    [HttpPost("{id:guid}/retry")]
    [ProducesResponseType<BookImportStatus>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RetryAsync(
        Guid id, RetryImportRequest request, [FromServices] BibliothecaDbContext database,
        [FromServices] BookImportQueue queue, CancellationToken cancellationToken)
    {
        var owner = User.GetUsername();
        var owned = database.BookImports.Where(import => import.Owner == owner && (import.BatchId ?? import.Id) == id);
        var batch = await owned.OrderBy(import => import.CreatedAtUtc).ThenBy(import => import.Id)
            .Select(import => new { import.ShelfId }).FirstOrDefaultAsync(cancellationToken);
        if (batch == null)
            return NotFound();

        var failedImports = owned.Where(import => import.IsbnFailurePositions.Length > 0);
        if (request.Items != null)
        {
            if (request.Items.Any(item => item == null))
                return BadRequest("Select a failed item and enter a valid ISBN.");
            var ids = request.Items.Select(item => item.ImportId).Distinct().ToArray();
            failedImports = failedImports.Where(import => ids.Contains(import.Id));
        }
        var imports = await failedImports.OrderBy(import => import.CreatedAtUtc).ThenBy(import => import.Id)
            .Select(import => new { import.Id, import.IsbnFailurePositions, import.Isbns })
            .ToArrayAsync(cancellationToken);

        var failures = imports.SelectMany(import => import.IsbnFailurePositions
            .Select(position => KeyValuePair.Create((import.Id, position), import.Isbns[position])))
            .ToDictionary();
        if (request.Items is { } items)
        {
            var corrected = new List<string>();
            foreach (var item in items)
            {
                if (!failures.ContainsKey((item.ImportId, item.Position)) ||
                    EditionIsbn.Normalize(item.Isbn) is not { } isbn)
                    return Problem(statusCode: StatusCodes.Status400BadRequest,
                        detail: "Select a failed item and enter a valid ISBN.");
                corrected.Add(isbn);
            }

            return await queue.SubmitAsync(owner, corrected.Distinct().ToArray(), cancellationToken, batch.ShelfId);
        }

        return await queue.SubmitAsync(owner, failures.Values.Distinct().ToArray(), cancellationToken, batch.ShelfId);
    }
}

/// <summary>Identifies a failed ISBN within an import and supplies its correction.</summary>
public sealed record IsbnCorrection(Guid ImportId, int Position, string Isbn);

/// <summary>Retry all ISBN failures, or only the specified ones with corrections.</summary>
public sealed record RetryImportRequest(IsbnCorrection[]? Items);

public sealed record ImportBatchSummary(Guid Id, DateTime CreatedAtUtc, int? ShelfId, int Total, int Succeeded,
    int Pending, int Failed, int IsbnFailureCount, string Status);

public sealed record BatchIsbnFailure(Guid ImportId, int Position, string Isbn, string Reason);

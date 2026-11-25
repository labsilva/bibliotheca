using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Bibliotheca.Backend.Authorization;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Jobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Controllers;

[ApiController, Route("api/author-duplicates"), Authorize(Policy = CataloguePolicies.Manage)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthorDuplicatesController(BibliothecaDbContext database, ILogger<AuthorDuplicatesController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AuthorDuplicateOverview>> Get(
        CancellationToken cancellationToken, bool deferred = false, int after = 0)
    {
        var latest = await database.AuthorDuplicateScans.AsNoTracking().OrderByDescending(row => row.CreatedAtUtc)
            .Select(row => new AuthorDuplicateProgress(row.Id, row.ActiveSlot == 1, row.Processed, row.Total, row.NewSuggestions))
            .FirstOrDefaultAsync(cancellationToken);

        var candidates = database.AuthorDuplicateSuggestions.AsNoTracking().Where(pair =>
            database.Authors.Any(author => author.AuthorId == pair.FirstAuthorId) &&
            database.Authors.Any(author => author.AuthorId == pair.SecondAuthorId));
        var candidate = await candidates.Where(pair => pair.Id > after &&
                pair.Status == (deferred ? AuthorDuplicateSuggestion.Deferred : AuthorDuplicateSuggestion.Pending))
            .OrderBy(pair => pair.Id).FirstOrDefaultAsync(cancellationToken);
        var pendingCount = await candidates.CountAsync(pair => pair.Status == AuthorDuplicateSuggestion.Pending, cancellationToken);
        var deferredCount = await candidates.CountAsync(pair => pair.Status == AuthorDuplicateSuggestion.Deferred, cancellationToken);

        AuthorDuplicateComparison? comparison = null;
        if (candidate is not null)
        {
            var authors = await ReadProfiles(candidate, cancellationToken);
            if (authors.Length == 2)
            {
                comparison = new AuthorDuplicateComparison(candidate.Id, Version(candidate, authors),
                    AuthorDuplicateScanWorker.Normalize(authors[0].Name), authors[0], authors[1],
                    authors[0].Books.Select(book => book.Id).Intersect(authors[1].Books.Select(book => book.Id)).Count());
            }
        }
        return Ok(new AuthorDuplicateOverview(latest, pendingCount, deferredCount, comparison));
    }

    [HttpPost("scan")]
    [ProducesResponseType<AuthorDuplicateProgress>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        if (await ScanActive(cancellationToken))
            return Conflict(new ProblemDetails { Detail = "A duplicate author scan is already running." });

        var scan = new AuthorDuplicateScan
        {
            RequestedBy = User.GetUsername(),
            Total = await database.Authors.CountAsync(cancellationToken),
            MaxAuthorId = await database.Authors.MaxAsync(author => (int?)author.AuthorId, cancellationToken) ?? 0
        };
        if (scan.Total == 0)
        {
            scan.ActiveSlot = null;
            scan.CompletedAtUtc = DateTime.UtcNow;
        }
        database.AuthorDuplicateScans.Add(scan);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Accepted("/api/author-duplicates", new AuthorDuplicateProgress(scan.Id, scan.ActiveSlot == 1, 0, scan.Total, 0));
    }

    [HttpPost("{id:int}/review")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Review(int id, ReviewAuthorDuplicateRequest request, CancellationToken cancellationToken)
    {
        if (request.Decision is not ("merge" or "reject" or "defer") ||
            (request.Decision == "merge") != request.KeepAuthorId.HasValue)
            return BadRequest(new ProblemDetails { Detail = "Select a valid review decision and choose an author when merging." });

        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var candidate = await database.AuthorDuplicateSuggestions.AsTracking().SingleOrDefaultAsync(pair => pair.Id == id, cancellationToken);
        if (candidate is null)
            return NotFound();
        if (candidate.Status is not (AuthorDuplicateSuggestion.Pending or AuthorDuplicateSuggestion.Deferred))
            return Conflict(new ProblemDetails { Detail = "This suggestion has already been resolved." });
        if (request.KeepAuthorId is { } chosen && chosen != candidate.FirstAuthorId && chosen != candidate.SecondAuthorId)
            return BadRequest(new ProblemDetails { Detail = "Choose one of the two authors in this suggestion." });

        var profiles = await ReadProfiles(candidate, cancellationToken);
        if (profiles.Length != 2 || request.Version != Version(candidate, profiles) ||
            AuthorDuplicateScanWorker.Normalize(profiles[0].Name) != AuthorDuplicateScanWorker.Normalize(profiles[1].Name))
            return Conflict(new ProblemDetails { Detail = "The comparison changed. Reload it before reviewing." });

        if (request.KeepAuthorId is { } keepId)
        {
            if (await ScanActive(cancellationToken) ||
                await database.AuthorRefreshes.AnyAsync(row => row.ActiveSlot == 1, cancellationToken) ||
                await database.CatalogueRefreshes.AnyAsync(row => row.ActiveSlot == 1, cancellationToken))
                return Conflict(new ProblemDetails { Detail = "Wait for catalogue maintenance to finish before merging authors." });

            await Merge(candidate, keepId, cancellationToken);
        }

        candidate.Status = request.Decision switch
        {
            "merge" => AuthorDuplicateSuggestion.Merged,
            "reject" => AuthorDuplicateSuggestion.Rejected,
            _ => AuthorDuplicateSuggestion.Deferred
        };
        candidate.ReviewedAtUtc = DateTime.UtcNow;
        candidate.ReviewedBy = User.GetUsername();
        candidate.KeptAuthorId = request.KeepAuthorId;
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Author duplicate suggestion {SuggestionId} resolved as {Decision}, retained author ID {AuthorId}",
            candidate.Id, candidate.Status, candidate.KeptAuthorId);
        return NoContent();
    }

    private Task<bool> ScanActive(CancellationToken cancellationToken) =>
        database.AuthorDuplicateScans.AnyAsync(row => row.ActiveSlot == 1, cancellationToken);

    private Task<AuthorDuplicateProfile[]> ReadProfiles(AuthorDuplicateSuggestion pair, CancellationToken cancellationToken) =>
        database.Authors.AsNoTracking().Where(author => author.AuthorId == pair.FirstAuthorId || author.AuthorId == pair.SecondAuthorId)
            .OrderBy(author => author.AuthorId)
            .Select(author => new AuthorDuplicateProfile(author.AuthorId, author.AuthorName, author.PhotoUrl,
                author.Biography, author.BiographySourceUrl, author.SourceAuthorKey,
                author.Books.OrderBy(book => book.BookId)
                    .Select(book => new AuthorDuplicateBook(book.BookId, book.Title, book.Isbn)).ToArray()))
            .ToArrayAsync(cancellationToken);

    private static string Version(AuthorDuplicateSuggestion candidate, AuthorDuplicateProfile[] profiles) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            candidate.Status, ReviewedAtTicks = candidate.ReviewedAtUtc?.Ticks, Profiles = profiles
        })));

    private async Task Merge(AuthorDuplicateSuggestion candidate, int keepId, CancellationToken cancellationToken)
    {
        var removeId = keepId == candidate.FirstAuthorId ? candidate.SecondAuthorId : candidate.FirstAuthorId;
        var authors = await database.Authors.AsTracking().Include(author => author.Books)
            .Where(author => author.AuthorId == keepId || author.AuthorId == removeId).ToArrayAsync(cancellationToken);
        var keep = authors.Single(author => author.AuthorId == keepId);
        var remove = authors.Single(author => author.AuthorId == removeId);

        var keepHasProfile = !string.IsNullOrWhiteSpace(keep.PhotoUrl) || !string.IsNullOrWhiteSpace(keep.Biography) ||
            !string.IsNullOrWhiteSpace(keep.BiographySourceUrl);
        var conflictingIdentity = !string.IsNullOrWhiteSpace(keep.SourceAuthorKey) &&
            !string.IsNullOrWhiteSpace(remove.SourceAuthorKey) && keep.SourceAuthorKey != remove.SourceAuthorKey;
        if (!conflictingIdentity)
        {
            // Treat the biography and its citation as one value.
            if (string.IsNullOrWhiteSpace(keep.Biography) && !string.IsNullOrWhiteSpace(remove.Biography))
            {
                keep.Biography = remove.Biography;
                keep.BiographySourceUrl = remove.BiographySourceUrl;
            }
            else if (keep.Biography == remove.Biography && !string.IsNullOrWhiteSpace(keep.Biography) &&
                     string.IsNullOrWhiteSpace(keep.BiographySourceUrl))
            {
                keep.BiographySourceUrl = remove.BiographySourceUrl;
            }
            if (string.IsNullOrWhiteSpace(keep.PhotoUrl))
                keep.PhotoUrl = remove.PhotoUrl;
            if (!keepHasProfile && string.IsNullOrWhiteSpace(keep.SourceAuthorKey))
                keep.SourceAuthorKey = remove.SourceAuthorKey;
        }

        var keptBookIds = keep.Books.Select(book => book.BookId).ToHashSet();
        foreach (var book in remove.Books)
        {
            if (keptBookIds.Add(book.BookId))
                keep.Books.Add(book);
        }

        var related = await database.AuthorDuplicateSuggestions.AsTracking().Where(pair => pair.Id != candidate.Id &&
                (pair.FirstAuthorId == removeId || pair.SecondAuthorId == removeId ||
                 pair.FirstAuthorId == keepId || pair.SecondAuthorId == keepId))
            .ToArrayAsync(cancellationToken);
        var keptPairs = related.Where(pair => pair.FirstAuthorId == keepId || pair.SecondAuthorId == keepId)
            .ToDictionary(pair => pair.FirstAuthorId == keepId ? pair.SecondAuthorId : pair.FirstAuthorId);
        foreach (var pair in related.Where(pair => pair.FirstAuthorId == removeId || pair.SecondAuthorId == removeId))
        {
            if (pair.Status is AuthorDuplicateSuggestion.Pending or AuthorDuplicateSuggestion.Deferred)
                pair.Status = AuthorDuplicateSuggestion.Superseded;

            // Keep the original rejection and inherit it when one author acquires a new canonical ID.
            if (pair.Status != AuthorDuplicateSuggestion.Rejected)
                continue;
            var otherId = pair.FirstAuthorId == removeId ? pair.SecondAuthorId : pair.FirstAuthorId;
            if (!await database.Authors.AnyAsync(author => author.AuthorId == otherId, cancellationToken))
                continue;

            if (!keptPairs.TryGetValue(otherId, out var inherited))
            {
                inherited = new AuthorDuplicateSuggestion
                {
                    FirstAuthorId = Math.Min(otherId, keepId),
                    SecondAuthorId = Math.Max(otherId, keepId)
                };
                database.AuthorDuplicateSuggestions.Add(inherited);
            }
            if (inherited.Status is AuthorDuplicateSuggestion.Pending or AuthorDuplicateSuggestion.Deferred)
            {
                inherited.Status = AuthorDuplicateSuggestion.Rejected;
                inherited.ReviewedBy = pair.ReviewedBy;
                inherited.ReviewedAtUtc = pair.ReviewedAtUtc;
            }
        }

        database.Authors.Remove(remove);
    }
}

/// <summary>The latest scan and current review queue.</summary>
public sealed record AuthorDuplicateOverview(AuthorDuplicateProgress? Latest, int PendingCount,
    int DeferredCount, AuthorDuplicateComparison? Comparison);

/// <summary>Durable progress and number of newly identified pairs.</summary>
public sealed record AuthorDuplicateProgress(Guid Id, bool Active, int Processed, int Total, int NewSuggestions);

/// <summary>Two matching profiles with the normalized name and shared book count.</summary>
public sealed record AuthorDuplicateComparison(int Id, string Version, string NormalizedName, AuthorDuplicateProfile First,
    AuthorDuplicateProfile Second, int SharedBookCount);

/// <summary>An author and their linked catalogue editions.</summary>
public sealed record AuthorDuplicateProfile(int Id, string Name, string? PhotoUrl, string? Biography,
    string? BiographySourceUrl, string? SourceAuthorKey, AuthorDuplicateBook[] Books);

/// <summary>A book credit included in the comparison.</summary>
public sealed record AuthorDuplicateBook(int Id, string Title, string Isbn);

/// <summary>A decision based on a particular version of a comparison.</summary>
public sealed record ReviewAuthorDuplicateRequest(string Decision, string Version, int? KeepAuthorId);

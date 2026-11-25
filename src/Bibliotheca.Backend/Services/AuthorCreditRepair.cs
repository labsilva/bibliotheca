using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Fetchers;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Services;

internal static class AuthorCreditRepair
{
    private static readonly string[] BylineSeparators = [", ", " e ", " and ", " & ", "; "];

    internal static async Task RepairCatalogueAsync(BibliothecaDbContext database, ILogger logger,
        CancellationToken cancellationToken)
    {
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var candidates = database.Books.Where(book => book.Authors!.Any(author => author.AuthorName.Contains(",") ||
                author.AuthorName.Contains(";") || author.AuthorName.Contains(":") ||
                author.AuthorName.Contains(" e ") || author.AuthorName.Contains(" and ") ||
                author.AuthorName.Contains(" & ")));
        var books = await candidates.AsTracking().AsSplitQuery().Include(book => book.Authors)
            .Include(book => book.ExternalLinks).ToArrayAsync(cancellationToken);
        if (books.Length == 0)
            return;

        var editions = await database.BookImports.AsNoTracking()
            .Where(import => import.CompletedAtUtc != null && import.Succeeded > 0 && import.SelectedEdition != null &&
                candidates.Any(book => book.BookId == import.ResultBookId))
            .Select(import => new { import.ResultBookId, import.SelectedEdition }).ToArrayAsync(cancellationToken);
        var editionsByBook = editions.ToLookup(entry => entry.ResultBookId, entry => entry.SelectedEdition!);

        var repaired = 0;
        foreach (var book in books)
        {
            var bookIsbn = EditionIsbn.Normalize(book.Isbn);
            var evidence = editionsByBook[book.BookId]
                .Where(edition => bookIsbn is null || EditionIsbn.Normalize(edition.Isbn) is not { } editionIsbn ||
                    EditionIsbn.CatalogueVariants(editionIsbn).Contains(bookIsbn))
                .Select(edition => edition.Details)
                .Where(details => details is { HasStructuredAuthors: true, Authors.Length: > 0 })
                .Select(details => details!.Authors).ToArray();
            var fromWook = book.ExternalLinks.Any(link => link.Notes == "Metadata fetched from" &&
                WookCatalogue.IsWookAddress(link.Url));

            repaired += await RepairBookAsync(database, book, evidence, fromWook, logger, cancellationToken);
        }

        if (repaired > 0)
            await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        if (repaired > 0)
            logger.LogInformation("Repaired {AuthorLinkCount} malformed catalogue author links.", repaired);
    }

    internal static async Task<int> RepairBookAsync(BibliothecaDbContext database, Book book,
        IReadOnlyList<string[]> evidence, bool fromWook, ILogger? logger, CancellationToken cancellationToken)
    {
        var repaired = 0;
        var authors = book.Authors ??= new List<Author>();
        foreach (var legacy in authors.ToArray())
        {
            if (!BylineSeparators.Any(legacy.AuthorName.Contains) && !legacy.AuthorName.Contains(':'))
                continue;

            var annotation = fromWook ? WookCatalogue.IllustrationCredit().Match(legacy.AuthorName) : null;
            var byline = annotation is { Success: true }
                ? legacy.AuthorName[..annotation.Index].Trim()
                : legacy.AuthorName;
            var replacements = FindSplit(byline, evidence);
            if (annotation is { Success: true } && replacements is null)
                replacements = byline.Length > 0 ? [byline] : [];

            if (replacements is null || (replacements.Length == 0 && authors.Count == 1))
            {
                // An exact structured credit can legitimately contain punctuation or a conjunction.
                if (!evidence.Any(names => names.Contains(legacy.AuthorName, StringComparer.Ordinal)))
                    logger?.LogWarning("Author credit for book {BookId}, author {AuthorId} needs review.",
                        book.BookId, legacy.AuthorId);
                continue;
            }

            foreach (var name in replacements)
            {
                var author = await FindOrCreateAuthorAsync(database, book, name, cancellationToken);
                if (!authors.Contains(author))
                    authors.Add(author);
            }
            // Keep the old row and its profile. A merged credit does not identify a photo's owner.
            authors.Remove(legacy);
            repaired++;
        }

        return repaired;
    }

    private static string[]? FindSplit(string byline, IReadOnlyList<string[]> evidence)
    {
        string[]? result = null;
        foreach (var names in evidence)
        {
            if (names.Contains(byline, StringComparer.Ordinal))
                return null;

            // The old Wook parser split on " e " before saving, so a comma-joined prefix
            // can be a contiguous part of the source byline even when the book has more authors.
            for (var start = 0; start < names.Length - 1; start++)
            {
                for (var end = start + 2; end <= names.Length; end++)
                {
                    var group = names[start..end];
                    if (group.Any(string.IsNullOrWhiteSpace) || group.Distinct(StringComparer.Ordinal).Count() != group.Length ||
                        !BylineSeparators.Any(separator => string.Join(separator, group) == byline))
                        continue;
                    if (result is not null && !result.SequenceEqual(group, StringComparer.Ordinal))
                        return null;
                    result = group;
                }
            }
        }
        return result;
    }

    internal static async Task<Author> FindOrCreateAuthorAsync(BibliothecaDbContext database, Book book,
        string name, CancellationToken cancellationToken)
    {
        var author = book.Authors?.FirstOrDefault(author => author.AuthorName == name)
            ?? database.Authors.Local.FirstOrDefault(author => author.AuthorName == name)
            ?? await database.Authors.AsTracking().OrderBy(author => author.AuthorId)
                .FirstOrDefaultAsync(author => author.AuthorName == name, cancellationToken);
        if (author is null)
            database.Authors.Add(author = new Author { AuthorName = name });
        return author;
    }
}

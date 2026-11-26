using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Extractors;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace Bibliotheca.Backend.Jobs;

internal sealed class AuthorRefreshProfiles(ILogger<AuthorRefreshWorker> logger)
{
    public async Task<AuthorRefreshResolution> ResolveAsync(BibliothecaDbContext database, IServiceProvider services,
        Author? author, CancellationToken cancellationToken)
    {
        AuthorProfile? profile = null;
        WookAuthorResult? wook = null;
        BertrandAuthorResult? bertrand = null;
        AuthorBiographyResult? goodreads = null;
        string? error = author is null ? "The author was deleted." : null;
        if (author is not null)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(90));
            Exception? openFailure = null;
            try
            {
                var lookup = services.GetRequiredService<IAuthorProfileLookup>();
                try { profile = await FindProfileAsync(database, lookup, author, timeout.Token); }
                catch (Exception exception) when (!timeout.IsCancellationRequested)
                {
                    openFailure = exception;
                    logger.LogWarning("Open Library author refresh failed for {AuthorId}: {FailureType}", author.AuthorId, exception.GetType().Name);
                }
                var biographyExtractors = services.GetServices<IAuthorBiographyLookup>()
                    .ToDictionary(extractor => extractor.Id, StringComparer.Ordinal);
                biographyExtractors.TryGetValue("wook", out var wookExtractor);
                var wookLookup = services.GetService<IWookAuthorLookup>();
                if (wookLookup is not null || wookExtractor is not null)
                {
                    var editions = await database.Books.AsNoTracking().Where(book => book.Authors!.Any(a => a.AuthorId == author.AuthorId))
                        .OrderBy(book => book.BookId).Select(book => new WookAuthorEdition(book.Isbn,
                            database.BookEditionReferences.Where(r => r.BookId == book.BookId && r.ProviderId == "wook")
                                .OrderBy(r => r.ExternalId).Select(r => r.ExternalId).FirstOrDefault())).ToArrayAsync(timeout.Token);
                    var prioritized = editions.OrderByDescending(e => e.WookId is not null)
                        .ThenByDescending(e => EditionIsbn.Normalize(e.Isbn) is not null).Take(3).ToArray();
                    var biography = await FindBiographyAsync(wookExtractor, author,
                        prioritized.Select(edition => new AuthorBiographyEdition(edition.Isbn, edition.WookId)).ToArray(),
                        timeout.Token);
                    try
                    {
                        if (wookLookup is not null)
                            wook = await wookLookup.FindAsync(author.AuthorName, prioritized, timeout.Token);
                    }
                    catch (Exception exception) when (!timeout.IsCancellationRequested)
                    {
                        logger.LogWarning("Wook author refresh failed for {AuthorId}: {FailureType}", author.AuthorId, exception.GetType().Name);
                        if (profile is null && biography is null) error = "Wook author lookup failed.";
                    }
                    if (biography is { Ambiguous: true })
                        wook = new(null, null, null, true);
                    else if (biography?.Biography is { } wookBiography)
                        wook = new(wook?.PhotoUrl, wookBiography, biography.SourceUrl);
                }
                biographyExtractors.TryGetValue("bertrand", out var bertrandExtractor);
                biographyExtractors.TryGetValue("goodreads", out var goodreadsExtractor);
                var bertrandLookup = services.GetService<BertrandAuthorLookup>();
                if (bertrandLookup is not null || bertrandExtractor is not null || goodreadsExtractor is not null)
                {
                    var isbns = await database.Books.AsNoTracking()
                        .Where(book => book.Isbn != null && book.Authors!.Any(a => a.AuthorId == author.AuthorId))
                        .GroupBy(book => book.Isbn).OrderBy(group => group.Min(book => book.BookId))
                        .Select(group => group.Key).Take(3).ToArrayAsync(timeout.Token);
                    var editions = isbns.Select(isbn => new AuthorBiographyEdition(isbn)).ToArray();
                    var biography = await FindBiographyAsync(bertrandExtractor, author, editions, timeout.Token);
                    try
                    {
                        if (bertrandLookup is not null)
                            bertrand = await bertrandLookup.FindAsync(author.AuthorName, isbns, timeout.Token);
                    }
                    catch (Exception exception) when (!timeout.IsCancellationRequested)
                    {
                        logger.LogWarning("Bertrand author refresh failed for {AuthorId}: {FailureType}", author.AuthorId, exception.GetType().Name);
                    }
                    if (biography is { Ambiguous: true } && bertrand is not null)
                        bertrand = bertrand with { Biography = null };
                    else if (biography?.Biography is { } bertrandBiography)
                        bertrand = new(bertrandBiography, bertrand?.PhotoUrl, biography.SourceUrl!);

                    if (string.IsNullOrWhiteSpace(author.Biography) &&
                        string.IsNullOrWhiteSpace(profile?.Biography) && string.IsNullOrWhiteSpace(wook?.Biography) &&
                        string.IsNullOrWhiteSpace(bertrand?.Biography))
                        goodreads = await FindBiographyAsync(goodreadsExtractor, author, editions, timeout.Token);

                    if ((bertrand is not null || goodreads?.Biography is not null) && error == "Wook author lookup failed.")
                        error = null;
                }
                if (openFailure is not null && wook is null && bertrand is null && profile is null && goodreads?.Biography is null)
                    error = "Author profile providers unavailable.";
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Could not refresh author {AuthorId}", author.AuthorId);
                error = exception is OperationCanceledException ? "Provider request timed out." : "Could not refresh this author.";
            }
        }
        return new(profile, wook, bertrand, goodreads, error);
    }

    private async Task<AuthorBiographyResult?> FindBiographyAsync(IAuthorBiographyLookup? extractor, Author author,
        IReadOnlyList<AuthorBiographyEdition> editions, CancellationToken token)
    {
        if (extractor is null) return null;
        try
        {
            var result = await extractor.FindAsync(author.AuthorName, editions, token);
            logger.LogInformation("Author biography extractor {ExtractorId} for {AuthorId}: {Outcome}",
                extractor.Id, author.AuthorId, result is null ? "no match" : result.Ambiguous ? "ambiguous" : "matched");
            return result;
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            logger.LogWarning("Author biography extractor {ExtractorId} failed for {AuthorId}: {FailureType}",
                extractor.Id, author.AuthorId, exception.GetType().Name);
            return null;
        }
    }

    private async Task<AuthorProfile?> FindProfileAsync(BibliothecaDbContext database, IAuthorProfileLookup lookup,
        Author author, CancellationToken cancellationToken)
    {
        if (author.SourceAuthorKey is { } key)
        {
            var knownProfile = await lookup.FindProfileByKeyAsync(key, author.AuthorName, cancellationToken).WaitAsync(cancellationToken);
            return knownProfile?.SourceAuthorKey == key ? knownProfile : null;
        }

        // A legacy profile needs an edition actually linked to this author. Never search by the name alone.
        var books = await database.Books.AsNoTracking().Where(book => book.Authors!.Any(person => person.AuthorId == author.AuthorId))
            .OrderBy(book => book.BookId).Select(book => new
            {
                book.Isbn, book.Title, book.Subtitle,
                OpenLibraryId = database.BookEditionReferences.Where(reference => reference.BookId == book.BookId &&
                    reference.ProviderId == OpenLibraryEditionProvider.ProviderId).OrderBy(reference => reference.ExternalId)
                    .Select(reference => reference.ExternalId).FirstOrDefault()
            }).ToArrayAsync(cancellationToken);
        var editions = books.Select(book => new
            {
                Isbn = EditionIsbn.Normalize(book.Isbn), book.Title, book.Subtitle,
                Url = book.OpenLibraryId is { } id ? $"https://openlibrary.org/books/{Uri.EscapeDataString(id)}" : null
            }).Where(book => book.Url is not null || book.Isbn is not null)
            .OrderByDescending(book => book.Url is not null).DistinctBy(book => book.Url ?? book.Isbn).Take(3);

        AuthorProfile? found = null;
        Exception? failure = null;
        foreach (var edition in editions)
        {
            try
            {
                var context = new FetchedBook { Isbn = edition.Isbn ?? "", Title = edition.Title, Subtitle = edition.Subtitle,
                    Url = edition.Url ?? "", Authors = [author.AuthorName] };
                var profiles = await lookup.FindProfilesAsync(context, context.Authors, cancellationToken).WaitAsync(cancellationToken);
                if (!profiles.TryGetValue(author.AuthorName, out var candidate) || string.IsNullOrWhiteSpace(candidate.SourceAuthorKey))
                    continue;
                if (found is not null && found.SourceAuthorKey != candidate.SourceAuthorKey)
                    return null;
                if (found is null)
                {
                    found = candidate;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(found.PhotoUrl))
                    found = found with { PhotoUrl = candidate.PhotoUrl };
                if (string.IsNullOrWhiteSpace(found.Biography) || string.IsNullOrWhiteSpace(found.BiographySourceUrl))
                    found = found with { Biography = candidate.Biography, BiographySourceUrl = candidate.BiographySourceUrl };
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                failure = exception;
            }
        }

        if (found is null && failure is not null)
            throw new InvalidOperationException("Author profile lookup failed.", failure);
        return found;
    }
}

internal sealed record AuthorRefreshResolution(AuthorProfile? Profile, WookAuthorResult? Wook,
    BertrandAuthorResult? Bertrand, AuthorBiographyResult? Goodreads, string? Error)
{
    public bool IsAmbiguous => Wook?.Ambiguous == true;

    public bool ApplyTo(Author current)
    {
        var (profile, wook, bertrand, goodreads, _) = this;
        var changed = profile is not null && profile.ApplyTo(current, refreshBiography: true);
        if (wook is { Ambiguous: false })
        {
            if (string.IsNullOrWhiteSpace(current.PhotoUrl) && !string.IsNullOrWhiteSpace(wook.PhotoUrl))
            { current.PhotoUrl = wook.PhotoUrl; changed = true; }
            if (!string.IsNullOrWhiteSpace(wook.Biography) && !string.IsNullOrWhiteSpace(wook.SourceUrl) &&
                (string.IsNullOrWhiteSpace(current.Biography) ||
                 (current.Biography.Length < 250 && wook.Biography.Length >= current.Biography.Length * 2 &&
                  current.BiographySourceUrl?.StartsWith("https://openlibrary.org/", StringComparison.Ordinal) == true)))
            { current.Biography = wook.Biography; current.BiographySourceUrl = wook.SourceUrl; changed = true; }
        }
        if (string.IsNullOrWhiteSpace(current.Biography) && bertrand is { Biography: { Length: > 0 } bertrandText })
        { current.Biography = bertrandText; current.BiographySourceUrl = bertrand.SourceUrl; changed = true; }
        if (string.IsNullOrWhiteSpace(current.PhotoUrl) && !string.IsNullOrWhiteSpace(bertrand?.PhotoUrl))
        { current.PhotoUrl = bertrand.PhotoUrl; changed = true; }
        if (string.IsNullOrWhiteSpace(current.Biography) && goodreads?.Biography is { } goodreadsBiography)
        {
            current.Biography = goodreadsBiography;
            current.BiographySourceUrl = goodreads.SourceUrl;
            changed = true;
        }
        return changed;
    }
}

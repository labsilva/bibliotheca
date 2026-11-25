using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Fetchers;

namespace Bibliotheca.Backend.Services;

public interface IAuthorProfileLookup
{
    Task<IReadOnlyDictionary<string, AuthorProfile>> FindProfilesAsync(
        FetchedBook book, IReadOnlyList<string> authors, CancellationToken cancellationToken);

    Task<AuthorProfile?> FindProfileByKeyAsync(string sourceAuthorKey, string authorName, CancellationToken cancellationToken) =>
        Task.FromResult<AuthorProfile?>(null);
}

public sealed record AuthorProfile(string? SourceAuthorKey, string? PhotoUrl, string? Biography, string? BiographySourceUrl)
{
    public bool ApplyTo(Author author, bool refreshBiography = false)
    {
        if (author.SourceAuthorKey is not null && author.SourceAuthorKey != SourceAuthorKey)
            return false;

        var changed = false;
        if (string.IsNullOrWhiteSpace(author.PhotoUrl) && !string.IsNullOrWhiteSpace(PhotoUrl))
        {
            author.PhotoUrl = PhotoUrl;
            changed = true;
        }

        var keepArticle = author.BiographySourceUrl?.StartsWith("https://en.wikipedia.org/", StringComparison.Ordinal) == true
            && !string.IsNullOrWhiteSpace(author.Biography)
            && BiographySourceUrl?.StartsWith("https://openlibrary.org/", StringComparison.Ordinal) == true;
        if (!keepArticle && (refreshBiography || string.IsNullOrWhiteSpace(author.Biography) || string.IsNullOrWhiteSpace(author.BiographySourceUrl))
            && !string.IsNullOrWhiteSpace(Biography) && !string.IsNullOrWhiteSpace(BiographySourceUrl)
            && (author.Biography != Biography || author.BiographySourceUrl != BiographySourceUrl))
        {
            author.Biography = Biography;
            author.BiographySourceUrl = BiographySourceUrl;
            changed = true;
        }

        if (changed)
            author.SourceAuthorKey ??= SourceAuthorKey;
        return changed;
    }
}

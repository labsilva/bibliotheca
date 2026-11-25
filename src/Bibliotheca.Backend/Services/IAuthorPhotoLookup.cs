using Bibliotheca.Backend.Fetchers;

namespace Bibliotheca.Backend.Services;

public interface IAuthorPhotoLookup : IAuthorProfileLookup
{
    Task<IReadOnlyDictionary<string, string>> FindPhotosAsync(
        FetchedBook book, IReadOnlyList<string> authors, CancellationToken cancellationToken);

    async Task<IReadOnlyDictionary<string, AuthorProfile>> IAuthorProfileLookup.FindProfilesAsync(
        FetchedBook book, IReadOnlyList<string> authors, CancellationToken cancellationToken)
    {
        var photos = await FindPhotosAsync(book, authors, cancellationToken);
        return photos.ToDictionary(photo => photo.Key, photo => new AuthorProfile(null, photo.Value, null, null));
    }
}

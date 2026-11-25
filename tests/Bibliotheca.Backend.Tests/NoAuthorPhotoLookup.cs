using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Services;

namespace Bibliotheca.Backend.Tests;

internal sealed class NoAuthorPhotoLookup : IAuthorPhotoLookup
{
    public static readonly NoAuthorPhotoLookup Instance = new();
    public Task<IReadOnlyDictionary<string, string>> FindPhotosAsync(FetchedBook book,
        IReadOnlyList<string> authors, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
}

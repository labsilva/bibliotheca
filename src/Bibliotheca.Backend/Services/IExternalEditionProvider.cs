namespace Bibliotheca.Backend.Services;

public interface IExternalEditionProvider
{
    string Id { get; }
    string Name { get; }
    bool RequiresCredential => false;

    // Honour cancellation throughout the request and enforce the limit upstream when possible.
    Task<IReadOnlyList<ExternalEdition>> SearchAsync(
        string title, string? author, int limit, string? credential, CancellationToken cancellationToken);
}

public sealed record ExternalEdition(
    string ExternalId, string Title, string? Author = null, string? Isbn = null,
    string? Language = null, string? Publisher = null, int? Year = null,
    string? CoverUrl = null, string? SourceUrl = null, string? Format = null,
    ExternalEditionDetails? Details = null);

public sealed record ExternalEditionDetails(
    string? Subtitle, string[] Authors, DateTime? PublishedOn, string? Collection,
    string? Pages, string? Synopsis, string[]? Genres, bool HasStructuredAuthors = true);

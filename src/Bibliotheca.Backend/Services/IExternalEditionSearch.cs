namespace Bibliotheca.Backend.Services;

public interface IExternalEditionSearch
{
    Task<ExternalEditionSearchResponse> SearchAsync(string title, string? author, CancellationToken cancellationToken);
    IReadOnlyList<ExternalProviderStatus> GetProviders();
    Task<ExternalEdition?> ResolveAsync(string providerId, string externalId, CancellationToken cancellationToken);
}

/// <summary>External editions and the providers that failed during this search.</summary>
public sealed record ExternalEditionSearchResponse(
    IReadOnlyList<ExternalEditionResult> Items, IReadOnlyList<string> FailedProviders);

/// <summary>An edition with its provider references and matching local catalogue entry, if any.</summary>
public sealed record ExternalEditionResult(
    string ExternalId, string Title, string? Author, string? Isbn, string? Language, string? Publisher,
    int? Year, string? CoverUrl, int? BookId, IReadOnlyList<ExternalEditionSource> Sources, string? Format = null);

/// <summary>A provider's reference to a specific external edition.</summary>
public sealed record ExternalEditionSource(string ProviderId, string Name, string ExternalId, string? Url);

/// <summary>A registered provider's configuration readiness.</summary>
public sealed record ExternalProviderStatus(string Id, string Name, string State, bool Available);

namespace Bibliotheca.Backend.Services;

public interface IExternalEditionResolver
{
    Task<ExternalEdition?> GetEditionAsync(string externalId, string? credential, CancellationToken cancellationToken);
}

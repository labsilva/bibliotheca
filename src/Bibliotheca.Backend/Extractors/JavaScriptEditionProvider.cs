using System.Text.Json;
using Bibliotheca.Backend.Services;

namespace Bibliotheca.Backend.Extractors;

internal sealed record EditionExtractorInput(int Limit, string? Title = null, string? Author = null, string? ExternalId = null);

internal sealed class JavaScriptEditionProvider(ExtractorDefinition definition, ExtractorRuntime runtime,
    IExternalEditionProvider? legacy, bool fallback) : IExternalEditionProvider, IExternalEditionResolver
{
    public string Id => definition.Manifest.Id;
    public string Name => legacy?.Name ?? Id;

    public async Task<IReadOnlyList<ExternalEdition>> SearchAsync(string title, string? author, int limit,
        string? credential, CancellationToken cancellationToken)
    {
        if (limit <= 0) return [];
        if (!definition.Manifest.Operations.Contains("editionSearch"))
            return legacy is null ? [] : await legacy.SearchAsync(title, author, limit, credential, cancellationToken);
        try
        {
            return await runtime.RunEditionAsync(definition, "editionSearch", new(Math.Min(limit, 64), title, author),
                cancellationToken) ?? [];
        }
        catch (Exception exception) when (fallback && legacy is not null && !cancellationToken.IsCancellationRequested &&
            exception is TimeoutException or HttpRequestException)
        {
            return await legacy.SearchAsync(title, author, limit, credential, cancellationToken);
        }
    }

    public async Task<ExternalEdition?> GetEditionAsync(string externalId, string? credential, CancellationToken cancellationToken)
    {
        if (externalId is not { Length: > 0 and <= 256 }) return null;
        if (!definition.Manifest.Operations.Contains("editionResolve"))
            return legacy is IExternalEditionResolver resolver
                ? await resolver.GetEditionAsync(externalId, credential, cancellationToken) : null;
        try
        {
            var editions = await runtime.RunEditionAsync(definition, "editionResolve", new(1, ExternalId: externalId),
                cancellationToken);
            return editions?.SingleOrDefault();
        }
        catch (Exception exception) when (fallback && legacy is IExternalEditionResolver && !cancellationToken.IsCancellationRequested &&
            exception is TimeoutException or HttpRequestException)
        {
            return await ((IExternalEditionResolver)legacy).GetEditionAsync(externalId, credential, cancellationToken);
        }
    }

    internal static ExternalEdition[] Parse(string json, ExtractorManifest manifest, EditionExtractorInput input, Uri? source)
    {
        var results = JsonSerializer.Deserialize<ExternalEdition[]>(json, AuthorBiographyResult.JsonOptions) ?? [];
        if (results.Length > input.Limit)
            throw new InvalidDataException("Extractor returned too many editions.");

        foreach (var edition in results)
        {
            if (edition is null || edition.ExternalId is not { Length: > 0 and <= 256 } ||
                edition.Title is not { Length: > 0 and <= 512 } || string.IsNullOrWhiteSpace(edition.Title) ||
                !Uri.TryCreate(edition.SourceUrl, UriKind.Absolute, out var url) ||
                !ExtractorDefinition.Allows(url, manifest.NavigationOrigins) ||
                input.ExternalId is { } expected && (edition.ExternalId != expected || url != source) ||
                edition.Isbn is not null && EditionIsbn.Normalize(edition.Isbn) is null)
                throw new InvalidDataException("Extractor returned an invalid edition.");
        }
        return results.Select(edition => edition with
        {
            Isbn = EditionIsbn.Normalize(edition.Isbn),
            CoverUrl = Uri.TryCreate(edition.CoverUrl, UriKind.Absolute, out var image) &&
                ExtractorDefinition.Allows(image, manifest.ResourceOrigins) ? image.AbsoluteUri : null
        }).ToArray();
    }
}

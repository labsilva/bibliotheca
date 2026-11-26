using System.Text.Json;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Services;

namespace Bibliotheca.Backend.Extractors;

internal static class ApiExtractorProviders
{
    public static IServiceCollection AddApiExtractorProviders(this IServiceCollection services, IReadOnlyList<ExtractorDefinition> definitions,
        bool useBuiltIn)
    {
        var library = Find("openlibrary");
        var google = Find("googlebooks");
        services.AddHttpClient(OpenLibraryEditionProvider.ProviderId);
        services.AddSingleton<OpenLibraryEditionProvider>();
        if (useBuiltIn || library?.Manifest.Operations.Any(operation => operation is "editionSearch" or "editionResolve") == true)
            services.AddSingleton<IExternalEditionProvider>(provider => Wrap(library, provider.GetRequiredService<OpenLibraryEditionProvider>()));
        services.AddSingleton<IAuthorPhotoLookup>(provider =>
        {
            var transport = provider.GetRequiredService<OpenLibraryEditionProvider>();
            return library?.Manifest.Operations.Contains("authorProfile") == true
                ? new ApiAuthorProfileExtractor(library, transport)
                : useBuiltIn ? transport : new EmptyAuthorPhotoLookup();
        });
        services.AddSingleton<IAuthorProfileLookup>(provider => provider.GetRequiredService<IAuthorPhotoLookup>());
        services.AddHttpClient(GoogleBooksEditionProvider.ProviderId)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        services.AddSingleton<GoogleBooksEditionProvider>();
        if (useBuiltIn || google is not null)
            services.AddSingleton<IExternalEditionProvider>(provider => Wrap(google, provider.GetRequiredService<GoogleBooksEditionProvider>()));
        return services;

        ExtractorDefinition? Find(string id) => definitions.SingleOrDefault(definition => definition.Manifest.Id == id);
        IExternalEditionProvider Wrap(ExtractorDefinition? definition, IExternalEditionProvider transport) =>
            definition is null ? transport : new ApiEditionExtractor(definition, transport, useBuiltIn);
    }
}

internal sealed class EmptyAuthorPhotoLookup : IAuthorPhotoLookup
{
    public Task<IReadOnlyDictionary<string, string>> FindPhotosAsync(FetchedBook book, IReadOnlyList<string> authors,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
}

internal sealed class ApiEditionExtractor(ExtractorDefinition definition, IExternalEditionProvider transport, bool useBuiltIn)
    : IExternalEditionProvider, IExternalEditionResolver
{
    public string Id => transport.Id;
    public string Name => transport.Name;
    public bool RequiresCredential => transport.RequiresCredential;

    public async Task<IReadOnlyList<ExternalEdition>> SearchAsync(string title, string? author, int limit,
        string? credential, CancellationToken cancellationToken)
    {
        if (!useBuiltIn && !definition.Manifest.Operations.Contains("editionSearch")) return [];
        var candidates = await transport.SearchAsync(title, author, limit, credential, cancellationToken);
        return await SelectAsync("editionSearch", candidates, cancellationToken);
    }

    public async Task<ExternalEdition?> GetEditionAsync(string externalId, string? credential, CancellationToken cancellationToken)
    {
        if (transport is not IExternalEditionResolver resolver ||
            !useBuiltIn && !definition.Manifest.Operations.Contains("editionResolve")) return null;
        var candidate = await resolver.GetEditionAsync(externalId, credential, cancellationToken);
        return candidate is null ? null :
            (await SelectAsync("editionResolve", [candidate], cancellationToken)).SingleOrDefault();
    }

    private async Task<IReadOnlyList<ExternalEdition>> SelectAsync(string operation, IReadOnlyList<ExternalEdition> candidates,
        CancellationToken token)
    {
        if (!definition.Manifest.Operations.Contains(operation)) return candidates;
        var selected = await ApiExtractorSelection.SelectAsync(definition, operation, candidates, token);
        return selected.Select(index => candidates[index]).ToArray();
    }
}

internal sealed class ApiAuthorProfileExtractor(ExtractorDefinition definition, IAuthorPhotoLookup transport) : IAuthorPhotoLookup
{
    public async Task<IReadOnlyDictionary<string, string>> FindPhotosAsync(FetchedBook book, IReadOnlyList<string> authors,
        CancellationToken cancellationToken) =>
        (await FindProfilesAsync(book, authors, cancellationToken))
            .Where(pair => pair.Value.PhotoUrl is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.PhotoUrl!, StringComparer.Ordinal);

    public async Task<IReadOnlyDictionary<string, AuthorProfile>> FindProfilesAsync(FetchedBook book, IReadOnlyList<string> authors,
        CancellationToken cancellationToken)
    {
        var candidates = (await transport.FindProfilesAsync(book, authors, cancellationToken)).ToArray();
        var selected = await ApiExtractorSelection.SelectAsync(definition, "authorProfile",
            candidates.Select(pair => new { name = pair.Key, profile = pair.Value }).ToArray(), cancellationToken);
        return selected.ToDictionary(index => candidates[index].Key, index => candidates[index].Value, StringComparer.Ordinal);
    }

    public async Task<AuthorProfile?> FindProfileByKeyAsync(string sourceAuthorKey, string authorName, CancellationToken cancellationToken)
    {
        var candidate = await transport.FindProfileByKeyAsync(sourceAuthorKey, authorName, cancellationToken);
        if (candidate is null) return null;
        var selected = await ApiExtractorSelection.SelectAsync(definition, "authorProfile",
            new[] { new { name = authorName, profile = candidate } }, cancellationToken);
        return selected.Length == 0 ? null : candidate;
    }
}

internal static class ApiExtractorSelection
{
    public static async Task<int[]> SelectAsync<T>(ExtractorDefinition definition, string operation,
        IReadOnlyList<T> candidates, CancellationToken token)
    {
        var input = JsonSerializer.Serialize(candidates, AuthorBiographyResult.JsonOptions);
        if (input.Length > 512 * 1024)
            throw new InvalidDataException("API extractor input exceeds its size limit.");
        var json = await JintExtractorExecutor.EvaluateAsync(definition.Script, operation, input,
            (_, _, _) => throw new InvalidDataException("API extractor cannot call the transport."),
            token, definition.Manifest.Id, structuredInput: true);
        var selection = JsonSerializer.Deserialize<int[]>(json);
        if (selection is null || selection.Length > candidates.Count ||
            selection.Any(index => (uint)index >= candidates.Count) || selection.Distinct().Count() != selection.Length)
            throw new InvalidDataException("API extractor selected an invalid result.");
        return selection;
    }
}

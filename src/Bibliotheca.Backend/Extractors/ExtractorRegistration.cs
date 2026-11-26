using System.Reflection;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Services;

namespace Bibliotheca.Backend.Extractors;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class ExtractorProviderAttribute(string id) : Attribute
{
    public string Id => id;
}

internal interface IExtractor
{
    ExtractorDefinition Definition { get; }
    Task<ExtractorResult?> ExtractBookAsync(string operation, string isbn, CancellationToken token);
    Task<AuthorBiographyResult?> ExtractAuthorAsync(AuthorBiographyInput input, CancellationToken token);
}

internal sealed class JavaScriptExtractor(ExtractorDefinition definition, ExtractorRuntime runtime) : IExtractor
{
    public ExtractorDefinition Definition => definition;

    public Task<ExtractorResult?> ExtractBookAsync(string operation, string isbn, CancellationToken token) =>
        runtime.RunAsync(definition, operation, isbn, token);

    public Task<AuthorBiographyResult?> ExtractAuthorAsync(AuthorBiographyInput input, CancellationToken token) =>
        runtime.RunAuthorAsync(definition, input, token);
}

internal sealed class ExtractorFactory(ExtractorRuntime runtime)
{
    public IExtractor Create(ExtractorDefinition definition) => new JavaScriptExtractor(definition, runtime);
}

internal static class ExtractorRegistration
{
    public static IServiceCollection AddExtractors(this IServiceCollection services, IConfiguration configuration,
        string contentRoot)
    {
        var definitions = ExtractorCatalog.Load(configuration.GetSection("Extractors"), contentRoot);
        var capabilities = definitions.SelectMany(definition => definition.Manifest.Operations
            .Select(operation => (definition.Manifest.Id, Operation: operation))).ToHashSet();

        services.AddHttpClient<CoverDownloader>();
        services.AddHttpClient(ExtractorRuntime.ClientName)
            .ConfigurePrimaryHttpMessageHandler(ExtractorTransport.CreateHandler)
            .RemoveAllLoggers();
        services.AddSingleton<ExtractorRuntime>();
        services.AddSingleton<ExtractorFactory>();

        foreach (var definition in definitions.Where(definition => definition.Manifest.NavigationOrigins.Length > 0))
        {
            var id = definition.Manifest.Id;
            services.AddKeyedSingleton<IExtractor>(id, (provider, _) =>
                provider.GetRequiredService<ExtractorFactory>().Create(definition));

            if (capabilities.Contains((id, "metadata")))
                services.AddSingleton<IBookFetcher>(provider =>
                    new JavaScriptExtractorFetcher(provider.GetRequiredKeyedService<IExtractor>(id), definition.Priority));
            if (capabilities.Contains((id, "cover")))
                services.AddSingleton<ICoverFetcher>(provider =>
                    new JavaScriptExtractorFetcher(provider.GetRequiredKeyedService<IExtractor>(id),
                        definition.CoverPriority ?? definition.Priority));
            if (definition.AuthorBiographyEnabled && capabilities.Contains((id, "authorBiography")))
                services.AddSingleton<IAuthorBiographyLookup>(provider =>
                    new JavaScriptAuthorBiographyLookup(provider.GetRequiredKeyedService<IExtractor>(id)));
        }

        services.Scan(scan => scan.FromAssemblyOf<IBookFetcher>()
            .AddClasses(classes => classes.AssignableTo<IBookFetcher>().Where(type => UseBuiltIn(type, "metadata")))
            .AsSelfWithInterfaces());
        services.Scan(scan => scan.FromAssemblyOf<ICoverFetcher>()
            .AddClasses(classes => classes.AssignableTo<ICoverFetcher>().Where(type => UseBuiltIn(type, "cover")))
            .AsSelfWithInterfaces());

        var hardcover = definitions.SingleOrDefault(definition => definition.Manifest.Id == HardcoverEditionProvider.ProviderId);
        services.AddHttpClient(HardcoverEditionProvider.ProviderId)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        services.AddSingleton<IExternalEditionProvider>(provider => new HardcoverEditionProvider(
            provider.GetRequiredService<IHttpClientFactory>(), hardcover,
            provider.GetRequiredService<ILogger<HardcoverEditionProvider>>()));
        return services;

        bool UseBuiltIn(Type type, string operation) =>
            type.GetCustomAttribute<ExtractorProviderAttribute>() is not { } metadata ||
            !capabilities.Contains((metadata.Id, operation));
    }
}

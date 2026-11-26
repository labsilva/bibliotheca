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
        var settings = configuration.GetSection("Extractors");
        var definitions = ExtractorCatalog.Load(settings, contentRoot);
        var useBuiltInProviders = settings.GetValue("UseBuiltInProviders", true);
        var fallback = useBuiltInProviders && settings.GetValue("FallbackToBuiltIn", false);
        var capabilities = definitions.SelectMany(definition => definition.Manifest.Operations
            .Select(operation => (definition.Manifest.Id, Operation: operation))).ToHashSet();

        services.AddHttpClient<CoverDownloader>();
        services.AddHttpClient(ExtractorRuntime.ClientName)
            .ConfigurePrimaryHttpMessageHandler(ExtractorTransport.CreateHandler)
            .RemoveAllLoggers();
        services.AddSingleton<ExtractorRuntime>();
        services.AddSingleton<ExtractorFactory>();
        foreach (var definition in definitions) services.AddSingleton(definition);
        services.AddSingleton<IBertrandReader, BertrandReader>();
        services.AddSingleton<BertrandCatalogueLookup>();
        services.AddSingleton<WookPlaywrightPageReader>();
        services.AddSingleton<IWookPageReader>(provider => provider.GetRequiredService<WookPlaywrightPageReader>());
        services.AddSingleton<IWookCoverReader>(provider => provider.GetRequiredService<WookPlaywrightPageReader>());
        if (useBuiltInProviders)
        {
            services.AddSingleton<BertrandAuthorLookup>();
            services.AddSingleton<IWookAuthorLookup, WookAuthorLookup>();
        }

        if (fallback)
        {
            services.Scan(scan => scan.FromAssemblyOf<IBookFetcher>()
                .AddClasses(classes => classes.AssignableTo<IBookFetcher>().Where(type => !UseBuiltIn(type, "metadata"))).AsSelf());
            services.Scan(scan => scan.FromAssemblyOf<ICoverFetcher>()
                .AddClasses(classes => classes.AssignableTo<ICoverFetcher>().Where(type => !UseBuiltIn(type, "cover"))).AsSelf());
        }

        foreach (var definition in definitions.Where(definition => definition.Manifest.NavigationOrigins.Length > 0))
        {
            var id = definition.Manifest.Id;
            services.AddKeyedSingleton<IExtractor>(id, (provider, _) =>
                provider.GetRequiredService<ExtractorFactory>().Create(definition));

            if (capabilities.Contains((id, "metadata")))
                services.AddSingleton<IBookFetcher>(provider =>
                    new JavaScriptExtractorFetcher(provider.GetRequiredKeyedService<IExtractor>(id), definition.Priority,
                        fallback: ResolveBuiltIn<IBookFetcher>(provider, id), logger: provider.GetService<ILogger<JavaScriptExtractorFetcher>>()));
            if (capabilities.Contains((id, "cover")))
                services.AddSingleton<ICoverFetcher>(provider =>
                    new JavaScriptExtractorFetcher(provider.GetRequiredKeyedService<IExtractor>(id),
                        definition.CoverPriority ?? definition.Priority, coverFallback: ResolveBuiltIn<ICoverFetcher>(provider, id),
                        logger: provider.GetService<ILogger<JavaScriptExtractorFetcher>>()));
            if (definition.AuthorBiographyEnabled && capabilities.Contains((id, "authorBiography")))
                services.AddSingleton<IAuthorBiographyLookup>(provider =>
                    new JavaScriptAuthorBiographyLookup(provider.GetRequiredKeyedService<IExtractor>(id)));
        }

        if (useBuiltInProviders)
        {
            services.Scan(scan => scan.FromAssemblyOf<IBookFetcher>()
                .AddClasses(classes => classes.AssignableTo<IBookFetcher>().Where(type => UseBuiltIn(type, "metadata")))
                .AsSelfWithInterfaces());
            services.Scan(scan => scan.FromAssemblyOf<ICoverFetcher>()
                .AddClasses(classes => classes.AssignableTo<ICoverFetcher>().Where(type => UseBuiltIn(type, "cover")))
                .AsSelfWithInterfaces());
        }

        services.AddSingleton<WookEditionProvider>();
        foreach (var definition in definitions.Where(definition => definition.Manifest.NavigationOrigins.Length > 0 &&
            definition.Manifest.Operations.Any(operation => operation is "editionSearch" or "editionResolve")))
        {
            services.AddSingleton<IExternalEditionProvider>(provider => new JavaScriptEditionProvider(definition,
                provider.GetRequiredService<ExtractorRuntime>(), useBuiltInProviders && definition.Manifest.Id == "wook"
                    ? provider.GetRequiredService<WookEditionProvider>() : null, fallback));
        }
        if (useBuiltInProviders && !capabilities.Contains(("wook", "editionSearch")) &&
            !capabilities.Contains(("wook", "editionResolve")))
            services.AddSingleton<IExternalEditionProvider>(provider => provider.GetRequiredService<WookEditionProvider>());

        services.AddApiExtractorProviders(definitions, useBuiltInProviders);

        var hardcover = definitions.SingleOrDefault(definition => definition.Manifest.Id == HardcoverEditionProvider.ProviderId);
        services.AddHttpClient(HardcoverEditionProvider.ProviderId)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        if (useBuiltInProviders || hardcover is not null)
            services.AddSingleton<IExternalEditionProvider>(provider => new HardcoverEditionProvider(
                provider.GetRequiredService<IHttpClientFactory>(), hardcover,
                provider.GetRequiredService<ILogger<HardcoverEditionProvider>>(), useBuiltInProviders));
        return services;

        T? ResolveBuiltIn<T>(IServiceProvider provider, string id) where T : class
        {
            if (!fallback) return null;
            var type = typeof(T).Assembly.GetExportedTypes().SingleOrDefault(type =>
                !type.IsAbstract && typeof(T).IsAssignableFrom(type) && type.GetCustomAttribute<ExtractorProviderAttribute>()?.Id == id);
            return type is null ? null : (T)provider.GetRequiredService(type);
        }

        bool UseBuiltIn(Type type, string operation) =>
            type.GetCustomAttribute<ExtractorProviderAttribute>() is not { } metadata ||
            !capabilities.Contains((metadata.Id, operation));
    }
}

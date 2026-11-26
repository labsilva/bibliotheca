using Bibliotheca.Backend.Authorization;
using Bibliotheca.Backend.Data;
using Bibliotheca.Backend.Extractors;
using Microsoft.AspNetCore.Authorization;
using Bibliotheca.Backend.Fetchers;
using Bibliotheca.Backend.Hubs;
using Bibliotheca.Backend.Initialization;
using Bibliotheca.Backend.Jobs;
using Bibliotheca.Backend.Options;
using Bibliotheca.Backend.Services;
using Bibliotheca.Backend.Security;
using Figgle.Fonts;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using NLog;
using NLog.Web;
using Scalar.AspNetCore;
using System.Text.Json.Serialization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Identity;
using System.Security.Cryptography.X509Certificates;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Bibliotheca.Backend;

public class Program
{
    public static async Task Main(string[] args)
    {
        NLog.LogManager.Setup().SetupExtensions(extensions => extensions.RegisterLayoutRenderer<SafeLogMessage>("safe-message"));
        var logger = NLog.LogManager.Setup().LoadConfigurationFromAppSettings().GetCurrentClassLogger();
        Console.WriteLine(FiggleFonts.Ogre.Render("Bibliotheca API"));
        var builder = WebApplication.CreateBuilder(args);
        if (builder.Configuration.GetValue<bool>("Deployment:Combined"))
        {
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Configure(new ConfigurationBuilder().Build());
                options.Listen(IPAddress.Loopback, 5500);
            });
        }
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
        builder.Host.UseNLog();

        // Add services to the container.
        builder.Services.AddControllersWithViews();
        builder.Services.AddOptions<ExternalEditionSearchOptions>()
            .BindConfiguration(ExternalEditionSearchOptions.SectionName)
            .Validate(options => options.IsValid(), "External edition search limits are out of range.")
            .ValidateOnStart();
        builder.Services.AddKeyedSingleton<SemaphoreSlim>(ExternalEditionSearchOptions.SectionName, (services, _) =>
        {
            var limit = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExternalEditionSearchOptions>>()
                .Value.MaxConcurrentRequests;
            return new SemaphoreSlim(limit, limit);
        });
        builder.Services.AddScoped<IExternalEditionSearch, ExternalEditionSearch>();
        builder.Services.AddHttpClient(OpenLibraryEditionProvider.ProviderId);
        builder.Services.AddSingleton<OpenLibraryEditionProvider>();
        builder.Services.AddSingleton<IExternalEditionProvider>(services => services.GetRequiredService<OpenLibraryEditionProvider>());
        builder.Services.AddSingleton<IAuthorPhotoLookup>(services => services.GetRequiredService<OpenLibraryEditionProvider>());
        builder.Services.AddSingleton<IAuthorProfileLookup>(services => services.GetRequiredService<IAuthorPhotoLookup>());
        builder.Services.AddHttpClient(GoogleBooksEditionProvider.ProviderId)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        builder.Services.AddSingleton<IExternalEditionProvider, GoogleBooksEditionProvider>();
        var extractors = ExtractorDefinition.Load(builder.Configuration.GetSection("Extractors"),
            builder.Environment.ContentRootPath);
        var hardcoverExtractor = extractors.SingleOrDefault(extractor => extractor.Manifest.Id == HardcoverEditionProvider.ProviderId);
        builder.Services.AddHttpClient(HardcoverEditionProvider.ProviderId)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        builder.Services.AddSingleton<IExternalEditionProvider>(services => new HardcoverEditionProvider(
            services.GetRequiredService<IHttpClientFactory>(), hardcoverExtractor,
            services.GetRequiredService<ILogger<HardcoverEditionProvider>>()));
        builder.Services.AddSingleton<IBertrandReader, BertrandReader>();
        builder.Services.AddSingleton<BertrandCatalogueLookup>();
        builder.Services.AddSingleton<BertrandAuthorLookup>();
        builder.Services.AddSingleton<WookPlaywrightPageReader>();
        builder.Services.AddSingleton<IWookPageReader>(services => services.GetRequiredService<WookPlaywrightPageReader>());
        builder.Services.AddSingleton<IWookCoverReader>(services => services.GetRequiredService<WookPlaywrightPageReader>());
        builder.Services.AddSingleton<IWookAuthorLookup, WookAuthorLookup>();
        builder.Services.AddSingleton<IExternalEditionProvider, WookEditionProvider>();
        builder.Services.AddSignalR();
        builder.Services.AddSingleton<IUserIdProvider, BookImportUserIdProvider>();
        builder.Services.AddSingleton<NotifyFetchedBookService>();
        builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("account-entry", context => RateLimitPartition.GetFixedWindowLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "local",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1)
                }));
        });

        builder.Services.AddExpensiveOperationLimits(builder.Configuration);

        var development = builder.Environment.IsDevelopment();
        var auth = builder.Configuration.GetSection("Authentication");
        var frontendOrigin = (auth["FrontendOrigin"] ?? "https://books.example.org").TrimEnd('/');
        builder.Services.AddAntiforgery(options =>
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always);
        X509Certificate2? signingCertificate = null;
        X509Certificate2? encryptionCertificate = null;
        Uri? issuer = null;
        if (!development)
        {
            if (!Uri.TryCreate(auth["Issuer"], UriKind.Absolute, out issuer) || issuer.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("Authentication:Issuer must be an absolute HTTPS URL.");

            signingCertificate = LoadCertificate(auth.GetSection("SigningCertificate"));
            encryptionCertificate = LoadCertificate(auth.GetSection("EncryptionCertificate"));
            if (signingCertificate.Thumbprint == encryptionCertificate.Thumbprint)
                throw new InvalidOperationException("Use separate OIDC signing and encryption certificates.");

            var keysPath = auth["DataProtectionKeyPath"];
            if (string.IsNullOrWhiteSpace(keysPath))
                throw new InvalidOperationException("Authentication:DataProtectionKeyPath is required.");

            builder.Services.AddDataProtection()
                .SetApplicationName("Bibliotheca")
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
                .ProtectKeysWithCertificate(encryptionCertificate);
        }

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost |
                                       ForwardedHeaders.XForwardedPrefix | ForwardedHeaders.XForwardedProto;
            if (issuer is not null)
                options.AllowedHosts.Add(issuer.IdnHost);
            if (builder.Configuration.GetValue<bool>("Deployment:Combined"))
            {
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Add(IPAddress.Loopback);
                options.ForwardLimit = 1;
            }
            foreach (var proxy in builder.Configuration.GetValue<bool>("Deployment:Combined")
                         ? [] : auth.GetSection("KnownProxies").Get<string[]>() ?? [])
                options.KnownProxies.Add(IPAddress.Parse(proxy));
        });
        builder.Services.AddScoped<IAuthorizationHandler, ManageCatalogueHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, AdministratorHandler>();
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(CataloguePolicies.Administrator, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new AdministratorRequirement()));
            options.AddPolicy(CataloguePolicies.Import, policy => policy.RequireAuthenticatedUser());
            options.AddPolicy(CataloguePolicies.Manage, policy =>
                policy.RequireAuthenticatedUser().AddRequirements(new ManageCatalogueRequirement()));
        });

        // Authentication & Authorization
        builder.Services.AddScoped<BrowserSessionEvents>();
        builder.Services
            .AddAuthentication("Bibliotheca")
            .AddPolicyScheme("Bibliotheca", "Bibliotheca authentication", options =>
            {
                options.ForwardDefaultSelector = context =>
                    context.Request.Path.StartsWithSegments("/api")
                        ? OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
                        : CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(opts =>
            {
                opts.LoginPath = "/account/login";
                opts.LogoutPath = "/account/logout";
                opts.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                opts.Cookie.Name = "idp_auth";
                opts.Cookie.HttpOnly = true;
                opts.Cookie.SameSite = SameSiteMode.Lax;
                opts.SlidingExpiration = false;
                opts.ExpireTimeSpan = TimeSpan.FromHours(8);
                opts.EventsType = typeof(BrowserSessionEvents);
            });


        builder.Services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                       .UseDbContext<BibliothecaDbContext>();
            })
            // Servidor OIDC
            .AddServer(options =>
            {
                options.DisableAccessTokenEncryption();
                options.SetAccessTokenLifetime(BrowserSessionEvents.AccessTokenLifetime);
                options.DisableSlidingRefreshTokenExpiration();
                options.RequireProofKeyForCodeExchange();

                options.AllowAuthorizationCodeFlow()
                       .AllowRefreshTokenFlow();

                options.SetAuthorizationEndpointUris("/connect/authorize")
                       .SetTokenEndpointUris("/connect/token")
                       .SetEndSessionEndpointUris("/connect/logout")
                       .SetUserInfoEndpointUris("/connect/userinfo") // opcional
                       ;
                // Scopes padrão
                options.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.Email, Scopes.OfflineAccess);

                // Autenticação do utilizador feita via Cookie
                options.UseAspNetCore()
                       .EnableAuthorizationEndpointPassthrough()
                       .EnableTokenEndpointPassthrough()
                       .EnableEndSessionEndpointPassthrough()
                       .EnableUserInfoEndpointPassthrough();

                if (development)
                {
                    options.UseAspNetCore().DisableTransportSecurityRequirement();
                    options.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
                }
                else
                {
                    options.SetIssuer(issuer!);
                    options.AddEncryptionCertificate(encryptionCertificate!);
                    options.AddSigningCertificate(signingCertificate!);
                }

                // Tokens (JWT ou referência). Aqui JWT auto-assinado.

                // Subject do utilizador virá das ClaimsName/Subject (ver AccountController)
            })
            // Validação (se precisares validar tokens no mesmo serviço/API)
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.EnableAuthorizationEntryValidation();
                options.EnableTokenEntryValidation();
                options.AddAudiences("bibliotheca-api");
                options.UseAspNetCore()
                    .DisableAccessTokenExtractionFromBodyForm()
                    .DisableAccessTokenExtractionFromQueryString();
            });



        builder.Services
            .AddAsyncInitialization()
            .AddAsyncInitializer<EntityFrameworkMigrationInitializer>()
            .AddAsyncInitializer<OpeniddictInitialization>()
            ;

        builder.Services.AddCors(options =>
        {
            options.AddPolicy(name: "default",
                              policy =>
                              {
                                  policy
                                    .WithOrigins(development ? [frontendOrigin, "http://localhost:5173"] : [frontendOrigin])
                                    .AllowAnyHeader()
                                    .AllowAnyMethod()
                                    .AllowCredentials();
                              });
        });

        builder.Services.AddOpenApi();
        builder.Services
            .AddResponseCaching()
            .AddResponseCompression();

        builder.Services.AddSingleton<BookImportSignal>();
        builder.Services.AddScoped<BookImportQueue>();
        builder.Services.AddScoped<FetchBookJob>();
        builder.Services.AddHostedService<BookImportWorker>();
        builder.Services.AddHostedService<CatalogueRefreshWorker>();
        builder.Services.AddHostedService<AuthorDuplicateScanWorker>();
        builder.Services.AddHostedService<AuthorRefreshWorker>();
        builder.Services.AddHostedService<FetcherDiagnosticsCleanup>();

        // Data
        builder.Services.AddDbContext<BibliothecaDbContext>((sp, options) =>
        {
            options.UseSqlite(builder.Configuration["ConnectionStrings:SQLiteDefault"]);
            options.AddInterceptors(new DiacriticsInterceptor());
        });

        // Fetchers
        builder.Services.AddHttpClient<CoverDownloader>();
        builder.Services.AddHttpClient(ExtractorRuntime.ClientName)
            .ConfigurePrimaryHttpMessageHandler(ExtractorTransport.CreateHandler)
            .RemoveAllLoggers();
        builder.Services.AddSingleton<ExtractorRuntime>();
        foreach (var extractor in extractors)
        {
            if (extractor.AuthorBiographyEnabled && extractor.Manifest.Operations.Contains("authorBiography"))
                builder.Services.AddSingleton<IAuthorBiographyLookup>(services =>
                    new JavaScriptAuthorBiographyLookup(extractor, services.GetRequiredService<ExtractorRuntime>()));
            if (extractor.Manifest.Operations.Contains("metadata"))
                builder.Services.AddSingleton<IBookFetcher>(services =>
                    new JavaScriptExtractorFetcher(extractor, services.GetRequiredService<ExtractorRuntime>()));
            if (extractor.Manifest.Operations.Contains("cover"))
                builder.Services.AddSingleton<ICoverFetcher>(services =>
                    new JavaScriptExtractorFetcher(extractor with { Priority = extractor.CoverPriority ?? extractor.Priority },
                        services.GetRequiredService<ExtractorRuntime>()));
        }
        bool HasExtractor(string id, string operation) => extractors.Any(extractor =>
            extractor.Manifest.Id == id && extractor.Manifest.Operations.Contains(operation));
        builder.Services.Scan(scan =>
            scan.FromAssemblyOf<Program>()
                .AddClasses(classes => classes.AssignableTo<IBookFetcher>().Where(type =>
                    (type != typeof(BertrandBookFetcher) || !HasExtractor("bertrand", "metadata")) &&
                    (type != typeof(WookBookFetcher) || !HasExtractor("wook", "metadata")) &&
                    (type != typeof(GoodReadsBookFetcher) || !HasExtractor("goodreads", "metadata"))))
                .AsSelfWithInterfaces());
        builder.Services.Scan(scan =>
            scan.FromAssemblyOf<Program>()
                .AddClasses(classes => classes.AssignableTo<ICoverFetcher>().Where(type =>
                    (type != typeof(BertrandCoverFetcher) || !HasExtractor("bertrand", "cover")) &&
                    (type != typeof(WookCoverFetcher) || !HasExtractor("wook", "cover")) &&
                    (type != typeof(AmazonCoverFetcher) || !HasExtractor("amazon", "cover")) &&
                    (type != typeof(IsbndbCoverFetcher) || !HasExtractor("isbndb", "cover"))))
                .AsSelfWithInterfaces());
        builder.Services.Scan(scan =>
            scan.FromAssemblyOf<Program>()
                .AddClasses(classes => classes.AssignableTo(typeof(IServiceExecutor<,>)))
                .AsSelfWithInterfaces());

        // Services
        builder.Services.AddScoped<StoreFetchedBookService>();
        builder.Services.AddScoped<StoreExternalEdition>();

        builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options =>
        {
            options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.Services
            .Configure<PlaywrightOptions>(builder.Configuration.GetSection(PlaywrightOptions.ConfigurationName))
            .Configure<FetcherOptions>(builder.Configuration.GetSection(FetcherOptions.ConfigurationName))
            .PostConfigure<FetcherOptions>(options =>
            {
                if (!options.SaveTempFiles)
                    return;

                var contentRoot = builder.Environment.ContentRootPath;
                var webRoot = builder.Environment.WebRootPath ?? Path.Combine(contentRoot, "wwwroot");
                var directory = Path.GetFullPath(options.TempFilesSaveLocation, contentRoot);
                if (IsWithin(webRoot, directory) || IsWithin(directory, webRoot) || IsWithin(directory, contentRoot))
                    throw new InvalidOperationException("Fetcher diagnostics require a dedicated directory outside the web root.");

                options.TempFilesSaveLocation =
                    Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
            });

        var app = builder.Build();

        try
        {
            app.UseForwardedHeaders();
            if (!development)
                app.UseHsts();
            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseCors("default");
            app.Use(async (context, next) =>
            {
                // Historical installations stored private files below these directories.
                // Reject before StaticFiles, regardless of extension or MIME configuration.
                var path = context.Request.Path;
                if (path.StartsWithSegments("/data", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWithSegments("/temp", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    context.Response.Headers.CacheControl = "no-store";
                    return;
                }
                await next(context);
            });
            app.UseStaticFiles();
            app.UseResponseCaching();
            app.UseResponseCompression();
            app.Use(async (context, next) =>
            {
                var request = context.Request;
                // Browser WebSocket and SSE clients send the SignalR token in the query string.
                if (request.Path == "/api/bibliotheca-hub" &&
                    HttpMethods.IsGet(request.Method) &&
                    !request.Headers.ContainsKey("Authorization") &&
                    request.Query.TryGetValue("access_token", out var token) &&
                    token.Count == 1 && !string.IsNullOrWhiteSpace(token[0]))
                {
                    request.Headers.Authorization = "Bearer " + token[0];
                }

                await next(context);
            });
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();
            app.MapControllers().RequireAuthorization();
            app.MapDefaultControllerRoute().RequireAuthorization();
            app.MapOpenApi();
            app.MapScalarApiReference((options, context) =>
            {
                options.WithProxy(context.Request.PathBase);
            });
            app.MapHub<BibliothecaHub>("/api/bibliotheca-hub").RequireAuthorization();
            await app.InitAsync();
            await app.RunAsync();
        }
        finally
        {
            LogManager.Shutdown();
        }
    }
    private static bool IsWithin(string parent, string path)
    {
        var relative = Path.GetRelativePath(parent, path);
        return relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !Path.IsPathRooted(relative);
    }

    private static X509Certificate2 LoadCertificate(IConfigurationSection configuration)
    {
        var path = configuration["Path"];
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"{configuration.Path}:Path is required.");

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            path, Bibliotheca.Backend.Security.CertificatePassword.Read(configuration), X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException($"{configuration.Path} must contain a private key.");

        return certificate;
    }
}

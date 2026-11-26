using System.Diagnostics;
using System.Text.Json;
using Bibliotheca.Backend.Options;
using Bibliotheca.Backend.Services;
using Microsoft.Extensions.Logging.Console;

namespace Bibliotheca.Backend.Extractors;

internal static class ExtractorCheckCommand
{
    private const string Usage = """
        Usage: extractors check <provider> <operation> [options]
          metadata or cover   --isbn <ISBN>
          editionSearch       --title <title> [--author <author>]
          editionResolve      --id <external edition ID>
          authorBiography     --name <name> --isbn <ISBN> [--isbn <ISBN> ...]
          authorProfile       --name <name> --id <external author ID>
        """;

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args is [ "help" ] or [ "--help" ])
        {
            Console.WriteLine(Usage);
            return 0;
        }

        if (args.Length < 3 || args[0] != "check" ||
            !TryParseOptions(args[2], args.AsSpan(3), out var options))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        var provider = args[1];
        var operation = args[2];
        var elapsed = Stopwatch.StartNew();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        ConsoleCancelEventHandler cancelOnInterrupt = (_, interrupt) =>
        {
            interrupt.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelOnInterrupt;
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
            builder.Configuration["Extractors:UseBuiltInProviders"] = "false";
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole();
            builder.Services.Configure<ConsoleLoggerOptions>(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Services.Configure<PlaywrightOptions>(builder.Configuration.GetSection(PlaywrightOptions.ConfigurationName));
            builder.Services.AddExtractors(builder.Configuration, builder.Environment.ContentRootPath);
            await using var application = builder.Build();
            var outcome = await CheckAsync(application.Services, builder.Configuration, provider, operation, options, cancellation.Token);
            return Finish(outcome.Status, outcome.Reason, outcome.Code, outcome.Count);
        }
        catch (TimeoutException)
        {
            return Finish("failed", "timeout", 1);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return Finish("failed", "cancelled_or_timed_out", 1);
        }
        catch (HttpRequestException error)
        {
            return Finish("failed", error.StatusCode is { } status ? $"upstream_http_{(int)status}" :
                error.HttpRequestError.ToString(), 1);
        }
        catch (InvalidDataException)
        {
            return Finish("failed", "invalid_package_or_result", 1);
        }
        catch (Jint.Runtime.MemoryLimitExceededException)
        {
            return Finish("failed", "memory_limit", 1);
        }
        catch (Exception error)
        {
            return Finish("failed", error.GetType().Name, 1);
        }
        finally { Console.CancelKeyPress -= cancelOnInterrupt; }

        int Finish(string status, string? reason, int code, int? count = null)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { provider, operation, status, reason, count, elapsedMs = elapsed.ElapsedMilliseconds }));
            return code;
        }
    }

    private static async Task<Outcome> CheckAsync(IServiceProvider services, IConfiguration configuration,
        string id, string operation, Dictionary<string, List<string>> options, CancellationToken token)
    {
        var definition = services.GetServices<ExtractorDefinition>().SingleOrDefault(package => package.Manifest.Id == id);
        if (definition is null || !definition.Manifest.Operations.Contains(operation))
            return new("failed", "package_or_operation_not_enabled", 2);

        if (operation is "metadata" or "cover")
        {
            if (EditionIsbn.Normalize(options["--isbn"][0]) is not { } isbn)
                return new("failed", "invalid_isbn", 2);
            var extractor = services.GetRequiredKeyedService<IExtractor>(id);
            var result = await extractor.ExtractBookAsync(operation, isbn, token);
            return Outcome.FromCount(operation == "cover" ? result?.Cover is { Success: true } ? 1 : 0 :
                result is null ? 0 : 1);
        }

        if (operation == "authorBiography")
        {
            if (!definition.AuthorBiographyEnabled)
                return new("failed", "package_or_operation_not_enabled", 2);
            var lookup = services.GetServices<IAuthorBiographyLookup>().Single(item => item.Id == id);
            if (options["--isbn"].Any(isbn => EditionIsbn.Normalize(isbn) is null))
                return new("failed", "invalid_isbn", 2);
            var result = await lookup.FindAsync(options["--name"][0], options["--isbn"]
                .Select(isbn => new AuthorBiographyEdition(isbn)).ToArray(), token);
            return result?.Ambiguous == true ? new("no_match", "ambiguous_author", 3) : Outcome.FromCount(result is null ? 0 : 1);
        }

        if (operation == "authorProfile")
        {
            var lookup = services.GetRequiredService<IAuthorProfileLookup>();
            return Outcome.FromCount(await lookup.FindProfileByKeyAsync(options["--id"][0], options["--name"][0], token)
                is null ? 0 : 1);
        }

        var api = services.GetServices<IExternalEditionProvider>()
            .SingleOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (api is null) return new("failed", "package_or_operation_not_enabled", 2);
        var settings = configuration.GetSection($"{ExternalEditionSearchOptions.SectionName}:Providers:{api.Id}")
            .Get<ExternalEditionProviderOptions>() ?? new();
        if (!settings.Enabled)
            return new("failed", "edition_provider_not_enabled", 2);
        var credential = string.IsNullOrWhiteSpace(settings.CredentialKey) ? null : configuration[settings.CredentialKey];
        if ((api.RequiresCredential || !string.IsNullOrWhiteSpace(settings.CredentialKey)) &&
            string.IsNullOrWhiteSpace(credential))
            return new("failed", "credential_not_configured", 2);
        if (settings.TimeoutSeconds is < 1 or > 60 || settings.ResultLimit is < 1 or > 100)
            return new("failed", "invalid_edition_provider_settings", 2);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            if (operation == "editionSearch")
                return Outcome.FromCount((await api.SearchAsync(options["--title"][0],
                    options.GetValueOrDefault("--author")?[0], settings.ResultLimit, credential, deadline.Token)).Count);
            return Outcome.FromCount(api is IExternalEditionResolver resolver &&
                await resolver.GetEditionAsync(options["--id"][0], credential, deadline.Token) is not null ? 1 : 0);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !token.IsCancellationRequested)
        {
            return new("failed", "timeout", 1);
        }
    }

    private static bool TryParseOptions(string operation, ReadOnlySpan<string> args,
        out Dictionary<string, List<string>> options)
    {
        options = new(StringComparer.Ordinal);
        string[] required = operation switch
        {
            "metadata" or "cover" => ["--isbn"],
            "editionSearch" => ["--title"],
            "editionResolve" => ["--id"],
            "authorBiography" => ["--name", "--isbn"],
            "authorProfile" => ["--name", "--id"],
            _ => []
        };
        if (required.Length == 0 || args.Length % 2 != 0) return false;
        for (var index = 0; index < args.Length; index += 2)
        {
            var key = args[index];
            var value = args[index + 1];
            if ((!required.Contains(key) && !(operation == "editionSearch" && key == "--author")) ||
                string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal) || value.Length > 512)
                return false;
            if (!options.TryGetValue(key, out var values))
                options[key] = values = [];
            if (values.Count >= (operation == "authorBiography" && key == "--isbn" ? 3 : 1)) return false;
            values.Add(value);
        }
        foreach (var key in required)
            if (!options.ContainsKey(key)) return false;
        return true;
    }

    private sealed record Outcome(string Status, string? Reason, int Code, int? Count = null)
    {
        public static Outcome FromCount(int count) => count == 0 ? new("no_match", "no_match", 3, 0) : new("success", null, 0, count);
    }
}

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bibliotheca.Backend.Extractors;

internal static class ExtractorCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static IReadOnlyList<ExtractorDefinition> Load(IConfiguration configuration, string contentRoot)
    {
        var definitions = new List<ExtractorDefinition>();
        var root = Path.GetFullPath(configuration["Directory"] ?? "extractors", contentRoot);
        var providers = configuration.GetSection("Providers");
        var directories = Directory.Exists(root)
            ? Directory.EnumerateDirectories(root).Where(directory => File.Exists(Path.Combine(directory, "manifest.json")))
                .ToDictionary(directory => Path.GetFileName(directory)!, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var provider in providers.GetChildren().Where(provider => provider.GetValue<bool>("Enabled")))
        {
            var id = provider.Key;
            if (!Regex.IsMatch(id, @"\A[a-z][a-z0-9-]{0,63}\z"))
                throw new InvalidDataException("Invalid extractor identifier.");

            if (!directories.TryGetValue(id, out var directory))
                throw new InvalidDataException($"Enabled extractor '{id}' has no manifest in the extractor directory.");
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Enabled extractor '{id}' must be installed as a local directory.");
            var manifest = JsonSerializer.Deserialize<ExtractorManifest>(
                ReadFile(Path.Combine(directory, "manifest.json"), 16_384), JsonOptions);
            var apiOrigins = id switch
            {
                "hardcover" => new[] { "https://api.hardcover.app" },
                "openlibrary" => new[] { "https://openlibrary.org", "https://en.wikipedia.org" },
                "googlebooks" => new[] { "https://www.googleapis.com" },
                _ => null
            };
            if (manifest is null || manifest.Id != id || manifest.ContractVersion != 1 ||
                manifest.Entry != "extract.js" || manifest.Operations is not { Length: > 0 } ||
                manifest.Operations.Any(operation => apiOrigins is not null
                    ? operation is not ("editionSearch" or "editionResolve") && !(id == "openlibrary" && operation == "authorProfile")
                    : operation is not ("metadata" or "cover" or "editionSearch" or "editionResolve" or "authorBiography")) ||
                (apiOrigins is not null && (manifest.NavigationOrigins is not { Length: 0 } ||
                    manifest.ResourceOrigins is null || !manifest.ResourceOrigins.SequenceEqual(apiOrigins, StringComparer.Ordinal) ||
                    manifest.OptionalResourceOrigins is { Length: > 0 })))
                throw new InvalidDataException($"Invalid version 1 manifest for extractor '{id}'.");

            ValidateOrigins(manifest.NavigationOrigins, provider.GetSection("NavigationOrigins").Get<string[]>(), apiOrigins is null);
            var approvedResources = provider.GetSection("ResourceOrigins").Get<string[]>() ?? [];
            ValidateOrigins(manifest.ResourceOrigins, approvedResources, false);
            var optionalResources = manifest.OptionalResourceOrigins ?? [];
            ValidateOrigins(optionalResources, optionalResources, false);
            manifest = manifest with
            {
                RequireAuthorEvidence = manifest.RequireAuthorEvidence ||
                    (id is not ("wook" or "bertrand" or "goodreads") && manifest.Operations.Contains("authorBiography")),
                ResourceOrigins = manifest.ResourceOrigins.Concat(optionalResources.Intersect(approvedResources, StringComparer.Ordinal))
                    .Distinct(StringComparer.Ordinal).ToArray()
            };
            var enabledOperations = provider.GetSection("EnabledOperations").Get<string[]>();
            if (enabledOperations is not null)
            {
                if (enabledOperations.Any(operation => !manifest.Operations.Contains(operation, StringComparer.Ordinal)))
                    throw new InvalidDataException($"Extractor '{id}' enables an undeclared operation.");
                manifest = manifest with { Operations = manifest.Operations.Intersect(enabledOperations, StringComparer.Ordinal).ToArray() };
            }
            else if (id == "goodreads" && optionalResources.Length > 0 &&
                !optionalResources.Intersect(approvedResources, StringComparer.Ordinal).Any())
                manifest = manifest with { Operations = manifest.Operations.Where(operation => operation != "cover").ToArray() };

            definitions.Add(new(manifest, ReadFile(Path.Combine(directory, manifest.Entry), 131_072),
                provider.GetValue("Priority", id switch { "wook" => 5, "goodreads" => 10, "amazon" => 2, "isbndb" => 100, _ => 6 }),
                provider.GetValue<int?>("CoverPriority") ?? (id switch { "wook" => 0, "goodreads" => 1, _ => (int?)null }),
                provider.GetValue<bool>("AuthorBiographyEnabled")));
        }
        return definitions;
    }

    private static string ReadFile(string path, int limit)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"Missing or linked extractor file '{Path.GetFileName(path)}'.");
        using var stream = File.OpenRead(path);
        if (stream.Length > limit) throw new InvalidDataException("Extractor file exceeds its size limit.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1) throw new InvalidDataException("Extractor file changed while being read.");
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private static void ValidateOrigins(string[]? declared, string[]? approved, bool required)
    {
        if (declared is null || (required && declared.Length == 0) || declared.Any(origin =>
            !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !ExtractorDefinition.Allows(uri, [origin]) ||
            !StringComparer.Ordinal.Equals(uri.GetLeftPart(UriPartial.Authority), origin) ||
            !(approved ?? []).Contains(origin, StringComparer.Ordinal)))
            throw new InvalidDataException("Extractor declares an invalid or unapproved HTTPS origin.");
    }

}

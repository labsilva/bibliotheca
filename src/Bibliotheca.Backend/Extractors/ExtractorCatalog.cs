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
            if (manifest is null || manifest.Id != id || manifest.ContractVersion != 1 ||
                manifest.Entry != "extract.js" || manifest.Operations is not { Length: > 0 } ||
                manifest.Operations.Any(operation => id == "hardcover"
                    ? operation is not ("editionSearch" or "editionResolve")
                    : operation is not ("metadata" or "cover") &&
                        !(operation == "authorBiography" && id is "wook" or "bertrand" or "goodreads")) ||
                (id == "hardcover" && (manifest.NavigationOrigins is not { Length: 0 } ||
                    manifest.ResourceOrigins is not ["https://api.hardcover.app"])))
                throw new InvalidDataException($"Invalid version 1 manifest for extractor '{id}'.");

            ValidateOrigins(manifest.NavigationOrigins, provider.GetSection("NavigationOrigins").Get<string[]>(), id != "hardcover");
            ValidateOrigins(manifest.ResourceOrigins, provider.GetSection("ResourceOrigins").Get<string[]>(), false);
            definitions.Add(new(manifest, ReadFile(Path.Combine(directory, manifest.Entry), 131_072),
                provider.GetValue("Priority", id switch { "wook" => 5, "goodreads" => 10, "amazon" => 2, "isbndb" => 100, _ => 6 }),
                provider.GetValue<int?>("CoverPriority") ?? (id == "wook" ? 0 : null),
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

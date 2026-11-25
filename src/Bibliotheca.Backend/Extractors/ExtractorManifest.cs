using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Bibliotheca.Backend.Extractors;

internal sealed record ExtractorManifest(string Id, int ContractVersion, string Entry, string[] Operations,
    string[] NavigationOrigins, string[] ResourceOrigins);

internal sealed record ExtractorDefinition(ExtractorManifest Manifest, string Script, int Priority)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };

    public static IReadOnlyList<ExtractorDefinition> Load(IConfiguration configuration, string contentRoot)
    {
        var definitions = new List<ExtractorDefinition>();
        var root = Path.GetFullPath(configuration["Directory"] ?? "extractors", contentRoot);
        foreach (var provider in configuration.GetSection("Providers").GetChildren())
        {
            if (!provider.GetValue<bool>("Enabled")) continue;
            var id = provider.Key;
            if (!Regex.IsMatch(id, @"\A[a-z][a-z0-9-]{0,63}\z"))
                throw new InvalidDataException("Invalid extractor identifier.");

            var directory = Path.Combine(root, id);
            var manifest = JsonSerializer.Deserialize<ExtractorManifest>(
                ReadFile(Path.Combine(directory, "manifest.json"), 16_384), JsonOptions);
            if (manifest is null || manifest.Id != id || manifest.ContractVersion != 1 ||
                manifest.Entry != "extract.js" || manifest.Operations is not { Length: > 0 } ||
                manifest.Operations.Any(operation => operation is not ("metadata" or "cover")))
                throw new InvalidDataException($"Invalid version 1 manifest for extractor '{id}'.");

            ValidateOrigins(manifest.NavigationOrigins, provider.GetSection("NavigationOrigins").Get<string[]>(), true);
            ValidateOrigins(manifest.ResourceOrigins, provider.GetSection("ResourceOrigins").Get<string[]>(), false);
            definitions.Add(new(manifest, ReadFile(Path.Combine(directory, manifest.Entry), 131_072),
                provider.GetValue("Priority", 6)));
        }
        return definitions;
    }

    private static string ReadFile(string path, int limit)
    {
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
            !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !Allows(uri, [origin]) ||
            !StringComparer.Ordinal.Equals(uri.GetLeftPart(UriPartial.Authority), origin) ||
            !(approved ?? []).Contains(origin, StringComparer.Ordinal)))
            throw new InvalidDataException("Extractor declares an invalid or unapproved HTTPS origin.");
    }

    public static bool Allows(Uri uri, IReadOnlyCollection<string> origins) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        uri.HostNameType == UriHostNameType.Dns && !uri.IdnHost.EndsWith('.') &&
        uri.UserInfo.Length == 0 && origins.Contains(uri.GetLeftPart(UriPartial.Authority), StringComparer.Ordinal);
}

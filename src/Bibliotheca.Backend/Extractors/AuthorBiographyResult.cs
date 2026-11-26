using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bibliotheca.Backend.Services;

namespace Bibliotheca.Backend.Extractors;

internal sealed record AuthorBiographyEdition(string? Isbn, string? ExternalId = null);
internal sealed record AuthorBiographyInput(string Name, IReadOnlyList<AuthorBiographyEdition> Editions);
internal sealed record AuthorBiographyPayload(string? Name, string? Biography, int? EditionIndex, string? EditionUrl,
    string? Isbn, bool Ambiguous = false);

internal sealed record AuthorBiographyResult(string? Biography, string? SourceUrl, bool Ambiguous = false)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static AuthorBiographyResult? Parse(string json, ExtractorManifest manifest, AuthorBiographyInput input,
        Uri? source, IReadOnlyDictionary<Uri, byte> visited)
    {
        var payload = JsonSerializer.Deserialize<AuthorBiographyPayload>(json, JsonOptions);
        if (payload is null) return null;
        if (payload.Ambiguous)
        {
            if (payload is { Name: null, Biography: null, EditionIndex: null, EditionUrl: null, Isbn: null })
                return new(null, null, true);
            throw new InvalidDataException("Ambiguous extractor result included biography data.");
        }

        var profilePattern = manifest.Id == "goodreads" ? @"^/author/show/[0-9]+(?:[.\w-]+)?/?$" :
            @"^/autor/[^/]+/[0-9]+(?:/[0-9]+)?/?$";
        if (source is null || !ExtractorDefinition.Allows(source, manifest.NavigationOrigins) ||
            !Regex.IsMatch(source.AbsolutePath, profilePattern) || !visited.ContainsKey(source) ||
            payload.Name is not { Length: > 0 and <= 512 } || Normalize(payload.Name) != Normalize(input.Name) ||
            payload.Biography is not { Length: > 0 and <= 16_384 } || string.IsNullOrWhiteSpace(payload.Biography) ||
            payload.EditionIndex is not int index || (uint)index >= input.Editions.Count ||
            !Uri.TryCreate(payload.EditionUrl, UriKind.Absolute, out var editionUrl) || !visited.ContainsKey(editionUrl) ||
            !IsMatchingEdition(manifest.Id, input.Editions[index], payload.Isbn, editionUrl))
            throw new InvalidDataException("Extractor biography does not match the author or edition.");

        var biography = payload.Biography.Contains('<')
            ? new AngleSharp.Html.Parser.HtmlParser().ParseDocument(payload.Biography).Body?.TextContent
            : System.Net.WebUtility.HtmlDecode(payload.Biography);
        biography = Regex.Replace(biography ?? "", @"\s+", " ").Trim();
        return biography.Length == 0 ? null : new(biography, source.AbsoluteUri);
    }

    private static bool IsMatchingEdition(string provider, AuthorBiographyEdition edition, string? observedIsbn, Uri url)
    {
        if (provider == "goodreads" ? !Regex.IsMatch(url.AbsolutePath, @"^/book/show/[0-9]+(?:[.\w-]+)?/?$") :
            !Regex.IsMatch(url.AbsolutePath, @"^/(?:en/)?(?:livro|ebook|audiolivro)/[^/]+/[0-9]+/?$"))
            return false;

        if (EditionIsbn.Normalize(edition.Isbn) is { } isbn)
            return EditionIsbn.Normalize(observedIsbn) == isbn;

        return provider == "wook" && edition.ExternalId is { } id &&
            url.AbsolutePath.Trim('/').Split('/').Last() == id.Trim('/').Split('/').Last();
    }

    private static string Normalize(string name) =>
        new(name.Normalize(NormalizationForm.FormD).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

internal interface IAuthorBiographyLookup
{
    string Id { get; }
    Task<AuthorBiographyResult?> FindAsync(string name, IReadOnlyList<AuthorBiographyEdition> editions, CancellationToken token);
}

internal sealed class JavaScriptAuthorBiographyLookup(ExtractorDefinition definition, ExtractorRuntime runtime)
    : IAuthorBiographyLookup
{
    public string Id => definition.Manifest.Id;

    public Task<AuthorBiographyResult?> FindAsync(string name, IReadOnlyList<AuthorBiographyEdition> editions, CancellationToken token)
    {
        if (name.Length is 0 or > 512 || !name.Any(char.IsLetterOrDigit)) return Task.FromResult<AuthorBiographyResult?>(null);
        var candidates = editions.Select(edition => new AuthorBiographyEdition(EditionIsbn.Normalize(edition.Isbn),
            Id == "wook" && edition.ExternalId is { Length: > 0 and <= 256 } ? edition.ExternalId : null))
            .Where(edition => edition.Isbn is not null || edition.ExternalId is not null).Distinct().Take(3).ToArray();
        return candidates.Length == 0 ? Task.FromResult<AuthorBiographyResult?>(null) :
            runtime.RunAuthorAsync(definition, new(name, candidates), token);
    }
}

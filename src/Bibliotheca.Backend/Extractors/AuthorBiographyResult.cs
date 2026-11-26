using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bibliotheca.Backend.Services;

namespace Bibliotheca.Backend.Extractors;

internal sealed record AuthorBiographyEdition(string? Isbn, string? ExternalId = null);
internal sealed record AuthorBiographyInput(string Name, IReadOnlyList<AuthorBiographyEdition> Editions);
internal sealed record AuthorBiographyPayload(string? Name, string? Biography, int? EditionIndex, string? EditionUrl,
    string? Isbn, bool Ambiguous = false, string? PhotoUrl = null);

internal sealed record AuthorBiographyResult(string? Biography, string? SourceUrl, bool Ambiguous = false, string? PhotoUrl = null)
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static AuthorBiographyResult? Parse(string json, ExtractorManifest manifest, AuthorBiographyInput input,
        Uri? source, IReadOnlyDictionary<Uri, byte> visited, IReadOnlySet<(Uri Edition, Uri Profile, string Name)> evidence)
    {
        var payload = JsonSerializer.Deserialize<AuthorBiographyPayload>(json, JsonOptions);
        if (payload is null) return null;
        if (payload.Ambiguous)
        {
            if (payload is { Name: null, Biography: null, EditionIndex: null, EditionUrl: null, Isbn: null, PhotoUrl: null })
                return new(null, null, true);
            throw new InvalidDataException("Ambiguous extractor result included biography data.");
        }

        if (source is null || !ExtractorDefinition.Allows(source, manifest.NavigationOrigins) ||
            !SameProfile(manifest.Id, source, source) || !visited.ContainsKey(source) ||
            payload.Name is not { Length: > 0 and <= 512 } || Normalize(payload.Name) != Normalize(input.Name) ||
            payload.Biography is { Length: > 16_384 } ||
            (payload.Biography is null && payload.PhotoUrl is null) ||
            payload.EditionIndex is not int index || (uint)index >= input.Editions.Count ||
            !Uri.TryCreate(payload.EditionUrl, UriKind.Absolute, out var editionUrl) || source == editionUrl || !visited.ContainsKey(editionUrl) ||
            !IsMatchingEdition(manifest.Id, input.Editions[index], payload.Isbn, editionUrl) ||
            manifest.RequireAuthorEvidence && !evidence.Any(link => link.Edition == editionUrl &&
                link.Name == Normalize(input.Name) && SameProfile(manifest.Id, link.Profile, source)))
            throw new InvalidDataException("Extractor biography does not match the author or edition.");

        string? photo = null;
        if (payload.PhotoUrl is { Length: > 0 and <= 2048 } &&
            Uri.TryCreate(source, payload.PhotoUrl, out var photoUri) &&
            ExtractorDefinition.Allows(photoUri, manifest.ResourceOrigins))
            photo = photoUri.AbsoluteUri;

        var biography = payload.Biography?.Contains('<') == true
            ? new AngleSharp.Html.Parser.HtmlParser().ParseDocument(payload.Biography).Body?.TextContent
            : System.Net.WebUtility.HtmlDecode(payload.Biography);
        biography = Regex.Replace(biography ?? "", @"\s+", " ").Trim();
        return biography.Length == 0 && photo is null ? null :
            new(biography.Length == 0 ? null : biography, source.AbsoluteUri, PhotoUrl: photo);
    }

    internal static bool SameProfile(string provider, Uri expected, Uri actual)
    {
        if (expected.GetLeftPart(UriPartial.Authority) != actual.GetLeftPart(UriPartial.Authority)) return false;
        if (provider is not ("wook" or "bertrand" or "goodreads")) return expected == actual;
        var pattern = provider == "goodreads" ? @"^/author/show/([0-9]+)(?:[.\w-]+)?/?$" :
            @"^/autor/[^/]+/([0-9]+)(?:/[0-9]+)?/?$";
        var left = Regex.Match(expected.AbsolutePath, pattern);
        var right = Regex.Match(actual.AbsolutePath, pattern);
        return left.Success && right.Success && left.Groups[1].Value == right.Groups[1].Value;
    }

    private static bool IsMatchingEdition(string provider, AuthorBiographyEdition edition, string? observedIsbn, Uri url)
    {
        if (provider == "goodreads" && !Regex.IsMatch(url.AbsolutePath, @"^/book/show/[0-9]+(?:[.\w-]+)?/?$") ||
            provider is ("wook" or "bertrand") && !Regex.IsMatch(url.AbsolutePath, @"^/(?:en/)?(?:livro|ebook|audiolivro)/[^/]+/[0-9]+/?$"))
            return false;

        if (EditionIsbn.Normalize(edition.Isbn) is { } isbn)
            return EditionIsbn.Normalize(observedIsbn) == isbn;

        return provider == "wook" && edition.ExternalId is { } id &&
            url.AbsolutePath.Trim('/').Split('/').Last() == id.Trim('/').Split('/').Last();
    }

    internal static string Normalize(string name) =>
        new(name.Normalize(NormalizationForm.FormD).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

internal interface IAuthorBiographyLookup
{
    string Id { get; }
    int Priority => 100;
    Task<AuthorBiographyResult?> FindAsync(string name, IReadOnlyList<AuthorBiographyEdition> editions, CancellationToken token);
}

internal sealed class JavaScriptAuthorBiographyLookup(IExtractor extractor)
    : IAuthorBiographyLookup
{
    internal JavaScriptAuthorBiographyLookup(ExtractorDefinition definition, ExtractorRuntime runtime)
        : this(new JavaScriptExtractor(definition, runtime)) { }

    public string Id => extractor.Definition.Manifest.Id;
    public int Priority => extractor.Definition.Priority;

    public Task<AuthorBiographyResult?> FindAsync(string name, IReadOnlyList<AuthorBiographyEdition> editions, CancellationToken token)
    {
        if (name.Length is 0 or > 512 || !name.Any(char.IsLetterOrDigit)) return Task.FromResult<AuthorBiographyResult?>(null);
        var candidates = editions.Select(edition => new AuthorBiographyEdition(EditionIsbn.Normalize(edition.Isbn),
            Id == "wook" && edition.ExternalId is { Length: > 0 and <= 256 } ? edition.ExternalId : null))
            .Where(edition => edition.Isbn is not null || edition.ExternalId is not null).Distinct().Take(3).ToArray();
        return candidates.Length == 0 ? Task.FromResult<AuthorBiographyResult?>(null) :
            extractor.ExtractAuthorAsync(new(name, candidates), token);
    }
}

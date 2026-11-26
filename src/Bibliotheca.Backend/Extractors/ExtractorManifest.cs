namespace Bibliotheca.Backend.Extractors;

internal sealed record ExtractorManifest(string Id, int ContractVersion, string Entry, string[] Operations,
    string[] NavigationOrigins, string[] ResourceOrigins);

internal sealed record ExtractorDefinition(ExtractorManifest Manifest, string Script, int Priority, int? CoverPriority = null,
    bool AuthorBiographyEnabled = false)
{
    public static bool Allows(Uri uri, IReadOnlyCollection<string> origins) =>
        uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        uri.HostNameType == UriHostNameType.Dns && !uri.IdnHost.EndsWith('.') &&
        uri.UserInfo.Length == 0 && origins.Contains(uri.GetLeftPart(UriPartial.Authority), StringComparer.Ordinal);
}

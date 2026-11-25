namespace Bibliotheca.Backend.Fetchers;

internal static class ContributorRoles
{
    internal static bool IsAuthor(string? role) => role?.Trim().Trim('(', ')').Trim().ToLowerInvariant() is
        null or "" or "author" or "co-author" or "coauthor" or "writer" or "adaptor" or "adapter" or
        "adaptation" or "story";
}

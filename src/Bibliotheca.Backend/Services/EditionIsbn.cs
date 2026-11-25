namespace Bibliotheca.Backend.Services;

internal static class EditionIsbn
{
    public static string? Normalize(string? value)
    {
        if (value is null)
            return null;

        var isbn = value.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        if (isbn.Length == 10 && isbn[..9].All(char.IsAsciiDigit) &&
            isbn[^1] == Isbn10CheckDigit(isbn[..9]))
        {
            var prefix = "978" + isbn[..9];
            return prefix + Isbn13CheckDigit(prefix);
        }

        return isbn.Length == 13 && (isbn.StartsWith("978", StringComparison.Ordinal) ||
            isbn.StartsWith("979", StringComparison.Ordinal)) && isbn.All(char.IsAsciiDigit) &&
            isbn[^1] == Isbn13CheckDigit(isbn[..12]) ? isbn : null;
    }

    public static IEnumerable<string> CatalogueVariants(string isbn)
    {
        yield return isbn;

        if (isbn.StartsWith("978", StringComparison.Ordinal))
        {
            var prefix = isbn[3..12];
            yield return prefix + Isbn10CheckDigit(prefix);
        }
    }

    private static char Isbn10CheckDigit(string prefix)
    {
        var sum = prefix.Select((digit, index) => (digit - '0') * (10 - index)).Sum();
        var check = (11 - sum % 11) % 11;
        return check == 10 ? 'X' : (char)('0' + check);
    }

    private static char Isbn13CheckDigit(string prefix)
    {
        var sum = prefix.Select((digit, index) => (digit - '0') * (index % 2 == 0 ? 1 : 3)).Sum();
        return (char)('0' + (10 - sum % 10) % 10);
    }
}

using System.Text;
using System.Text.RegularExpressions;
using NLog;
using NLog.LayoutRenderers;

namespace Bibliotheca.Backend.Services;

// All console targets use this renderer, including framework and HTTP client logs.
[LayoutRenderer("safe-message")]
public sealed partial class SafeLogMessage : LayoutRenderer
{
    protected override void Append(StringBuilder builder, LogEventInfo logEvent)
    {
        builder.Append(Redact(logEvent.FormattedMessage));
        var exception = logEvent.Exception;
        for (var depth = 0; exception is not null && depth < 5; depth++, exception = exception.InnerException)
        {
            builder.Append(depth == 0 ? " | " : " -> ");
            builder.Append(exception.GetType().FullName);
            if (exception is HttpRequestException { StatusCode: { } status })
                builder.Append(" (HTTP ").Append((int)status).Append(')');
            if (exception is Fetchers.PageNavigationException navigation)
            {
                builder.Append(" (HTTP ").Append(navigation.StatusCode).Append(')');
                AppendAddress(builder, navigation.Url);
            }
            if (exception is Microsoft.Playwright.PlaywrightException)
            {
                if (NetworkError().Match(exception.Message) is { Success: true } error)
                    builder.Append(" (").Append(error.Value).Append(')');
                if (HttpAddress().Match(exception.Message) is { Success: true } address)
                    AppendAddress(builder, address.Value);
            }
            if (exception is InvalidDataException && SafeDataErrors.Contains(exception.Message))
                builder.Append(": ").Append(exception.Message);
        }
    }

    private static void AppendAddress(StringBuilder builder, string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return;
        var address = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri;
        builder.Append(" ").Append(address.GetLeftPart(UriPartial.Path));
        if (uri.Query.Length > 0)
            builder.Append(IsbnQuery().IsMatch(uri.Query) ? uri.Query : "?[redacted]");
    }

    private static readonly HashSet<string> SafeDataErrors = new(StringComparer.Ordinal)
    {
        "Wook product metadata could not be recognized.",
        "Wook search results could not be recognized.",
        "Wook did not return a search or product page.",
        "Wook catalogue page is unavailable.",
        "Wook navigation did not return a catalogue page."
    };

    public static string Redact(string message) => Credentials().Replace(
        HeaderValues().Replace(UrlQueries().Replace(message, "$1?[redacted]"), "$1[redacted]"), "$1[redacted]");

    [GeneratedRegex(@"((?:authorization|cookie)[""']?\s*[:=]\s*)(?:""[^""\r\n]*""|'[^'\r\n]*'|[^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex HeaderValues();

    [GeneratedRegex(@"((?:https?://|/)[^\s?""<>]*)\?[^\s""<>]*", RegexOptions.IgnoreCase)]
    private static partial Regex UrlQueries();

    [GeneratedRegex(@"((?:Bearer\s+)|(?:(?:access_token|refresh_token|id_token|code|code_verifier|client_secret|password|authorization|cookie)[""']?\s*[=:]\s*[""']?))[^\s,;""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex Credentials();

    [GeneratedRegex(@"\bnet::ERR_[A-Z0-9_]+\b", RegexOptions.CultureInvariant)]
    private static partial Regex NetworkError();

    [GeneratedRegex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpAddress();

    [GeneratedRegex(@"\A\?keyword=[0-9]{13}\z", RegexOptions.CultureInvariant)]
    private static partial Regex IsbnQuery();
}

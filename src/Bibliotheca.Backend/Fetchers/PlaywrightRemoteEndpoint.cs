using System.Net.Http.Json;

namespace Bibliotheca.Backend.Fetchers;

public static class PlaywrightRemoteEndpoint
{
    public static async Task<string> ResolveAsync(string remote, HttpClient client, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(remote, UriKind.Absolute, out var address))
            throw new InvalidOperationException("Playwright remote endpoint must be an absolute URL.");
        if (address.Scheme is "ws" or "wss") return remote;
        if (address.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("Unsupported Playwright remote endpoint scheme.");

        var endpoint = await client.GetFromJsonAsync<BrowserEndpoint>(address, cancellationToken);
        if (endpoint is null || endpoint.Port is < 1 or > 65535 || string.IsNullOrWhiteSpace(endpoint.WsPath) ||
            !endpoint.WsPath.StartsWith('/') || endpoint.WsPath.StartsWith("//") ||
            endpoint.WsPath.IndexOfAny(['?', '#', '\\']) >= 0)
            throw new InvalidOperationException("The browser service returned an invalid endpoint.");

        // The browser cannot redirect discovery to another host.
        return new UriBuilder(address.Scheme == "https" ? "wss" : "ws", address.Host, endpoint.Port)
            { Path = endpoint.WsPath }.Uri.AbsoluteUri;
    }

    private sealed record BrowserEndpoint(string WsPath, int Port);
}

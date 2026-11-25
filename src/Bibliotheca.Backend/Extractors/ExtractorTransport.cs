using System.Net;
using System.Net.Sockets;

namespace Bibliotheca.Backend.Extractors;

internal sealed record ExtractorResponse(Uri Url, string? MediaType, byte[] Body);

internal sealed class ExtractorTransport
{
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        ConnectCallback = ConnectPublicAsync,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    };

    public static async Task<ExtractorResponse?> GetAsync(HttpClient client, Uri url,
        IReadOnlyCollection<string> origins, int limit, CancellationToken token)
    {
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (!ExtractorDefinition.Allows(url, origins))
                throw new InvalidDataException("Extractor requested an unapproved destination.");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308 &&
                response.Headers.Location is { } location)
            {
                url = new Uri(url, location);
                continue;
            }
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > limit)
                throw new InvalidDataException("Extractor response exceeds its size limit.");

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var body = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, token)) > 0)
            {
                if (body.Length + read > limit)
                    throw new InvalidDataException("Extractor response exceeds its size limit.");
                body.Write(buffer, 0, read);
            }
            return new(url, response.Content.Headers.ContentType?.MediaType, body.ToArray());
        }

        throw new InvalidDataException("Extractor exceeded its redirect limit.");
    }

    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var endpoint = context.DnsEndPoint;
        var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, AddressFamily.InterNetwork, token);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicIPv4(address)))
            throw new InvalidDataException("Extractor DNS resolution returned a prohibited address.");

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(addresses, endpoint.Port, token);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool IsPublicIPv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        var (first, second) = (bytes[0], bytes[1]);
        // Keep the same conservative address policy as the browser egress proxy.
        return !(first is 0 or 10 or 127 or >= 224 ||
            first == 100 && second is >= 64 and <= 127 ||
            first == 169 && second == 254 ||
            first == 172 && second is >= 16 and <= 31 ||
            first == 192 && second is 0 or 168 ||
            first == 198 && second is 18 or 19 or 51 ||
            first == 203 && second == 0);
    }
}

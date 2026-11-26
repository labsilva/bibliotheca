using System.Net;
using System.Net.Sockets;

namespace Bibliotheca.Backend.Extractors;

internal sealed record ExtractorResponse(Uri Url, string? MediaType, byte[] Body, int Status, IReadOnlyDictionary<string, string> Headers);

internal sealed class ExtractorTransport
{
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        UseProxy = false,
        AutomaticDecompression = DecompressionMethods.All,
        ConnectCallback = ConnectPublicAsync,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    };

    public static async Task<ExtractorResponse> SendAsync(HttpClient client, Uri url,
        IReadOnlyCollection<string> origins, int limit, HttpMethod method, byte[]? data,
        IReadOnlyDictionary<string, string>? headers, CancellationToken token, bool sameOriginRedirects = false, bool rejectRedirects = false)
    {
        var originalOrigin = url.GetLeftPart(UriPartial.Authority);
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (!ExtractorDefinition.Allows(url, origins))
                throw new InvalidDataException("Extractor requested an unapproved destination.");

            using var request = new HttpRequestMessage(method, url);
            if (data is not null) request.Content = new ByteArrayContent(data);
            if (headers is not null)
            {
                foreach (var (name, value) in headers)
                {
                    if (name is "content-type") request.Content?.Headers.TryAddWithoutValidation(name, value);
                    else request.Headers.TryAddWithoutValidation(name, value);
                }
            }
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            var status = (int)response.StatusCode;
            if (status is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } location)
            {
                if (rejectRedirects)
                    throw new InvalidDataException("Extractor cover download cannot redirect.");
                var next = new Uri(url, location);
                // A fulfilled browser response keeps the initiating origin.
                if (sameOriginRedirects && originalOrigin != next.GetLeftPart(UriPartial.Authority))
                    throw new InvalidDataException("Extractor browser redirects must stay on the initiating origin.");
                // The browser did not select cookies for the redirect target path.
                headers = null;
                if (status == 303 && method != HttpMethod.Head || status is 301 or 302 && method == HttpMethod.Post)
                {
                    method = HttpMethod.Get;
                    data = null;
                }
                url = next;
                continue;
            }
            if (status is >= 300 and < 400)
                throw new InvalidDataException("Extractor response contains an unsupported redirect.");
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
            var responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, values) in response.Headers.Concat(response.Content.Headers))
            {
                if (name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) && redirects > 0)
                    continue;
                if (ForwardedResponseHeaders.Contains(name))
                    responseHeaders[name] = string.Join(name.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)
                        ? "\n" : ", ", values);
            }
            return new(url, response.Content.Headers.ContentType?.MediaType, body.ToArray(), status, responseHeaders);
        }

        throw new InvalidDataException("Extractor exceeded its redirect limit.");
    }

    private static readonly HashSet<string> ForwardedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type", "Content-Security-Policy", "X-Content-Type-Options", "X-Frame-Options",
        "Access-Control-Allow-Origin", "Access-Control-Allow-Credentials", "Access-Control-Allow-Headers",
        "Access-Control-Allow-Methods", "Access-Control-Expose-Headers", "Set-Cookie"
    };

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

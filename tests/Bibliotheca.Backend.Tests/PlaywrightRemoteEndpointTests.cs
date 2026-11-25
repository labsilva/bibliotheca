using System.Net;
using Bibliotheca.Backend.Fetchers;

namespace Bibliotheca.Backend.Tests;

public class PlaywrightRemoteEndpointTests
{
    [Fact]
    public async Task ExistingWebsocketEndpointsDoNotRequireDiscovery()
    {
        using var client = new HttpClient(new DiscoveryHandler(_ => throw new InvalidOperationException("Unexpected request")));
        Assert.Equal("ws://browser:8081/token", await PlaywrightRemoteEndpoint.ResolveAsync("ws://browser:8081/token", client, default));
    }

    [Fact]
    public async Task DiscoveryUsesItsConfiguredHostAndTheServersRandomPath()
    {
        using var client = new HttpClient(new DiscoveryHandler(request =>
        {
            Assert.Equal("http://browser:8080/endpoint", request.RequestUri!.AbsoluteUri);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"wsPath\":\"/random-browser-token\",\"port\":8081,\"host\":\"untrusted.invalid\"}", System.Text.Encoding.UTF8, "application/json") };
        }));
        Assert.Equal("ws://browser:8081/random-browser-token", await PlaywrightRemoteEndpoint.ResolveAsync("http://browser:8080/endpoint", client, default));
    }

    [Theory]
    [InlineData("//other-host/token", 8081)]
    [InlineData("/token?redirect=other", 8081)]
    [InlineData("/token", 0)]
    public async Task InvalidDiscoveryDoesNotConnect(string path, int port)
    {
        using var client = new HttpClient(new DiscoveryHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { wsPath = path, port }), System.Text.Encoding.UTF8, "application/json")
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => PlaywrightRemoteEndpoint.ResolveAsync("http://browser:8080/endpoint", client, default));
    }

    private sealed class DiscoveryHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}

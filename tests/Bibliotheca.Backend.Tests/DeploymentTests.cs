using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;

namespace Bibliotheca.Backend.Tests;

public partial class HttpApplicationTests
{
    [Fact]
    public async Task ReadinessIsAvailableOnlyToLoopbackProbesAfterDatabaseInitialization()
    {
        var directory = Directory.CreateTempSubdirectory("bibliotheca-health-tests-");
        try
        {
            await using var factory = CreateFactory(directory);
            using var client = CreateClient(factory);
            using var response = await client.GetAsync("/api/health");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var local = await factory.Server.SendAsync(context =>
            {
                context.Request.Scheme = "https";
                context.Request.Path = "/api/health";
                context.Connection.RemoteIpAddress = IPAddress.Loopback;
            });
            Assert.Equal(200, local.Response.StatusCode);
            Assert.Equal("no-store,no-cache", local.Response.Headers.CacheControl.ToString().Replace(" ", ""));
            var remote = await factory.Server.SendAsync(context =>
            {
                context.Request.Scheme = "https";
                context.Request.Path = "/api/health";
                context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.5");
            });
            Assert.Equal(404, remote.Response.StatusCode);
        }
        finally { directory.Delete(true); }
    }
}

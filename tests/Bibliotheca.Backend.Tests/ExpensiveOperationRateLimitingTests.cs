using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Security.Claims;
using Bibliotheca.Backend.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bibliotheca.Backend.Tests;

public sealed class ExpensiveOperationRateLimitingTests
{
    [Theory]
    [InlineData("GET", "/api/external-editions/search", true)]
    [InlineData("PUT", "/api/fetch/isbn", true)]
    [InlineData("POST", "/api/imports/123/retry", true)]
    [InlineData("POST", "/api/catalogue-refresh", true)]
    [InlineData("POST", "/api/author-refresh", true)]
    [InlineData("POST", "/api/author-duplicates/scan", true)]
    [InlineData("GET", "/api/books", false)]
    [InlineData("GET", "/api/imports/123", false)]
    [InlineData("GET", "/api/catalogue-refresh", false)]
    [InlineData("GET", "/api/external-editions/providers", false)]
    public void OnlyExpensiveOperationsAreLimited(string method, string path, bool limited)
    {
        var context = Context("alice", "192.0.2.1");
        context.Request.Method = method;
        context.Request.Path = path;
        Assert.Equal(limited, ExpensiveOperationRateLimiting.IsExpensive(context.Request));
    }

    [Fact]
    public void AccountBudgetFollowsAccountAcrossIpAddresses()
    {
        using var limiter = ExpensiveOperationRateLimiting.CreateLimiter(new()
        { RequestsPerMinutePerAccount = 1 });
        using var first = limiter.AttemptAcquire(Context("alice", "192.0.2.1"));
        using var second = limiter.AttemptAcquire(Context("alice", "192.0.2.2"));
        using var other = limiter.AttemptAcquire(Context("bob", "192.0.2.2"));
        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
        Assert.True(other.IsAcquired);
    }

    [Fact]
    public void IpBudgetCannotBeBypassedByChangingAccountOrForwardedHeader()
    {
        using var limiter = ExpensiveOperationRateLimiting.CreateLimiter(new()
        { RequestsPerMinutePerIp = 1 });
        using var first = limiter.AttemptAcquire(Context("alice", "192.0.2.1"));
        var context = Context("bob", "192.0.2.1");
        context.Request.Headers["X-Forwarded-For"] = "192.0.2.2";
        using var second = limiter.AttemptAcquire(context);
        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
    }

    [Fact]
    public void ConcurrencyPermitIsReturnedAndOrdinaryReadsRemainAvailable()
    {
        using var limiter = ExpensiveOperationRateLimiting.CreateLimiter(new()
        { ConcurrentRequests = 1 });
        var first = limiter.AttemptAcquire(Context("alice", "192.0.2.1"));
        using var blocked = limiter.AttemptAcquire(Context("bob", "192.0.2.2"));
        var read = Context("alice", "192.0.2.1");
        read.Request.Path = "/api/books";
        using var ordinary = limiter.AttemptAcquire(read);
        Assert.False(blocked.IsAcquired);
        Assert.True(ordinary.IsAcquired);
        first.Dispose();
        using var resumed = limiter.AttemptAcquire(Context("bob", "192.0.2.2"));
        Assert.True(resumed.IsAcquired);
    }

    [Fact]
    public void ConfigurationRejectsNonPositiveLimits()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddExpensiveOperationLimits(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ExpensiveOperationLimits:ConcurrentRequests"] = "0" }).Build());
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ExpensiveOperationLimits>>().Value);
    }

    [Fact]
    public async Task HttpRejectionIncludesProblemDetailsAndRetryHint()
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddExpensiveOperationLimits(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            { ["ExpensiveOperationLimits:RequestsPerMinutePerAccount"] = "1" }).Build());
        await using var app = builder.Build();
        app.UseRouting();
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "alice")], "test"));
            await next(context);
        });
        app.UseRateLimiter();
        app.MapGet("/api/external-editions/search", () => "ok");
        await app.StartAsync();
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/external-editions/search")).StatusCode);
        using var rejected = await client.GetAsync("/api/external-editions/search");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.True(rejected.Headers.CacheControl?.NoStore);
        Assert.Contains("operation limit", await rejected.Content.ReadAsStringAsync());
    }
    private static DefaultHttpContext Context(string account, string ip)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/external-editions/search";
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", account)], "test"));
        return context;
    }
}

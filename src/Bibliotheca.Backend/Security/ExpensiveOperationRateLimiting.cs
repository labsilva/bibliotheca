using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Bibliotheca.Backend.Security;

public sealed class ExpensiveOperationLimits
{
    public const string SectionName = "ExpensiveOperationLimits";
    public int RequestsPerMinutePerAccount { get; set; } = 60;
    public int RequestsPerMinutePerIp { get; set; } = 180;
    public int ConcurrentRequestsPerAccount { get; set; } = 2;
    public int ConcurrentRequests { get; set; } = 8;
}

public static class ExpensiveOperationRateLimiting
{
    public static IServiceCollection AddExpensiveOperationLimits(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ExpensiveOperationLimits>()
            .Bind(configuration.GetSection(ExpensiveOperationLimits.SectionName))
            .Validate(o => o.RequestsPerMinutePerAccount > 0 && o.RequestsPerMinutePerIp > 0 &&
                o.ConcurrentRequestsPerAccount > 0 && o.ConcurrentRequests > 0,
                "Expensive operation limits must be positive.")
            .ValidateOnStart();
        services.AddRateLimiter(_ => { });
        services.AddOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>()
            .Configure<IOptions<ExpensiveOperationLimits>>((options, configured) =>
            {
                var limits = configured.Value;
                options.GlobalLimiter = CreateLimiter(limits);
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (rejected, cancellationToken) =>
                {
                    var seconds = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
                        ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds)) : 1;
                    rejected.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                    rejected.HttpContext.Response.Headers.CacheControl = "no-store";
                    await rejected.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests",
                        Detail = "The operation limit was reached. Wait before trying again."
                    }, options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: cancellationToken);
                };
            });
        return services;
    }

    public static PartitionedRateLimiter<HttpContext> CreateLimiter(ExpensiveOperationLimits limits) =>
        PartitionedRateLimiter.CreateChained(
            Window(limits.RequestsPerMinutePerAccount, Account),
            Window(limits.RequestsPerMinutePerIp, context => context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown"),
            Concurrent(limits.ConcurrentRequestsPerAccount, Account),
            Concurrent(limits.ConcurrentRequests, _ => "all"));

    private static string Account(HttpContext context) =>
        context.User.FindFirstValue("sub") ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        context.User.Identity?.Name ?? "anonymous";

    private static PartitionedRateLimiter<HttpContext> Window(int count, Func<HttpContext, string> key) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context => IsExpensive(context.Request)
            ? RateLimitPartition.GetFixedWindowLimiter("limited:" + key(context), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = count, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                AutoReplenishment = true
            })
            : RateLimitPartition.GetNoLimiter("unlimited"));

    private static PartitionedRateLimiter<HttpContext> Concurrent(int count, Func<HttpContext, string> key) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context => IsExpensive(context.Request)
            ? RateLimitPartition.GetConcurrencyLimiter("limited:" + key(context), _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = count, QueueLimit = 0
            })
            : RateLimitPartition.GetNoLimiter("unlimited"));

    public static bool IsExpensive(HttpRequest request)
    {
        var path = request.Path.Value?.TrimEnd('/');
        if (path is null) return false;
        if (path.Equals("/api/external-editions/search", StringComparison.OrdinalIgnoreCase))
            return HttpMethods.IsGet(request.Method);
        if (request.Path.StartsWithSegments("/api/fetch")) return true;
        if (!HttpMethods.IsPost(request.Method)) return false;
        return path.Equals("/api/external-editions/import", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/books/fetch", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/catalogue-refresh", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/author-refresh", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/author-duplicates/scan", StringComparison.OrdinalIgnoreCase) ||
            (request.Path.StartsWithSegments("/api/imports") && path.EndsWith("/retry", StringComparison.OrdinalIgnoreCase));
    }
}

using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Common.Http;

public static class RateLimitPolicies
{
    /// <summary>Credential endpoints: 10 requests/minute per IP.</summary>
    public const string Auth = "auth";

    /// <summary>Writes that create work for staff (submissions, tickets, appeals): 30/minute per user.</summary>
    public const string Submissions = "submissions";

    /// <summary>Public unauthenticated endpoints (landing pages, tracking redirects, postbacks): 120/minute per IP.</summary>
    public const string Public = "public";

    public static IServiceCollection AddAppRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, config) =>
        {
            var enabled = config.GetValue("RateLimiting:Enabled", true);
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.ContentType = "application/problem+json";
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                await context.HttpContext.Response.WriteAsync(
                    "{\"status\":429,\"title\":\"Too many requests. Please wait and try again.\",\"code\":\"rate_limited\"}", ct);
            };

            // Global safety net per client IP.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                !enabled ? RateLimitPartition.GetNoLimiter("off")
                    : RateLimitPartition.GetTokenBucketLimiter(ClientKey(ctx), _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 300, TokensPerPeriod = 300, ReplenishmentPeriod = TimeSpan.FromMinutes(1), QueueLimit = 0,
                    }));

            options.AddPolicy(Auth, ctx => !enabled ? RateLimitPartition.GetNoLimiter("off")
                : RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = config.GetValue("RateLimiting:AuthPerMinute", 10), Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                }));

            options.AddPolicy(Submissions, ctx => !enabled ? RateLimitPartition.GetNoLimiter("off")
                : RateLimitPartition.GetSlidingWindowLimiter(UserKey(ctx), _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 30, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0,
                }));

            options.AddPolicy(Public, ctx => !enabled ? RateLimitPartition.GetNoLimiter("off")
                : RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                }));
        });
        return services;
    }

    private static string ClientKey(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string UserKey(HttpContext ctx) =>
        ctx.User.FindFirst(AppClaims.UserId)?.Value ?? ClientKey(ctx);
}

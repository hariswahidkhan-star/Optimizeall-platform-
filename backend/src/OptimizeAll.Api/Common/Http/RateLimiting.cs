using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Common.Http;

public static class RateLimitPolicies
{
    /// <summary>Credential endpoints: 10 requests/minute per IP.</summary>
    public const string Auth = "auth";

    /// <summary>
    /// Session refresh: 240/minute per IP by default (<c>RateLimiting:RefreshPerMinute</c>). Every page load and tab refreshes
    /// silently, and many people can share one IP (an office behind NAT), so this is generous; refresh tokens are
    /// 256-bit random and rotate with reuse detection, so no credential can be guessed here.
    /// </summary>
    public const string Refresh = "refresh";

    /// <summary>Writes that create work for staff (submissions, tickets, appeals): 30/minute per user.</summary>
    public const string Submissions = "submissions";

    /// <summary>Staff global search (command palette, typed as you go): 60/minute per user.</summary>
    public const string Search = "search";

    /// <summary>Public unauthenticated endpoints (landing pages, tracking redirects, postbacks): 120/minute per IP.</summary>
    public const string Public = "public";

    /// <summary>
    /// Email open pixels, click redirects and one-click unsubscribes: 1,200/minute per IP (mailbox providers fetch pixels
    /// and post unsubscribes for many recipients from a few proxy IPs). Exempt from the global per-IP limiter.
    /// </summary>
    public const string Tracking = "tracking";

    /// <summary>
    /// Signature-verified provider webhooks (ESP events, SMS status/inbound): 6,000/minute per endpoint path, i.e. per
    /// provider and workspace, never per IP (providers send one request per message from shared IPs). Exempt from the
    /// global per-IP limiter so a large send cannot lose bounce/complaint events (and therefore suppressions).
    /// </summary>
    public const string Webhooks = "webhooks";

    /// <summary>
    /// Server-rendered public pages and SEO files (HTML documents, sitemaps, robots.txt, llms.txt): 600/minute per IP by
    /// default (<c>RateLimiting:DocumentsPerMinute</c>). Search engine crawlers fetch many pages from few addresses, and
    /// every visit to the site loads one document, so this sits above the global per-IP limiter it is exempt from.
    /// </summary>
    public const string Documents = "documents";

    /// <summary>Policies whose endpoints bypass the global per-IP limiter (they carry their own, higher limits).</summary>
    private static readonly HashSet<string> HighVolumePolicies = new(StringComparer.Ordinal) { Tracking, Webhooks, Documents };

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

            // Global safety net per client IP (RateLimiting:GlobalPerMinute, default 300).
            var globalPerMinute = GlobalPerMinute(config);
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                !enabled || IsHighVolume(ctx) ? RateLimitPartition.GetNoLimiter("off")
                    : RateLimitPartition.GetTokenBucketLimiter(ClientKey(ctx), _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = globalPerMinute, TokensPerPeriod = globalPerMinute, ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            options.AddPolicy(Auth, ctx => !enabled ? RateLimitPartition.GetNoLimiter("off")
                : RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = config.GetValue("RateLimiting:AuthPerMinute", 10), Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                }));

            options.AddPolicy(Refresh, ctx => !enabled ? RateLimitPartition.GetNoLimiter("off")
                : RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = config.GetValue("RateLimiting:RefreshPerMinute", 240), Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                }));

            options.AddPolicy(Submissions, ctx => !enabled ? RateLimitPartition.GetNoLimiter("off")
                : RateLimitPartition.GetSlidingWindowLimiter(UserKey(ctx), _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 30, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0,
                }));

            options.AddPolicy(Search, ctx => !enabled ? RateLimitPartition.GetNoLimiter("off")
                : RateLimitPartition.GetSlidingWindowLimiter("search:" + UserKey(ctx), _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = config.GetValue("RateLimiting:SearchPerMinute", 60), Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6, QueueLimit = 0,
                }));

            options.AddPolicy(Public, ctx => !enabled ? RateLimitPartition.GetNoLimiter("off")
                : RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                }));

            options.AddPolicy(Tracking, ctx => !enabled ? RateLimitPartition.GetNoLimiter("off")
                : RateLimitPartition.GetFixedWindowLimiter(ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = config.GetValue("RateLimiting:TrackingPerMinute", 1200), Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                }));

            options.AddPolicy(Documents, ctx => !enabled ? RateLimitPartition.GetNoLimiter("off")
                : RateLimitPartition.GetFixedWindowLimiter("documents:" + ClientKey(ctx), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = config.GetValue("RateLimiting:DocumentsPerMinute", 600), Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                }));

            options.AddPolicy(Webhooks, ctx => !enabled ? RateLimitPartition.GetNoLimiter("off")
                : RateLimitPartition.GetFixedWindowLimiter("webhook:" + ctx.Request.Path.Value?.ToLowerInvariant(), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = config.GetValue("RateLimiting:WebhooksPerMinute", 6000), Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
                }));
        });
        return services;
    }

    /// <summary>Global per-IP budget (<c>RateLimiting:GlobalPerMinute</c>, default 300; the e2e harness, where every actor shares 127.0.0.1, raises it).</summary>
    private static int GlobalPerMinute(IConfiguration config) => Math.Max(1, config.GetValue("RateLimiting:GlobalPerMinute", 300));

    private static bool IsHighVolume(HttpContext ctx) =>
        ctx.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName is { } policy && HighVolumePolicies.Contains(policy);

    private static string ClientKey(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string UserKey(HttpContext ctx) =>
        ctx.User.FindFirst(AppClaims.UserId)?.Value ?? ClientKey(ctx);
}

namespace OptimizeAll.Api.Common.Hosting;

public static class SecurityHeaders
{
    /// <summary>Conservative headers for an API that only returns JSON, CSV and private images.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            if (!context.Request.Path.StartsWithSegments("/api/docs"))
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; img-src 'self' data:";
            if (context.Request.Path.StartsWithSegments("/api"))
                headers.CacheControl = "no-store";
            await next();
        });
}

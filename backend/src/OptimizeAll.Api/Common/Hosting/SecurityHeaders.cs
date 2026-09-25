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
            // Server-rendered pages (/_document) get the web app's CSP from the web server (nginx), not the API's.
            if (!context.Request.Path.StartsWithSegments("/api/docs") && !context.Request.Path.StartsWithSegments("/_document"))
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; img-src 'self' data:";
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                headers.CacheControl = "no-store";
                // API responses (JSON, CSV, feeds) are data, never search results. Uploaded images stay indexable (Google
                // Images, the image sitemap); private files are protected by authorization, not by this header.
                if (!context.Request.Path.StartsWithSegments("/api/v1/files")) headers["X-Robots-Tag"] = "noindex";
            }
            await next();
        });
}

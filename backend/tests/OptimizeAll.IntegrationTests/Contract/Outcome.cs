using System.Text.Json;

namespace OptimizeAll.IntegrationTests.ApiContract;

/// <summary>A response read once: status, media type, body and (for JSON) the parsed document and problem <c>code</c>.</summary>
public sealed class Outcome
{
    public required int Status { get; init; }
    public string MediaType { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public JsonElement? Json { get; init; }

    public string? Code => Json is { ValueKind: JsonValueKind.Object } j && j.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String
        ? c.GetString()
        : null;

    public bool IsSuccess => Status is >= 200 and < 300;

    /// <summary>The authentication challenge (token missing or rejected), as opposed to a business 401 with its own code.</summary>
    public bool IsChallenge => Status == 401 && (Body.Length == 0 || Code == "http_401");

    /// <summary>A model-validation / binding failure (400 with an <c>errors</c> map), as opposed to a business 400.</summary>
    public bool IsValidationFailure => Status == 400 && Json is { ValueKind: JsonValueKind.Object } j && j.TryGetProperty("errors", out _);

    /// <summary>A 403 produced by the permission checks (authorization middleware or [RequireAnyPermission]), not by a business rule.</summary>
    public bool IsPermissionDenial => Status == 403 && (Body.Length == 0 || Code is "auth.forbidden");

    public static async Task<Outcome> ReadAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            var media = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            JsonElement? json = null;
            if (media.Contains("json", StringComparison.OrdinalIgnoreCase) && body.Length > 0)
            {
                try { json = JsonSerializer.Deserialize<JsonElement>(body); } catch (JsonException) { }
            }
            return new Outcome { Status = (int)response.StatusCode, MediaType = media, Body = body, Json = json };
        }
    }

    public string Short => $"{Status} {(Code ?? (Body.Length > 160 ? Body[..160] + "…" : Body)).ReplaceLineEndings(" ")}";

    private static readonly string[] StackTraceMarkers =
    {
        "   at ", "Exception:", "System.InvalidOperationException", "System.NullReferenceException", "Microsoft.EntityFrameworkCore",
        "MySqlConnector", "SqliteException", "OptimizeAll.Api.",
    };

    /// <summary>
    /// Endpoints that serve HTML pages, not API resources, so their 4xx is a page, not a problem document. Only the
    /// server-rendered public page (<c>SeoDocumentController</c>, docs/SEO_CRO.md § Rendering): nginx and the Vite
    /// dev/preview server pass its response to browsers and crawlers as is, and a missing page must be a real
    /// <c>404</c>/<c>410</c> HTML page (noindex, helpful links, the app shell) — a JSON problem there would be what
    /// visitors and search engines see. Everything else still applies to them: no 5xx, no internals, and the 4xx must
    /// be a complete HTML document (or the API's usual problem when the request never reached the renderer).
    /// </summary>
    public static readonly IReadOnlySet<string> HtmlDocumentEndpoints = new HashSet<string>(StringComparer.Ordinal)
    {
        "GET _document/{**path}",
        "HEAD _document/{**path}",
    };

    /// <summary>
    /// The contract every response keeps whatever the input: never a 5xx (except a documented 503 "not configured" problem),
    /// never internals in the body, and every 4xx is an RFC 7807 problem with <c>code</c> and <c>traceId</c> (for
    /// <see cref="HtmlDocumentEndpoints"/>: a complete HTML page). Returns the violation, or null.
    /// </summary>
    public string? ContractViolation(string? endpointKey = null)
    {
        // A request that never reached the page renderer (e.g. "/_document/.." collapses to "/") gets the API's usual
        // problem, checked below like any other endpoint's.
        if (endpointKey is not null && HtmlDocumentEndpoints.Contains(endpointKey) && Status is >= 400 and < 500 &&
            !MediaType.Equals("application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            if (!MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase))
                return $"[page] {Status} of an HTML document endpoint is neither an HTML page nor a problem (media '{MediaType}', body '{Short}')";
            // HEAD answers carry no body; a GET must be a whole page.
            if (Body.Length > 0 && !(Body.StartsWith("<!doctype html>", StringComparison.OrdinalIgnoreCase) && Body.Contains("</html>", StringComparison.OrdinalIgnoreCase)))
                return $"[page] {Status} of an HTML document endpoint is not a complete page: {Short}";
            return StackTraceMarkers.FirstOrDefault(m => Body.Contains(m, StringComparison.Ordinal)) is { } leak ? $"[leak] {Status} page exposes internals ({leak})" : null;
        }
        if (Status >= 500 && !(Status == 503 && Code is not null)) return $"[5xx] {Short}";
        if (StackTraceMarkers.Any(m => Body.Contains(m, StringComparison.Ordinal)) && !MediaType.Contains("csv") && !MediaType.StartsWith("text/html"))
        {
            // Business content can legitimately mention these words only in real payloads; errors never may.
            if (Status >= 400)
            {
                var marker = StackTraceMarkers.First(m => Body.Contains(m, StringComparison.Ordinal));
                var at = Math.Max(0, Body.IndexOf(marker, StringComparison.Ordinal) - 60);
                return $"[leak] {Status} body exposes internals: …{Body.Substring(at, Math.Min(160, Body.Length - at)).ReplaceLineEndings(" ")}…";
            }
        }
        if (Status is >= 400 and < 500)
        {
            if (!MediaType.Equals("application/problem+json", StringComparison.OrdinalIgnoreCase))
                return $"[problem] {Status} is not problem+json (media '{MediaType}', body '{Short}')";
            if (Json is not { ValueKind: JsonValueKind.Object } j || Code is null || !j.TryGetProperty("traceId", out var t) || t.ValueKind != JsonValueKind.String)
                return $"[problem] {Status} problem lacks code/traceId: {Short}";
        }
        return null;
    }
}

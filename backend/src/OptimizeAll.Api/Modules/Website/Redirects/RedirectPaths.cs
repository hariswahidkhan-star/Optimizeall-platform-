namespace OptimizeAll.Api.Modules.Website.Redirects;

/// <summary>A parsed request address: the lookup <see cref="Key"/> and the query parameters to carry over to the target.</summary>
public readonly record struct RedirectKey(string Key, string Query);

/// <summary>
/// Normalization of public addresses for redirects (pure, unit-tested). A key is a same-site path, lower-cased, without
/// a trailing slash, query or fragment ("/About-Us/?utm_source=x" → "/about-us"). The service-line filter is the one
/// query parameter that is part of an address: "/services?category=local-seo" keeps it in the key.
/// </summary>
public static class RedirectPaths
{
    public const int MaxLength = 500;

    /// <summary>Public addresses of the kinds of content whose slug changes are redirected automatically.</summary>
    public const string Page = "page", Post = "post", Service = "service", ServiceLine = "service-line", CaseStudy = "case-study",
        Industry = "industry", LandingPage = "landing-page";

    public static string PagePath(string slug) => "/" + slug;
    public static string PostPath(string slug) => "/blog/" + slug;
    public static string ServicePath(string slug) => "/services/" + slug;
    public static string ServiceLinePath(string slug) => "/services?category=" + slug;
    public static string CaseStudyPath(string slug) => "/case-studies/" + slug;
    public static string IndustryPath(string slug) => "/industries/" + slug;
    public static string LandingPath(string clientSlug, string slug) => $"/lp/{clientSlug}/{slug}";

    private const string ServiceLinePrefix = "/services?category=";

    /// <summary>
    /// First path segments of the signed-in portals, the API, sign-in and account emails, short links and static assets.
    /// Nothing there is ever redirected (the gate answers without touching the database).
    /// </summary>
    private static readonly HashSet<string> AppSegments = new(StringComparer.Ordinal)
    {
        "app", "agency", "client", "admin", "finance", "review", "manage", "api", "auth", "login", "register", "check-email",
        "verify-email", "forgot-password", "reset-password", "join", "c", "t", "e", "p", "i", "email", "f", "assets", "health",
        "healthz", "design-system",
    };

    /// <summary>Built-in public pages (frontend features/public/routes.tsx): they always render, so they are never a redirect source.</summary>
    private static readonly HashSet<string> BuiltInPages = new(StringComparer.Ordinal)
    {
        "/", "/services", "/industries", "/case-studies", "/pricing", "/team", "/careers", "/blog", "/contact", "/free-audit",
        "/get-a-quote", "/book-a-consultation", "/newsletter", "/newsletter/confirm", "/newsletter/unsubscribe", "/search", "/faq",
        "/creators", "/lp", "/robots.txt", "/sitemap.xml", "/favicon.ico", "/partners",
    };

    /// <summary>Parses a raw request target ("/path?query#fragment"); null when it is not a safe same-site address.</summary>
    public static RedirectKey? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim();
        if (value.Length > 2000 || !value.StartsWith('/') || value.StartsWith("//", StringComparison.Ordinal)) return null;
        var hash = value.IndexOf('#');
        if (hash >= 0) value = value[..hash];
        var q = value.IndexOf('?');
        var rawPath = q >= 0 ? value[..q] : value;
        var query = q >= 0 ? value[(q + 1)..] : string.Empty;

        var path = NormalizePath(rawPath);
        if (path is null) return null;

        if (path == "/services" && query.Length > 0)
        {
            var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries).ToList();
            var index = parts.FindIndex(p => p.StartsWith("category=", StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                var slug = Decode(parts[index]["category=".Length..], query: true)?.Trim().ToLowerInvariant();
                if (slug is { Length: > 0 and <= 100 } && Accounts.FieldRules.IsSlug(slug))
                {
                    parts.RemoveAt(index);
                    return new RedirectKey(ServiceLinePrefix + slug, string.Join('&', parts));
                }
            }
        }
        return path.Length > MaxLength ? null : new RedirectKey(path, query);
    }

    /// <summary>A normalized path (decoded, lower case, no trailing slash); null when unsafe (control characters, backslashes, "..").</summary>
    public static string? NormalizePath(string rawPath)
    {
        var decoded = Decode(rawPath);
        if (decoded is null || !decoded.StartsWith('/') || decoded.StartsWith("//", StringComparison.Ordinal)) return null;
        if (decoded.Any(c => char.IsControl(c) || c == '\\' || c == '?' || c == '#')) return null;
        var segments = decoded.Split('/');
        if (segments.Any(s => s is "." or "..")) return null;
        var path = decoded.ToLowerInvariant();
        while (path.Length > 1 && path.EndsWith('/')) path = path[..^1];
        return path.Contains("//", StringComparison.Ordinal) ? null : path;
    }

    /// <summary>
    /// True for addresses that are never redirected: the home page and the other built-in public pages, and everything
    /// under the portals, the API, sign-in, short links and assets. Content addresses such as "/services/{slug}" are not.
    /// </summary>
    public static bool IsProtected(string key)
    {
        if (key.StartsWith(ServiceLinePrefix, StringComparison.Ordinal)) return false;
        if (BuiltInPages.Contains(key)) return true;
        var first = key.TrimStart('/').Split('/')[0];
        return AppSegments.Contains(first);
    }

    /// <summary>
    /// The Location for a target, carrying over the request's other query parameters (e.g. UTM tags). Characters outside
    /// printable ASCII are percent-encoded (UTF-8), so the value is always a valid header.
    /// </summary>
    public static string Location(string toPath, string query)
    {
        var location = query.Length == 0 ? toPath : toPath + (toPath.Contains('?') ? "&" : "?") + query;
        if (location.All(c => c is > ' ' and < (char)127)) return location;
        var sb = new System.Text.StringBuilder(location.Length + 16);
        Span<byte> bytes = stackalloc byte[4];
        foreach (var rune in location.EnumerateRunes())
        {
            if (rune.Value is > ' ' and < 127) { sb.Append((char)rune.Value); continue; }
            var n = rune.EncodeToUtf8(bytes);
            for (var i = 0; i < n; i++) sb.Append('%').Append(bytes[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>
    /// A redirect target typed by staff: a same-site path (query allowed, fragment dropped), normalized like a key.
    /// Null when it is not a same-site path.
    /// </summary>
    public static string? NormalizeTarget(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim();
        if (!value.StartsWith('/') || value.StartsWith("//", StringComparison.Ordinal) || value.Any(c => char.IsControl(c) || c == '\\')) return null;
        var hash = value.IndexOf('#');
        if (hash >= 0) value = value[..hash];
        var key = Parse(value);
        if (key is null) return null;
        var target = key.Value.Query.Length == 0 ? key.Value.Key : key.Value.Key + (key.Value.Key.Contains('?') ? "&" : "?") + key.Value.Query;
        return target.Length > MaxLength ? null : target;
    }

    public static bool IsServiceLine(string key, out string slug)
    {
        var ok = key.StartsWith(ServiceLinePrefix, StringComparison.Ordinal);
        slug = ok ? key[ServiceLinePrefix.Length..] : string.Empty;
        return ok;
    }

    private static string? Decode(string value, bool query = false)
    {
        try
        {
            return Uri.UnescapeDataString(query ? value.Replace('+', ' ') : value);
        }
        catch (UriFormatException)
        {
            return null;
        }
    }
}

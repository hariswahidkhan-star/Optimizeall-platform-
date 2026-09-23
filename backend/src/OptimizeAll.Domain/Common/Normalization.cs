using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace OptimizeAll.Domain.Common;

public static partial class Normalization
{
    public static string Email(string email) => email.Trim().ToUpperInvariant();

    /// <summary>Lower-cases a social handle and strips a leading '@' and surrounding whitespace.</summary>
    public static string Handle(string handle) => handle.Trim().TrimStart('@').ToLowerInvariant();

    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "utm_id",
        "igshid", "igsh", "fbclid", "gclid", "si", "s", "t", "ref", "ref_src", "ref_url", "_r", "is_from_webapp", "sender_device",
        "feature", "share_id", "mibextid", "rcm", "trk",
    };

    /// <summary>
    /// Canonical form of a public post URL used for duplicate detection: https scheme, lower-case host
    /// without "www."/"m."/"mobile.", no fragment, no tracking parameters, no trailing slash.
    /// Returns null when the value is not an absolute http(s) URL.
    /// </summary>
    public static string? PostUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;
        if (string.IsNullOrEmpty(uri.Host)) return null;

        var host = uri.Host.ToLowerInvariant();
        foreach (var prefix in new[] { "www.", "m.", "mobile.", "web." })
        {
            if (host.StartsWith(prefix, StringComparison.Ordinal)) { host = host[prefix.Length..]; break; }
        }
        if (host == "twitter.com") host = "x.com";

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length == 0) path = "/";

        var kept = new List<string>();
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = Uri.UnescapeDataString(pair.Split('=', 2)[0]);
            if (!TrackingParams.Contains(key)) kept.Add(pair);
        }
        kept.Sort(StringComparer.Ordinal);

        var sb = new StringBuilder("https://").Append(host).Append(path);
        if (kept.Count > 0) sb.Append('?').Append(string.Join('&', kept));
        return sb.ToString();
    }

    /// <summary>Hash of caption text with whitespace, case and punctuation removed, for repeated-content detection.</summary>
    public static string? ContentHash(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var collapsed = NonWordRegex().Replace(text.ToLowerInvariant(), string.Empty);
        if (collapsed.Length == 0) return null;
        return Sha256Hex(Encoding.UTF8.GetBytes(collapsed));
    }

    public static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static string Sha256Hex(string value) => Sha256Hex(Encoding.UTF8.GetBytes(value));

    [GeneratedRegex(@"[\W_]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonWordRegex();
}

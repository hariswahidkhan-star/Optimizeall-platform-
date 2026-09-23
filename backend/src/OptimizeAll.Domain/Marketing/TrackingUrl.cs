using System.Text;
using System.Text.RegularExpressions;

namespace OptimizeAll.Domain.Marketing;

/// <summary>Pure helpers for tracking links: UTM merging and bot detection.</summary>
public static partial class TrackingUrl
{
    /// <summary>True for an absolute http(s) URL with a host.</summary>
    public static bool IsValidDestination(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        !string.IsNullOrEmpty(uri.Host);

    /// <summary>
    /// Merges parameters into the destination's query string. Existing parameters with the same name
    /// (case-insensitive) are replaced; other parameters and the fragment are preserved; null/empty values are skipped.
    /// </summary>
    public static string MergeQuery(string destination, IEnumerable<KeyValuePair<string, string?>> parameters)
    {
        if (!IsValidDestination(destination))
            throw new ArgumentException("Destination must be an absolute http(s) URL.", nameof(destination));

        var uri = new Uri(destination);
        var toSet = parameters.Where(p => !string.IsNullOrEmpty(p.Value)).ToList();
        var names = new HashSet<string>(toSet.Select(p => p.Key), StringComparer.OrdinalIgnoreCase);

        var kept = new List<string>();
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var rawKey = pair.Split('=', 2)[0];
            string key;
            try { key = Uri.UnescapeDataString(rawKey.Replace('+', ' ')); }
            catch (UriFormatException) { key = rawKey; }
            if (!names.Contains(key)) kept.Add(pair);
        }
        foreach (var (key, value) in toSet)
            kept.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value!));

        var builder = new StringBuilder();
        builder.Append(uri.GetLeftPart(UriPartial.Path));
        if (kept.Count > 0) builder.Append('?').Append(string.Join('&', kept));
        builder.Append(uri.Fragment);
        return builder.ToString();
    }

    /// <summary>Standard UTM parameter set in canonical order.</summary>
    public static IReadOnlyList<KeyValuePair<string, string?>> Utm(
        string source, string medium, string campaign, string? content = null, string? term = null) => new[]
    {
        new KeyValuePair<string, string?>("utm_source", source),
        new KeyValuePair<string, string?>("utm_medium", medium),
        new KeyValuePair<string, string?>("utm_campaign", campaign),
        new KeyValuePair<string, string?>("utm_content", content),
        new KeyValuePair<string, string?>("utm_term", term),
    };

    /// <summary>Heuristic: user agents of crawlers, link previewers and scripts (recorded but excluded from measured metrics).</summary>
    public static bool IsSuspectedBot(string? userAgent) =>
        string.IsNullOrWhiteSpace(userAgent) || BotRegex().IsMatch(userAgent);

    [GeneratedRegex(@"bot|crawler|spider|preview|facebookexternalhit|slackbot|whatsapp|curl|wget|python-requests|headless",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BotRegex();
}

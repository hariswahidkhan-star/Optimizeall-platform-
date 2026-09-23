using System.Text.RegularExpressions;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Submissions;

public enum PostUrlError
{
    None,
    /// <summary>Not an absolute http(s) URL without credentials, or the canonical key would be too long.</summary>
    InvalidUrl,
    /// <summary>The host is not one of the platform's hosts / allow-listed subdomains, or the path is the site root.</summary>
    PlatformMismatch,
}

/// <summary>
/// Result of <see cref="PlatformUrlRules.Parse"/>. <see cref="CanonicalKey"/> identifies the post for duplicate
/// detection (stored in <c>Submission.NormalizedPostUrl</c>, compared case-sensitively).
/// </summary>
public sealed record PostUrlParseResult(PostUrlError Error, string? CanonicalKey, bool IsShortLink)
{
    public bool IsValid => Error == PostUrlError.None;
}

/// <summary>
/// Which hosts a post URL may use for each platform, and the canonical <b>post key</b> that identifies a post
/// regardless of how its URL is written.
///
/// Hosts: a URL must use one of the platform's hosts exactly, or one of that host's explicitly allow-listed
/// subdomains (e.g. <c>www.</c>, <c>m.</c>). A trailing dot on the host is ignored; any other subdomain is rejected.
///
/// Keys: when the post id can be read from the URL, the key is <c>{platform}:{id}</c> (e.g. <c>instagram:Cabc123</c>,
/// <c>youtube:dQw4w9WgXcQ</c>) so that <c>/p/</c> vs <c>/reel/</c>, <c>youtu.be</c> vs <c>watch?v=</c>, user names, tracking
/// parameters and host aliases all collide. Otherwise the key is the normalized URL (canonical host, path without a
/// trailing slash, only identity query parameters). Ids are case-sensitive and kept as written.
/// Short links whose target can't be known offline (vm./vt.tiktok.com, tiktok.com/t/, fb.watch, pin.it, lnkd.in) get a
/// <c>{platform}-short:{code}</c> key (e.g. <c>tiktok-short:ZMabc</c>) and <see cref="PostUrlParseResult.IsShortLink"/> so reviewers resolve them.
/// Pure code: no I/O.
/// </summary>
public static partial class PlatformUrlRules
{
    /// <summary>Maximum length of a canonical key (the NormalizedPostUrl column).</summary>
    public const int MaxKeyLength = 768;

    private sealed record HostRule(string Host, string[] Subdomains, bool IsShortLinkHost = false);

    private static readonly IReadOnlyDictionary<SocialPlatform, HostRule[]> Rules = new Dictionary<SocialPlatform, HostRule[]>
    {
        [SocialPlatform.Instagram] = new[]
        {
            new HostRule("instagram.com", new[] { "www", "m" }),
            new HostRule("instagr.am", new[] { "www" }),
        },
        [SocialPlatform.TikTok] = new[]
        {
            new HostRule("tiktok.com", new[] { "www", "m", "vm", "vt" }),
        },
        [SocialPlatform.X] = new[]
        {
            new HostRule("x.com", new[] { "www", "m", "mobile" }),
            new HostRule("twitter.com", new[] { "www", "m", "mobile" }),
        },
        [SocialPlatform.Facebook] = new[]
        {
            new HostRule("facebook.com", new[] { "www", "m", "mobile", "web", "mbasic" }),
            new HostRule("fb.com", new[] { "www", "m" }),
            new HostRule("fb.watch", new[] { "www" }, IsShortLinkHost: true),
        },
        [SocialPlatform.LinkedIn] = new[]
        {
            new HostRule("linkedin.com", new[] { "www", "m" }),
            new HostRule("lnkd.in", Array.Empty<string>(), IsShortLinkHost: true),
        },
        [SocialPlatform.YouTube] = new[]
        {
            new HostRule("youtube.com", new[] { "www", "m" }),
            new HostRule("youtu.be", new[] { "www" }),
        },
        [SocialPlatform.Threads] = new[]
        {
            new HostRule("threads.net", new[] { "www" }),
            new HostRule("threads.com", new[] { "www" }),
        },
        [SocialPlatform.Pinterest] = new[]
        {
            new HostRule("pinterest.com", new[] { "www", "m" }),
            new HostRule("pin.it", Array.Empty<string>(), IsShortLinkHost: true),
        },
        [SocialPlatform.Snapchat] = new[]
        {
            new HostRule("snapchat.com", new[] { "www", "story", "web" }),
        },
    };

    /// <summary>Query parameters that are part of a post's identity; every other parameter is dropped.</summary>
    private static readonly IReadOnlyDictionary<SocialPlatform, string[]> IdentityParams = new Dictionary<SocialPlatform, string[]>
    {
        [SocialPlatform.YouTube] = new[] { "v" },
        [SocialPlatform.Facebook] = new[] { "story_fbid", "id", "v", "fbid" },
    };

    private static readonly string[] TikTokShortSubdomains = { "vm", "vt" };

    public static IReadOnlyCollection<string> HostsFor(SocialPlatform platform) =>
        Rules.TryGetValue(platform, out var hosts) ? hosts.Select(h => h.Host).ToArray() : Array.Empty<string>();

    /// <summary>
    /// True when <paramref name="host"/> (case-insensitive, trailing dot ignored) is one of the platform's hosts or
    /// an allow-listed subdomain of one. Arbitrary subdomains (e.g. <c>de.instagram.com</c>) are not accepted.
    /// </summary>
    public static bool HostBelongsTo(SocialPlatform platform, string host) => MatchHost(platform, host) is not null;

    /// <summary>Validates an absolute URL for <paramref name="platform"/>: https/http, allowed host and a non-root path.</summary>
    public static bool IsValidPostUrl(SocialPlatform platform, string? url) => Parse(platform, url).IsValid;

    /// <summary>The canonical post key, or null when the URL is not a valid post URL for the platform.</summary>
    public static string? CanonicalKey(SocialPlatform platform, string? url) => Parse(platform, url).CanonicalKey;

    /// <summary>Validates <paramref name="raw"/> for <paramref name="platform"/> and computes its canonical post key.</summary>
    public static PostUrlParseResult Parse(SocialPlatform platform, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri))
            return Fail(PostUrlError.InvalidUrl);
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return Fail(PostUrlError.InvalidUrl);
        if (!string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrEmpty(uri.Host)) return Fail(PostUrlError.InvalidUrl);

        var match = MatchHost(platform, uri.Host);
        if (match is null) return Fail(PostUrlError.PlatformMismatch);
        var (rule, subdomain) = match.Value;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString).ToArray();
        if (segments.Length == 0) return Fail(PostUrlError.PlatformMismatch);
        var query = ParseQuery(uri.Query);

        string? key;
        var shortLink = false;
        if (rule.IsShortLinkHost ||
            (platform == SocialPlatform.TikTok && (TikTokShortSubdomains.Contains(subdomain) || Eq(segments[0], "t"))))
        {
            shortLink = true;
            // tiktok.com/t/{code} and vm./vt.tiktok.com/{code} share one code space.
            var path = platform == SocialPlatform.TikTok && Eq(segments[0], "t") ? segments[1..] : segments;
            if (path.Length == 0) return Fail(PostUrlError.PlatformMismatch);
            key = $"{PlatformPrefix(platform)}-short:{string.Join('/', path)}";
        }
        else
        {
            key = platform switch
            {
                SocialPlatform.Instagram => InstagramKey(segments),
                SocialPlatform.TikTok => TikTokKey(segments),
                SocialPlatform.X => XKey(segments),
                SocialPlatform.YouTube => YouTubeKey(rule.Host, segments, query),
                SocialPlatform.Facebook => FacebookKey(segments, query),
                SocialPlatform.LinkedIn => LinkedInKey(segments),
                SocialPlatform.Threads => ThreadsKey(segments),
                SocialPlatform.Pinterest => PinterestKey(segments),
                SocialPlatform.Snapchat => SnapchatKey(segments),
                _ => null,
            };
            key ??= FallbackKey(platform, rule.Host, uri, query);
        }

        if (key.Length > MaxKeyLength) return Fail(PostUrlError.InvalidUrl);
        return new PostUrlParseResult(PostUrlError.None, key, shortLink);
    }

    // ------------------------------------------------------------------ hosts

    private static (HostRule Rule, string? Subdomain)? MatchHost(SocialPlatform platform, string? host)
    {
        if (host is null) return null;
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 || !Rules.TryGetValue(platform, out var rules)) return null;
        foreach (var rule in rules)
        {
            if (host == rule.Host) return (rule, null);
            foreach (var sub in rule.Subdomains)
                if (host == sub + "." + rule.Host) return (rule, sub);
        }
        return null;
    }

    // ------------------------------------------------------------------ per-platform keys

    private static readonly string[] InstagramTypes = { "p", "reel", "reels", "tv" };

    /// <summary><c>/p|reel|reels|tv/{code}</c>, optionally after a user name segment.</summary>
    private static string? InstagramKey(string[] s)
    {
        for (var i = 0; i <= 1 && i + 1 < s.Length; i++)
            if (InstagramTypes.Any(t => Eq(s[i], t)) && IsToken(s[i + 1]))
                return $"instagram:{s[i + 1]}";
        return null;
    }

    /// <summary><c>/@user/video/{id}</c>, <c>/@user/photo/{id}</c> or <c>/video/{id}</c>.</summary>
    private static string? TikTokKey(string[] s)
    {
        for (var i = 0; i <= 1 && i + 1 < s.Length; i++)
            if ((Eq(s[i], "video") || Eq(s[i], "photo")) && IsDigits(s[i + 1]) && (i == 0 || s[0].StartsWith('@')))
                return $"tiktok:{s[i + 1]}";
        return null;
    }

    /// <summary><c>/{user}/status/{id}</c> (also <c>/i/web/status/{id}</c>, <c>statuses</c>).</summary>
    private static string? XKey(string[] s)
    {
        for (var i = 1; i + 1 < s.Length; i++)
            if ((Eq(s[i], "status") || Eq(s[i], "statuses")) && IsDigits(s[i + 1]))
                return $"x:{s[i + 1]}";
        return null;
    }

    private static string? YouTubeKey(string host, string[] s, IReadOnlyDictionary<string, string> q)
    {
        if (host == "youtu.be") return IsToken(s[0]) ? $"youtube:{s[0]}" : null;
        if (Eq(s[0], "watch") && q.TryGetValue("v", out var v) && IsToken(v)) return $"youtube:{v}";
        if (s.Length >= 2 && (Eq(s[0], "shorts") || Eq(s[0], "live") || Eq(s[0], "embed") || Eq(s[0], "v")) && IsToken(s[1]))
            return $"youtube:{s[1]}";
        return null;
    }

    private static string? FacebookKey(string[] s, IReadOnlyDictionary<string, string> q)
    {
        var first = s[0];
        if ((Eq(first, "story.php") || Eq(first, "permalink.php")) &&
            q.TryGetValue("story_fbid", out var story) && IsToken(story) && q.TryGetValue("id", out var owner) && IsToken(owner))
            return $"facebook:story:{owner}:{story}";
        if ((Eq(first, "watch") || Eq(first, "video.php")) && q.TryGetValue("v", out var v) && IsToken(v))
            return $"facebook:video:{v}";
        if ((Eq(first, "photo.php") || Eq(first, "photo")) && q.TryGetValue("fbid", out var fbid) && IsToken(fbid))
            return $"facebook:photo:{fbid}";
        if (s.Length >= 2 && (Eq(first, "reel") || Eq(first, "reels")) && IsToken(s[1]))
            return $"facebook:reel:{s[1]}";
        for (var i = 0; i + 1 < s.Length; i++)
        {
            if (Eq(s[i], "posts") && IsToken(s[i + 1])) return $"facebook:post:{s[i + 1]}";
            if (Eq(s[i], "videos") && IsToken(s[i + 1])) return $"facebook:video:{s[i + 1]}";
        }
        return null;
    }

    private static string? LinkedInKey(string[] s)
    {
        var path = string.Join('/', s);
        var urn = LinkedInUrnRegex().Match(path);
        if (urn.Success) return $"linkedin:{urn.Groups[1].Value.ToLowerInvariant()}:{urn.Groups[2].Value}";
        if (s.Length >= 2 && Eq(s[0], "posts"))
        {
            var activity = LinkedInActivityRegex().Match(s[1]);
            if (activity.Success) return $"linkedin:activity:{activity.Groups[1].Value}";
        }
        return null;
    }

    /// <summary><c>/@user/post/{code}</c>.</summary>
    private static string? ThreadsKey(string[] s) =>
        s.Length >= 3 && s[0].StartsWith('@') && Eq(s[1], "post") && IsToken(s[2]) ? $"threads:{s[2]}" : null;

    /// <summary><c>/pin/{id}</c>.</summary>
    private static string? PinterestKey(string[] s)
    {
        for (var i = 0; i + 1 < s.Length && i <= 1; i++)
            if (Eq(s[i], "pin") && IsToken(s[i + 1])) return $"pinterest:{s[i + 1]}";
        return null;
    }

    /// <summary><c>/spotlight/{id}</c>.</summary>
    private static string? SnapchatKey(string[] s) =>
        s.Length >= 2 && Eq(s[0], "spotlight") && IsToken(s[1]) ? $"snapchat:spotlight:{s[1]}" : null;

    /// <summary>https + canonical host (subdomain dropped, twitter.com → x.com) + path without trailing slash + identity params.</summary>
    private static string FallbackKey(SocialPlatform platform, string host, Uri uri, IReadOnlyDictionary<string, string> q)
    {
        if (host == "twitter.com") host = "x.com";
        var path = uri.AbsolutePath.TrimEnd('/');
        var keep = IdentityParams.TryGetValue(platform, out var names)
            ? names.Where(q.ContainsKey).OrderBy(n => n, StringComparer.Ordinal)
                .Select(n => $"{n}={Uri.EscapeDataString(q[n])}").ToList()
            : new List<string>();
        return $"https://{host}{path}" + (keep.Count > 0 ? "?" + string.Join('&', keep) : "");
    }

    // ------------------------------------------------------------------ helpers

    private static string PlatformPrefix(SocialPlatform platform) => platform.ToString().ToLowerInvariant();

    /// <summary>First value of each query parameter (names lower-cased).</summary>
    private static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var name = Unescape(parts[0]).ToLowerInvariant();
            var value = parts.Length > 1 ? Unescape(parts[1]) : string.Empty;
            result.TryAdd(name, value);
        }
        return result;
    }

    private static string Unescape(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static bool IsDigits(string value) => value.Length is > 0 and <= 40 && value.All(char.IsAsciiDigit);

    /// <summary>An id token: 1–200 chars of letters, digits, '-' and '_'.</summary>
    private static bool IsToken(string value) =>
        value.Length is > 0 and <= 200 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static PostUrlParseResult Fail(PostUrlError error) => new(error, null, false);

    [GeneratedRegex(@"urn:li:(activity|share|ugcPost):(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkedInUrnRegex();

    [GeneratedRegex(@"-activity-(\d+)(?:-|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LinkedInActivityRegex();
}

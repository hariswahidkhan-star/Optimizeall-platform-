using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Submissions;

/// <summary>
/// Which hosts a post URL may use for each platform. A submission's URL must be on its platform's hosts (or a
/// subdomain of one) and point at a specific post rather than the site root.
/// Works on URLs normalized by <see cref="Normalization.PostUrl"/> (www./m./mobile./web. prefixes already removed,
/// twitter.com mapped to x.com) as well as raw URLs.
/// </summary>
public static class PlatformUrlRules
{
    private static readonly IReadOnlyDictionary<SocialPlatform, string[]> Hosts = new Dictionary<SocialPlatform, string[]>
    {
        [SocialPlatform.Instagram] = new[] { "instagram.com", "instagr.am" },
        [SocialPlatform.TikTok] = new[] { "tiktok.com" },
        [SocialPlatform.X] = new[] { "x.com", "twitter.com" },
        [SocialPlatform.Facebook] = new[] { "facebook.com", "fb.com", "fb.watch" },
        [SocialPlatform.LinkedIn] = new[] { "linkedin.com", "lnkd.in" },
        [SocialPlatform.YouTube] = new[] { "youtube.com", "youtu.be" },
        [SocialPlatform.Threads] = new[] { "threads.net", "threads.com" },
        [SocialPlatform.Pinterest] = new[] { "pinterest.com", "pin.it" },
        [SocialPlatform.Snapchat] = new[] { "snapchat.com" },
    };

    public static IReadOnlyCollection<string> HostsFor(SocialPlatform platform) =>
        Hosts.TryGetValue(platform, out var hosts) ? hosts : Array.Empty<string>();

    /// <summary>True when <paramref name="host"/> is one of the platform's hosts or a subdomain of one.</summary>
    public static bool HostBelongsTo(SocialPlatform platform, string host)
    {
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0) return false;
        return HostsFor(platform).Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));
    }

    /// <summary>Validates an absolute URL for <paramref name="platform"/>: https/http, platform host and a non-root path.</summary>
    public static bool IsValidPostUrl(SocialPlatform platform, string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        if (!HostBelongsTo(platform, uri.Host)) return false;
        return uri.AbsolutePath.Trim('/').Length > 0;
    }
}

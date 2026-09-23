using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Modules.Social;

/// <summary>Pure rules for participant-declared social profiles.</summary>
public static class SocialProfileRules
{
    public const int MaxActiveAccountsPerUser = 10;
    public const int MaxFollowers = 1_000_000_000;

    /// <summary>Earliest plausible profile creation date accepted (before any supported network was public).</summary>
    public static readonly DateTime EarliestAccountCreatedAt = new(2004, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Registrable domains per platform; subdomains (www., m., mobile., …) are accepted.</summary>
    public static readonly IReadOnlyDictionary<SocialPlatform, string[]> PlatformHosts = new Dictionary<SocialPlatform, string[]>
    {
        [SocialPlatform.Instagram] = new[] { "instagram.com" },
        [SocialPlatform.TikTok] = new[] { "tiktok.com" },
        [SocialPlatform.X] = new[] { "x.com", "twitter.com" },
        [SocialPlatform.Facebook] = new[] { "facebook.com", "fb.com" },
        [SocialPlatform.LinkedIn] = new[] { "linkedin.com" },
        [SocialPlatform.YouTube] = new[] { "youtube.com", "youtu.be" },
        [SocialPlatform.Threads] = new[] { "threads.net", "threads.com" },
        [SocialPlatform.Pinterest] = new[] { "pinterest.com" },
        [SocialPlatform.Snapchat] = new[] { "snapchat.com" },
    };

    /// <summary>True when <paramref name="host"/> equals one of the platform's domains or is a subdomain of it.</summary>
    public static bool HostMatchesPlatform(SocialPlatform platform, string host)
    {
        if (!PlatformHosts.TryGetValue(platform, out var domains)) return false;
        var h = host.Trim().TrimEnd('.').ToLowerInvariant();
        return domains.Any(d => h == d || h.EndsWith("." + d, StringComparison.Ordinal));
    }

    /// <summary>Validates a profile URL: absolute https whose host belongs to the platform. Returns an error message or null.</summary>
    public static string? ValidateProfileUrl(SocialPlatform platform, string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return "Enter the full link to your profile, starting with https://.";
        if (uri.Scheme != Uri.UriSchemeHttps)
            return "The profile link must start with https://.";
        if (!string.IsNullOrEmpty(uri.UserInfo))
            return "The profile link must not contain credentials.";
        if (!HostMatchesPlatform(platform, uri.Host))
            return $"This link isn't a {platform} profile. Use a link on {string.Join(" or ", PlatformHosts[platform])}.";
        return null;
    }

    /// <summary>
    /// Platforms whose profile URLs always carry the handle in the path (e.g. instagram.com/handle, tiktok.com/@handle,
    /// youtube.com/@handle). Facebook and LinkedIn profile URLs may use numeric/opaque ids, so they are not checked.
    /// </summary>
    public static readonly IReadOnlySet<SocialPlatform> HandleInUrlPlatforms = new HashSet<SocialPlatform>
    {
        SocialPlatform.Instagram, SocialPlatform.TikTok, SocialPlatform.X, SocialPlatform.Threads,
        SocialPlatform.YouTube, SocialPlatform.Pinterest, SocialPlatform.Snapchat,
    };

    /// <summary>
    /// True when the profile URL's path has a segment equal to the handle (case-insensitive, optional leading '@'),
    /// or when the platform does not put handles in profile URLs. Assumes the URL already passed <see cref="ValidateProfileUrl"/>.
    /// </summary>
    public static bool ProfileUrlMatchesHandle(SocialPlatform platform, string url, string handle)
    {
        if (!HandleInUrlPlatforms.Contains(platform)) return true;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;
        var normalized = Normalization.Handle(handle);
        if (normalized.Length == 0) return false;
        return uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => Uri.UnescapeDataString(segment).Trim().TrimStart('@'))
            .Any(segment => string.Equals(segment, normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Handle rules: 1–100 characters after stripping a leading '@'; no whitespace or URL characters.</summary>
    public static string? ValidateHandle(string? handle)
    {
        var h = (handle ?? string.Empty).Trim().TrimStart('@');
        if (h.Length is < 1 or > 100) return "Enter your handle (1–100 characters).";
        if (h.Any(c => char.IsWhiteSpace(c) || c is '/' or '?' or '#' or '@' or '<' or '>' or '"'))
            return "Enter just the handle, without spaces or a link.";
        return null;
    }

    public static string? ValidateAccountCreatedAt(DateTime value, DateTime nowUtc)
    {
        var utc = value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
        if (utc > nowUtc) return "The creation date can't be in the future.";
        if (utc < EarliestAccountCreatedAt) return "The creation date can't be before 1 January 2004.";
        return null;
    }

    public static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
}

using System.Security.Cryptography;
using System.Text;

namespace OptimizeAll.Domain.EmailMarketing;

/// <summary>Observed performance of one A/B variant.</summary>
public sealed record VariantStats(string Key, int Delivered, int UniqueOpens, int UniqueClicks);

/// <summary>A/B test rules: cohort assignment and winner selection.</summary>
public static class AbTesting
{
    /// <summary>
    /// Deterministic 0–9999 bucket for a subscriber in a campaign (stable across retries and workers, uniformly spread).
    /// </summary>
    public static int Bucket(Guid campaignId, Guid subscriberId)
    {
        Span<byte> input = stackalloc byte[32];
        campaignId.TryWriteBytes(input[..16]);
        subscriberId.TryWriteBytes(input[16..]);
        var hash = SHA256.HashData(input);
        return (int)(BitConverter.ToUInt32(hash, 0) % 10_000);
    }

    /// <summary>
    /// Assigns the subscriber to a test variant (test cohort) or to the held remainder (null). The test cohort is
    /// <paramref name="testPercent"/>% of the audience, split evenly between the variants.
    /// </summary>
    public static string? Assign(Guid campaignId, Guid subscriberId, int testPercent, IReadOnlyList<string> variantKeys)
    {
        if (variantKeys.Count == 0) return null;
        var bucket = Bucket(campaignId, subscriberId);
        var testBuckets = Math.Clamp(testPercent, 1, 100) * 100;
        if (bucket >= testBuckets) return null;
        return variantKeys[bucket % variantKeys.Count];
    }

    public static double Rate(VariantStats v, AbWinnerMetric metric)
    {
        if (v.Delivered <= 0) return 0;
        return metric == AbWinnerMetric.ClickRate ? v.UniqueClicks / (double)v.Delivered : v.UniqueOpens / (double)v.Delivered;
    }

    /// <summary>
    /// The winning variant by open or click rate (machine opens excluded by the caller). Ties are broken by the other
    /// metric, then by variant key order (A before B), so the result is deterministic.
    /// </summary>
    public static string PickWinner(IReadOnlyList<VariantStats> variants, AbWinnerMetric metric)
    {
        if (variants.Count == 0) throw new ArgumentException("No variants.", nameof(variants));
        var other = metric == AbWinnerMetric.OpenRate ? AbWinnerMetric.ClickRate : AbWinnerMetric.OpenRate;
        return variants
            .OrderByDescending(v => Math.Round(Rate(v, metric), 9))
            .ThenByDescending(v => Math.Round(Rate(v, other), 9))
            .ThenBy(v => v.Key, StringComparer.Ordinal)
            .First().Key;
    }
}

/// <summary>Result of classifying an open/click request.</summary>
public sealed record EngagementClassification(bool IsMachine, DeviceType Device, string? MailClient, string? Reason);

/// <summary>
/// Heuristics that separate human engagement from machine traffic: Apple Mail Privacy Protection prefetches every image
/// through Apple proxies (so its "opens" say nothing about the reader), Gmail's image proxy fetches on open (human),
/// and link scanners (Microsoft Safe Links/Defender, Proofpoint, Mimecast, Barracuda, headless browsers) click every
/// link seconds after delivery.
/// </summary>
public static class EngagementHeuristics
{
    private static readonly string[] ScannerMarkers =
    {
        "bot", "crawler", "spider", "preview", "scanner", "safelinks", "safe links", "proofpoint", "mimecast", "barracuda",
        "headlesschrome", "phantomjs", "python-requests", "curl/", "wget", "go-http-client", "java/", "okhttp", "libwww",
        "microsoft office existence discovery", "ms-office", "urlscan", "virustotal", "symantec", "trend micro", "forcepoint",
        "zscaler", "cisco", "fortinet", "sophos", "slackbot", "facebookexternalhit", "linkedinbot", "twitterbot",
    };

    /// <summary>Clicks faster than this after sending are treated as scanner clicks.</summary>
    public static readonly TimeSpan MinimumHumanClickDelay = TimeSpan.FromSeconds(10);

    public static EngagementClassification ClassifyOpen(string? userAgent)
    {
        var ua = userAgent ?? string.Empty;
        var lower = ua.ToLowerInvariant();
        if (lower.Length == 0) return new(true, DeviceType.Unknown, null, "empty user agent");
        if (lower.Contains("googleimageproxy") || lower.Contains("ggpht.com"))
            return new(false, DeviceType.Unknown, "Gmail", null);
        if (lower.Contains("yahoomailproxy")) return new(false, DeviceType.Unknown, "Yahoo Mail", null);
        // Apple MPP fetches with a generic "Mozilla/5.0" UA without a Safari/Mobile token from Apple proxy ranges.
        if (lower == "mozilla/5.0" || (lower.StartsWith("mozilla/5.0") && lower.Contains("applewebkit") && !lower.Contains("safari") && !lower.Contains("mobile/")))
            return new(true, DeviceType.Unknown, "Apple Mail (Privacy Protection)", "Apple Mail Privacy Protection prefetch");
        if (IsScanner(lower)) return new(true, DeviceType.Unknown, null, "automated client");
        return new(false, Device(lower), MailClient(lower), null);
    }

    public static EngagementClassification ClassifyClick(string? userAgent, DateTime? sentAt, DateTime clickedAt)
    {
        var lower = (userAgent ?? string.Empty).ToLowerInvariant();
        if (lower.Length == 0) return new(true, DeviceType.Unknown, null, "empty user agent");
        if (IsScanner(lower)) return new(true, DeviceType.Unknown, null, "link scanner");
        if (sentAt is { } sent && clickedAt - sent < MinimumHumanClickDelay)
            return new(true, Device(lower), MailClient(lower), "clicked within seconds of delivery");
        return new(false, Device(lower), MailClient(lower), null);
    }

    public static bool IsScanner(string lowerUserAgent) => ScannerMarkers.Any(lowerUserAgent.Contains);

    public static DeviceType Device(string lower)
    {
        if (lower.Contains("ipad") || lower.Contains("tablet") || (lower.Contains("android") && !lower.Contains("mobile"))) return DeviceType.Tablet;
        if (lower.Contains("iphone") || lower.Contains("android") || lower.Contains("mobile")) return DeviceType.Mobile;
        if (lower.Contains("windows") || lower.Contains("macintosh") || lower.Contains("x11") || lower.Contains("cros")) return DeviceType.Desktop;
        return DeviceType.Unknown;
    }

    public static string? MailClient(string lower)
    {
        if (lower.Contains("outlook") || lower.Contains("microsoft office") || lower.Contains("ms-office")) return "Outlook";
        if (lower.Contains("thunderbird")) return "Thunderbird";
        if (lower.Contains("gmail")) return "Gmail";
        if (lower.Contains("iphone") || lower.Contains("ipad")) return "Apple Mail (iOS)";
        if (lower.Contains("macintosh") && lower.Contains("applewebkit") && !lower.Contains("chrome") && !lower.Contains("safari")) return "Apple Mail (macOS)";
        if (lower.Contains("android")) return "Android";
        if (lower.Contains("edg/")) return "Edge";
        if (lower.Contains("chrome")) return "Chrome";
        if (lower.Contains("firefox")) return "Firefox";
        if (lower.Contains("safari")) return "Safari";
        return null;
    }
}

/// <summary>Engagement tiers used by list health and re-engagement.</summary>
public enum EngagementTier
{
    /// <summary>Opened or clicked in the last 30 days.</summary>
    Active,
    /// <summary>Last engagement 31–90 days ago.</summary>
    Warm,
    /// <summary>No engagement for more than 90 days (or never, after being sent to).</summary>
    Cold,
    /// <summary>Joined in the last 30 days and not yet engaged.</summary>
    New,
}

public static class EngagementTiers
{
    public static EngagementTier Classify(DateTime nowUtc, DateTime createdAt, DateTime? lastOpenAt, DateTime? lastClickAt)
    {
        var last = new[] { lastOpenAt, lastClickAt }.Where(d => d.HasValue).Select(d => d!.Value).DefaultIfEmpty(DateTime.MinValue).Max();
        if (last >= nowUtc.AddDays(-30)) return EngagementTier.Active;
        if (last >= nowUtc.AddDays(-90)) return EngagementTier.Warm;
        if (createdAt >= nowUtc.AddDays(-30)) return EngagementTier.New;
        return EngagementTier.Cold;
    }
}

/// <summary>Content checks used by the pre-send checklist.</summary>
public static class ContentChecks
{
    /// <summary>Phrases that commonly trigger spam filters (warning only).</summary>
    public static readonly string[] SpamPhrases =
    {
        "100% free", "act now", "apply now", "as seen on", "buy direct", "cash bonus", "cheap", "click below", "click here",
        "congratulations", "dear friend", "double your", "earn extra cash", "eliminate debt", "extra income", "free money",
        "free gift", "get paid", "guarantee", "increase sales", "limited time only", "lowest price", "make money", "miracle",
        "no catch", "no cost", "no credit check", "no obligation", "once in a lifetime", "order now", "risk-free", "risk free",
        "special promotion", "urgent", "winner", "you have been selected", "$$$", "100% satisfied", "best price", "call now",
        "while supplies last", "work from home",
    };

    public static IReadOnlyList<string> FindSpamPhrases(params string?[] texts)
    {
        var joined = string.Join(' ', texts.Where(t => !string.IsNullOrEmpty(t))).ToLowerInvariant();
        return SpamPhrases.Where(p => joined.Contains(p, StringComparison.Ordinal)).Distinct().ToList();
    }

    /// <summary>Subject line warnings: shouting, excessive punctuation, length.</summary>
    public static IReadOnlyList<string> SubjectWarnings(string? subject)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(subject)) return warnings;
        var letters = subject.Where(char.IsLetter).ToList();
        if (letters.Count >= 8 && letters.Count(char.IsUpper) > letters.Count * 0.6) warnings.Add("The subject line is mostly capital letters.");
        if (subject.Count(c => c == '!') > 1) warnings.Add("The subject line has more than one exclamation mark.");
        if (subject.Length > 90) warnings.Add("The subject line is longer than 90 characters and will be truncated on most devices.");
        return warnings;
    }

    /// <summary>Gmail clips messages whose HTML is larger than about 102 KB.</summary>
    public const int GmailClipBytes = 102 * 1024;

    public static int SizeBytes(string html) => Encoding.UTF8.GetByteCount(html);
}

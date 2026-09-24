using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Submissions;

/// <summary>
/// Content-format rules for submissions. The participant may declare the format; when they don't, it is inferred from
/// the post URL where the URL says so unambiguously (e.g. <c>/reel/</c>, <c>/shorts/</c>). A declared format that
/// contradicts an unambiguous URL is refused, so a Short cannot be priced as a long video.
/// </summary>
public static class ContentFormats
{
    /// <summary>The format the URL proves, or null when the URL doesn't say (e.g. an Instagram <c>/p/</c> link).</summary>
    public static ContentFormat? Infer(SocialPlatform platform, string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(s => s.ToLowerInvariant()).ToArray();
        bool Has(string s) => segments.Contains(s);
        var host = uri.Host.ToLowerInvariant();
        return platform switch
        {
            SocialPlatform.Instagram when Has("reel") || Has("reels") => ContentFormat.ShortVideo,
            SocialPlatform.Instagram when Has("stories") => ContentFormat.Story,
            SocialPlatform.Instagram when Has("tv") => ContentFormat.LongVideo,
            SocialPlatform.YouTube when Has("shorts") => ContentFormat.ShortVideo,
            SocialPlatform.YouTube when Has("watch") || Has("live") || host.EndsWith("youtu.be", StringComparison.Ordinal) => ContentFormat.LongVideo,
            SocialPlatform.TikTok => ContentFormat.ShortVideo,
            SocialPlatform.Facebook when Has("reel") || Has("reels") => ContentFormat.ShortVideo,
            SocialPlatform.Facebook when Has("stories") => ContentFormat.Story,
            SocialPlatform.Snapchat when Has("spotlight") => ContentFormat.ShortVideo,
            _ => null,
        };
    }

    /// <summary>
    /// The format stored on the submission: the declared one, else the inferred one. Throws 400
    /// <c>submission.format_mismatch</c> when both exist and differ.
    /// </summary>
    public static ContentFormat? Resolve(SocialPlatform platform, string? url, ContentFormat? declared)
    {
        var inferred = Infer(platform, url);
        if (declared is { } d && inferred is { } i && d != i)
            throw new DomainException("submission.format_mismatch",
                $"This link is a {Describe(i)}, not a {Describe(d)}. Choose the matching format.",
                errors: new Dictionary<string, string[]> { ["format"] = new[] { $"The link is a {Describe(i)}." } });
        return declared ?? inferred;
    }

    public static string Describe(ContentFormat format) => format switch
    {
        ContentFormat.Post => "feed post",
        ContentFormat.Story => "story",
        ContentFormat.ShortVideo => "short video (reel/short)",
        ContentFormat.LongVideo => "long video",
        ContentFormat.Carousel => "carousel",
        _ => format.ToString(),
    };
}

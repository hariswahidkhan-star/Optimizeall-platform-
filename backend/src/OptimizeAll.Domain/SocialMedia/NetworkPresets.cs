namespace OptimizeAll.Domain.SocialMedia;

/// <summary>
/// Validation rules for one network. The Baseline seed writes <see cref="Defaults"/> into <c>sm_network_presets</c>,
/// where staff can adjust them when a network changes its limits; validation always reads the stored preset.
/// </summary>
public sealed record NetworkRules(
    SocialNetwork Network,
    int MaxTextLength,
    int? MaxTitleLength,
    bool RequiresTitle,
    int MaxHashtags,
    int? RecommendedHashtags,
    int MaxMentions,
    int MaxMedia,
    int MaxVideos,
    bool RequiresMedia,
    bool RequiresVideo,
    bool AllowsMixedMedia,
    decimal? MinAspectRatio,
    decimal? MaxAspectRatio,
    int? MinVideoSeconds,
    int? MaxVideoSeconds,
    long? MaxImageBytes,
    int MaxAltTextLength,
    bool SupportsFirstComment,
    LinkHandling LinkHandling,
    int? UrlWeight,
    IReadOnlyList<string> RecommendedTimes,
    string Source)
{
    public static NetworkRules From(SocialNetworkPreset p) => new(
        p.Network, p.MaxTextLength, p.MaxTitleLength, p.Network == SocialNetwork.YouTube, p.MaxHashtags, p.RecommendedHashtags,
        p.MaxMentions, p.MaxMedia, p.MaxVideos, p.RequiresMedia, p.RequiresVideo, p.AllowsMixedMedia, p.MinAspectRatio,
        p.MaxAspectRatio, p.MinVideoSeconds, p.MaxVideoSeconds, p.MaxImageBytes, p.MaxAltTextLength, p.SupportsFirstComment,
        p.LinkHandling, p.Network == SocialNetwork.X ? PostValidator.XUrlWeight : null, p.RecommendedTimes, p.Source);

    public SocialNetworkPreset ToEntity(DateTime now) => new()
    {
        Network = Network,
        MaxTextLength = MaxTextLength,
        MaxTitleLength = MaxTitleLength,
        MaxHashtags = MaxHashtags,
        RecommendedHashtags = RecommendedHashtags,
        MaxMentions = MaxMentions,
        MaxMedia = MaxMedia,
        MaxVideos = MaxVideos,
        RequiresMedia = RequiresMedia,
        RequiresVideo = RequiresVideo,
        AllowsMixedMedia = AllowsMixedMedia,
        MinAspectRatio = MinAspectRatio,
        MaxAspectRatio = MaxAspectRatio,
        MinVideoSeconds = MinVideoSeconds,
        MaxVideoSeconds = MaxVideoSeconds,
        MaxImageBytes = MaxImageBytes,
        MaxAltTextLength = MaxAltTextLength,
        SupportsFirstComment = SupportsFirstComment,
        LinkHandling = LinkHandling,
        RecommendedTimes = RecommendedTimes.ToList(),
        Source = Source,
        UpdatedAt = now,
    };
}

public static class NetworkPresets
{
    public const string TimesSource =
        "General guidance compiled from public industry best-time studies (e.g. Sprout Social, Hootsuite); " +
        "replace it with your own data under Analytics → Best times.";

    /// <summary>Best-practice defaults (limits as documented by each network's help center / API reference).</summary>
    public static readonly IReadOnlyList<NetworkRules> Defaults = new[]
    {
        new NetworkRules(SocialNetwork.Facebook, 63_206, null, false, 30, 3, 50, 10, 1, false, false, false,
            null, null, 1, 14_400, 10 * 1024 * 1024, 1_000, true, LinkHandling.Attachment, null,
            new[] { "Tue 09:00", "Wed 10:00", "Thu 12:00", "Fri 09:00" },
            "Facebook Help Center (post length, photo/video specs); Graph API /{page-id}/feed and /photos"),
        new NetworkRules(SocialNetwork.Instagram, 2_200, null, false, 30, 5, 20, 10, 10, true, false, true,
            0.8m, 1.91m, 3, 900, 8 * 1024 * 1024, 1_000, true, LinkHandling.NotClickable, null,
            new[] { "Tue 11:00", "Wed 12:00", "Thu 10:00", "Fri 11:00" },
            "Instagram Graph API content publishing limits (caption 2,200, 30 hashtags, 20 tags, carousel 10, aspect 4:5–1.91:1)"),
        new NetworkRules(SocialNetwork.X, 280, null, false, 10, 2, 50, 4, 1, false, false, false,
            null, null, 1, 140, 5 * 1024 * 1024, 1_000, true, LinkHandling.InText, PostValidator.XUrlWeight,
            new[] { "Tue 09:00", "Wed 10:00", "Thu 09:00" },
            "X developer docs: counting characters (weighted length 280, URLs count as 23), media (4 images or 1 video)"),
        new NetworkRules(SocialNetwork.LinkedIn, 3_000, null, false, 30, 5, 50, 20, 1, false, false, false,
            0.4167m, 2.4m, 3, 1_800, 8 * 1024 * 1024, 4_086, true, LinkHandling.Attachment, null,
            new[] { "Tue 10:00", "Wed 09:00", "Thu 11:00" },
            "LinkedIn Posts API (commentary 3,000 characters, multi-image up to 20, alt text 4,086)"),
        new NetworkRules(SocialNetwork.TikTok, 2_200, null, false, 30, 5, 20, 35, 1, true, false, false,
            null, null, 3, 600, 20 * 1024 * 1024, 0, false, LinkHandling.NotClickable, null,
            new[] { "Tue 16:00", "Thu 17:00", "Fri 17:00" },
            "TikTok Content Posting API (caption 2,200 UTF-16 units, 1 video or photo mode up to 35 images)"),
        new NetworkRules(SocialNetwork.YouTube, 5_000, 100, true, 60, 3, 50, 1, 1, true, true, false,
            null, null, 1, 43_200, null, 0, false, LinkHandling.InText, null,
            new[] { "Fri 15:00", "Sat 11:00", "Sun 11:00" },
            "YouTube Data API videos.insert (title 100, description 5,000; more than 60 hashtags are all ignored)"),
        new NetworkRules(SocialNetwork.Pinterest, 500, 100, false, 20, 5, 0, 1, 1, true, false, false,
            null, null, 4, 900, 20 * 1024 * 1024, 500, false, LinkHandling.Destination, null,
            new[] { "Fri 20:00", "Sat 20:00", "Sun 14:00" },
            "Pinterest API v5 pins (title 100, description 500, alt text 500)"),
        new NetworkRules(SocialNetwork.GoogleBusiness, 1_500, null, false, 0, 0, 0, 1, 0, false, false, false,
            null, null, null, null, 5 * 1024 * 1024, 0, false, LinkHandling.Destination, null,
            new[] { "Mon 08:00", "Thu 08:00" },
            "Google Business Profile API localPosts (summary 1,500 characters, one photo, call-to-action URL)"),
    };

    public static NetworkRules Default(SocialNetwork network) => Defaults.First(d => d.Network == network);

    /// <summary>Parses "Tue 09:00" into (day, minutes after midnight). Returns null when malformed.</summary>
    public static (DayOfWeek Day, int Minute)? ParseTime(string value)
    {
        var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return null;
        var day = parts[0].ToLowerInvariant() switch
        {
            "mon" => DayOfWeek.Monday,
            "tue" => DayOfWeek.Tuesday,
            "wed" => DayOfWeek.Wednesday,
            "thu" => DayOfWeek.Thursday,
            "fri" => DayOfWeek.Friday,
            "sat" => DayOfWeek.Saturday,
            "sun" => DayOfWeek.Sunday,
            _ => (DayOfWeek?)null,
        };
        if (day is null || !TimeOnly.TryParseExact(parts[1], "HH:mm", out var time)) return null;
        return (day.Value, time.Hour * 60 + time.Minute);
    }
}

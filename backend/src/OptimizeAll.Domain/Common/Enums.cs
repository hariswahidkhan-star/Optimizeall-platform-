namespace OptimizeAll.Domain.Common;

public enum SocialPlatform
{
    Instagram,
    TikTok,
    X,
    Facebook,
    LinkedIn,
    YouTube,
    Threads,
    Pinterest,
    Snapchat,
}

/// <summary>Participant tier used by tier-specific reward rates and segmentation.</summary>
public enum ParticipantTier
{
    Standard,
    Silver,
    Gold,
    Platinum,
}

/// <summary>
/// Kind of content a submission is (rate cards can price formats differently). Nullable on submissions: rows created
/// before formats existed, or where the format is unknown, only match format-agnostic rates.
/// </summary>
public enum ContentFormat
{
    /// <summary>Feed post (single image or text).</summary>
    Post,
    Story,
    /// <summary>Reel, Short or TikTok-style short video.</summary>
    ShortVideo,
    /// <summary>Long-form video (e.g. a YouTube video).</summary>
    LongVideo,
    Carousel,
}

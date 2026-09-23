using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OptimizeAll.Domain.SocialMedia;

public enum IssueSeverity
{
    Error,
    Warning,
}

public sealed record ValidationIssue(SocialNetwork Network, string Field, IssueSeverity Severity, string Code, string Message);

/// <summary>What the validator needs to know about a media item.</summary>
public sealed record MediaInfo(Guid Id, MediaKind Kind, int? Width, int? Height, int? DurationSeconds, long? SizeBytes, bool IsPublic);

/// <summary>The content of one network variant as the validator sees it.</summary>
public sealed record VariantContent(
    SocialNetwork Network,
    string Text,
    string? Title,
    IReadOnlyList<MediaInfo> Media,
    IReadOnlyList<string> AltTexts,
    string? Link,
    string? FirstComment,
    IReadOnlyList<string> Hashtags,
    IReadOnlyList<string> Mentions);

public sealed record VariantValidation(
    SocialNetwork Network, string FinalText, int TextLength, int MaxTextLength, int? TitleLength, int? MaxTitleLength,
    int HashtagCount, int MentionCount, int MediaCount, IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsValid => Issues.All(i => i.Severity != IssueSeverity.Error);
}

/// <summary>
/// Per-network validation of a post variant against the stored preset: text length (X uses its weighted count with
/// URLs as 23), title, hashtags, mentions, media count/type/aspect ratio/size/duration, alt text, first comment and
/// link handling. Pure and deterministic; the API runs it on save, on preview and before scheduling.
/// </summary>
public static partial class PostValidator
{
    public const int XUrlWeight = 23;

    public static VariantValidation Validate(VariantContent v, NetworkRules rules)
    {
        var issues = new List<ValidationIssue>();
        void Error(string field, string code, string message) => issues.Add(new(v.Network, field, IssueSeverity.Error, code, message));
        void Warn(string field, string code, string message) => issues.Add(new(v.Network, field, IssueSeverity.Warning, code, message));

        var network = Label(v.Network);
        var finalText = ComposeText(v.Text, v.Hashtags, v.Link, rules.LinkHandling);
        var length = TextLength(finalText, rules.UrlWeight);

        if (string.IsNullOrWhiteSpace(finalText) && v.Media.Count == 0)
            Error("text", "social.empty", $"{network}: add text or media.");
        if (length > rules.MaxTextLength)
            Error("text", "social.text_too_long", $"{network}: text is {length:N0} characters; the limit is {rules.MaxTextLength:N0}.");

        int? titleLength = v.Title is null ? null : new StringInfo(v.Title).LengthInTextElements;
        if (rules.RequiresTitle && string.IsNullOrWhiteSpace(v.Title))
            Error("title", "social.title_required", $"{network}: a title is required.");
        if (rules.MaxTitleLength is { } maxTitle && titleLength > maxTitle)
            Error("title", "social.title_too_long", $"{network}: the title is {titleLength} characters; the limit is {maxTitle}.");
        if (rules.MaxTitleLength is null && !string.IsNullOrWhiteSpace(v.Title))
            Warn("title", "social.title_ignored", $"{network} posts have no title; it will not be published.");

        var hashtags = CountHashtags(finalText);
        if (hashtags > rules.MaxHashtags)
            Error("hashtags", "social.too_many_hashtags", $"{network}: {hashtags} hashtags; the limit is {rules.MaxHashtags}.");
        else if (rules.RecommendedHashtags is { } rec && hashtags > rec)
            Warn("hashtags", "social.hashtags_above_recommended", $"{network}: {hashtags} hashtags; best practice is at most {rec}.");

        var mentions = CountMentions(finalText, v.Mentions);
        if (mentions > rules.MaxMentions)
            Error("mentions", "social.too_many_mentions", $"{network}: {mentions} mentions/tags; the limit is {rules.MaxMentions}.");

        ValidateMedia(v, rules, network, Error, Warn);

        if (!string.IsNullOrWhiteSpace(v.Link))
        {
            if (!IsHttpUrl(v.Link))
                Error("link", "social.invalid_link", $"{network}: the link must be an absolute http(s) URL.");
            else if (rules.LinkHandling == LinkHandling.NotClickable)
                Warn("link", "social.link_not_clickable", $"Links in {network} captions are not clickable; use the link in bio or a link sticker.");
        }

        if (!string.IsNullOrWhiteSpace(v.FirstComment))
        {
            if (!rules.SupportsFirstComment)
                Error("firstComment", "social.first_comment_unsupported", $"{network} does not support a first comment.");
            else if (TextLength(v.FirstComment, rules.UrlWeight) > rules.MaxTextLength)
                Error("firstComment", "social.first_comment_too_long", $"{network}: the first comment is longer than {rules.MaxTextLength:N0} characters.");
        }

        return new VariantValidation(v.Network, finalText, length, rules.MaxTextLength, titleLength, rules.MaxTitleLength,
            hashtags, mentions, v.Media.Count, issues);
    }

    private static void ValidateMedia(VariantContent v, NetworkRules rules, string network,
        Action<string, string, string> error, Action<string, string, string> warn)
    {
        var media = v.Media;
        var videos = media.Count(m => m.Kind == MediaKind.Video);
        var images = media.Count - videos;

        if (rules.RequiresMedia && media.Count == 0)
            error("media", "social.media_required", $"{network} posts need at least one {(rules.RequiresVideo ? "video" : "image or video")}.");
        if (rules.RequiresVideo && media.Count > 0 && videos == 0)
            error("media", "social.video_required", $"{network} posts need a video.");
        if (media.Count > rules.MaxMedia)
            error("media", "social.too_many_media", $"{network}: {media.Count} media items; the limit is {rules.MaxMedia}.");
        if (videos > rules.MaxVideos)
            error("media", "social.too_many_videos", rules.MaxVideos == 0
                ? $"{network} posts cannot include video."
                : $"{network}: at most {rules.MaxVideos} video(s) per post.");
        if (!rules.AllowsMixedMedia && videos > 0 && images > 0)
            error("media", "social.mixed_media", $"{network} posts cannot mix images and videos.");

        for (var i = 0; i < media.Count; i++)
        {
            var m = media[i];
            var n = i + 1;
            if (m.Kind == MediaKind.Image)
            {
                if (m.Width is > 0 && m.Height is > 0 && (rules.MinAspectRatio is not null || rules.MaxAspectRatio is not null))
                {
                    var ratio = Math.Round((decimal)m.Width.Value / m.Height.Value, 4);
                    if (ratio < rules.MinAspectRatio || ratio > rules.MaxAspectRatio)
                        error($"media[{i}]", "social.aspect_ratio",
                            $"{network}: image {n} has aspect ratio {ratio:0.##}:1; allowed is {rules.MinAspectRatio:0.##}:1 to {rules.MaxAspectRatio:0.##}:1.");
                }
                if (rules.MaxImageBytes is { } maxBytes && m.SizeBytes > maxBytes)
                    error($"media[{i}]", "social.image_too_large", $"{network}: image {n} is larger than {maxBytes / (1024 * 1024)} MB.");
            }
            else
            {
                if (m.DurationSeconds is null)
                    warn($"media[{i}]", "social.video_duration_unknown", $"{network}: the length of video {n} is unknown, so it was not checked.");
                else if (m.DurationSeconds < rules.MinVideoSeconds || m.DurationSeconds > rules.MaxVideoSeconds)
                    error($"media[{i}]", "social.video_duration",
                        $"{network}: video {n} is {m.DurationSeconds}s; allowed is {rules.MinVideoSeconds}–{rules.MaxVideoSeconds}s.");
            }

            var alt = i < v.AltTexts.Count ? v.AltTexts[i] : null;
            if (!string.IsNullOrWhiteSpace(alt))
            {
                if (rules.MaxAltTextLength == 0)
                    warn($"altTexts[{i}]", "social.alt_text_unsupported", $"{network} does not accept alt text through its API; it will not be sent.");
                else if (alt.Length > rules.MaxAltTextLength)
                    error($"altTexts[{i}]", "social.alt_text_too_long", $"{network}: alt text {n} is longer than {rules.MaxAltTextLength:N0} characters.");
            }
            else if (m.Kind == MediaKind.Image && rules.MaxAltTextLength > 0)
            {
                warn($"altTexts[{i}]", "social.alt_text_missing", $"{network}: image {n} has no alt text (needed by screen-reader users).");
            }
        }
    }

    /// <summary>The text as published: body, then hashtags not already in it, then the link when the network puts links in the text.</summary>
    public static string ComposeText(string text, IReadOnlyList<string> hashtags, string? link, LinkHandling linkHandling)
    {
        var sb = new StringBuilder(text.TrimEnd());
        var existing = new HashSet<string>(HashtagRegex().Matches(text).Select(m => m.Value.ToLowerInvariant()));
        var extra = hashtags.Select(NormalizeHashtag).Where(h => h.Length > 1 && existing.Add(h.ToLowerInvariant())).ToList();
        if (extra.Count > 0)
        {
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(string.Join(' ', extra));
        }
        if (linkHandling == LinkHandling.InText && !string.IsNullOrWhiteSpace(link) && !text.Contains(link, StringComparison.Ordinal))
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(link.Trim());
        }
        return sb.ToString();
    }

    public static string NormalizeHashtag(string tag)
    {
        var t = tag.Trim().TrimStart('#');
        t = new string(t.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        return "#" + t;
    }

    /// <summary>
    /// Length as the network counts it. With a URL weight (X), every URL counts as that many characters and other text
    /// uses X's weighted count: code points in the Latin/General-punctuation ranges count 1, everything else (CJK, emoji) 2,
    /// an emoji sequence counts once. Without a URL weight the count is in user-perceived characters (text elements).
    /// </summary>
    public static int TextLength(string text, int? urlWeight)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        if (urlWeight is null) return new StringInfo(text).LengthInTextElements;

        var total = 0;
        var last = 0;
        foreach (Match url in UrlRegex().Matches(text))
        {
            total += WeightedLength(text[last..url.Index]);
            total += urlWeight.Value;
            last = url.Index + url.Length;
        }
        total += WeightedLength(text[last..]);
        return total;
    }

    private static int WeightedLength(string segment)
    {
        var total = 0;
        var e = StringInfo.GetTextElementEnumerator(segment);
        while (e.MoveNext())
        {
            var element = (string)e.Current;
            var runes = element.EnumerateRunes().ToList();
            if (runes.Count > 1 && runes.Any(IsEmojiRune))
            {
                total += 2;
                continue;
            }
            foreach (var r in runes) total += IsLightRune(r.Value) ? 1 : 2;
        }
        return total;
    }

    private static bool IsLightRune(int cp) =>
        cp is >= 0 and <= 4351 or >= 8192 and <= 8205 or >= 8208 and <= 8223 or >= 8242 and <= 8247;

    private static bool IsEmojiRune(Rune r) => r.Value >= 0x1F000 || r.Value is 0x200D or 0xFE0F || r.Value is >= 0x2600 and <= 0x27BF;

    public static int CountHashtags(string text) => HashtagRegex().Matches(text).Count;

    public static int CountMentions(string text, IReadOnlyList<string> tagged)
    {
        var handles = new HashSet<string>(MentionRegex().Matches(text).Select(m => m.Groups[1].Value.ToLowerInvariant()));
        foreach (var t in tagged)
        {
            var h = t.Trim().TrimStart('@').ToLowerInvariant();
            if (h.Length > 0) handles.Add(h);
        }
        return handles.Count;
    }

    public static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && !string.IsNullOrEmpty(uri.Host);

    public static string Label(SocialNetwork network) => network switch
    {
        SocialNetwork.GoogleBusiness => "Google Business Profile",
        SocialNetwork.X => "X",
        _ => network.ToString(),
    };

    [GeneratedRegex(@"(?<![\w&])#[\p{L}\p{N}_]+", RegexOptions.CultureInvariant)]
    private static partial Regex HashtagRegex();

    [GeneratedRegex(@"(?<![\w@])@([A-Za-z0-9_.]{1,30})", RegexOptions.CultureInvariant)]
    private static partial Regex MentionRegex();

    [GeneratedRegex(@"https?://[^\s]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}

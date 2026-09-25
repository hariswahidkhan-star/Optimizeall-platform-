using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using OptimizeAll.Api.Modules.Website.Settings;
using OptimizeAll.Api.Modules.Website.SiteSeo;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Api.Modules.Website.Pages;

/// <summary>Block types a CMS page can contain. Each has its own payload schema, validated on save.</summary>
public static class PageBlockTypes
{
    public const string Hero = "hero";
    public const string RichText = "richText";
    public const string FeaturesGrid = "featuresGrid";
    public const string Stats = "stats";
    public const string Cta = "cta";
    public const string Faq = "faq";
    public const string Testimonials = "testimonials";
    public const string LogoCloud = "logoCloud";
    public const string ServicesGrid = "servicesGrid";
    public const string CaseStudyHighlight = "caseStudyHighlight";
    public const string Video = "video";

    public static readonly string[] All =
        { Hero, RichText, FeaturesGrid, Stats, Cta, Faq, Testimonials, LogoCloud, ServicesGrid, CaseStudyHighlight, Video };
}

/// <summary>
/// A self-hosted video (docs/SEO_CRO.md § Video): MP4 and/or WebM files, a poster image and a WebVTT captions track,
/// served from the site's <c>/media/videos/</c> folder (frontend/public/media/videos), uploads or an allowed https host.
/// Rendered as an accessible, lazy <c>&lt;video&gt;</c> player and described by a VideoObject + the video sitemap.
/// </summary>
public sealed record VideoBlock(
    string Title, string? Description, string? Mp4Url, string? WebmUrl, string? PosterUrl, string? CaptionsUrl, string? CaptionsLanguage,
    int? DurationSeconds, DateOnly? UploadDate, string? Transcript);

public sealed record HeroBlock(string? Eyebrow, string Title, string? Subtitle, SiteLink? PrimaryCta, SiteLink? SecondaryCta, string? ImageUrl);

public sealed record RichTextBlock(string Markdown);

public sealed record FeatureItem(string Title, string Text, string? Icon);

public sealed record FeaturesGridBlock(string? Title, string? Intro, IReadOnlyList<FeatureItem> Items);

public sealed record StatItem(string Label, string Value, MetricMeasurement Measurement, string? Context);

public sealed record StatsBlock(string? Title, IReadOnlyList<StatItem> Items);

public sealed record CtaBlock(string Title, string? Text, SiteLink Primary, SiteLink? Secondary);

public sealed record FaqBlock(string? Title, IReadOnlyList<FaqEntry> Items);

/// <summary>Empty <see cref="TestimonialIds"/> shows the featured testimonials.</summary>
public sealed record TestimonialsBlock(string? Title, IReadOnlyList<Guid> TestimonialIds);

/// <summary>Empty <see cref="Logos"/> shows the site's trust logos.</summary>
public sealed record LogoCloudBlock(string? Title, IReadOnlyList<TrustLogo> Logos);

/// <summary>Null <see cref="CategorySlug"/> shows every service line.</summary>
public sealed record ServicesGridBlock(string? Title, string? Intro, string? CategorySlug);

public sealed record CaseStudyHighlightBlock(string? Title, string CaseStudySlug);

/// <summary>A block as stored and returned: <c>data</c> holds the type's normalized payload.</summary>
public sealed record PageBlock(string Id, string Type, JsonElement Data);

public sealed class PageBlockInput
{
    [MaxLength(40)]
    public string? Id { get; set; }

    [Required, MaxLength(40)]
    public string Type { get; set; } = string.Empty;

    public JsonElement Data { get; set; }
}

/// <summary>Validates block payloads per type and normalizes them (trimmed strings, sanitized Markdown, checked links and images).</summary>
public sealed class PageBlockValidator(WebsiteRules rules)
{
    public const int MaxBlocks = 40;

    public List<PageBlock> Validate(IReadOnlyList<PageBlockInput>? blocks, FieldErrors errors)
    {
        var result = new List<PageBlock>();
        var input = blocks ?? Array.Empty<PageBlockInput>();
        if (input.Count > MaxBlocks) errors.Add("blocks", $"A page can have at most {MaxBlocks} blocks.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < input.Count; i++)
        {
            var f = $"blocks[{i}]";
            var block = input[i];
            var id = WebsiteRules.Clean(block.Id) ?? Guid.NewGuid().ToString("N")[..12];
            if (!ids.Add(id)) id = Guid.NewGuid().ToString("N")[..12];
            if (!PageBlockTypes.All.Contains(block.Type))
            {
                errors.Add(f + ".type", "Unknown block type.");
                continue;
            }
            object? normalized;
            try
            {
                normalized = Normalize(block.Type, block.Data, f, errors);
            }
            catch (JsonException)
            {
                errors.Add(f + ".data", "The block content is not in the expected format.");
                continue;
            }
            if (normalized is null) continue;
            result.Add(new PageBlock(id, block.Type, JsonSerializer.SerializeToElement(normalized, normalized.GetType(), SiteSettingsService.Json)));
        }
        return result;
    }

    private object? Normalize(string type, JsonElement data, string f, FieldErrors e)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            e.Add(f + ".data", "The block content is missing.");
            return null;
        }
        T Read<T>() => data.Deserialize<T>(SiteSettingsService.Json) ?? throw new JsonException();

        string Req(string? v, string field, int max)
        {
            var c = WebsiteRules.Clean(v);
            if (c is null) e.Add($"{f}.data.{field}", "Required.");
            else if (c.Length > max) e.Add($"{f}.data.{field}", $"At most {max} characters.");
            return c ?? string.Empty;
        }
        string? Opt(string? v, string field, int max)
        {
            var c = WebsiteRules.Clean(v);
            if (c is not null && c.Length > max) e.Add($"{f}.data.{field}", $"At most {max} characters.");
            return c;
        }
        SiteLink? Link(SiteLink? l, string field, bool required)
        {
            if (l is null || (WebsiteRules.Clean(l.Label) is null && WebsiteRules.Clean(l.Url) is null))
            {
                if (required) e.Add($"{f}.data.{field}", "Add a button label and link.");
                return null;
            }
            var url = WebsiteRules.Link(l.Url, $"{f}.data.{field}.url", e);
            if (url is null) e.Add($"{f}.data.{field}.url", "Add the link.");
            return new SiteLink(Req(l.Label, field + ".label", 60), url ?? string.Empty);
        }

        switch (type)
        {
            case PageBlockTypes.Hero:
            {
                var b = Read<HeroBlock>();
                return new HeroBlock(Opt(b.Eyebrow, "eyebrow", 60), Req(b.Title, "title", 150), Opt(b.Subtitle, "subtitle", 400),
                    Link(b.PrimaryCta, "primaryCta", false), Link(b.SecondaryCta, "secondaryCta", false),
                    rules.Image(b.ImageUrl, $"{f}.data.imageUrl", e));
            }
            case PageBlockTypes.RichText:
            {
                var b = Read<RichTextBlock>();
                var md = WebsiteRules.Markdown(b.Markdown, $"{f}.data.markdown", e, 60000);
                if (md is null) e.Add($"{f}.data.markdown", "Required.");
                return new RichTextBlock(md ?? string.Empty);
            }
            case PageBlockTypes.FeaturesGrid:
            {
                var b = Read<FeaturesGridBlock>();
                var items = (b.Items ?? Array.Empty<FeatureItem>()).Select((it, i) =>
                    new FeatureItem(Req(it.Title, $"items[{i}].title", 100), Req(it.Text, $"items[{i}].text", 500), Opt(it.Icon, $"items[{i}].icon", 40))).ToList();
                if (items.Count is 0 or > 12) e.Add($"{f}.data.items", "Add between 1 and 12 features.");
                return new FeaturesGridBlock(Opt(b.Title, "title", 150), Opt(b.Intro, "intro", 400), items);
            }
            case PageBlockTypes.Stats:
            {
                var b = Read<StatsBlockInput>();
                var items = (b.Items ?? new List<StatItemInput>()).Select((it, i) =>
                {
                    if (it.Measurement is not { } m || !Enum.IsDefined(m)) e.Add($"{f}.data.items[{i}].measurement", "Say whether the figure is measured or estimated.");
                    return new StatItem(Req(it.Label, $"items[{i}].label", 80), Req(it.Value, $"items[{i}].value", 20),
                        it.Measurement ?? MetricMeasurement.Estimated, Opt(it.Context, $"items[{i}].context", 160));
                }).ToList();
                if (items.Count is 0 or > 8) e.Add($"{f}.data.items", "Add between 1 and 8 stats.");
                return new StatsBlock(Opt(b.Title, "title", 150), items);
            }
            case PageBlockTypes.Cta:
            {
                var b = Read<CtaBlock>();
                return new CtaBlock(Req(b.Title, "title", 150), Opt(b.Text, "text", 400), Link(b.Primary, "primary", true)!, Link(b.Secondary, "secondary", false));
            }
            case PageBlockTypes.Faq:
            {
                var b = Read<FaqBlockInput>();
                var items = WebsiteRules.Faqs(b.Items, $"{f}.data.items", e);
                if (items.Count == 0) e.Add($"{f}.data.items", "Add at least one question.");
                return new FaqBlock(Opt(b.Title, "title", 150), items);
            }
            case PageBlockTypes.Testimonials:
            {
                var b = Read<TestimonialsBlock>();
                var ids = (b.TestimonialIds ?? Array.Empty<Guid>()).Distinct().ToList();
                if (ids.Count > 12) e.Add($"{f}.data.testimonialIds", "Pick at most 12 testimonials.");
                return new TestimonialsBlock(Opt(b.Title, "title", 150), ids);
            }
            case PageBlockTypes.LogoCloud:
            {
                var b = Read<LogoCloudBlock>();
                var logos = (b.Logos ?? Array.Empty<TrustLogo>()).Select((l, i) =>
                {
                    var img = rules.Image(l.ImageUrl, $"{f}.data.logos[{i}].imageUrl", e);
                    if (img is null) e.Add($"{f}.data.logos[{i}].imageUrl", "Upload the logo image.");
                    return new TrustLogo(Req(l.Name, $"logos[{i}].name", 80), img ?? string.Empty, WebsiteRules.Link(l.Url, $"{f}.data.logos[{i}].url", e));
                }).ToList();
                if (logos.Count > 24) e.Add($"{f}.data.logos", "Use at most 24 logos.");
                return new LogoCloudBlock(Opt(b.Title, "title", 150), logos);
            }
            case PageBlockTypes.ServicesGrid:
            {
                var b = Read<ServicesGridBlock>();
                var slug = WebsiteRules.Clean(b.CategorySlug);
                if (slug is not null) WebsiteRules.Slug(slug, $"{f}.data.categorySlug", e);
                return new ServicesGridBlock(Opt(b.Title, "title", 150), Opt(b.Intro, "intro", 400), slug);
            }
            case PageBlockTypes.CaseStudyHighlight:
            {
                var b = Read<CaseStudyHighlightBlock>();
                return new CaseStudyHighlightBlock(Opt(b.Title, "title", 150), WebsiteRules.Slug(b.CaseStudySlug, $"{f}.data.caseStudySlug", e));
            }
            case PageBlockTypes.Video:
            {
                var b = Read<VideoBlock>();
                string? Media(string? value, string field, string kind)
                {
                    var v = WebsiteRules.Clean(value);
                    if (v is null) return null;
                    if (v.StartsWith("/media/", StringComparison.Ordinal))
                    {
                        if (!SiteMedia.IsSiteMediaPath(v, kind)) e.Add($"{f}.data.{field}", SiteMedia.Message(kind));
                        return v;
                    }
                    return rules.Image(v, $"{f}.data.{field}", e);
                }
                var mp4 = Media(b.Mp4Url, "mp4Url", "mp4");
                var webm = Media(b.WebmUrl, "webmUrl", "webm");
                if (mp4 is null && webm is null) e.Add($"{f}.data.mp4Url", "Add the MP4 (or WebM) file of the video.");
                var poster = Media(b.PosterUrl, "posterUrl", "image");
                if (poster is null) e.Add($"{f}.data.posterUrl", "Add a poster image: it is shown before the video plays and used by search engines.");
                var captions = Media(b.CaptionsUrl, "captionsUrl", "vtt");
                if (captions is null) e.Add($"{f}.data.captionsUrl", "Add a WebVTT captions file (.vtt): videos need captions for viewers who can't hear them.");
                var language = Opt(b.CaptionsLanguage, "captionsLanguage", 10) ?? "en";
                if (!System.Text.RegularExpressions.Regex.IsMatch(language, "^[a-z]{2,3}(-[A-Z]{2})?$"))
                    e.Add($"{f}.data.captionsLanguage", "Use a language code such as en or en-GB.");
                if (b.DurationSeconds is { } d && (d < 1 || d > 36000)) e.Add($"{f}.data.durationSeconds", "Enter the length in seconds.");
                return new VideoBlock(Req(b.Title, "title", 150), Opt(b.Description, "description", 500), mp4, webm, poster, captions, language,
                    b.DurationSeconds, b.UploadDate, WebsiteRules.Markdown(b.Transcript, $"{f}.data.transcript", e, 20000));
            }
            default:
                return null;
        }
    }

    private sealed record FaqBlockInput(string? Title, List<FaqEntryInput>? Items);

    private sealed record StatItemInput(string? Label, string? Value, MetricMeasurement? Measurement, string? Context);

    private sealed record StatsBlockInput(string? Title, List<StatItemInput>? Items);

    public static List<PageBlock> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<PageBlock>();
        try
        {
            return JsonSerializer.Deserialize<List<PageBlock>>(json, SiteSettingsService.Json) ?? new List<PageBlock>();
        }
        catch (JsonException)
        {
            return new List<PageBlock>();
        }
    }

    public static string Serialize(IEnumerable<PageBlock> blocks) => JsonSerializer.Serialize(blocks, SiteSettingsService.Json);

    /// <summary>Builds a stored block from a typed payload (seeders and tests).</summary>
    public static PageBlock Block(string type, object data, string? id = null) =>
        new(id ?? Guid.NewGuid().ToString("N")[..12], type, JsonSerializer.SerializeToElement(data, data.GetType(), SiteSettingsService.Json));
}

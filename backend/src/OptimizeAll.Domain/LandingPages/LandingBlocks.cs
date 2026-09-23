using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OptimizeAll.Domain.LandingPages;

#pragma warning disable CS1591
public sealed class HeroProps
{
    public string Headline { get; set; } = string.Empty;
    public string? Subheadline { get; set; }
    public string? CtaLabel { get; set; }
    public string? CtaHref { get; set; }
    public string? ImageUrl { get; set; }
    public string? ImageAlt { get; set; }
    public string Align { get; set; } = "center";
    public string Theme { get; set; } = "brand";
}

public sealed class TextProps
{
    public string? Heading { get; set; }
    public string Body { get; set; } = string.Empty;
}

public sealed class ImageProps
{
    public string Url { get; set; } = string.Empty;
    public string? Alt { get; set; }
    public bool Decorative { get; set; }
    public string? Caption { get; set; }
    public string? LinkHref { get; set; }
}

public sealed class VideoProps
{
    /// <summary>A YouTube or Vimeo page/embed URL; normalized into <see cref="Provider"/> + <see cref="VideoId"/>.</summary>
    public string? Url { get; set; }
    public string? Provider { get; set; }
    public string? VideoId { get; set; }
    public string Title { get; set; } = string.Empty;
}

public sealed class FeatureItem
{
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? Icon { get; set; }
}

public sealed class FeaturesProps
{
    public string? Heading { get; set; }
    public string? Intro { get; set; }
    public List<FeatureItem> Items { get; set; } = new();
}

public sealed class TestimonialItem
{
    public string Quote { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? Role { get; set; }
    public string? AvatarUrl { get; set; }
    public int? Rating { get; set; }
}

public sealed class TestimonialsProps
{
    public string? Heading { get; set; }
    public List<TestimonialItem> Items { get; set; } = new();
}

public sealed class PricingPlan
{
    public string Name { get; set; } = string.Empty;
    public string Price { get; set; } = string.Empty;
    public string? Period { get; set; }
    public string? Description { get; set; }
    public List<string> Features { get; set; } = new();
    public string? CtaLabel { get; set; }
    public string? CtaHref { get; set; }
    public bool Highlighted { get; set; }
}

public sealed class PricingProps
{
    public string? Heading { get; set; }
    public List<PricingPlan> Plans { get; set; } = new();
    public string? Footnote { get; set; }
}

public sealed class FaqItem
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

public sealed class FaqProps
{
    public string? Heading { get; set; }
    public List<FaqItem> Items { get; set; } = new();
}

public sealed class CountdownProps
{
    public string? Heading { get; set; }
    public DateTime EndsAt { get; set; }
    public string? ExpiredText { get; set; }
}

public sealed class FormBlockProps
{
    public Guid FormId { get; set; }
    public string? Heading { get; set; }
    public string? Description { get; set; }
}

public sealed class CtaProps
{
    public string Heading { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string ButtonLabel { get; set; } = string.Empty;
    public string ButtonHref { get; set; } = string.Empty;
    public string Style { get; set; } = "primary";
}

public sealed class LogoItem
{
    public string Name { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string? Href { get; set; }
}

public sealed class LogosProps
{
    public string? Heading { get; set; }
    public List<LogoItem> Items { get; set; } = new();
}

public sealed class SpacerProps
{
    public string Size { get; set; } = "md";
}
#pragma warning restore CS1591

/// <summary>A validated block: id, type and its type-specific props.</summary>
public sealed record LandingBlock(string Id, string Type, object Props);

/// <summary>A draft or published variant.</summary>
public sealed record LandingVariant(string Key, string Name, int Weight, IReadOnlyList<LandingBlock> Blocks);

/// <summary>Lookups the validator needs from the application (image URL policy, forms of the page's client).</summary>
public sealed record BlockValidationContext(Func<string, bool> IsAllowedImageUrl, Func<Guid, bool> IsUsableForm);

public sealed class LandingValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("The landing page content is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>
/// Server-side schema of landing-page content. Every block type has a strict props schema (unknown properties are
/// rejected), lengths are bounded, links must be http(s), site-relative ("/path"), "#anchor", mailto: or tel:, images must
/// satisfy the image URL policy, videos are only YouTube/Vimeo (stored as provider + id, never as raw embed HTML), and no
/// text may carry script/iframe/object/embed markup or javascript:/vbscript:/data:text/html URLs. Content is rendered
/// as text by the web app; tracking scripts are never allowed inside pages (CSP).
/// </summary>
public static partial class LandingBlocks
{
    public static readonly IReadOnlyList<string> Types = new[]
    {
        "hero", "text", "image", "video", "features", "testimonials", "pricing", "faq", "countdown", "form", "cta", "logos", "spacer",
    };

    public const int MaxBlocks = 60;
    public const int MaxVariants = 4;
    private static readonly string[] VariantKeys = { "A", "B", "C", "D" };

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static Type PropsType(string type) => type switch
    {
        "hero" => typeof(HeroProps),
        "text" => typeof(TextProps),
        "image" => typeof(ImageProps),
        "video" => typeof(VideoProps),
        "features" => typeof(FeaturesProps),
        "testimonials" => typeof(TestimonialsProps),
        "pricing" => typeof(PricingProps),
        "faq" => typeof(FaqProps),
        "countdown" => typeof(CountdownProps),
        "form" => typeof(FormBlockProps),
        "cta" => typeof(CtaProps),
        "logos" => typeof(LogosProps),
        "spacer" => typeof(SpacerProps),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Parses and validates a variants array (JSON). Throws <see cref="LandingValidationException"/>.</summary>
    public static IReadOnlyList<LandingVariant> ParseVariants(JsonElement variants, BlockValidationContext context)
    {
        var errors = new Errors();
        var result = new List<LandingVariant>();
        if (variants.ValueKind != JsonValueKind.Array || variants.GetArrayLength() == 0)
        {
            errors.Add("variants", "At least one variant is required.");
            errors.ThrowIfAny();
        }
        if (variants.GetArrayLength() > MaxVariants) errors.Add("variants", $"At most {MaxVariants} variants are allowed.");

        var index = 0;
        var seen = new HashSet<string>();
        foreach (var v in variants.EnumerateArray())
        {
            var path = $"variants[{index++}]";
            if (v.ValueKind != JsonValueKind.Object) { errors.Add(path, "Each variant must be an object."); continue; }
            foreach (var p in v.EnumerateObject())
                if (p.Name is not ("key" or "name" or "weight" or "blocks")) errors.Add($"{path}.{p.Name}", "Unknown property.");

            var key = v.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString()! : string.Empty;
            if (!VariantKeys.Contains(key)) errors.Add($"{path}.key", "Variant keys are A, B, C or D.");
            else if (!seen.Add(key)) errors.Add($"{path}.key", "Variant keys must be unique.");
            var name = v.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : $"Variant {key}";
            if (name.Length is 0 or > 100) errors.Add($"{path}.name", "Name must be 1–100 characters.");
            var weight = v.TryGetProperty("weight", out var w) && w.TryGetInt32(out var wv) ? wv : 50;
            if (weight is < 1 or > 100) errors.Add($"{path}.weight", "Weight must be between 1 and 100.");
            var blocks = v.TryGetProperty("blocks", out var b)
                ? ParseBlocks(b, context, errors, $"{path}.blocks")
                : new List<LandingBlock>();
            result.Add(new LandingVariant(key, name, weight, blocks));
        }
        if (result.Count > 0 && result.All(r => r.Key != "A")) errors.Add("variants", "Variant A (the control) is required.");
        errors.ThrowIfAny();
        return result.OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>Parses and validates a blocks array.</summary>
    public static IReadOnlyList<LandingBlock> ParseBlocks(JsonElement blocks, BlockValidationContext context)
    {
        var errors = new Errors();
        var result = ParseBlocks(blocks, context, errors, "blocks");
        errors.ThrowIfAny();
        return result;
    }

    private static List<LandingBlock> ParseBlocks(JsonElement blocks, BlockValidationContext context, Errors errors, string basePath)
    {
        var result = new List<LandingBlock>();
        if (blocks.ValueKind != JsonValueKind.Array)
        {
            errors.Add(basePath, "Blocks must be an array.");
            return result;
        }
        if (blocks.GetArrayLength() > MaxBlocks) errors.Add(basePath, $"A page may have at most {MaxBlocks} blocks.");
        var ids = new HashSet<string>();
        var i = 0;
        foreach (var block in blocks.EnumerateArray())
        {
            var path = $"{basePath}[{i++}]";
            if (block.ValueKind != JsonValueKind.Object) { errors.Add(path, "Each block must be an object."); continue; }
            foreach (var p in block.EnumerateObject())
                if (p.Name is not ("id" or "type" or "props")) errors.Add($"{path}.{p.Name}", "Unknown property.");

            var id = block.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString()! : string.Empty;
            if (!BlockIdRegex().IsMatch(id)) errors.Add($"{path}.id", "Block ids are 1–40 letters, digits, '-' or '_'.");
            else if (!ids.Add(id)) errors.Add($"{path}.id", "Block ids must be unique.");

            var type = block.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : string.Empty;
            if (!Types.Contains(type))
            {
                errors.Add($"{path}.type", $"Unknown block type '{Short(type)}'. Allowed: {string.Join(", ", Types)}.");
                continue;
            }
            if (!block.TryGetProperty("props", out var propsEl) || propsEl.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{path}.props", "Block props are required.");
                continue;
            }

            // Reject dangerous markup anywhere in the raw props before binding.
            ScanForMarkup(propsEl, $"{path}.props", errors);

            object? props;
            try
            {
                props = propsEl.Deserialize(PropsType(type), Json);
            }
            catch (JsonException ex)
            {
                errors.Add($"{path}.props", $"Invalid props for a {type} block: {Short(ex.Message, 200)}");
                continue;
            }
            if (props is null) { errors.Add($"{path}.props", "Block props are required."); continue; }
            var v = new PropsValidator(errors, $"{path}.props", context);
            v.Validate(type, props);
            result.Add(new LandingBlock(id, type, props));
        }
        return result;
    }

    /// <summary>Serializes variants to the stored/draft JSON shape.</summary>
    public static string Serialize(IEnumerable<LandingVariant> variants) =>
        new JsonArray(variants.Select(v => (JsonNode)new JsonObject
        {
            ["key"] = v.Key,
            ["name"] = v.Name,
            ["weight"] = v.Weight,
            ["blocks"] = SerializeBlocksNode(v.Blocks),
        }).ToArray()).ToJsonString();

    public static JsonArray SerializeBlocksNode(IEnumerable<LandingBlock> blocks) =>
        new(blocks.Select(b => (JsonNode)new JsonObject
        {
            ["id"] = b.Id,
            ["type"] = b.Type,
            ["props"] = JsonSerializer.SerializeToNode(b.Props, b.Props.GetType(), Json),
        }).ToArray());

    /// <summary>Form ids referenced by form blocks.</summary>
    public static IEnumerable<Guid> FormIds(IEnumerable<LandingVariant> variants) =>
        variants.SelectMany(v => v.Blocks).Select(b => b.Props).OfType<FormBlockProps>().Select(p => p.FormId).Distinct();

    private static readonly string[] DangerousMarkers =
    {
        "<script", "</script", "<iframe", "<object", "<embed", "<frame", "<frameset", "<applet", "<meta", "<link", "<base",
        "javascript:", "vbscript:", "data:text/html", "srcdoc", "onerror=", "onload=",
    };

    private static void ScanForMarkup(JsonElement element, string path, Errors errors)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject()) ScanForMarkup(p.Value, $"{path}.{p.Name}", errors);
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in element.EnumerateArray()) ScanForMarkup(item, $"{path}[{i++}]", errors);
                break;
            case JsonValueKind.String:
                var compact = WhitespaceRegex().Replace(element.GetString() ?? string.Empty, string.Empty).ToLowerInvariant();
                var hit = DangerousMarkers.FirstOrDefault(m => compact.Contains(m, StringComparison.Ordinal));
                if (hit is not null)
                    errors.Add(path, $"Scripts, frames and executable URLs are not allowed in page content ('{hit.TrimEnd('=')}').");
                break;
        }
    }

    private sealed class PropsValidator(Errors errors, string path, BlockValidationContext context)
    {
        public void Validate(string type, object props)
        {
            switch (props)
            {
                case HeroProps h:
                    Text(h.Headline, "headline", 150, required: true);
                    Text(h.Subheadline, "subheadline", 400);
                    Text(h.CtaLabel, "ctaLabel", 60);
                    Link(h.CtaHref, "ctaHref");
                    if (h.CtaLabel is { Length: > 0 } != h.CtaHref is { Length: > 0 }) errors.Add($"{path}.ctaHref", "A call to action needs both a label and a link.");
                    Image(h.ImageUrl, "imageUrl");
                    Text(h.ImageAlt, "imageAlt", 200);
                    OneOf(h.Align, "align", "left", "center");
                    OneOf(h.Theme, "theme", "light", "dark", "brand");
                    break;
                case TextProps t:
                    Text(t.Heading, "heading", 150);
                    Text(t.Body, "body", 10000, required: true);
                    break;
                case ImageProps im:
                    Image(im.Url, "url", required: true);
                    if (!im.Decorative) Text(im.Alt, "alt", 200, required: true, message: "Alt text is required unless the image is decorative.");
                    Text(im.Caption, "caption", 300);
                    Link(im.LinkHref, "linkHref");
                    break;
                case VideoProps vd:
                    Text(vd.Title, "title", 150, required: true, message: "A video title is required (it labels the player for screen readers).");
                    var video = VideoEmbed.Resolve(vd.Url, vd.Provider, vd.VideoId);
                    if (video is null)
                        errors.Add($"{path}.url", "Only YouTube and Vimeo videos can be embedded. Paste a youtube.com, youtu.be or vimeo.com link.");
                    else
                    {
                        vd.Provider = video.Value.Provider;
                        vd.VideoId = video.Value.Id;
                        vd.Url = null;
                    }
                    break;
                case FeaturesProps f:
                    Text(f.Heading, "heading", 150);
                    Text(f.Intro, "intro", 500);
                    Count(f.Items, "items", 1, 12);
                    for (var i = 0; i < f.Items.Count; i++)
                    {
                        Text(f.Items[i].Title, $"items[{i}].title", 100, required: true);
                        Text(f.Items[i].Body, $"items[{i}].body", 500);
                        if (f.Items[i].Icon is { } icon && !IconRegex().IsMatch(icon)) errors.Add($"{path}.items[{i}].icon", "Icon names are lower-case letters and dashes.");
                    }
                    break;
                case TestimonialsProps ts:
                    Text(ts.Heading, "heading", 150);
                    Count(ts.Items, "items", 1, 12);
                    for (var i = 0; i < ts.Items.Count; i++)
                    {
                        Text(ts.Items[i].Quote, $"items[{i}].quote", 600, required: true);
                        Text(ts.Items[i].Author, $"items[{i}].author", 100, required: true);
                        Text(ts.Items[i].Role, $"items[{i}].role", 100);
                        Image(ts.Items[i].AvatarUrl, $"items[{i}].avatarUrl");
                        if (ts.Items[i].Rating is { } r && r is < 1 or > 5) errors.Add($"{path}.items[{i}].rating", "Ratings are 1–5.");
                    }
                    break;
                case PricingProps pr:
                    Text(pr.Heading, "heading", 150);
                    Text(pr.Footnote, "footnote", 300);
                    Count(pr.Plans, "plans", 1, 4);
                    for (var i = 0; i < pr.Plans.Count; i++)
                    {
                        var plan = pr.Plans[i];
                        Text(plan.Name, $"plans[{i}].name", 60, required: true);
                        Text(plan.Price, $"plans[{i}].price", 30, required: true);
                        Text(plan.Period, $"plans[{i}].period", 30);
                        Text(plan.Description, $"plans[{i}].description", 300);
                        Text(plan.CtaLabel, $"plans[{i}].ctaLabel", 60);
                        Link(plan.CtaHref, $"plans[{i}].ctaHref");
                        if (plan.Features.Count > 15) errors.Add($"{path}.plans[{i}].features", "At most 15 features per plan.");
                        for (var j = 0; j < plan.Features.Count; j++) Text(plan.Features[j], $"plans[{i}].features[{j}]", 150, required: true);
                    }
                    break;
                case FaqProps fq:
                    Text(fq.Heading, "heading", 150);
                    Count(fq.Items, "items", 1, 30);
                    for (var i = 0; i < fq.Items.Count; i++)
                    {
                        Text(fq.Items[i].Question, $"items[{i}].question", 250, required: true);
                        Text(fq.Items[i].Answer, $"items[{i}].answer", 2000, required: true);
                    }
                    break;
                case CountdownProps cd:
                    Text(cd.Heading, "heading", 150);
                    Text(cd.ExpiredText, "expiredText", 200);
                    if (cd.EndsAt == default) errors.Add($"{path}.endsAt", "An end date and time is required.");
                    else cd.EndsAt = DateTime.SpecifyKind(cd.EndsAt.Kind == DateTimeKind.Local ? cd.EndsAt.ToUniversalTime() : cd.EndsAt, DateTimeKind.Utc);
                    break;
                case FormBlockProps fb:
                    Text(fb.Heading, "heading", 150);
                    Text(fb.Description, "description", 500);
                    if (fb.FormId == Guid.Empty || !context.IsUsableForm(fb.FormId))
                        errors.Add($"{path}.formId", "Choose an active form of this client.");
                    break;
                case CtaProps c:
                    Text(c.Heading, "heading", 150, required: true);
                    Text(c.Body, "body", 500);
                    Text(c.ButtonLabel, "buttonLabel", 60, required: true);
                    Link(c.ButtonHref, "buttonHref", required: true);
                    OneOf(c.Style, "style", "primary", "highlight", "secondary");
                    break;
                case LogosProps l:
                    Text(l.Heading, "heading", 150);
                    Count(l.Items, "items", 1, 24);
                    for (var i = 0; i < l.Items.Count; i++)
                    {
                        Text(l.Items[i].Name, $"items[{i}].name", 100, required: true);
                        Image(l.Items[i].ImageUrl, $"items[{i}].imageUrl", required: true);
                        Link(l.Items[i].Href, $"items[{i}].href");
                    }
                    break;
                case SpacerProps s:
                    OneOf(s.Size, "size", "sm", "md", "lg", "xl");
                    break;
            }
        }

        private void Text(string? value, string field, int max, bool required = false, string? message = null)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (required) errors.Add($"{path}.{field}", message ?? "This field is required.");
                return;
            }
            if (value.Length > max) errors.Add($"{path}.{field}", $"At most {max} characters.");
        }

        private void Count<T>(List<T> items, string field, int min, int max)
        {
            if (items.Count < min || items.Count > max) errors.Add($"{path}.{field}", $"Add between {min} and {max} items.");
        }

        private void OneOf(string value, string field, params string[] allowed)
        {
            if (!allowed.Contains(value)) errors.Add($"{path}.{field}", $"Allowed values: {string.Join(", ", allowed)}.");
        }

        private void Link(string? value, string field, bool required = false)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (required) errors.Add($"{path}.{field}", "A link is required.");
                return;
            }
            if (!IsSafeHref(value)) errors.Add($"{path}.{field}", "Links must be https/http URLs, site paths (/…), #anchors, mailto: or tel:.");
        }

        private void Image(string? value, string field, bool required = false)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (required) errors.Add($"{path}.{field}", "An image is required.");
                return;
            }
            if (value.Length > 1000 || !context.IsAllowedImageUrl(value))
                errors.Add($"{path}.{field}", "Use an uploaded image or an https URL on an allowed image host.");
        }
    }

    /// <summary>http(s) without credentials, a single-slash site path, an #anchor, mailto: or tel:.</summary>
    public static bool IsSafeHref(string value)
    {
        value = value.Trim();
        if (value.Length is 0 or > 2000) return false;
        if (value.StartsWith('#')) return AnchorRegex().IsMatch(value);
        if (value.StartsWith('/')) return !value.StartsWith("//", StringComparison.Ordinal) && !value.Contains('\\');
        if (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) return MailtoRegex().IsMatch(value);
        if (value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)) return TelRegex().IsMatch(value);
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
               string.IsNullOrEmpty(uri.UserInfo) && uri.Host.Length > 0 && !value.Contains('\\');
    }

    private static string Short(string s, int max = 40) => s.Length <= max ? s : s[..max];

    private sealed class Errors
    {
        private readonly Dictionary<string, List<string>> _errors = new();

        public void Add(string key, string message)
        {
            if (!_errors.TryGetValue(key, out var list)) _errors[key] = list = new List<string>();
            if (!list.Contains(message)) list.Add(message);
        }

        public void ThrowIfAny()
        {
            if (_errors.Count > 0)
                throw new LandingValidationException(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex BlockIdRegex();

    [GeneratedRegex("^[a-z][a-z0-9-]{0,39}$")]
    private static partial Regex IconRegex();

    [GeneratedRegex(@"^#[A-Za-z][A-Za-z0-9_-]{0,80}$")]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(@"^mailto:[^\s@<>""]+@[^\s@<>""]+$", RegexOptions.IgnoreCase)]
    private static partial Regex MailtoRegex();

    [GeneratedRegex(@"^tel:\+?[0-9 ().-]{3,30}$", RegexOptions.IgnoreCase)]
    private static partial Regex TelRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

/// <summary>YouTube/Vimeo allowlist: turns a page or embed URL into (provider, id), and builds the privacy embed URL.</summary>
public static partial class VideoEmbed
{
    public static (string Provider, string Id)? Resolve(string? url, string? provider, string? videoId)
    {
        if (!string.IsNullOrWhiteSpace(provider) && !string.IsNullOrWhiteSpace(videoId) && string.IsNullOrWhiteSpace(url))
        {
            return provider switch
            {
                "youtube" when YouTubeIdRegex().IsMatch(videoId) => ("youtube", videoId),
                "vimeo" when VimeoIdRegex().IsMatch(videoId) => ("vimeo", videoId),
                _ => null,
            };
        }
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return null;
        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.", StringComparison.Ordinal)) host = host[4..];
        if (host.StartsWith("m.", StringComparison.Ordinal)) host = host[2..];
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? id = null;
        switch (host)
        {
            case "youtube.com":
            case "youtube-nocookie.com":
                if (uri.AbsolutePath == "/watch")
                    id = System.Web.HttpUtility.ParseQueryString(uri.Query)["v"];
                else if (segments.Length >= 2 && segments[0] is "embed" or "shorts" or "live" or "v") id = segments[1];
                return id is not null && YouTubeIdRegex().IsMatch(id) ? ("youtube", id) : null;
            case "youtu.be":
                id = segments.FirstOrDefault();
                return id is not null && YouTubeIdRegex().IsMatch(id) ? ("youtube", id) : null;
            case "vimeo.com":
                id = segments.FirstOrDefault(s => VimeoIdRegex().IsMatch(s));
                return id is not null ? ("vimeo", id) : null;
            case "player.vimeo.com":
                id = segments.Length >= 2 && segments[0] == "video" ? segments[1] : null;
                return id is not null && VimeoIdRegex().IsMatch(id) ? ("vimeo", id) : null;
            default:
                return null;
        }
    }

    public static string EmbedUrl(string provider, string id) => provider == "vimeo"
        ? $"https://player.vimeo.com/video/{id}?dnt=1"
        : $"https://www.youtube-nocookie.com/embed/{id}";

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex YouTubeIdRegex();

    [GeneratedRegex("^[0-9]{6,12}$")]
    private static partial Regex VimeoIdRegex();
}

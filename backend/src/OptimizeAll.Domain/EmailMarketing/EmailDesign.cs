using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OptimizeAll.Domain.EmailMarketing;

/// <summary>Block-based email design (stored as JSON). Rendered server-side to responsive HTML and plain text.</summary>
public sealed class EmailDesign
{
    public DesignSettings Settings { get; set; } = new();
    public List<DesignBlock> Blocks { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 16,
    };

    /// <summary>Parses design JSON; throws <see cref="FormatException"/> when it is not a design document.</summary>
    public static EmailDesign Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new EmailDesign();
        try
        {
            return JsonSerializer.Deserialize<EmailDesign>(json, JsonOptions) ?? new EmailDesign();
        }
        catch (JsonException ex)
        {
            throw new FormatException("The email design is not valid JSON: " + ex.Message, ex);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

public sealed class DesignSettings
{
    public string BackgroundColor { get; set; } = "#f4f5f7";
    public string ContentBackgroundColor { get; set; } = "#ffffff";
    public int ContentWidth { get; set; } = 600;
    public string FontFamily { get; set; } = "Arial, Helvetica, sans-serif";
    public string TextColor { get; set; } = "#1f2937";
    public string LinkColor { get; set; } = "#1F2659";
}

public sealed class DesignBlock
{
    /// <summary>header, text, image, button, divider, spacer, columns, social, footer.</summary>
    public string Type { get; set; } = "text";
    public string? Id { get; set; }

    // header
    public string? LogoUrl { get; set; }
    public string? LogoAlt { get; set; }
    public string? Title { get; set; }
    public string? Subtitle { get; set; }

    // text / footer extra text (sanitized rich text)
    public string? Html { get; set; }

    // image
    public string? Src { get; set; }
    public string? Alt { get; set; }
    public int? Width { get; set; }

    // button / image link
    public string? Text { get; set; }
    public string? Href { get; set; }
    public string? Color { get; set; }
    public string? TextColor { get; set; }

    public string? Align { get; set; }
    public string? BackgroundColor { get; set; }

    // spacer / divider
    public int? Height { get; set; }

    // columns
    public List<DesignColumn>? Columns { get; set; }

    // social
    public List<SocialLink>? Links { get; set; }

    // footer
    public bool? ShowPreferencesLink { get; set; }
}

public sealed class DesignColumn
{
    public List<DesignBlock> Blocks { get; set; } = new();
}

public sealed class SocialLink
{
    /// <summary>instagram, facebook, x, linkedin, tiktok, youtube, website.</summary>
    public string Network { get; set; } = "website";
    public string Url { get; set; } = string.Empty;
}

/// <summary>Validation of a design against the platform's content rules.</summary>
public static partial class DesignRules
{
    public const int MaxBlocks = 80;
    public const int MaxHtmlLength = 20_000;

    public static readonly IReadOnlySet<string> BlockTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "header", "text", "image", "button", "divider", "spacer", "columns", "social", "footer",
    };

    public static readonly IReadOnlySet<string> SocialNetworks = new HashSet<string>(StringComparer.Ordinal)
    {
        "instagram", "facebook", "x", "linkedin", "tiktok", "youtube", "pinterest", "website",
    };

    public static bool IsColor(string? value) => value is not null && ColorRegex().IsMatch(value);

    /// <summary>Errors that make the design unusable (saving is rejected).</summary>
    public static List<string> Validate(EmailDesign design)
    {
        var errors = new List<string>();
        var s = design.Settings;
        if (!IsColor(s.BackgroundColor)) errors.Add("settings.backgroundColor must be a hex color.");
        if (!IsColor(s.ContentBackgroundColor)) errors.Add("settings.contentBackgroundColor must be a hex color.");
        if (!IsColor(s.TextColor)) errors.Add("settings.textColor must be a hex color.");
        if (!IsColor(s.LinkColor)) errors.Add("settings.linkColor must be a hex color.");
        if (s.ContentWidth is < 320 or > 800) errors.Add("settings.contentWidth must be between 320 and 800.");
        if (string.IsNullOrWhiteSpace(s.FontFamily) || !FontRegex().IsMatch(s.FontFamily)) errors.Add("settings.fontFamily is invalid.");

        var count = 0;
        ValidateBlocks(design.Blocks, "blocks", nested: false, errors, ref count);
        if (count > MaxBlocks) errors.Add($"A design may contain at most {MaxBlocks} blocks.");
        return errors;
    }

    /// <summary>True when the design has the required footer block (physical address + unsubscribe link).</summary>
    public static bool HasFooter(EmailDesign design) => design.Blocks.Any(b => b.Type == "footer");

    /// <summary>All merge tags used anywhere in the design.</summary>
    public static IEnumerable<string> MergeTagNames(EmailDesign design) =>
        AllBlocks(design.Blocks).SelectMany(b => new[] { b.Title, b.Subtitle, b.Html, b.Alt, b.Text, b.Href, b.LogoAlt })
            .SelectMany(MergeTags.Names);

    public static IEnumerable<DesignBlock> AllBlocks(IEnumerable<DesignBlock> blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;
            if (block.Columns is null) continue;
            foreach (var column in block.Columns)
            foreach (var inner in AllBlocks(column.Blocks))
                yield return inner;
        }
    }

    private static void ValidateBlocks(List<DesignBlock>? blocks, string path, bool nested, List<string> errors, ref int count)
    {
        if (blocks is null) return;
        var footers = 0;
        for (var i = 0; i < blocks.Count; i++)
        {
            count++;
            var b = blocks[i];
            var p = $"{path}[{i}]";
            if (!BlockTypes.Contains(b.Type)) { errors.Add($"{p}: unknown block type '{Trim(b.Type)}'."); continue; }
            if (nested && b.Type is "columns" or "footer" or "header")
            {
                errors.Add($"{p}: {b.Type} blocks cannot be placed inside columns.");
                continue;
            }
            if (b.Align is not null && b.Align is not ("left" or "center" or "right")) errors.Add($"{p}.align must be left, center or right.");
            if (b.BackgroundColor is not null && !IsColor(b.BackgroundColor)) errors.Add($"{p}.backgroundColor must be a hex color.");
            if (b.Color is not null && !IsColor(b.Color)) errors.Add($"{p}.color must be a hex color.");
            if (b.TextColor is not null && !IsColor(b.TextColor)) errors.Add($"{p}.textColor must be a hex color.");
            if (b.Html is { Length: > MaxHtmlLength }) errors.Add($"{p}.html is longer than {MaxHtmlLength} characters.");
            foreach (var text in new[] { b.Title, b.Subtitle, b.Alt, b.Text, b.LogoAlt })
                if (text is { Length: > 1000 }) errors.Add($"{p}: text is longer than 1000 characters.");

            switch (b.Type)
            {
                case "header":
                    if (b.LogoUrl is not null && !IsImageUrl(b.LogoUrl)) errors.Add($"{p}.logoUrl must be an https image URL.");
                    break;
                case "image":
                    if (!IsImageUrl(b.Src)) errors.Add($"{p}.src must be an https image URL.");
                    if (b.Href is not null && !IsLink(b.Href)) errors.Add($"{p}.href must be an http(s) URL.");
                    if (b.Width is < 20 or > 800) errors.Add($"{p}.width must be between 20 and 800.");
                    break;
                case "button":
                    if (string.IsNullOrWhiteSpace(b.Text)) errors.Add($"{p}.text is required.");
                    if (!IsLink(b.Href)) errors.Add($"{p}.href must be an http(s), mailto: or tel: URL.");
                    break;
                case "spacer":
                case "divider":
                    if (b.Height is < 0 or > 200) errors.Add($"{p}.height must be between 0 and 200.");
                    break;
                case "columns":
                    if (b.Columns is null || b.Columns.Count is < 2 or > 3) { errors.Add($"{p}: columns need 2 or 3 columns."); break; }
                    for (var c = 0; c < b.Columns.Count; c++)
                        ValidateBlocks(b.Columns[c].Blocks, $"{p}.columns[{c}].blocks", nested: true, errors, ref count);
                    break;
                case "social":
                    if (b.Links is null || b.Links.Count == 0) { errors.Add($"{p}: add at least one social link."); break; }
                    if (b.Links.Count > 8) errors.Add($"{p}: at most 8 social links.");
                    foreach (var link in b.Links)
                    {
                        if (!SocialNetworks.Contains(link.Network)) errors.Add($"{p}: unknown network '{Trim(link.Network)}'.");
                        if (!HtmlSanitizer.IsHttpUrl(link.Url)) errors.Add($"{p}: social links must be http(s) URLs.");
                    }
                    break;
                case "footer":
                    footers++;
                    break;
            }
        }
        if (!nested && footers > 1) errors.Add("A design may contain only one footer block.");
    }

    public static bool IsImageUrl(string? url) =>
        url is not null && url.Length <= 2000 && !url.Contains("{{", StringComparison.Ordinal) &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);

    public static bool IsLink(string? url) => HtmlSanitizer.SafeHref(url) is not null;

    private static string Trim(string? value) => value is null ? string.Empty : value.Length <= 30 ? value : value[..30];

    [GeneratedRegex(@"^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex ColorRegex();

    [GeneratedRegex(@"^[A-Za-z0-9 ,\-]{1,100}$")]
    private static partial Regex FontRegex();
}

/// <summary>Output of <see cref="EmailRenderer"/>.</summary>
public sealed record RenderedEmail(string Html, string Text);

/// <summary>Per-message rendering inputs.</summary>
public sealed class RenderOptions
{
    public string Subject { get; init; } = string.Empty;
    public string? PreviewText { get; init; }
    public IReadOnlyDictionary<string, string?> Values { get; init; } = new Dictionary<string, string?>();

    /// <summary>
    /// Maps a raw link (merge tags not yet resolved) to the final href — e.g. a signed click-tracking URL. When null,
    /// links are merged and used as is (previews).
    /// </summary>
    public Func<string, string?>? LinkRewriter { get; init; }

    /// <summary>Open-tracking pixel URL appended before &lt;/body&gt;.</summary>
    public string? OpenPixelUrl { get; init; }
    public string Language { get; init; } = "en";
}

/// <summary>
/// Renders an <see cref="EmailDesign"/> to table-based, inline-styled HTML (renders in Outlook, Gmail, Apple Mail; the
/// columns stack below 620px) and to a plain-text alternative. Text-block HTML is sanitized again at render time, every
/// other value is HTML-encoded, and merge tags are resolved with context-appropriate encoding.
/// </summary>
public static partial class EmailRenderer
{
    public static RenderedEmail Render(EmailDesign design, RenderOptions options) => Render(design, options, merge: true);

    private static RenderedEmail Render(EmailDesign design, RenderOptions options, bool merge)
    {
        var s = design.Settings;
        var html = new StringBuilder(8192);
        var width = Math.Clamp(s.ContentWidth, 320, 800);
        var font = WebUtility.HtmlEncode(s.FontFamily);
        html.Append("<!DOCTYPE html><html lang=\"").Append(WebUtility.HtmlEncode(options.Language)).Append("\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta name=\"x-apple-disable-message-reformatting\">")
            .Append("<meta name=\"color-scheme\" content=\"light\"><title>").Append(Encode(options.Subject)).Append("</title>")
            .Append("<style>body{margin:0;padding:0}img{border:0;line-height:100%;outline:none;text-decoration:none}")
            .Append("a{color:").Append(s.LinkColor).Append("}")
            .Append("@media only screen and (max-width:").Append(width + 20).Append("px){.oa-container{width:100%!important;max-width:100%!important}")
            .Append(".oa-col{display:block!important;width:100%!important;max-width:100%!important}.oa-img{width:100%!important;height:auto!important}")
            .Append(".oa-pad{padding-left:16px!important;padding-right:16px!important}}</style></head>")
            .Append("<body style=\"margin:0;padding:0;background-color:").Append(s.BackgroundColor).Append("\">");
        if (!string.IsNullOrWhiteSpace(options.PreviewText))
            html.Append("<div style=\"display:none;max-height:0;overflow:hidden;opacity:0;mso-hide:all\">")
                .Append(Encode(options.PreviewText)).Append(string.Concat(Enumerable.Repeat("&#847;&zwnj;&nbsp;", 20))).Append("</div>");
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background-color:")
            .Append(s.BackgroundColor).Append("\"><tr><td align=\"center\" style=\"padding:24px 12px\">")
            .Append("<table role=\"presentation\" class=\"oa-container\" width=\"").Append(width).Append("\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:")
            .Append(width).Append("px;max-width:").Append(width).Append("px;background-color:").Append(s.ContentBackgroundColor)
            .Append(";font-family:").Append(font).Append(";color:").Append(s.TextColor).Append(";font-size:16px;line-height:1.55\">");

        var text = new StringBuilder();
        foreach (var block in design.Blocks)
        {
            html.Append("<tr><td class=\"oa-pad\" style=\"padding:").Append(block.Type is "divider" or "spacer" ? "0 32px" : "12px 32px").Append("\">");
            RenderBlock(block, s, width - 64, html, text);
            html.Append("</td></tr>");
        }
        html.Append("</table></td></tr></table>");
        if (options.OpenPixelUrl is not null)
            html.Append("<img src=\"").Append(WebUtility.HtmlEncode(options.OpenPixelUrl))
                .Append("\" width=\"1\" height=\"1\" alt=\"\" style=\"display:block;width:1px;height:1px;border:0\">");
        html.Append("</body></html>");

        if (!merge) return new RenderedEmail(html.ToString(), text.ToString());
        var mergedHtml = RewriteAndMerge(html.ToString(), options);
        var plain = RewriteAndMergeText(text.ToString().Trim() + "\n", options);
        return new RenderedEmail(mergedHtml, plain);
    }

    /// <summary>Distinct trackable links (raw, with merge tags) in document order. System URLs, mailto: and tel: are excluded.</summary>
    public static IReadOnlyList<string> ExtractLinks(EmailDesign design)
    {
        var rendered = Render(design, new RenderOptions(), merge: false);
        var links = new List<string>();
        foreach (Match m in HrefRegex().Matches(rendered.Html))
        {
            var raw = WebUtility.HtmlDecode(m.Groups["href"].Value);
            if (IsTrackable(raw) && !links.Contains(raw)) links.Add(raw);
        }
        return links;
    }

    public static bool IsTrackable(string raw) =>
        HtmlSanitizer.IsHttpUrl(raw) && !MergeTags.Names(raw).Any(MergeTags.SystemUrlTags.Contains);

    private static void RenderBlock(DesignBlock b, DesignSettings s, int innerWidth, StringBuilder html, StringBuilder text)
    {
        var align = b.Align is "left" or "right" or "center" ? b.Align : "left";
        switch (b.Type)
        {
            case "header":
            {
                var bg = b.BackgroundColor ?? s.ContentBackgroundColor;
                html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background-color:")
                    .Append(bg).Append("\"><tr><td align=\"").Append(b.Align ?? "center").Append("\" style=\"padding:16px 0\">");
                if (DesignRules.IsImageUrl(b.LogoUrl))
                    html.Append("<img src=\"").Append(WebUtility.HtmlEncode(b.LogoUrl)).Append("\" alt=\"").Append(Encode(b.LogoAlt ?? b.Title ?? string.Empty))
                        .Append("\" width=\"160\" style=\"display:inline-block;max-width:160px;height:auto\">");
                if (!string.IsNullOrWhiteSpace(b.Title))
                {
                    html.Append("<h1 style=\"margin:12px 0 0;font-size:26px;line-height:1.25;color:").Append(b.TextColor ?? s.TextColor).Append("\">")
                        .Append(Encode(b.Title)).Append("</h1>");
                    text.Append(b.Title.Trim()).Append("\n\n");
                }
                if (!string.IsNullOrWhiteSpace(b.Subtitle))
                {
                    html.Append("<p style=\"margin:8px 0 0;font-size:16px;color:").Append(b.TextColor ?? s.TextColor).Append("\">").Append(Encode(b.Subtitle)).Append("</p>");
                    text.Append(b.Subtitle.Trim()).Append("\n\n");
                }
                html.Append("</td></tr></table>");
                break;
            }
            case "text":
            {
                var clean = HtmlSanitizer.Sanitize(b.Html);
                html.Append("<div style=\"text-align:").Append(align).Append(b.BackgroundColor is null ? string.Empty : ";background-color:" + b.BackgroundColor)
                    .Append(";color:").Append(b.TextColor ?? s.TextColor).Append("\">").Append(clean).Append("</div>");
                var plain = HtmlSanitizer.ToText(HrefRegex().Replace(clean, m => "href=\"\u0001" + m.Groups["href"].Value + "\u0002\""));
                if (plain.Length > 0) text.Append(plain).Append("\n\n");
                break;
            }
            case "image":
            {
                if (!DesignRules.IsImageUrl(b.Src)) break;
                var w = Math.Min(b.Width ?? innerWidth, innerWidth);
                var img = $"<img class=\"oa-img\" src=\"{WebUtility.HtmlEncode(b.Src)}\" alt=\"{Encode(b.Alt ?? string.Empty)}\" width=\"{w}\" style=\"display:block;width:{w}px;max-width:100%;height:auto;margin:0 auto\">";
                html.Append("<div style=\"text-align:").Append(b.Align ?? "center").Append("\">");
                if (DesignRules.IsLink(b.Href)) html.Append("<a href=\"").Append(WebUtility.HtmlEncode(b.Href)).Append("\">").Append(img).Append("</a>");
                else html.Append(img);
                html.Append("</div>");
                if (!string.IsNullOrWhiteSpace(b.Alt) || DesignRules.IsLink(b.Href))
                {
                    text.Append(string.IsNullOrWhiteSpace(b.Alt) ? "Image" : "[" + b.Alt.Trim() + "]");
                    if (DesignRules.IsLink(b.Href)) text.Append(' ').Append(HrefToken(b.Href!));
                    text.Append("\n\n");
                }
                break;
            }
            case "button":
            {
                if (!DesignRules.IsLink(b.Href) || string.IsNullOrWhiteSpace(b.Text)) break;
                var color = b.Color ?? "#1F2659";
                var fg = b.TextColor ?? "#ffffff";
                html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" align=\"").Append(b.Align ?? "center")
                    .Append("\" style=\"margin:8px ").Append(b.Align == "left" ? "0" : "auto").Append("\"><tr><td style=\"border-radius:6px;background-color:").Append(color).Append("\">")
                    .Append("<a href=\"").Append(WebUtility.HtmlEncode(b.Href)).Append("\" style=\"display:inline-block;padding:12px 24px;font-weight:bold;font-size:16px;color:")
                    .Append(fg).Append(";text-decoration:none;border-radius:6px\">").Append(Encode(b.Text)).Append("</a></td></tr></table>");
                text.Append(b.Text.Trim()).Append(": ").Append(HrefToken(b.Href!)).Append("\n\n");
                break;
            }
            case "divider":
                html.Append("<hr style=\"border:0;border-top:1px solid ").Append(b.Color ?? "#e5e7eb").Append(";margin:")
                    .Append(Math.Clamp(b.Height ?? 16, 0, 200)).Append("px 0\">");
                text.Append("----------\n\n");
                break;
            case "spacer":
                html.Append("<div style=\"height:").Append(Math.Clamp(b.Height ?? 24, 0, 200)).Append("px;line-height:1px;font-size:1px\">&nbsp;</div>");
                break;
            case "columns":
            {
                var cols = b.Columns ?? new List<DesignColumn>();
                if (cols.Count == 0) break;
                var colWidth = innerWidth / cols.Count;
                html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\"><tr>");
                foreach (var column in cols)
                {
                    html.Append("<td class=\"oa-col\" width=\"").Append(colWidth).Append("\" valign=\"top\" style=\"width:").Append(colWidth).Append("px;padding:0 6px\">");
                    foreach (var inner in column.Blocks)
                    {
                        html.Append("<div style=\"padding:6px 0\">");
                        RenderBlock(inner, s, colWidth - 12, html, text);
                        html.Append("</div>");
                    }
                    html.Append("</td>");
                }
                html.Append("</tr></table>");
                break;
            }
            case "social":
            {
                var links = (b.Links ?? new List<SocialLink>()).Where(l => HtmlSanitizer.IsHttpUrl(l.Url)).ToList();
                if (links.Count == 0) break;
                html.Append("<p style=\"text-align:").Append(b.Align ?? "center").Append(";margin:0;font-size:14px\">");
                for (var i = 0; i < links.Count; i++)
                {
                    if (i > 0) html.Append(" &middot; ");
                    html.Append("<a href=\"").Append(WebUtility.HtmlEncode(links[i].Url)).Append("\" style=\"color:").Append(s.LinkColor).Append(";text-decoration:none\">")
                        .Append(Encode(NetworkLabel(links[i].Network))).Append("</a>");
                    text.Append(NetworkLabel(links[i].Network)).Append(": ").Append(HrefToken(links[i].Url)).Append('\n');
                }
                html.Append("</p>");
                text.Append('\n');
                break;
            }
            case "footer":
            {
                var extra = HtmlSanitizer.Sanitize(b.Html);
                html.Append("<div style=\"text-align:").Append(b.Align ?? "center").Append(";font-size:12px;line-height:1.5;color:#6b7280;border-top:1px solid #e5e7eb;padding-top:16px\">");
                if (extra.Length > 0) html.Append("<div style=\"margin-bottom:8px\">").Append(extra).Append("</div>");
                html.Append("<p style=\"margin:0\">{{org_name}}</p><p style=\"margin:0\">{{org_address}}</p>")
                    .Append("<p style=\"margin:8px 0 0\">You are receiving this email because you subscribed to our updates. ")
                    .Append("<a href=\"{{unsubscribe_url}}\" style=\"color:#6b7280;text-decoration:underline\">Unsubscribe</a>");
                if (b.ShowPreferencesLink != false)
                    html.Append(" &middot; <a href=\"{{preferences_url}}\" style=\"color:#6b7280;text-decoration:underline\">Manage preferences</a>");
                html.Append("</p></div>");
                text.Append("--\n");
                if (extra.Length > 0) text.Append(HtmlSanitizer.ToText(extra)).Append('\n');
                text.Append("{{org_name}}\n{{org_address}}\nUnsubscribe: {{unsubscribe_url}}\n");
                if (b.ShowPreferencesLink != false) text.Append("Manage preferences: {{preferences_url}}\n");
                break;
            }
        }
    }

    /// <summary>Marks a raw link in the text part so it gets the same rewrite as HTML links.</summary>
    private static string HrefToken(string href) => "\u0001" + href + "\u0002";

    private static string RewriteAndMerge(string html, RenderOptions options)
    {
        var rewritten = HrefRegex().Replace(html, m =>
        {
            var raw = WebUtility.HtmlDecode(m.Groups["href"].Value);
            return "href=\"" + WebUtility.HtmlEncode(FinalHref(raw, options)) + "\"";
        });
        return MergeTags.Render(rewritten, options.Values, MergeContext.Html);
    }

    private static string RewriteAndMergeText(string text, RenderOptions options)
    {
        var rewritten = TextHrefRegex().Replace(text, m => FinalHref(m.Groups["href"].Value, options));
        return MergeTags.Render(rewritten, options.Values, MergeContext.Text);
    }

    private static string FinalHref(string raw, RenderOptions options)
    {
        if (options.LinkRewriter is not null && IsTrackable(raw) && options.LinkRewriter(raw) is { } tracked) return tracked;
        // System URLs keep their merge tag so they resolve with the final values; others are merged URL-safely.
        var merged = MergeTags.Render(raw, options.Values, MergeContext.Url);
        return merged;
    }

    private static string NetworkLabel(string network) => network switch
    {
        "x" => "X",
        "linkedin" => "LinkedIn",
        "tiktok" => "TikTok",
        "youtube" => "YouTube",
        _ => network.Length == 0 ? network : char.ToUpperInvariant(network[0]) + network[1..],
    };

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    [GeneratedRegex(@"href=""(?<href>[^""]*)""")]
    private static partial Regex HrefRegex();

    [GeneratedRegex("\u0001(?<href>[^\u0001\u0002]*)\u0002")]
    private static partial Regex TextHrefRegex();
}

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using OptimizeAll.Api.Modules.Content.Copy;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>What kind of URL a path is, for robots and the SEO overview.</summary>
public enum SeoPageKind
{
    /// <summary>Public marketing content (indexable unless its SEO settings say noindex).</summary>
    Content,
    /// <summary>A public utility page (search, tokenized links, newsletter confirmation): noindex.</summary>
    Utility,
    /// <summary>A signed-in portal or auth page: noindex, nofollow, disallowed in robots.txt.</summary>
    Private,
    NotFound,
    Redirect,
}

/// <summary>Everything the server renders for one public URL: status, head metadata, JSON-LD and crawlable content.</summary>
public sealed class SeoPage
{
    public required string Path { get; init; }
    public int Status { get; set; } = 200;
    public SeoPageKind Kind { get; set; } = SeoPageKind.Content;
    /// <summary>Absolute or site-relative target of a 301/308/302 redirect.</summary>
    public string? RedirectTo { get; set; }
    /// <summary>Full document title (the site's title template already applied).</summary>
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Absolute canonical URL (null for noindex utility pages without one).</summary>
    public string? Canonical { get; set; }
    public bool NoIndex { get; set; }
    public bool NoFollow { get; set; }
    public string OgType { get; set; } = "website";
    public string? OgImage { get; set; }
    public int? OgImageWidth { get; set; }
    public int? OgImageHeight { get; set; }
    public string? OgImageAlt { get; set; }
    /// <summary>
    /// The words of the page's generated social card (SocialCards/SocialCardRenderer.cs). Null: derived from the page
    /// (<see cref="SocialCards.SocialCardFactory"/>). Only used when the page has no image of its own.
    /// </summary>
    public SocialCards.SocialCard? Card { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public string? Author { get; set; }
    public string? Section { get; set; }
    public string? PrevUrl { get; set; }
    public string? NextUrl { get; set; }
    public List<JsonElement> JsonLd { get; } = new();
    public List<ContentNode> Content { get; } = new();
    public List<Crumb> Breadcrumbs { get; } = new();
    public List<SeoImage> Images { get; } = new();
    public List<SeoVideo> Videos { get; } = new();
    /// <summary>Where staff edit this page's words (agency portal path), for the SEO overview.</summary>
    public string? EditPath { get; set; }
    /// <summary>What the metadata comes from ("Page texts", "Service", "Blog post"…), for the SEO overview.</summary>
    public string Source { get; set; } = "Built-in page";

    public bool IsIndexable => Status == 200 && Kind == SeoPageKind.Content && !NoIndex;

    public string Robots => NoIndex
        ? NoFollow ? "noindex, nofollow" : "noindex, follow"
        : "index, follow, max-image-preview:large, max-snippet:-1, max-video-preview:-1";

    public string PlainText(int max = int.MaxValue)
    {
        var text = string.Join(' ', Content.Select(n => n switch
        {
            HeadingNode h => h.Text,
            ParagraphNode p => p.Text,
            MarkdownNode m => OptimizeAll.Domain.Website.MarkdownSanitizer.ToPlainText(m.Markdown),
            _ => string.Empty,
        }).Where(s => s.Length > 0));
        return text.Length > max ? text[..max] : text;
    }
}

/// <summary>Length rules and helpers for titles and descriptions (docs/SEO_CRO.md § Technical SEO).</summary>
public static partial class SeoText
{
    public const int TitleMax = 60;
    public const int TitleMin = 30;
    public const int DescriptionMax = 155;
    public const int DescriptionMin = 70;

    /// <summary>Collapses whitespace and cuts at a word boundary with an ellipsis when longer than <paramref name="max"/>.</summary>
    public static string? Clamp(string? text, int max = DescriptionMax)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = SpaceRegex().Replace(text, " ").Trim();
        if (t.Length <= max) return t;
        var cut = t[..(max - 1)];
        var space = cut.LastIndexOf(' ');
        if (space > max / 2) cut = cut[..space];
        return cut.TrimEnd(',', ';', ':', '-', '—', ' ', '.') + "…";
    }

    /// <summary>
    /// Applies the title template ("%s | Optimize All"), unless the title already names the site, or the suffix would
    /// push a title that fits on its own past <see cref="TitleMax"/> (search results would cut the page's own words).
    /// </summary>
    public static string ApplyTemplate(string title, string template, string siteName)
    {
        if (title.Contains(siteName, StringComparison.OrdinalIgnoreCase)) return title;
        var full = template.Replace("%s", title, StringComparison.Ordinal);
        return full.Length > TitleMax && title.Length <= TitleMax ? title : full;
    }

    public static string Iso(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();
}

/// <summary>Effective page copy: the catalog defaults with the editors' overrides (same rules as the web app's useSiteCopy).</summary>
public sealed partial class CopyReader(IReadOnlyDictionary<string, string> overrides)
{
    public string Text(string key, params (string Name, string Value)[] vars)
    {
        var value = overrides.TryGetValue(key, out var v) ? v : SiteCopyCatalog.ByKey.TryGetValue(key, out var def) ? def.Default : string.Empty;
        foreach (var (name, val) in vars) value = value.Replace("{" + name + "}", val, StringComparison.Ordinal);
        return value;
    }

    public IReadOnlyList<string> List(string key) =>
        Text(key).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    public IReadOnlyList<(string Title, string Text)> Pairs(string key) =>
        List(key).Select(l =>
        {
            var i = l.IndexOf('|');
            return i < 0 ? (l, string.Empty) : (l[..i].Trim(), l[(i + 1)..].Trim());
        }).ToList();
}

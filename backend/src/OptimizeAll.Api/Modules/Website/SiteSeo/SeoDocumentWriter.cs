using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>
/// Writes the complete HTML document for a public URL: SEO head (title, description, canonical, robots, Open Graph,
/// Twitter, article dates, pagination, JSON-LD) and the server-rendered content, around the web app's shell.
///
/// The shell (the Vite build's script/style/icon tags) is not known to the API: the document carries two nginx SSI
/// directives, <c>&lt;!--# include virtual="/__shell/head.html" --&gt;</c> and <c>…/__shell/body.html</c>, which the web
/// server replaces with fragments extracted from the built index.html at build time (frontend/vite.config.ts,
/// <c>seoShell</c> plugin). In development and <c>vite preview</c> the same plugin fills them in. See docs/SEO_CRO.md.
/// </summary>
public static class SeoDocumentWriter
{
    public const string ShellHeadInclude = "<!--# include virtual=\"/__shell/head.html\" -->";
    public const string ShellBodyInclude = "<!--# include virtual=\"/__shell/body.html\" -->";

    /// <summary>
    /// Hides the server-rendered copy from browsers that run scripts (the React app renders the page; no flash of
    /// duplicate content). Crawlers without JavaScript and visitors with scripts off see it with these minimal styles.
    /// </summary>
    private const string InlineStyle =
        "<style>@media (scripting:enabled){#oa-ssr{display:none}}" +
        ".oa-ssr{max-width:72rem;margin:0 auto;padding:1rem 1rem 3rem;font:1rem/1.6 system-ui,-apple-system,'Segoe UI',sans-serif;color:#141833}" +
        ".oa-ssr a{color:#243ab8}.oa-ssr img,.oa-ssr video,.oa-ssr iframe{max-width:100%;height:auto}" +
        ".oa-ssr nav ul,.oa-ssr nav ol,.oa-ssr footer ul{display:flex;flex-wrap:wrap;gap:.25rem 1rem;list-style:none;padding:0}" +
        ".oa-ssr__skip{position:absolute;left:-999px}.oa-ssr__skip:focus{left:1rem}.oa-ssr dt{font-weight:600}" +
        ".oa-ssr footer{margin-top:3rem;border-top:1px solid #ccd;font-size:.9rem}</style>";

    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = JavaScriptEncoder.Default };

    /// <summary>
    /// The document for <paramref name="page"/>. <paramref name="partnerLinks"/> are the active partners' link rules: every
    /// link to a partner's website in the page gets <c>rel="sponsored noopener"</c>, <c>target="_blank"</c> and the
    /// partner's UTM tags (<see cref="SeoPartnerLinks"/>, Google's link-spam policy).
    /// </summary>
    public static string Write(SeoPage page, SiteChromeLinks chrome, string siteName, string? twitterHandle, string baseUrl,
        IReadOnlyList<OptimizeAll.Domain.Website.PartnerLinkRule>? partnerLinks = null)
    {
        var e = (Func<string?, string>)SeoHtml.Attr;
        var sb = new StringBuilder(16 * 1024);
        sb.Append("<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"UTF-8\">\n");
        sb.Append("<title>").Append(SeoHtml.E(page.Title)).Append("</title>\n");
        void Meta(string attr, string key, string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            sb.Append("<meta ").Append(attr).Append("=\"").Append(key).Append("\" content=\"").Append(e(content)).Append("\" data-oa-head data-oa-ssr>\n");
        }
        Meta("name", "description", page.Description);
        Meta("name", "robots", page.Robots);
        if (page.Canonical is not null) sb.Append("<link rel=\"canonical\" href=\"").Append(e(page.Canonical)).Append("\" data-oa-head data-oa-ssr>\n");
        if (page.PrevUrl is not null) sb.Append("<link rel=\"prev\" href=\"").Append(e(baseUrl + page.PrevUrl)).Append("\" data-oa-ssr>\n");
        if (page.NextUrl is not null) sb.Append("<link rel=\"next\" href=\"").Append(e(baseUrl + page.NextUrl)).Append("\" data-oa-ssr>\n");

        var url = page.Canonical ?? baseUrl + page.Path;
        Meta("property", "og:type", page.OgType);
        Meta("property", "og:site_name", siteName);
        Meta("property", "og:locale", "en_US");
        Meta("property", "og:title", page.Title);
        Meta("property", "og:description", page.Description);
        Meta("property", "og:url", url);
        Meta("property", "og:image", page.OgImage);
        if (page.OgImageWidth is { } w) Meta("property", "og:image:width", w.ToString(CultureInfo.InvariantCulture));
        if (page.OgImageHeight is { } h) Meta("property", "og:image:height", h.ToString(CultureInfo.InvariantCulture));
        Meta("property", "og:image:alt", page.OgImageAlt);
        if (page.OgType == "article")
        {
            if (page.PublishedAt is { } published) Meta("property", "article:published_time", SeoText.Iso(published));
            if (page.ModifiedAt is { } modified) Meta("property", "article:modified_time", SeoText.Iso(modified));
            Meta("property", "article:section", page.Section);
        }
        // A square default logo reads better as a small card; page images get the large card.
        var squareDefault = page.OgImageWidth is not null && page.OgImageWidth == page.OgImageHeight;
        Meta("name", "twitter:card", squareDefault ? "summary" : "summary_large_image");
        Meta("name", "twitter:title", page.Title);
        Meta("name", "twitter:description", page.Description);
        Meta("name", "twitter:image", page.OgImage);
        Meta("name", "twitter:image:alt", page.OgImageAlt);
        Meta("name", "twitter:site", twitterHandle);

        if (page.Kind == SeoPageKind.Content)
        {
            sb.Append("<link rel=\"alternate\" type=\"application/rss+xml\" title=\"").Append(e(siteName)).Append(" blog\" href=\"/api/v1/public/blog/rss.xml\">\n");
            if (page.IsIndexable)
                sb.Append("<link rel=\"alternate\" type=\"text/markdown\" title=\"").Append(e(page.Title)).Append("\" href=\"")
                    .Append(e(SeoMarkdownPaths.MarkdownPath(page.Path))).Append("\">\n");
        }
        foreach (var node in page.JsonLd)
            sb.Append("<script type=\"application/ld+json\" data-oa-head data-oa-ssr>").Append(JsonSerializer.Serialize(node, JsonOptions)).Append("</script>\n");
        foreach (var img in page.Content.OfType<ImageNode>().Where(i => i.Priority).Take(1))
            sb.Append("<link rel=\"preload\" as=\"image\" href=\"").Append(e(img.Src)).Append("\" fetchpriority=\"high\">\n");
        sb.Append(InlineStyle).Append('\n');
        sb.Append(ShellHeadInclude).Append("\n</head>\n<body>\n<div id=\"root\">");
        var body = new StringBuilder(8 * 1024);
        SeoHtml.WriteBody(body, page, chrome);
        sb.Append(SeoPartnerLinks.Apply(body.ToString(), partnerLinks ?? Array.Empty<OptimizeAll.Domain.Website.PartnerLinkRule>()));
        sb.Append("</div>\n").Append(ShellBodyInclude).Append("\n</body>\n</html>\n");
        return sb.ToString();
    }
}

/// <summary>Where the Markdown version of a page lives: <c>/index.md</c> for the home page, <c>/{path}.md</c> otherwise.</summary>
public static class SeoMarkdownPaths
{
    public static string MarkdownPath(string path) => path == "/" ? "/index.md" : path + ".md";

    public static string? PagePath(string markdownPath)
    {
        if (!markdownPath.EndsWith(".md", StringComparison.Ordinal)) return null;
        var p = markdownPath[..^3];
        return p == "/index" ? "/" : p;
    }
}

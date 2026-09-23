using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using OptimizeAll.Domain.Seo;

namespace OptimizeAll.Api.Modules.Seo.Crawling;

public sealed record PageLink(string Href, string? Rel, string Text);

public sealed record PageImage(string? Src, string? Alt);

public sealed record PageHeading(int Level, string Text);

public sealed record Hreflang(string Lang, string Href);

/// <summary>Everything the audit checks and the on-page analyzer need from one HTML document.</summary>
public sealed class PageData
{
    public string? Title { get; init; }
    public int TitleCount { get; init; }
    public string? MetaDescription { get; init; }
    public string? MetaRobots { get; init; }
    public string? Canonical { get; init; }
    public string? Lang { get; init; }
    public IReadOnlyList<string> H1 { get; init; } = Array.Empty<string>();
    public IReadOnlyList<PageHeading> Headings { get; init; } = Array.Empty<PageHeading>();
    public IReadOnlyList<PageImage> Images { get; init; } = Array.Empty<PageImage>();
    public IReadOnlyList<PageLink> Links { get; init; } = Array.Empty<PageLink>();
    public IReadOnlyList<Hreflang> Hreflangs { get; init; } = Array.Empty<Hreflang>();
    public IReadOnlyDictionary<string, string> OpenGraph { get; init; } = new Dictionary<string, string>();
    public bool HasViewport { get; init; }
    public IReadOnlyList<string> JsonLd { get; init; } = Array.Empty<string>();
    public bool HasMicrodata { get; init; }

    /// <summary>Insecure (http://) subresources referenced by the page.</summary>
    public IReadOnlyList<string> InsecureResources { get; init; } = Array.Empty<string>();
    public string Text { get; init; } = string.Empty;
    public int WordCount { get; init; }
    public ulong SimHash { get; init; }

    public bool IsNoindex(string? xRobotsTag = null) =>
        (MetaRobots?.Contains("noindex", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (MetaRobots?.Contains("none", StringComparison.OrdinalIgnoreCase) ?? false) ||
        (xRobotsTag?.Contains("noindex", StringComparison.OrdinalIgnoreCase) ?? false);

    public IEnumerable<string> InvalidJsonLd()
    {
        foreach (var block in JsonLd)
        {
            string? error = null;
            try { using var _ = JsonDocument.Parse(block); }
            catch (JsonException ex) { error = ex.Message; }
            if (error is not null) yield return error;
        }
    }
}

/// <summary>Parses HTML with AngleSharp (MIT; no scripting) into <see cref="PageData"/>. Links/resources are made absolute.</summary>
public static class HtmlPageExtractor
{
    private static readonly HtmlParser Parser = new(new HtmlParserOptions { IsScripting = false });

    public static PageData Extract(string html, Uri pageUrl)
    {
        var doc = Parser.ParseDocument(html);
        var baseUrl = pageUrl;
        if (doc.QuerySelector("base[href]")?.GetAttribute("href") is { } baseHref && Uri.TryCreate(pageUrl, baseHref, out var b))
            baseUrl = b;

        string? Abs(string? href)
        {
            if (string.IsNullOrWhiteSpace(href)) return null;
            href = href.Trim();
            return Uri.TryCreate(baseUrl, href, out var u) ? u.AbsoluteUri : null;
        }

        string? Meta(string name) =>
            doc.QuerySelectorAll("meta[name]").FirstOrDefault(m => string.Equals(m.GetAttribute("name"), name, StringComparison.OrdinalIgnoreCase))
                ?.GetAttribute("content")?.Trim();

        var titles = doc.QuerySelectorAll("head title, title").Distinct().ToList();
        var og = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in doc.QuerySelectorAll("meta[property^='og:'], meta[name^='og:']"))
        {
            var key = (m.GetAttribute("property") ?? m.GetAttribute("name"))!.Trim();
            var content = m.GetAttribute("content")?.Trim();
            if (!string.IsNullOrEmpty(content) && !og.ContainsKey(key)) og[key] = content;
        }

        var headings = doc.QuerySelectorAll("h1, h2, h3, h4, h5, h6")
            .Select(h => new PageHeading(h.LocalName[1] - '0', Collapse(h.TextContent))).ToList();

        var links = doc.QuerySelectorAll("a[href]")
            .Select(a => (Href: Abs(a.GetAttribute("href")), Rel: a.GetAttribute("rel"), Text: Collapse(a.TextContent)))
            .Where(l => l.Href is not null && (l.Href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || l.Href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            .Select(l => new PageLink(StripFragment(l.Href!), l.Rel, l.Text.Length > 200 ? l.Text[..200] : l.Text))
            .ToList();

        var hreflangs = doc.QuerySelectorAll("link[rel='alternate'][hreflang]")
            .Select(l => new Hreflang(l.GetAttribute("hreflang")!.Trim(), Abs(l.GetAttribute("href")) ?? string.Empty))
            .ToList();

        var canonicalEl = doc.QuerySelectorAll("link[rel]").FirstOrDefault(l =>
            (l.GetAttribute("rel") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("canonical", StringComparer.OrdinalIgnoreCase));

        var insecure = new List<string>();
        if (pageUrl.Scheme == Uri.UriSchemeHttps)
        {
            void Check(string selector, string attribute)
            {
                foreach (var el in doc.QuerySelectorAll(selector))
                {
                    var abs = Abs(el.GetAttribute(attribute));
                    if (abs is not null && abs.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) insecure.Add(abs);
                }
            }
            Check("img[src]", "src");
            Check("script[src]", "src");
            Check("iframe[src]", "src");
            Check("video[src]", "src");
            Check("audio[src]", "src");
            Check("source[src]", "src");
            Check("embed[src]", "src");
            Check("object[data]", "data");
            foreach (var l in doc.QuerySelectorAll("link[href]"))
            {
                var rel = l.GetAttribute("rel") ?? string.Empty;
                if (!rel.Contains("stylesheet", StringComparison.OrdinalIgnoreCase) && !rel.Contains("icon", StringComparison.OrdinalIgnoreCase) &&
                    !rel.Contains("preload", StringComparison.OrdinalIgnoreCase)) continue;
                var abs = Abs(l.GetAttribute("href"));
                if (abs is not null && abs.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) insecure.Add(abs);
            }
        }

        var jsonLd = doc.QuerySelectorAll("script[type]")
            .Where(s => string.Equals(s.GetAttribute("type")?.Trim(), "application/ld+json", StringComparison.OrdinalIgnoreCase))
            .Select(s => s.TextContent).ToList();

        var text = VisibleText(doc);
        return new PageData
        {
            Title = titles.Count > 0 ? Collapse(titles[0].TextContent) : null,
            TitleCount = titles.Count,
            MetaDescription = Meta("description"),
            MetaRobots = string.Join(", ", new[] { Meta("robots"), Meta("googlebot") }.Where(s => !string.IsNullOrEmpty(s))) is { Length: > 0 } r ? r : null,
            Canonical = canonicalEl is null ? null : Abs(canonicalEl.GetAttribute("href")),
            Lang = doc.DocumentElement.GetAttribute("lang"),
            H1 = headings.Where(h => h.Level == 1).Select(h => h.Text).ToList(),
            Headings = headings,
            Images = doc.QuerySelectorAll("img").Select(i => new PageImage(Abs(i.GetAttribute("src")), i.GetAttribute("alt"))).ToList(),
            Links = links,
            Hreflangs = hreflangs,
            OpenGraph = og,
            HasViewport = !string.IsNullOrEmpty(Meta("viewport")),
            JsonLd = jsonLd,
            HasMicrodata = doc.QuerySelector("[itemscope]") is not null,
            InsecureResources = insecure.Distinct().ToList(),
            Text = text,
            WordCount = SeoText.WordCount(text),
            SimHash = SeoText.SimHash(text),
        };
    }

    /// <summary>Body text without script/style/template/noscript content, whitespace collapsed.</summary>
    public static string VisibleText(IDocument doc)
    {
        if (doc.Body is null) return string.Empty;
        var clone = (IElement)doc.Body.Clone(deep: true);
        foreach (var el in clone.QuerySelectorAll("script, style, template, noscript, svg").ToList()) el.Remove();
        // Separate block-level elements so words from adjacent blocks do not merge.
        foreach (var el in clone.QuerySelectorAll("p, div, li, h1, h2, h3, h4, h5, h6, br, td, th, section, article, header, footer").ToList())
            el.Insert(AdjacentPosition.BeforeEnd, "\n");
        return Collapse(clone.TextContent, keepParagraphs: true);
    }

    public static string StripFragment(string url)
    {
        var hash = url.IndexOf('#');
        return hash >= 0 ? url[..hash] : url;
    }

    private static string Collapse(string text, bool keepParagraphs = false)
    {
        if (!keepParagraphs) return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var lines = text.Split('\n').Select(l => string.Join(' ', l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Where(l => l.Length > 0);
        return string.Join('\n', lines);
    }
}

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>
/// The crawlable content of a public page as a small, format-neutral tree. <see cref="SeoHtml"/> writes it as semantic HTML
/// for the server-rendered document, <see cref="SeoMarkdown"/> as Markdown for <c>llms-full.txt</c> and the <c>.md</c>
/// page versions. All text is plain text (encoded on output); <see cref="MarkdownNode"/> holds CMS Markdown, which is
/// already sanitized on save (raw HTML removed) and is rendered by <see cref="SafeMarkdown"/>.
/// </summary>
public abstract record ContentNode;

/// <summary>A heading. Level 1 is the page's single h1.</summary>
public sealed record HeadingNode(int Level, string Text) : ContentNode;

public sealed record ParagraphNode(string Text) : ContentNode;

/// <summary>CMS Markdown. Its first heading level becomes <see cref="BaseLevel"/> (as in the web app).</summary>
public sealed record MarkdownNode(string Markdown, int BaseLevel = 2) : ContentNode;

public sealed record LinkItem(string Text, string Href, string? Description = null);

public sealed record LinkListNode(IReadOnlyList<LinkItem> Items) : ContentNode;

public sealed record ListNode(IReadOnlyList<string> Items, bool Ordered = false) : ContentNode;

/// <summary>Term / value pairs (prices, metrics, job facts) as a description list.</summary>
public sealed record FactsNode(IReadOnlyList<KeyValuePair<string, string>> Items) : ContentNode;

public sealed record QuestionNode(string Question, string AnswerMarkdown) : ContentNode;

public sealed record QuoteNode(string Text, string? Cite) : ContentNode;

/// <summary>A call-to-action link.</summary>
public sealed record ActionNode(string Text, string Href) : ContentNode;

/// <summary>An image; <see cref="Priority"/> marks the likely LCP element (eager, fetchpriority=high).</summary>
public sealed record ImageNode(string Src, string Alt, int Width, int Height, bool Priority = false) : ContentNode;

public sealed record VideoNode(SeoVideo Video) : ContentNode;

/// <summary>
/// A video on a page: self-hosted files (MP4/WebM sources, poster, WebVTT captions) or a YouTube/Vimeo embed. Feeds the
/// page's <c>VideoObject</c> JSON-LD, the <c>&lt;video&gt;</c> element and the video sitemap. URLs are absolute.
/// </summary>
public sealed record SeoVideo(
    string Name, string Description, string? Mp4Url, string? WebmUrl, string? PosterUrl, string? CaptionsUrl, string CaptionsLanguage,
    string? EmbedUrl, DateTime UploadDate, int? DurationSeconds, string? TranscriptMarkdown = null)
{
    public string? ContentUrl => Mp4Url ?? WebmUrl;
}

/// <summary>An image for the image sitemap (absolute URL).</summary>
public sealed record SeoImage(string Url, string? Title);

public sealed record Crumb(string Name, string Path);

/// <summary>Header/footer links written around every server-rendered page so crawlers can follow the site's structure.</summary>
public sealed record SiteChromeLinks(string SiteName, IReadOnlyList<LinkItem> Header, IReadOnlyList<(string Title, IReadOnlyList<LinkItem> Links)> Footer,
    IReadOnlyList<LinkItem> Legal)
{
    /// <summary>Whether /llms.txt is published (Website → SEO), so the footer links it.</summary>
    public bool LlmsTxt { get; init; } = true;
}

/// <summary>Writes the content tree and site chrome as HTML (every text encoded; links limited to safe schemes).</summary>
public static class SeoHtml
{
    public static string E(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    public static string Attr(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    public static void WriteBody(StringBuilder sb, SeoPage page, SiteChromeLinks chrome)
    {
        sb.Append("<div id=\"oa-ssr\" class=\"oa-ssr\">\n");
        sb.Append("<a class=\"oa-ssr__skip\" href=\"#main\">Skip to content</a>\n");
        sb.Append("<header class=\"oa-ssr__header\"><nav aria-label=\"Main\"><a href=\"/\">").Append(E(chrome.SiteName)).Append("</a>");
        if (chrome.Header.Count > 0)
        {
            sb.Append("<ul>");
            foreach (var l in chrome.Header) sb.Append("<li>").Append(Link(l.Href, l.Text)).Append("</li>");
            sb.Append("</ul>");
        }
        sb.Append("</nav></header>\n<main id=\"main\">\n");
        if (page.Breadcrumbs.Count > 1)
        {
            sb.Append("<nav aria-label=\"Breadcrumb\"><ol>");
            for (var i = 0; i < page.Breadcrumbs.Count; i++)
            {
                var c = page.Breadcrumbs[i];
                sb.Append("<li>").Append(i == page.Breadcrumbs.Count - 1 ? $"<span aria-current=\"page\">{E(c.Name)}</span>" : Link(c.Path, c.Name))
                    .Append("</li>");
            }
            sb.Append("</ol></nav>\n");
        }
        sb.Append("<article>\n");
        WriteNodes(sb, page.Content);
        sb.Append("</article>\n");
        if (page.PrevUrl is not null || page.NextUrl is not null)
        {
            sb.Append("<nav aria-label=\"Pagination\">");
            if (page.PrevUrl is not null) sb.Append("<a rel=\"prev\" href=\"").Append(Attr(page.PrevUrl)).Append("\">Newer articles</a> ");
            if (page.NextUrl is not null) sb.Append("<a rel=\"next\" href=\"").Append(Attr(page.NextUrl)).Append("\">Older articles</a>");
            sb.Append("</nav>\n");
        }
        sb.Append("</main>\n<footer class=\"oa-ssr__footer\">");
        foreach (var (title, links) in chrome.Footer)
        {
            sb.Append("<section><h2>").Append(E(title)).Append("</h2><ul>");
            foreach (var l in links) sb.Append("<li>").Append(Link(l.Href, l.Text)).Append("</li>");
            sb.Append("</ul></section>");
        }
        if (chrome.Legal.Count > 0)
        {
            sb.Append("<nav aria-label=\"Legal\"><ul>");
            foreach (var l in chrome.Legal) sb.Append("<li>").Append(Link(l.Href, l.Text)).Append("</li>");
            sb.Append("</ul></nav>");
        }
        // Machine-readable versions of the site, for crawlers and AI assistants (docs/SEO_CRO.md § llms.txt).
        sb.Append("<p>Machine-readable: <a href=\"/sitemap.xml\">Sitemap</a> · ")
            .Append(chrome.LlmsTxt ? "<a href=\"/llms.txt\">llms.txt</a> · " : string.Empty)
            .Append("<a href=\"/api/v1/public/blog/rss.xml\">Blog RSS feed</a></p>");
        sb.Append("<p>&copy; ").Append(DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(E(chrome.SiteName)).Append("</p>");
        sb.Append("</footer>\n</div>");
    }

    public static void WriteNodes(StringBuilder sb, IEnumerable<ContentNode> nodes)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case HeadingNode h:
                    var level = Math.Clamp(h.Level, 1, 6);
                    sb.Append("<h").Append(level).Append('>').Append(E(h.Text)).Append("</h").Append(level).Append(">\n");
                    break;
                case ParagraphNode p:
                    sb.Append("<p>").Append(E(p.Text)).Append("</p>\n");
                    break;
                case MarkdownNode md:
                    sb.Append(SafeMarkdown.ToHtml(md.Markdown, md.BaseLevel));
                    break;
                case LinkListNode list:
                    sb.Append("<ul>");
                    foreach (var item in list.Items)
                    {
                        sb.Append("<li>").Append(Link(item.Href, item.Text));
                        if (!string.IsNullOrWhiteSpace(item.Description)) sb.Append(" — ").Append(E(item.Description));
                        sb.Append("</li>");
                    }
                    sb.Append("</ul>\n");
                    break;
                case ListNode list:
                    var tag = list.Ordered ? "ol" : "ul";
                    sb.Append('<').Append(tag).Append('>');
                    foreach (var item in list.Items) sb.Append("<li>").Append(E(item)).Append("</li>");
                    sb.Append("</").Append(tag).Append(">\n");
                    break;
                case FactsNode facts:
                    sb.Append("<dl>");
                    foreach (var (term, value) in facts.Items) sb.Append("<dt>").Append(E(term)).Append("</dt><dd>").Append(E(value)).Append("</dd>");
                    sb.Append("</dl>\n");
                    break;
                case QuestionNode q:
                    sb.Append("<h3>").Append(E(q.Question)).Append("</h3>\n").Append(SafeMarkdown.ToHtml(q.AnswerMarkdown, 4));
                    break;
                case QuoteNode quote:
                    sb.Append("<blockquote><p>").Append(E(quote.Text)).Append("</p>");
                    if (!string.IsNullOrWhiteSpace(quote.Cite)) sb.Append("<footer>— ").Append(E(quote.Cite)).Append("</footer>");
                    sb.Append("</blockquote>\n");
                    break;
                case ActionNode a:
                    sb.Append("<p>").Append(Link(a.Href, a.Text)).Append("</p>\n");
                    break;
                case ImageNode img:
                    sb.Append("<img src=\"").Append(Attr(img.Src)).Append("\" alt=\"").Append(Attr(img.Alt)).Append("\" width=\"")
                        .Append(img.Width.ToString(CultureInfo.InvariantCulture)).Append("\" height=\"").Append(img.Height.ToString(CultureInfo.InvariantCulture))
                        .Append(img.Priority ? "\" fetchpriority=\"high\" decoding=\"async\">\n" : "\" loading=\"lazy\" decoding=\"async\">\n");
                    break;
                case VideoNode v:
                    WriteVideo(sb, v.Video);
                    break;
            }
        }
    }

    /// <summary>
    /// An accessible, lazy player: <c>preload="none"</c> with a poster (nothing downloads until play), WebM + MP4 sources,
    /// a captions track, and a download link as the fallback. Embeds (YouTube/Vimeo) are written as a titled iframe.
    /// </summary>
    public static void WriteVideo(StringBuilder sb, SeoVideo v)
    {
        sb.Append("<figure>");
        if (v.ContentUrl is not null)
        {
            sb.Append("<video controls preload=\"none\" playsinline width=\"1280\" height=\"720\"");
            if (v.PosterUrl is not null) sb.Append(" poster=\"").Append(Attr(v.PosterUrl)).Append('"');
            sb.Append(" aria-label=\"").Append(Attr(v.Name)).Append("\">");
            if (v.WebmUrl is not null) sb.Append("<source src=\"").Append(Attr(v.WebmUrl)).Append("\" type=\"video/webm\">");
            if (v.Mp4Url is not null) sb.Append("<source src=\"").Append(Attr(v.Mp4Url)).Append("\" type=\"video/mp4\">");
            if (v.CaptionsUrl is not null)
                sb.Append("<track kind=\"captions\" src=\"").Append(Attr(v.CaptionsUrl)).Append("\" srclang=\"").Append(Attr(v.CaptionsLanguage))
                    .Append("\" label=\"").Append(Attr(LanguageLabel(v.CaptionsLanguage))).Append("\" default>");
            sb.Append("<a href=\"").Append(Attr(v.ContentUrl)).Append("\">Download the video: ").Append(E(v.Name)).Append("</a></video>");
        }
        else if (v.EmbedUrl is not null)
        {
            sb.Append("<iframe src=\"").Append(Attr(v.EmbedUrl)).Append("\" title=\"").Append(Attr(v.Name))
                .Append("\" width=\"1280\" height=\"720\" loading=\"lazy\" allow=\"fullscreen; picture-in-picture\"></iframe>");
        }
        sb.Append("<figcaption>").Append(E(v.Name));
        if (!string.IsNullOrWhiteSpace(v.Description)) sb.Append(" — ").Append(E(v.Description));
        sb.Append("</figcaption></figure>\n");
        if (!string.IsNullOrWhiteSpace(v.TranscriptMarkdown))
            sb.Append("<details><summary>Transcript</summary>").Append(SafeMarkdown.ToHtml(v.TranscriptMarkdown, 3)).Append("</details>\n");
    }

    public static string LanguageLabel(string code)
    {
        try
        {
            return CultureInfo.GetCultureInfo(code).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }

    public static string Link(string href, string text) =>
        SafeMarkdown.SafeUrl(href) is { } url ? $"<a href=\"{Attr(url)}\">{E(text)}</a>" : E(text);
}

/// <summary>Writes the content tree as Markdown (llms-full.txt, <c>/{path}.md</c>). Links become absolute.</summary>
public static class SeoMarkdown
{
    public static string Write(SeoPage page, Func<string, string> absolute)
    {
        var sb = new StringBuilder();
        foreach (var node in page.Content) WriteNode(sb, node, absolute);
        return Regex.Replace(sb.ToString(), "\n{3,}", "\n\n").Trim() + "\n";
    }

    private static string Esc(string text) => text.Replace("\r", string.Empty).Replace("\n", " ").Trim();

    private static string LinkMd(string text, string href, Func<string, string> absolute) =>
        $"[{Esc(text).Replace("[", "\\[").Replace("]", "\\]")}]({(href.StartsWith('/') ? absolute(href) : href)})";

    private static void WriteNode(StringBuilder sb, ContentNode node, Func<string, string> absolute)
    {
        switch (node)
        {
            case HeadingNode h:
                sb.Append('\n').Append(new string('#', Math.Clamp(h.Level, 1, 6))).Append(' ').Append(Esc(h.Text)).Append("\n\n");
                break;
            case ParagraphNode p:
                sb.Append(p.Text.Trim()).Append("\n\n");
                break;
            case MarkdownNode md:
                sb.Append(SafeMarkdown.ShiftHeadings(md.Markdown, md.BaseLevel).Trim()).Append("\n\n");
                break;
            case LinkListNode list:
                foreach (var i in list.Items)
                    sb.Append("- ").Append(LinkMd(i.Text, i.Href, absolute))
                        .Append(string.IsNullOrWhiteSpace(i.Description) ? string.Empty : ": " + Esc(i.Description)).Append('\n');
                sb.Append('\n');
                break;
            case ListNode list:
                var n = 1;
                foreach (var i in list.Items) sb.Append(list.Ordered ? $"{n++}. " : "- ").Append(Esc(i)).Append('\n');
                sb.Append('\n');
                break;
            case FactsNode facts:
                foreach (var (term, value) in facts.Items) sb.Append("- **").Append(Esc(term)).Append(":** ").Append(Esc(value)).Append('\n');
                sb.Append('\n');
                break;
            case QuestionNode q:
                sb.Append("**").Append(Esc(q.Question)).Append("**\n\n").Append(q.AnswerMarkdown.Trim()).Append("\n\n");
                break;
            case QuoteNode quote:
                sb.Append("> ").Append(Esc(quote.Text)).Append('\n');
                if (!string.IsNullOrWhiteSpace(quote.Cite)) sb.Append("> — ").Append(Esc(quote.Cite)).Append('\n');
                sb.Append('\n');
                break;
            case ActionNode a:
                sb.Append(LinkMd(a.Text, a.Href, absolute)).Append("\n\n");
                break;
            case ImageNode img when !string.IsNullOrWhiteSpace(img.Alt):
                sb.Append("![").Append(Esc(img.Alt)).Append("](").Append(img.Src.StartsWith('/') ? absolute(img.Src) : img.Src).Append(")\n\n");
                break;
            case VideoNode v:
                sb.Append("Video: ").Append(LinkMd(v.Video.Name, v.Video.ContentUrl ?? v.Video.EmbedUrl ?? string.Empty, absolute));
                if (!string.IsNullOrWhiteSpace(v.Video.Description)) sb.Append(" — ").Append(Esc(v.Video.Description));
                sb.Append("\n\n");
                if (!string.IsNullOrWhiteSpace(v.Video.TranscriptMarkdown)) sb.Append("Transcript:\n\n").Append(v.Video.TranscriptMarkdown.Trim()).Append("\n\n");
                break;
        }
    }
}

/// <summary>
/// A deliberately small Markdown renderer for sanitized CMS Markdown: ATX headings, paragraphs, lists, blockquotes,
/// fenced code, links, images, bold, italic and inline code. Every text run is HTML-encoded and link targets are limited
/// to http(s), mailto, tel and site-relative URLs, so its output is safe to embed.
/// </summary>
public static partial class SafeMarkdown
{
    public static string? SafeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var u = url.Trim();
        if (u.StartsWith("//", StringComparison.Ordinal)) return null;
        if (u.StartsWith('/') || u.StartsWith('#')) return u;
        return u.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               u.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || u.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
            ? u
            : null;
    }

    /// <summary>Moves headings so the first level used becomes <paramref name="baseLevel"/> (capped at 6).</summary>
    public static string ShiftHeadings(string markdown, int baseLevel)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var inFence = false;
        var min = 7;
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) inFence = !inFence;
            else if (!inFence && HeadingRegex().Match(line) is { Success: true } m) min = Math.Min(min, m.Groups[1].Length);
        }
        if (min == 7) return markdown;
        var shift = baseLevel - min;
        inFence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("```", StringComparison.Ordinal)) inFence = !inFence;
            else if (!inFence && HeadingRegex().Match(lines[i]) is { Success: true } m)
                lines[i] = new string('#', Math.Clamp(m.Groups[1].Length + shift, 1, 6)) + " " + m.Groups[2].Value;
        }
        return string.Join('\n', lines);
    }

    public static string ToHtml(string? markdown, int baseLevel = 2)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
        var lines = ShiftHeadings(markdown, baseLevel).Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        var para = new List<string>();
        string? listTag = null;
        var quote = new List<string>();

        void FlushPara()
        {
            if (para.Count == 0) return;
            sb.Append("<p>").Append(Inline(string.Join(' ', para.Select(p => p.Trim())))).Append("</p>\n");
            para.Clear();
        }
        void CloseList()
        {
            if (listTag is null) return;
            sb.Append("</").Append(listTag).Append(">\n");
            listTag = null;
        }
        void FlushQuote()
        {
            if (quote.Count == 0) return;
            sb.Append("<blockquote><p>").Append(Inline(string.Join(' ', quote))).Append("</p></blockquote>\n");
            quote.Clear();
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                FlushPara(); CloseList(); FlushQuote();
                var code = new StringBuilder();
                for (i++; i < lines.Length && !lines[i].Trim().StartsWith("```", StringComparison.Ordinal); i++) code.Append(lines[i]).Append('\n');
                sb.Append("<pre><code>").Append(WebUtility.HtmlEncode(code.ToString())).Append("</code></pre>\n");
                continue;
            }
            if (trimmed.Length == 0)
            {
                FlushPara(); CloseList(); FlushQuote();
                continue;
            }
            if (HeadingRegex().Match(line) is { Success: true } h)
            {
                FlushPara(); CloseList(); FlushQuote();
                var level = h.Groups[1].Length;
                sb.Append("<h").Append(level).Append('>').Append(Inline(h.Groups[2].Value.Trim().TrimEnd('#').Trim())).Append("</h").Append(level).Append(">\n");
                continue;
            }
            if (trimmed.StartsWith('>'))
            {
                FlushPara(); CloseList();
                quote.Add(trimmed.TrimStart('>').Trim());
                continue;
            }
            var bullet = BulletRegex().Match(line);
            var ordered = OrderedRegex().Match(line);
            if (bullet.Success || ordered.Success)
            {
                FlushPara(); FlushQuote();
                var tag = bullet.Success ? "ul" : "ol";
                if (listTag != tag)
                {
                    CloseList();
                    sb.Append('<').Append(tag).Append('>');
                    listTag = tag;
                }
                sb.Append("<li>").Append(Inline((bullet.Success ? bullet : ordered).Groups[1].Value.Trim())).Append("</li>");
                continue;
            }
            if (HruleRegex().IsMatch(trimmed))
            {
                FlushPara(); CloseList(); FlushQuote();
                sb.Append("<hr>\n");
                continue;
            }
            if (listTag is not null && line.StartsWith("  ", StringComparison.Ordinal))
            {
                // Continuation of a list item: append to the last item.
                var close = sb.ToString().LastIndexOf("</li>", StringComparison.Ordinal);
                if (close >= 0) sb.Insert(close, " " + Inline(trimmed));
                continue;
            }
            CloseList(); FlushQuote();
            para.Add(line);
        }
        FlushPara(); CloseList(); FlushQuote();
        return sb.ToString();
    }

    /// <summary>Inline Markdown → HTML: code spans, images, links, bold, italic; everything else encoded.</summary>
    public static string Inline(string text)
    {
        var sb = new StringBuilder();
        var pos = 0;
        foreach (Match m in InlineRegex().Matches(text))
        {
            sb.Append(WebUtility.HtmlEncode(text[pos..m.Index]));
            if (m.Groups["code"].Success) sb.Append("<code>").Append(WebUtility.HtmlEncode(m.Groups["code"].Value)).Append("</code>");
            else if (m.Groups["img"].Success)
            {
                var src = SafeUrl(m.Groups["src"].Value);
                if (src is not null)
                    sb.Append("<img src=\"").Append(WebUtility.HtmlEncode(src)).Append("\" alt=\"").Append(WebUtility.HtmlEncode(m.Groups["alt"].Value))
                        .Append("\" loading=\"lazy\" decoding=\"async\">");
                else sb.Append(WebUtility.HtmlEncode(m.Groups["alt"].Value));
            }
            else if (m.Groups["link"].Success)
            {
                var href = SafeUrl(m.Groups["href"].Value);
                var inner = Inline(m.Groups["label"].Value);
                if (href is null) sb.Append(inner);
                else
                {
                    var external = href.StartsWith("http", StringComparison.OrdinalIgnoreCase);
                    sb.Append("<a href=\"").Append(WebUtility.HtmlEncode(href)).Append('"')
                        .Append(external ? " rel=\"noopener\"" : string.Empty).Append('>').Append(inner).Append("</a>");
                }
            }
            else if (m.Groups["bold"].Success) sb.Append("<strong>").Append(Inline(m.Groups["bold"].Value)).Append("</strong>");
            else if (m.Groups["em"].Success) sb.Append("<em>").Append(Inline(m.Groups["em"].Value)).Append("</em>");
            pos = m.Index + m.Length;
        }
        sb.Append(WebUtility.HtmlEncode(text[pos..]));
        return sb.ToString();
    }

    [GeneratedRegex(@"^(#{1,6})\s+(.*)$")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^\s{0,3}[-*+]\s+(.*)$")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"^\s{0,3}\d{1,9}[.)]\s+(.*)$")]
    private static partial Regex OrderedRegex();

    [GeneratedRegex(@"^([-*_])(\s*\1){2,}$")]
    private static partial Regex HruleRegex();

    [GeneratedRegex(@"`(?<code>[^`]+)`|(?<img>!\[(?<alt>[^\]]*)\]\((?<src>[^)\s]+)(?:\s+""[^""]*"")?\))|(?<link>\[(?<label>[^\]]+)\]\((?<href>[^)\s]+)(?:\s+""[^""]*"")?\))|\*\*(?<bold>[^*]+)\*\*|__(?<bold>[^_]+)__|\*(?<em>[^*\s][^*]*)\*|(?<![A-Za-z0-9])_(?<em>[^_\s][^_]*)_(?![A-Za-z0-9])")]
    private static partial Regex InlineRegex();
}

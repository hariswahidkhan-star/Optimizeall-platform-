using System.Globalization;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>
/// <c>/llms.txt</c>, <c>/llms-full.txt</c> and the section files under <c>/llms/</c> (llmstxt.org): Markdown guides to the
/// site for AI assistants, generated from published content. Every linked page also has a Markdown version at
/// <c>/{path}.md</c> (<c>/index.md</c> for the home page), rendered from the same content as the HTML.
/// <list type="bullet">
/// <item><c>/llms.txt</c>: the index: key pages, services, industries, case studies, articles, careers, partners and every
/// academy course, each with a one-line summary.</item>
/// <item><c>/llms-full.txt</c>: the full text of the agency site plus the academy home and every course page (lessons
/// are in the academy guide and their own .md files, which keeps this file a sensible size).</item>
/// <item><c>/llms/academy.txt</c>: every course with its modules and each lesson's summary (and whether it has a video
/// lecture with a transcript), linking each lesson's Markdown version.</item>
/// </list>
/// Outputs are cached in memory for a few minutes: building them resolves every public page.
/// </summary>
public sealed class LlmsTxtService(SeoPageResolver resolver, IMemoryCache cache)
{
    /// <summary>At most this many pages go into llms-full.txt (newest blog posts are the ones left out beyond it).</summary>
    public const int FullMaxPages = 300;

    /// <summary>The section files under /llms/ (name → what it holds).</summary>
    public static readonly IReadOnlyDictionary<string, string> Sections = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["academy"] = "every course with its modules and a summary of each lesson",
    };

    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    private sealed record Entry(SitemapUrl Url, SeoPage Page);

    private static bool IsLesson(SeoPage page) => page.Source == "Lesson";

    private static bool IsAcademyIndexPage(Entry e) =>
        e.Url.Group == SeoPageResolver.GroupLearn && !IsLesson(e.Page) && e.Page.Source != "Certificate";

    private async Task<List<Entry>> EntriesAsync(Func<SitemapUrl, bool> include, CancellationToken ct)
    {
        var urls = await resolver.SitemapUrlsAsync(ct);
        var entries = new List<Entry>();
        foreach (var url in urls.Where(include))
        {
            var (path, query) = Split(url.Path);
            var page = await resolver.ResolveAsync(path, query, ct);
            if (page.IsIndexable) entries.Add(new Entry(url, page));
        }
        return entries;
    }

    private static (string Path, string? Query) Split(string url)
    {
        var q = url.IndexOf('?');
        return q < 0 ? (url, null) : (url[..q], url[q..]);
    }

    /// <summary>
    /// Built once per content state: the key includes the number of public URLs and their latest modification, so any
    /// published change (content, page texts, site settings) produces a fresh file at once; the entry also expires.
    /// </summary>
    private async Task<string> CachedAsync(string name, Func<Task<string>> build, CancellationToken ct)
    {
        var urls = await resolver.SitemapUrlsAsync(ct);
        var latest = urls.Select(u => u.LastModified ?? DateTime.MinValue).DefaultIfEmpty(DateTime.MinValue).Max();
        var key = $"llms:{name}:{resolver.BaseUrl}:{urls.Count}:{latest.Ticks}";
        return (await cache.GetOrCreateAsync(key, e =>
        {
            e.AbsoluteExpirationRelativeToNow = CacheFor;
            e.Size = 1;
            return build();
        }))!;
    }

    private static string Name(SeoPage page, string siteName) => SocialCards.SocialCardFactory.StripSite(page.Title, siteName);

    /// <summary>The Markdown address of a sitemap URL (topic archives, which carry a query string, link their HTML page).</summary>
    private string MarkdownUrl(SitemapUrl url) =>
        url.Path.Contains('?') ? resolver.Absolute(url.Path) : resolver.Absolute(SeoMarkdownPaths.MarkdownPath(url.Path));

    public async Task<string> LlmsTxtAsync(CancellationToken ct)
    {
        await resolver.EnsureLoadedAsync(ct);
        return await CachedAsync("index", async () =>
        {
            var s = resolver.Settings;
            // Lessons are listed in the academy guide, not here (hundreds of lines).
            var entries = await EntriesAsync(u => u.Group != SeoPageResolver.GroupLearn || u.Path.Count(ch => ch == '/') <= 2 || !u.Path.StartsWith("/learn/", StringComparison.Ordinal), ct);
            var sb = new StringBuilder();
            sb.Append("# ").Append(s.SiteName).Append("\n\n");
            sb.Append("> ").Append(s.Seo.DefaultDescription ?? s.Tagline).Append("\n\n");
            if (!string.IsNullOrWhiteSpace(s.Footer.Blurb)) sb.Append(s.Footer.Blurb).Append("\n\n");
            sb.Append(s.SiteName).Append(" has two pillars. Optimize All Academy: free, self-paced courses in AI, marketing, SEO, sales and ")
                .Append("business, each with a verifiable certificate (a unique code and a public verification page). Optimize All Agency: a ")
                .Append("full-service digital marketing agency (SEO, paid media, social, content, email, brand and web). It also runs a creator program ")
                .Append("(people with established social accounts are paid to share company-approved, clearly disclosed posts). ")
                .Append("Prices on this site are starting prices; ad spend is billed at cost. Results in case studies are labelled measured ")
                .Append("or estimated. Every link below has a Markdown version (the same URL ending in .md).\n\n");
            if (s.Contact.Email is not null) sb.Append("Contact: ").Append(s.Contact.Email).Append("\n\n");

            void Section(string title, IEnumerable<Entry> items, string? intro = null)
            {
                var list = items.ToList();
                if (list.Count == 0) return;
                sb.Append("## ").Append(title).Append("\n\n");
                if (intro is not null) sb.Append(intro).Append("\n\n");
                foreach (var e in list)
                {
                    sb.Append("- [").Append(Name(e.Page, s.SiteName)).Append("](").Append(MarkdownUrl(e.Url)).Append(')');
                    if (!string.IsNullOrWhiteSpace(e.Page.Description)) sb.Append(": ").Append(e.Page.Description);
                    sb.Append('\n');
                }
                sb.Append('\n');
            }

            var keyPaths = new[] { "/", "/academy", "/learn", "/services", "/pricing", "/case-studies", "/industries", "/about", "/how-we-work", "/team",
                "/contact", "/free-audit", "/get-a-quote", "/book-a-consultation" };
            Section("Key pages", keyPaths.Select(p => entries.FirstOrDefault(e => e.Url.Path == p)).OfType<Entry>());
            Section("Services", entries.Where(e => e.Url.Group == SeoPageResolver.GroupServices));
            Section("Industries", entries.Where(e => e.Url.Path.StartsWith("/industries/", StringComparison.Ordinal)));
            Section("Case studies", entries.Where(e => e.Url.Group == SeoPageResolver.GroupCaseStudies));
            Section("Blog", entries.Where(e => e.Url.Group == SeoPageResolver.GroupBlog && !e.Url.Path.Contains('?')).Take(50));
            Section("Blog topics", entries.Where(e => e.Url.Group == SeoPageResolver.GroupBlog && e.Url.Path.Contains('?')));
            Section("Careers", entries.Where(e => e.Url.Path == "/careers" || e.Url.Group == SeoPageResolver.GroupCareers));
            Section("Creator program", entries.Where(e => e.Url.Path is "/creators" or "/faq"));
            Section("Partners", entries.Where(e => e.Url.Group == SeoPageResolver.GroupPartners));
            // The academy home, its courses (and learning paths when published). Each course page lists its lessons.
            var academy = entries.Where(IsAcademyIndexPage).ToList();
            if (academy.Count > 0)
            {
                Section("Academy (free courses)", academy.Where(e => e.Url.Path != "/learn"),
                    $"Free, self-paced courses with verified certificates. The [academy guide]({resolver.Absolute("/llms/academy.txt")}) lists " +
                    "every course with its modules and a summary of each lesson; every lesson has a Markdown version (video lectures include their transcript).");
            }
            var listed = new HashSet<string>(keyPaths.Concat(new[] { "/careers", "/creators", "/faq", "/blog" }));
            Section("Optional", entries.Where(e => e.Url.Group == SeoPageResolver.GroupPages && !listed.Contains(e.Url.Path) &&
                                                   !e.Url.Path.StartsWith("/industries/", StringComparison.Ordinal))
                .Concat(entries.Where(e => e.Url.Path == "/blog")));
            sb.Append("## Machine-readable\n\n");
            sb.Append("- [Sitemap](").Append(resolver.Absolute("/sitemap.xml")).Append("): every indexable URL with its last update\n");
            sb.Append("- [Blog RSS](").Append(resolver.Absolute("/api/v1/public/blog/rss.xml")).Append("): the latest articles\n");
            sb.Append("- [Full text](").Append(resolver.Absolute("/llms-full.txt")).Append("): the content of the agency pages and course pages above in one Markdown file\n");
            foreach (var (name, what) in Sections)
                sb.Append("- [").Append(CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name)).Append(" guide](").Append(resolver.Absolute($"/llms/{name}.txt"))
                    .Append("): ").Append(what).Append('\n');
            return sb.ToString();
        }, ct);
    }

    public async Task<string> LlmsFullAsync(CancellationToken ct)
    {
        await resolver.EnsureLoadedAsync(ct);
        return await CachedAsync("full", async () =>
        {
            var s = resolver.Settings;
            var entries = (await EntriesAsync(u => u.Group != SeoPageResolver.GroupLearn || u.Path.Count(ch => ch == '/') <= 2, ct))
                .Where(e => !IsLesson(e.Page)).Take(FullMaxPages).ToList();
            var sb = new StringBuilder();
            sb.Append("# ").Append(s.SiteName).Append(" — full site content\n\n> ").Append(s.Seo.DefaultDescription ?? s.Tagline).Append("\n\n");
            sb.Append("Academy lessons are not repeated here: see ").Append(resolver.Absolute("/llms/academy.txt"))
                .Append(" and each lesson's Markdown version.\n\n");
            foreach (var e in entries)
            {
                sb.Append("---\n\n");
                sb.Append("URL: ").Append(resolver.Absolute(e.Url.Path)).Append('\n');
                if (e.Page.ModifiedAt is { } m) sb.Append("Updated: ").Append(m.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
                sb.Append('\n');
                sb.Append(SeoMarkdown.Write(e.Page, resolver.Absolute)).Append('\n');
            }
            return sb.ToString();
        }, ct);
    }

    /// <summary>A section file under /llms/ (null when there is no such section).</summary>
    public async Task<string?> SectionAsync(string name, CancellationToken ct)
    {
        if (!Sections.ContainsKey(name)) return null;
        await resolver.EnsureLoadedAsync(ct);
        return await CachedAsync("section:" + name, () => AcademyAsync(ct), ct);
    }

    private async Task<string> AcademyAsync(CancellationToken ct)
    {
        var s = resolver.Settings;
        var entries = await EntriesAsync(u => u.Group == SeoPageResolver.GroupLearn, ct);
        var byPath = entries.ToDictionary(e => e.Url.Path, StringComparer.Ordinal);
        var sb = new StringBuilder();
        sb.Append("# ").Append(s.SiteName).Append(" Academy — courses and lessons\n\n");
        if (byPath.TryGetValue("/learn", out var home) && home.Page.Description is { } d) sb.Append("> ").Append(d).Append("\n\n");
        sb.Append("Every course is free to read. Each lesson link below is its Markdown version (the lesson text, key takeaways and, ")
            .Append("for video lectures, the transcript); remove \".md\" for the web page. Certificates are verified at ")
            .Append(resolver.Absolute("/verify/certificates/{id}")).Append(".\n\n");
        var courses = entries.Where(e => e.Page.Source == "Course").ToList();
        var others = entries.Where(e => e.Url.Path != "/learn" && !IsLesson(e.Page) && e.Page.Source is not ("Course" or "Certificate")).ToList();
        if (others.Count > 0)
        {
            sb.Append("## Learning paths and more\n\n");
            foreach (var e in others)
            {
                sb.Append("- [").Append(Name(e.Page, s.SiteName)).Append("](").Append(MarkdownUrl(e.Url)).Append(')');
                if (!string.IsNullOrWhiteSpace(e.Page.Description)) sb.Append(": ").Append(e.Page.Description);
                sb.Append('\n');
            }
            sb.Append('\n');
        }
        foreach (var course in courses)
        {
            var p = course.Page;
            sb.Append("## ").Append(Name(p, s.SiteName)).Append("\n\n");
            sb.Append("Course: ").Append(MarkdownUrl(course.Url)).Append('\n');
            var meta = p.Content.OfType<ParagraphNode>().FirstOrDefault()?.Text;
            if (!string.IsNullOrWhiteSpace(meta)) sb.Append("Facts: ").Append(meta).Append('\n');
            if (p.ModifiedAt is { } m) sb.Append("Updated: ").Append(m.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append('\n');
            if (!string.IsNullOrWhiteSpace(p.Description)) sb.Append(p.Description).Append("\n\n");
            // The course page's own structure: module headings (h3 under "Course content") and their lesson links.
            var inContent = false;
            foreach (var node in p.Content)
            {
                switch (node)
                {
                    case HeadingNode { Level: 2 } h2:
                        inContent = h2.Text == "Course content";
                        if (h2.Text == "What you will learn") inContent = false;
                        break;
                    case HeadingNode { Level: 3 } h3 when inContent:
                        sb.Append("### ").Append(h3.Text).Append("\n\n");
                        break;
                    case LinkListNode list when inContent:
                        foreach (var item in list.Items)
                        {
                            var lessonPath = item.Href.Split('#')[0];
                            byPath.TryGetValue(lessonPath, out var lesson);
                            sb.Append("- [").Append(item.Text).Append("](").Append(resolver.Absolute(SeoMarkdownPaths.MarkdownPath(lessonPath))).Append(')');
                            var notes = new List<string>();
                            if (!string.IsNullOrWhiteSpace(item.Description)) notes.Add(item.Description!);
                            if (lesson is not null && (lesson.Page.Videos.Count > 0 || lesson.Page.Content.OfType<HeadingNode>().Any(x => x.Text == "Video transcript")))
                                notes.Add("video lecture with transcript");
                            if (notes.Count > 0) sb.Append(" (").Append(string.Join(", ", notes)).Append(')');
                            if (lesson?.Page.Description is { Length: > 0 } summary) sb.Append(": ").Append(summary);
                            sb.Append('\n');
                        }
                        sb.Append('\n');
                        break;
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>A YAML front-matter value (a JSON string is valid YAML and escapes quotes, colons and line breaks).</summary>
    private static string Q(string value) => System.Text.Json.JsonSerializer.Serialize(value,
        new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>The Markdown version of one page, or null when the page is not an indexable 200 page.</summary>
    public async Task<string?> PageMarkdownAsync(string path, CancellationToken ct)
    {
        var page = await resolver.ResolveAsync(path, null, ct);
        if (!page.IsIndexable) return null;
        var sb = new StringBuilder();
        sb.Append("---\ntitle: ").Append(Q(page.Title)).Append('\n');
        if (page.Description is not null) sb.Append("description: ").Append(Q(page.Description)).Append('\n');
        sb.Append("url: ").Append(page.Canonical ?? resolver.Absolute(path)).Append('\n');
        if (page.ModifiedAt is { } m) sb.Append("updated: ").Append(m.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("---\n\n").Append(SeoMarkdown.Write(page, resolver.Absolute));
        return sb.ToString();
    }
}

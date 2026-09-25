using System.Text;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>
/// <c>/llms.txt</c> and <c>/llms-full.txt</c> (llmstxt.org): a Markdown guide to the site for AI assistants, generated
/// from published content. Every linked page also has a Markdown version at <c>/{path}.md</c> (<c>/index.md</c> for
/// the home page), rendered from the same content as the HTML.
/// </summary>
public sealed class LlmsTxtService(SeoPageResolver resolver)
{
    /// <summary>At most this many pages go into llms-full.txt (newest blog posts are the ones left out beyond it).</summary>
    public const int FullMaxPages = 150;

    private sealed record Entry(SitemapUrl Url, SeoPage Page);

    private async Task<List<Entry>> EntriesAsync(CancellationToken ct)
    {
        var urls = await resolver.SitemapUrlsAsync(ct);
        var entries = new List<Entry>();
        foreach (var url in urls)
        {
            var page = await resolver.ResolveAsync(url.Path, null, ct);
            if (page.IsIndexable) entries.Add(new Entry(url, page));
        }
        return entries;
    }

    private static string Name(SeoPage page, string siteName)
    {
        var title = page.Title;
        foreach (var sep in new[] { " | ", " — ", " · " })
        {
            var i = title.LastIndexOf(sep + siteName, StringComparison.Ordinal);
            if (i > 0) return title[..i];
        }
        return title;
    }

    public async Task<string> LlmsTxtAsync(CancellationToken ct)
    {
        await resolver.EnsureLoadedAsync(ct);
        var s = resolver.Settings;
        var entries = await EntriesAsync(ct);
        var sb = new StringBuilder();
        sb.Append("# ").Append(s.SiteName).Append("\n\n");
        sb.Append("> ").Append(s.Seo.DefaultDescription ?? s.Tagline).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(s.Footer.Blurb)) sb.Append(s.Footer.Blurb).Append("\n\n");
        sb.Append(s.SiteName).Append(" is a full-service digital marketing agency (SEO, paid media, social, content, email, brand and web) that ")
            .Append("also runs a creator program: people with established social accounts are paid to share company-approved, clearly disclosed ")
            .Append("posts. Prices on this site are starting prices; ad spend is billed at cost. Results in case studies are labelled measured ")
            .Append("or estimated. Every link below has a Markdown version (the same URL ending in .md).\n\n");
        if (s.Contact.Email is not null) sb.Append("Contact: ").Append(s.Contact.Email).Append("\n\n");

        void Section(string title, IEnumerable<Entry> items)
        {
            var list = items.ToList();
            if (list.Count == 0) return;
            sb.Append("## ").Append(title).Append("\n\n");
            foreach (var e in list)
            {
                sb.Append("- [").Append(Name(e.Page, s.SiteName)).Append("](").Append(resolver.Absolute(SeoMarkdownPaths.MarkdownPath(e.Url.Path))).Append(')');
                if (!string.IsNullOrWhiteSpace(e.Page.Description)) sb.Append(": ").Append(e.Page.Description);
                sb.Append('\n');
            }
            sb.Append('\n');
        }

        var keyPaths = new[] { "/", "/services", "/pricing", "/case-studies", "/industries", "/about", "/how-we-work", "/team", "/contact", "/free-audit",
            "/get-a-quote", "/book-a-consultation" };
        Section("Key pages", keyPaths.Select(p => entries.FirstOrDefault(e => e.Url.Path == p)).OfType<Entry>());
        Section("Services", entries.Where(e => e.Url.Group == SeoPageResolver.GroupServices));
        Section("Industries", entries.Where(e => e.Url.Path.StartsWith("/industries/", StringComparison.Ordinal)));
        Section("Case studies", entries.Where(e => e.Url.Group == SeoPageResolver.GroupCaseStudies));
        Section("Blog", entries.Where(e => e.Url.Group == SeoPageResolver.GroupBlog).Take(50));
        Section("Careers", entries.Where(e => e.Url.Path == "/careers" || e.Url.Group == SeoPageResolver.GroupCareers));
        Section("Creator program", entries.Where(e => e.Url.Path is "/creators" or "/faq"));
        Section("Partners", entries.Where(e => e.Url.Group == SeoPageResolver.GroupPartners));
        // The academy home and its courses (each course page links its lessons).
        Section("Academy (free courses)", entries.Where(e => e.Url.Group == SeoPageResolver.GroupLearn && e.Url.Path.Count(ch => ch == '/') <= 2));
        var listed = new HashSet<string>(keyPaths.Concat(new[] { "/careers", "/creators", "/faq", "/blog" }));
        Section("Optional", entries.Where(e => e.Url.Group == SeoPageResolver.GroupPages && !listed.Contains(e.Url.Path) &&
                                               !e.Url.Path.StartsWith("/industries/", StringComparison.Ordinal))
            .Concat(entries.Where(e => e.Url.Path == "/blog")));
        sb.Append("## Machine-readable\n\n");
        sb.Append("- [Sitemap](").Append(resolver.Absolute("/sitemap.xml")).Append("): every indexable URL with its last update\n");
        sb.Append("- [Blog RSS](").Append(resolver.Absolute("/api/v1/public/blog/rss.xml")).Append("): the latest articles\n");
        sb.Append("- [Full text](").Append(resolver.Absolute("/llms-full.txt")).Append("): the content of every page above in one Markdown file\n");
        return sb.ToString();
    }

    public async Task<string> LlmsFullAsync(CancellationToken ct)
    {
        await resolver.EnsureLoadedAsync(ct);
        var s = resolver.Settings;
        var entries = (await EntriesAsync(ct)).Take(FullMaxPages).ToList();
        var sb = new StringBuilder();
        sb.Append("# ").Append(s.SiteName).Append(" — full site content\n\n> ").Append(s.Seo.DefaultDescription ?? s.Tagline).Append("\n\n");
        foreach (var e in entries)
        {
            sb.Append("---\n\n");
            sb.Append("URL: ").Append(resolver.Absolute(e.Url.Path)).Append('\n');
            if (e.Page.ModifiedAt is { } m) sb.Append("Updated: ").Append(m.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            sb.Append('\n');
            sb.Append(SeoMarkdown.Write(e.Page, resolver.Absolute)).Append('\n');
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
        if (page.ModifiedAt is { } m) sb.Append("updated: ").Append(m.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        sb.Append("---\n\n").Append(SeoMarkdown.Write(page, resolver.Absolute));
        return sb.ToString();
    }
}

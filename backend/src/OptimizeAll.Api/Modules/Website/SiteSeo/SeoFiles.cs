using System.Globalization;
using System.Text;
using System.Xml;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>One sitemap file of the index (<c>/sitemaps/{Name}.xml</c>).</summary>
public sealed record SitemapFile(string Name, string Kind, IReadOnlyList<SitemapUrl> Urls, DateTime? LastModified);

/// <summary>
/// Sitemaps (sitemaps.org 0.9 + Google image and video extensions): a sitemap index at <c>/sitemap.xml</c> pointing at one
/// file per content group plus <c>images</c> and <c>videos</c>. A group larger than the per-file limit (50,000 URLs by
/// protocol; <c>Website:Seo:SitemapMaxUrls</c>, default 45,000, keeps a margin) is split into <c>{group}-2.xml</c>,
/// <c>{group}-3.xml</c>…; a file that would exceed 50 MB is split further.
/// </summary>
public static class SitemapWriter
{
    public const long MaxBytes = 50L * 1024 * 1024;
    private const string Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
    private const string ImageNs = "http://www.google.com/schemas/sitemap-image/1.1";
    private const string VideoNs = "http://www.google.com/schemas/sitemap-video/1.1";

    public static IReadOnlyList<SitemapFile> Files(IReadOnlyList<SitemapUrl> urls, int maxUrls)
    {
        var files = new List<SitemapFile>();
        void AddChunks(string group, string kind, IReadOnlyList<SitemapUrl> items)
        {
            if (items.Count == 0) return;
            var chunks = items.Chunk(Math.Max(1, maxUrls)).ToList();
            for (var i = 0; i < chunks.Count; i++)
            {
                var name = i == 0 ? group : $"{group}-{i + 1}";
                files.Add(new SitemapFile(name, kind, chunks[i], chunks[i].Max(u => u.LastModified)));
            }
        }
        foreach (var group in SeoPageResolver.UrlGroups) AddChunks(group, "urls", urls.Where(u => u.Group == group).ToList());
        AddChunks("images", "images", urls.Where(u => u.Images.Count > 0).ToList());
        AddChunks("videos", "videos", urls.Where(u => u.Videos.Any(Listable)).ToList());
        return files;
    }

    /// <summary>Google's video sitemap needs a thumbnail and the video file or a player URL.</summary>
    public static bool Listable(SeoVideo v) => v.PosterUrl is not null && (v.ContentUrl is not null || v.EmbedUrl is not null);

    private static string Date(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static XmlWriter Writer(StringBuilder sb) =>
        XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8, OmitXmlDeclaration = false });

    private static string Utf8(StringBuilder sb) => sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");

    public static string Index(IReadOnlyList<SitemapFile> files, string baseUrl)
    {
        var sb = new StringBuilder();
        using (var w = Writer(sb))
        {
            w.WriteStartDocument();
            w.WriteStartElement("sitemapindex", Ns);
            foreach (var f in files)
            {
                w.WriteStartElement("sitemap", Ns);
                w.WriteElementString("loc", Ns, $"{baseUrl}/sitemaps/{f.Name}.xml");
                if (f.LastModified is { } m) w.WriteElementString("lastmod", Ns, Date(m));
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndDocument();
        }
        return Utf8(sb);
    }

    /// <summary>A urlset: plain, with image entries or with video entries depending on <see cref="SitemapFile.Kind"/>.</summary>
    public static string UrlSet(SitemapFile file, string baseUrl)
    {
        var sb = new StringBuilder();
        using (var w = Writer(sb))
        {
            w.WriteStartDocument();
            w.WriteStartElement("urlset", Ns);
            if (file.Kind == "images") w.WriteAttributeString("xmlns", "image", null, ImageNs);
            if (file.Kind == "videos") w.WriteAttributeString("xmlns", "video", null, VideoNs);
            foreach (var u in file.Urls)
            {
                w.WriteStartElement("url", Ns);
                w.WriteElementString("loc", Ns, baseUrl + u.Path);
                if (u.LastModified is { } m) w.WriteElementString("lastmod", Ns, Date(m));
                if (file.Kind == "images")
                    foreach (var img in u.Images.Take(1000))
                    {
                        w.WriteStartElement("image", "image", ImageNs);
                        w.WriteElementString("image", "loc", ImageNs, img.Url);
                        w.WriteEndElement();
                    }
                if (file.Kind == "videos")
                    foreach (var v in u.Videos.Where(Listable))
                    {
                        w.WriteStartElement("video", "video", VideoNs);
                        w.WriteElementString("video", "thumbnail_loc", VideoNs, v.PosterUrl!);
                        w.WriteElementString("video", "title", VideoNs, v.Name);
                        w.WriteElementString("video", "description", VideoNs, Truncate(string.IsNullOrWhiteSpace(v.Description) ? v.Name : v.Description, 2048));
                        if (v.ContentUrl is not null) w.WriteElementString("video", "content_loc", VideoNs, v.ContentUrl);
                        if (v.EmbedUrl is not null) w.WriteElementString("video", "player_loc", VideoNs, v.EmbedUrl);
                        if (v.DurationSeconds is { } d) w.WriteElementString("video", "duration", VideoNs, d.ToString(CultureInfo.InvariantCulture));
                        w.WriteElementString("video", "publication_date", VideoNs, Date(v.UploadDate));
                        w.WriteElementString("video", "family_friendly", VideoNs, "yes");
                        w.WriteEndElement();
                    }
                w.WriteEndElement();
            }
            w.WriteEndElement();
            w.WriteEndDocument();
        }
        return Utf8(sb);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}

/// <summary>
/// robots.txt from the SEO settings: the public site is allowed, portals, sign-in pages, the API (except public SEO
/// files), personal links and search results are disallowed, and each named crawler group is allowed or blocked as set
/// in Website → SEO. Documented in docs/SEO_CRO.md § robots.txt.
/// </summary>
public static class RobotsWriter
{
    /// <summary>Paths no crawler may visit (the same list in every group, since a named group replaces <c>*</c>).</summary>
    /// <remarks>
    /// Each area is blocked as <c>/x$</c>, <c>/x/</c> and <c>/x?</c> rather than the bare prefix <c>/x</c>, which would also
    /// block public pages that merely start with the same letters (/app would block /apple-case-study).
    /// </remarks>
    public static IReadOnlyList<string> Disallowed { get; } = SeoPageResolver.PortalPrefixes
        .Concat(SeoPageResolver.AuthPaths)
        .Concat(new[] { "/search" })
        .SelectMany(p => new[] { p + "$", p + "/", p + "?" })
        .Concat(new[] { "/api/", "/p/", "/i/", "/email/", "/join/", "/f/", "/newsletter/", "/blog?q=" })
        .Distinct().ToList();

    /// <summary>Public SEO resources under disallowed prefixes that crawlers may still fetch.</summary>
    public static IReadOnlyList<string> Allowed { get; } = new[] { "/api/v1/public/blog/rss.xml", "/api/v1/public/sitemap.xml", "/api/v1/files/" };

    public static string Write(SeoSettings settings, string baseUrl)
    {
        var sb = new StringBuilder();
        sb.Append("# robots.txt for ").Append(baseUrl).Append('\n');
        sb.Append("# Public marketing pages are open to search engines and AI assistants; signed-in areas, the API, personal links\n");
        sb.Append("# and search results are not. Crawler groups are configured in Agency → Website → SEO (docs/SEO_CRO.md).\n\n");

        void Rules()
        {
            foreach (var a in Allowed) sb.Append("Allow: ").Append(a).Append('\n');
            foreach (var d in Disallowed) sb.Append("Disallow: ").Append(d).Append('\n');
            sb.Append("Allow: /\n\n");
        }

        foreach (var group in CrawlerCatalog.Groups)
        {
            var allowed = settings.Bots.IsAllowed(group.Key);
            sb.Append("# ").Append(group.Label).Append(allowed ? " — allowed" : " — blocked").Append('\n');
            foreach (var agent in group.UserAgents) sb.Append("User-agent: ").Append(agent).Append('\n');
            if (allowed) Rules();
            else sb.Append("Disallow: /\n\n");
        }
        sb.Append("# Everyone else\nUser-agent: *\n");
        Rules();
        sb.Append("Sitemap: ").Append(baseUrl).Append("/sitemap.xml\n");
        return sb.ToString();
    }
}

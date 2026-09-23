using System.Xml;
using System.Xml.Linq;

namespace OptimizeAll.Api.Modules.Seo.Crawling;

public sealed record SitemapParseResult(bool IsIndex, IReadOnlyList<string> Locations, IReadOnlyList<string> Errors);

/// <summary>Parses sitemap protocol documents (&lt;urlset&gt; and &lt;sitemapindex&gt;) safely (no DTDs, no external entities).</summary>
public static class SitemapReader
{
    public const string Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    public static SitemapParseResult Parse(byte[] content, int maxLocations)
    {
        var errors = new List<string>();
        XDocument doc;
        try
        {
            using var stream = new MemoryStream(content);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 60_000_000,
            });
            doc = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            return new SitemapParseResult(false, Array.Empty<string>(), new[] { $"Not valid XML: {ex.Message}" });
        }

        var root = doc.Root!;
        var isIndex = root.Name.LocalName == "sitemapindex";
        if (root.Name.LocalName is not ("urlset" or "sitemapindex"))
            return new SitemapParseResult(false, Array.Empty<string>(), new[] { $"Root element must be <urlset> or <sitemapindex>, found <{root.Name.LocalName}>." });
        if (root.Name.NamespaceName != Namespace)
            errors.Add($"The root element should use the sitemap namespace {Namespace}.");

        var entryName = isIndex ? "sitemap" : "url";
        var locations = new List<string>();
        var entries = 0;
        foreach (var entry in root.Elements().Where(e => e.Name.LocalName == entryName))
        {
            entries++;
            var loc = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "loc")?.Value.Trim();
            if (string.IsNullOrEmpty(loc))
            {
                if (errors.Count < 20) errors.Add($"<{entryName}> #{entries} has no <loc>.");
                continue;
            }
            if (!Uri.TryCreate(loc, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                if (errors.Count < 20) errors.Add($"<loc> is not an absolute http(s) URL: {Short(loc)}");
                continue;
            }
            if (locations.Count < maxLocations) locations.Add(uri.AbsoluteUri);
        }
        if (entries == 0) errors.Add($"The sitemap has no <{entryName}> entries.");
        if (!isIndex && entries > 50_000) errors.Add("A sitemap may list at most 50,000 URLs.");
        return new SitemapParseResult(isIndex, locations, errors);
    }

    private static string Short(string s) => s.Length <= 150 ? s : s[..150];
}

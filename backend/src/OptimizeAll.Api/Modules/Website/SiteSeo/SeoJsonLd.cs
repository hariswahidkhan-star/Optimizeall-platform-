using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OptimizeAll.Api.Modules.Website.Public;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>
/// schema.org nodes for the server-rendered pages that the page payloads (<see cref="JsonLd"/>) do not already carry:
/// WebPage subtypes, ItemList, OfferCatalog, VideoObject and Person lists. Values are data only (serialized JSON).
/// </summary>
public sealed class SeoJsonLd(JsonLd ld, string siteName)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public JsonLd Base => ld;

    private static readonly HashSet<string> ArticleTypes = new(StringComparer.Ordinal) { "Article", "BlogPosting", "NewsArticle", "TechArticle" };

    /// <summary>
    /// Adds <paramref name="imageUrl"/> as <c>image</c> to an Article-type node (top level or in <c>@graph</c>) that has
    /// none: Google's article rich results require an image. Other nodes are returned unchanged.
    /// </summary>
    public static JsonElement WithArticleImage(JsonElement node, string imageUrl)
    {
        static bool IsArticle(System.Text.Json.Nodes.JsonObject o) => o["@type"] switch
        {
            System.Text.Json.Nodes.JsonValue v when v.TryGetValue<string>(out var t) => ArticleTypes.Contains(t),
            System.Text.Json.Nodes.JsonArray a => a.Any(x => x is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var t) && ArticleTypes.Contains(t)),
            _ => false,
        };
        static bool Missing(System.Text.Json.Nodes.JsonObject o) =>
            !o.TryGetPropertyValue("image", out var img) || img is null || (img is System.Text.Json.Nodes.JsonArray arr && arr.Count == 0);
        if (node.ValueKind != JsonValueKind.Object) return node;
        var root = System.Text.Json.Nodes.JsonNode.Parse(node.GetRawText())!.AsObject();
        var targets = new List<System.Text.Json.Nodes.JsonObject> { root };
        if (root["@graph"] is System.Text.Json.Nodes.JsonArray graph) targets.AddRange(graph.OfType<System.Text.Json.Nodes.JsonObject>());
        var changed = false;
        foreach (var o in targets.Where(o => IsArticle(o) && Missing(o)))
        {
            o["image"] = new System.Text.Json.Nodes.JsonArray(imageUrl);
            changed = true;
        }
        return changed ? JsonSerializer.SerializeToElement(root) : node;
    }

    public static JsonElement Element(Dictionary<string, object?> value) =>
        JsonSerializer.SerializeToElement(value.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value), Options);

    /// <summary>A WebPage (or subtype: AboutPage, ContactPage, CollectionPage, FAQPage is separate) linked to the WebSite node.</summary>
    public JsonElement WebPage(string type, string name, string? description, string path, DateTime? modified = null) => Element(new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = type,
        ["@id"] = ld.Url(path) + "#webpage",
        ["name"] = name,
        ["description"] = description,
        ["url"] = ld.Url(path),
        ["inLanguage"] = "en",
        ["isPartOf"] = new Dictionary<string, object?> { ["@id"] = ld.Url("/") + "#website" },
        ["dateModified"] = modified is { } m ? SeoText.Iso(m) : null,
    });

    public JsonElement? ItemList(string name, IReadOnlyList<(string Name, string Path)> items)
    {
        if (items.Count == 0) return null;
        return Element(new()
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "ItemList",
            ["name"] = name,
            ["numberOfItems"] = items.Count,
            ["itemListElement"] = items.Select((item, i) => new Dictionary<string, object?>
            {
                ["@type"] = "ListItem",
                ["position"] = i + 1,
                ["name"] = item.Name,
                ["url"] = ld.Url(item.Path),
            }).ToArray(),
        });
    }

    /// <summary>The pricing page: every service with a priced package as an Offer in one OfferCatalog.</summary>
    public JsonElement? OfferCatalog(IReadOnlyList<PricingServiceDto> services)
    {
        var entries = services.Select(s => new Dictionary<string, object?>
        {
            ["@type"] = "OfferCatalog",
            ["name"] = s.Service.Name,
            ["itemListElement"] = s.Packages.Where(p => !p.IsCustomQuote && p.Price is not null).Select(p => new Dictionary<string, object?>
            {
                ["@type"] = "Offer",
                ["name"] = $"{s.Service.Name} — {p.Name}",
                ["description"] = p.Description,
                ["price"] = p.Price!.Value.ToString("0.00", CultureInfo.InvariantCulture),
                ["priceCurrency"] = p.Currency,
                ["url"] = ld.Url($"/services/{s.Service.Slug}#pricing"),
                ["itemOffered"] = new Dictionary<string, object?>
                {
                    ["@type"] = "Service", ["name"] = s.Service.Name, ["url"] = ld.Url($"/services/{s.Service.Slug}"),
                },
                ["priceSpecification"] = p.BillingPeriod.ToString() == "OneTime" ? null : new Dictionary<string, object?>
                {
                    ["@type"] = "UnitPriceSpecification",
                    ["price"] = p.Price!.Value.ToString("0.00", CultureInfo.InvariantCulture),
                    ["priceCurrency"] = p.Currency,
                    ["billingDuration"] = 1,
                    ["unitCode"] = p.BillingPeriod.ToString() switch { "Yearly" => "ANN", "Quarterly" => "QAN", _ => "MON" },
                },
            }.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value)).ToArray(),
        }).Where(e => ((Array)e["itemListElement"]!).Length > 0).ToArray();
        if (entries.Length == 0) return null;
        return Element(new()
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "OfferCatalog",
            ["name"] = $"{siteName} services and packages",
            ["url"] = ld.Url("/pricing"),
            ["itemListElement"] = entries,
        });
    }

    /// <summary>
    /// A VideoObject for a video on the page (required by Google: name, description, thumbnailUrl, uploadDate; plus
    /// contentUrl or embedUrl, duration as ISO 8601 and the captions track as <c>caption</c>).
    /// </summary>
    public JsonElement VideoObject(SeoVideo v, string pagePath) => Element(new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "VideoObject",
        ["name"] = v.Name,
        ["description"] = string.IsNullOrWhiteSpace(v.Description) ? v.Name : v.Description,
        ["thumbnailUrl"] = v.PosterUrl is null ? null : new[] { v.PosterUrl },
        ["uploadDate"] = SeoText.Iso(v.UploadDate),
        ["duration"] = v.DurationSeconds is { } d ? System.Xml.XmlConvert.ToString(TimeSpan.FromSeconds(d)) : null,
        ["contentUrl"] = v.ContentUrl,
        ["embedUrl"] = v.EmbedUrl,
        ["inLanguage"] = v.CaptionsLanguage,
        ["caption"] = v.CaptionsUrl is null ? null : new Dictionary<string, object?>
        {
            ["@type"] = "MediaObject", ["contentUrl"] = v.CaptionsUrl, ["encodingFormat"] = "text/vtt",
        },
        ["transcript"] = v.TranscriptMarkdown is null ? null : OptimizeAll.Domain.Website.MarkdownSanitizer.ToPlainText(v.TranscriptMarkdown),
        ["publisher"] = new Dictionary<string, object?> { ["@id"] = ld.Url("/") + "#organization" },
        ["mainEntityOfPage"] = ld.Url(pagePath),
    });

    public JsonElement? People(IReadOnlyList<PublicTeamMemberDto> team)
    {
        if (team.Count == 0) return null;
        return Element(new()
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "ItemList",
            ["name"] = $"{siteName} team",
            ["itemListElement"] = team.Select((m, i) => new Dictionary<string, object?>
            {
                ["@type"] = "ListItem",
                ["position"] = i + 1,
                ["item"] = new Dictionary<string, object?>
                {
                    ["@type"] = "Person",
                    ["name"] = m.Name,
                    ["jobTitle"] = m.Role,
                    ["image"] = m.PhotoUrl is null ? null : ld.Url(m.PhotoUrl),
                    ["sameAs"] = m.SocialLinks.Count > 0 ? m.SocialLinks.Select(l => l.Url).ToArray() : null,
                    ["worksFor"] = new Dictionary<string, object?> { ["@id"] = ld.Url("/") + "#organization" },
                }.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value),
            }).ToArray(),
        });
    }

    /// <summary>A campaign landing page: an Event-like offer is not appropriate, so it is described as a WebPage about a Service.</summary>
    public JsonElement Campaign(string title, string summary, string path, string? image) => Element(new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "WebPage",
        ["name"] = title,
        ["description"] = summary,
        ["url"] = ld.Url(path),
        ["primaryImageOfPage"] = image is null ? null : new Dictionary<string, object?> { ["@type"] = "ImageObject", ["url"] = ld.Url(image) },
        ["isPartOf"] = new Dictionary<string, object?> { ["@id"] = ld.Url("/") + "#website" },
    });
}

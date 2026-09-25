using System.Text.Json;
using System.Text.Json.Serialization;
using OptimizeAll.Api.Modules.Website.Public;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Api.Modules.Website.Partners;

/// <summary>
/// schema.org JSON-LD of the partner pages (docs/SEO_CRO.md "Partner links and Google's link-spam policy").
/// <list type="bullet">
/// <item>Profile page: the partner as an <c>Organization</c> (name, url, logo, description, sameAs, knowsAbout), a
/// <c>WebPage</c> whose <c>about</c> is that organization and whose <c>publisher</c> is Optimize All, and a
/// <c>BreadcrumbList</c>.</item>
/// <item>Partners page: a <c>CollectionPage</c> whose <c>mentions</c> are the partner organizations and whose
/// <c>mainEntity</c> is an <c>ItemList</c> of the profile pages.</item>
/// </list>
/// schema.org has no "partner" property: <c>member</c>/<c>memberOf</c> state membership, <c>sponsor</c>/<c>funder</c> state
/// funding, <c>parentOrganization</c>/<c>subOrganization</c> state ownership — none describes a marketing partnership, so
/// Optimize All's own Organization object is left unchanged and the relationship is stated in visible text instead.
/// </summary>
public sealed class PartnerJsonLd(JsonLd ld)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string OrganizationId(string slug) => ld.Url($"/partners/{slug}") + "#organization";

    private string OurOrganizationId => ld.Url("/") + "#organization";

    private static JsonElement Element(Dictionary<string, object?> value) =>
        JsonSerializer.SerializeToElement(value.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value), Options);

    private Dictionary<string, object?> OrganizationRef(WebsitePartner p) => new()
    {
        ["@type"] = "Organization",
        ["@id"] = OrganizationId(p.Slug),
        ["name"] = p.Name,
        ["url"] = p.WebsiteUrl,
    };

    public JsonElement Organization(WebsitePartner p, string description)
    {
        var sameAs = new[] { p.WebsiteUrl }.Concat(p.SameAs).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var org = OrganizationRef(p);
        org["@context"] = "https://schema.org";
        org["logo"] = ld.Url(p.LogoUrl);
        org["image"] = ld.Url(p.LogoUrl);
        org["description"] = description;
        org["slogan"] = p.Tagline;
        org["sameAs"] = sameAs.Length > 0 ? sameAs : null;
        org["knowsAbout"] = p.Keywords.Count > 0 ? p.Keywords.Take(15).ToArray() : null;
        return Element(org);
    }

    public JsonElement ProfilePage(WebsitePartner p, string title, string description, string path) => Element(new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "WebPage",
        ["@id"] = ld.Url(path) + "#webpage",
        ["url"] = ld.Url(path),
        ["name"] = title,
        ["description"] = description,
        ["about"] = new Dictionary<string, object?> { ["@id"] = OrganizationId(p.Slug) },
        ["primaryImageOfPage"] = new Dictionary<string, object?> { ["@type"] = "ImageObject", ["url"] = ld.Url(p.LogoUrl) },
        ["publisher"] = new Dictionary<string, object?> { ["@id"] = OurOrganizationId },
        ["dateModified"] = p.UpdatedAt.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
    });

    public JsonElement Directory(IReadOnlyList<WebsitePartner> partners, string title, string description) => Element(new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "CollectionPage",
        ["@id"] = ld.Url("/partners") + "#webpage",
        ["url"] = ld.Url("/partners"),
        ["name"] = title,
        ["description"] = description,
        ["publisher"] = new Dictionary<string, object?> { ["@id"] = OurOrganizationId },
        ["mentions"] = partners.Count > 0
            ? partners.Select(p => (object)OrganizationRef(p).Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value)).ToArray()
            : null,
        ["mainEntity"] = new Dictionary<string, object?>
        {
            ["@type"] = "ItemList",
            ["numberOfItems"] = partners.Count,
            ["itemListElement"] = partners.Select((p, i) => new Dictionary<string, object?>
            {
                ["@type"] = "ListItem",
                ["position"] = i + 1,
                ["name"] = p.Name,
                ["url"] = ld.Url($"/partners/{p.Slug}"),
            }).ToArray(),
        },
    });

    public JsonElement Breadcrumbs(params (string Name, string Path)[] items) => ld.Breadcrumbs(items);
}

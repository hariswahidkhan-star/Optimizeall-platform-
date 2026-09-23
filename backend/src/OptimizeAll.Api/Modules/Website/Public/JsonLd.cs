using System.Globalization;
using System.Text.Json;
using OptimizeAll.Api.Modules.Website.Settings;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Api.Modules.Website.Public;

/// <summary>
/// Builds schema.org JSON-LD objects (Organization, WebSite, Service, Article, BreadcrumbList, FAQPage, JobPosting) for the
/// public page payloads. The web app writes each object into a <c>script type="application/ld+json"</c> via
/// <c>textContent</c> (never as HTML).
/// </summary>
public sealed class JsonLd(string baseUrl, SiteSettings site)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public string BaseUrl { get; } = baseUrl.TrimEnd('/');

    /// <summary>Absolute URL for an app path or upload; https URLs pass through.</summary>
    public string Url(string? path) =>
        string.IsNullOrEmpty(path) ? BaseUrl + "/"
        : path.StartsWith("https://", StringComparison.Ordinal) || path.StartsWith("http://", StringComparison.Ordinal) ? path
        : BaseUrl + (path.StartsWith('/') ? path : "/" + path);

    private static JsonElement Element(Dictionary<string, object?> value) =>
        JsonSerializer.SerializeToElement(value.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value), Options);

    private Dictionary<string, object?> OrganizationRef() => new()
    {
        ["@type"] = "Organization",
        ["name"] = site.Organization.LegalName ?? site.SiteName,
        ["url"] = Url("/"),
    };

    public JsonElement Organization()
    {
        var o = site.Organization;
        var org = OrganizationRef();
        org["@context"] = "https://schema.org";
        org["@id"] = Url("/") + "#organization";
        org["logo"] = o.LogoUrl is null ? Url("/og-image.png") : Url(o.LogoUrl);
        org["description"] = site.Seo.DefaultDescription;
        org["slogan"] = site.Tagline;
        org["email"] = site.Contact.Email;
        org["telephone"] = site.Contact.Phone;
        org["foundingDate"] = o.FoundingYear?.ToString(CultureInfo.InvariantCulture);
        org["sameAs"] = site.Social.Count > 0 ? site.Social.Select(s => s.Url).ToArray() : null;
        org["areaServed"] = o.AreaServed.Count > 0 ? o.AreaServed.ToArray() : null;
        if (o.StreetAddress is not null || o.Locality is not null || o.CountryCode is not null)
        {
            org["address"] = new Dictionary<string, object?>
            {
                ["@type"] = "PostalAddress",
                ["streetAddress"] = o.StreetAddress,
                ["addressLocality"] = o.Locality,
                ["addressRegion"] = o.Region,
                ["postalCode"] = o.PostalCode,
                ["addressCountry"] = o.CountryCode,
            }.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);
        }
        return Element(org);
    }

    public JsonElement WebSite() => Element(new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "WebSite",
        ["name"] = site.SiteName,
        ["url"] = Url("/"),
        ["publisher"] = new Dictionary<string, object?> { ["@id"] = Url("/") + "#organization" },
    });

    public JsonElement Breadcrumbs(params (string Name, string Path)[] items) => Element(new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "BreadcrumbList",
        ["itemListElement"] = items.Select((item, i) => new Dictionary<string, object?>
        {
            ["@type"] = "ListItem",
            ["position"] = i + 1,
            ["name"] = item.Name,
            ["item"] = Url(item.Path),
        }).ToArray(),
    });

    public JsonElement? FaqPage(IReadOnlyList<FaqEntry> faqs) => faqs.Count == 0 ? null : Element(new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "FAQPage",
        ["mainEntity"] = faqs.Select(f => new Dictionary<string, object?>
        {
            ["@type"] = "Question",
            ["name"] = f.Question,
            ["acceptedAnswer"] = new Dictionary<string, object?> { ["@type"] = "Answer", ["text"] = MarkdownSanitizer.ToPlainText(f.Answer) },
        }).ToArray(),
    });

    public JsonElement Service(AgencyService s, string categoryName, IReadOnlyList<ServicePackage> packages)
    {
        var offers = packages.Where(p => p.IsActive && !p.IsCustomQuote && p.Price is not null).Select(p => new Dictionary<string, object?>
        {
            ["@type"] = "Offer",
            ["name"] = p.Name,
            ["price"] = p.Price!.Value.ToString("0.00", CultureInfo.InvariantCulture),
            ["priceCurrency"] = p.Currency,
            ["url"] = Url($"/services/{s.Slug}#pricing"),
        }).ToArray();
        return Element(new()
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Service",
            ["name"] = s.Name,
            ["serviceType"] = categoryName,
            ["description"] = s.Seo.Description ?? s.Tagline,
            ["url"] = Url($"/services/{s.Slug}"),
            ["image"] = s.HeroImageUrl is null ? null : Url(s.HeroImageUrl),
            ["provider"] = new Dictionary<string, object?> { ["@id"] = Url("/") + "#organization", ["@type"] = "Organization", ["name"] = site.Organization.LegalName ?? site.SiteName },
            ["areaServed"] = site.Organization.AreaServed.Count > 0 ? site.Organization.AreaServed.ToArray() : null,
            ["offers"] = offers.Length > 0 ? offers : null,
        });
    }

    public JsonElement Article(string type, string headline, string? description, string path, string? image, DateTime? published, DateTime modified, string? authorName) =>
        Element(new()
        {
            ["@context"] = "https://schema.org",
            ["@type"] = type,
            ["headline"] = headline.Length > 110 ? headline[..110] : headline,
            ["description"] = description,
            ["url"] = Url(path),
            ["mainEntityOfPage"] = Url(path),
            ["image"] = image is null ? null : Url(image),
            ["datePublished"] = published?.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["dateModified"] = modified.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["author"] = authorName is null ? OrganizationRef() : new Dictionary<string, object?> { ["@type"] = "Person", ["name"] = authorName },
            ["publisher"] = OrganizationRef(),
        });

    public JsonElement JobPosting(JobOpening j) => Element(new()
    {
        ["@context"] = "https://schema.org",
        ["@type"] = "JobPosting",
        ["title"] = j.Title,
        ["description"] = MarkdownSanitizer.ToPlainText(j.DescriptionMarkdown),
        ["datePosted"] = (j.PostedAt ?? j.CreatedAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["validThrough"] = j.ClosesAt?.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        ["employmentType"] = j.EmploymentType switch
        {
            EmploymentType.FullTime => "FULL_TIME",
            EmploymentType.PartTime => "PART_TIME",
            EmploymentType.Contract => "CONTRACTOR",
            EmploymentType.Internship => "INTERN",
            _ => "TEMPORARY",
        },
        ["hiringOrganization"] = new Dictionary<string, object?>
        {
            ["@type"] = "Organization", ["name"] = site.Organization.LegalName ?? site.SiteName, ["sameAs"] = Url("/"),
        },
        ["jobLocationType"] = j.Workplace == WorkplaceType.Remote ? "TELECOMMUTE" : null,
        ["applicantLocationRequirements"] = j.Workplace == WorkplaceType.Remote && j.CountryCode is not null
            ? new Dictionary<string, object?> { ["@type"] = "Country", ["name"] = j.CountryCode } : null,
        ["jobLocation"] = j.Workplace == WorkplaceType.Remote ? null : new Dictionary<string, object?>
        {
            ["@type"] = "Place",
            ["address"] = new Dictionary<string, object?>
            {
                ["@type"] = "PostalAddress", ["addressLocality"] = j.Location, ["addressCountry"] = j.CountryCode,
            }.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value),
        },
        ["baseSalary"] = j.SalaryCurrency is not null && (j.SalaryMin is not null || j.SalaryMax is not null) ? new Dictionary<string, object?>
        {
            ["@type"] = "MonetaryAmount",
            ["currency"] = j.SalaryCurrency,
            ["value"] = new Dictionary<string, object?>
            {
                ["@type"] = "QuantitativeValue",
                ["minValue"] = j.SalaryMin,
                ["maxValue"] = j.SalaryMax,
                ["unitText"] = (j.SalaryPeriod ?? SalaryPeriod.Year).ToString().ToUpperInvariant(),
            }.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value),
        } : null,
        ["url"] = Url($"/careers/{j.Slug}"),
    });
}

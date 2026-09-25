using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Settings;

public sealed record MenuItem(string Label, string? Url, string? Description, IReadOnlyList<MenuItem>? Children);

public sealed record HeaderSettings(IReadOnlyList<MenuItem> Menu, SiteLink? Cta);

public sealed record FooterColumn(string Title, IReadOnlyList<SiteLink> Links);

public sealed record FooterSettings(string? Blurb, IReadOnlyList<FooterColumn> Columns, IReadOnlyList<SiteLink> LegalLinks);

public sealed record ContactSettings(string? Email, string? Phone, string? WhatsApp, string? Address, string? Hours);

public sealed record SocialProfile(string Platform, string Url);

public sealed record TrustLogo(string Name, string ImageUrl, string? Url);

public sealed record AnnouncementBar(bool Enabled, string? Text, string? LinkLabel, string? LinkUrl);

public sealed record DefaultSeo(string? SiteUrl, string TitleTemplate, string DefaultTitle, string? DefaultDescription, string? DefaultOgImageUrl, string? TwitterHandle);

public sealed record OrganizationSchema(
    string? LegalName, string? LogoUrl, int? FoundingYear, string? StreetAddress, string? Locality, string? Region,
    string? PostalCode, string? CountryCode, IReadOnlyList<string> AreaServed);

/// <summary>Tag ids injected by the web app only after the visitor consents to analytics / marketing cookies.</summary>
public sealed record AnalyticsSettings(string? Ga4MeasurementId, string? GtmContainerId, string? MetaPixelId);

public sealed record HomeStat(string Label, string Value, MetricMeasurement Measurement, string? Context);

/// <summary>The site settings document (stored as JSON in <c>website_settings</c>).</summary>
public sealed record SiteSettings(
    string SiteName,
    string Tagline,
    HeaderSettings Header,
    FooterSettings Footer,
    ContactSettings Contact,
    IReadOnlyList<SocialProfile> Social,
    IReadOnlyList<TrustLogo> TrustLogos,
    AnnouncementBar Announcement,
    DefaultSeo Seo,
    OrganizationSchema Organization,
    AnalyticsSettings Analytics,
    IReadOnlyList<HomeStat> HomeStats);

public sealed record SiteSettingsDto(SiteSettings Settings, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class UpdateSiteSettingsRequest
{
    [Required]
    public SiteSettings? Settings { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

/// <summary>Loads, validates and saves the site settings document.</summary>
public sealed partial class SiteSettingsService(AppDbContext db, IAuditLogger audit, WebsiteRules rules)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static readonly string[] SocialPlatforms =
        { "LinkedIn", "Instagram", "Facebook", "X", "TikTok", "YouTube", "Pinterest", "Threads", "WhatsApp", "GitHub", "Behance", "Dribbble" };

    public async Task<SiteSettings> GetAsync(CancellationToken ct)
    {
        var doc = await db.Set<SiteSettingsDocument>().AsNoTracking().FirstOrDefaultAsync(d => d.Key == SiteSettingsDocument.DefaultKey, ct);
        return Parse(doc?.Json);
    }

    public async Task<SiteSettingsDto> GetForEditAsync(CancellationToken ct)
    {
        var doc = await EnsureAsync(ct);
        return new SiteSettingsDto(Parse(doc.Json), doc.UpdatedAt, doc.ConcurrencyStamp);
    }

    public async Task<SiteSettingsDto> UpdateAsync(UpdateSiteSettingsRequest request, CancellationToken ct)
    {
        var doc = await EnsureAsync(ct);
        CmsStore.CheckStamp(db, doc, request.ConcurrencyStamp);
        var before = Parse(doc.Json);
        var normalized = Validate(request.Settings!);
        doc.Json = JsonSerializer.Serialize(normalized, Json);
        audit.Record("website.settings_updated", nameof(SiteSettingsDocument), doc.Id, before, normalized);
        await db.SaveChangesAsync(ct);
        return new SiteSettingsDto(normalized, doc.UpdatedAt, doc.ConcurrencyStamp);
    }

    private async Task<SiteSettingsDocument> EnsureAsync(CancellationToken ct)
    {
        var doc = await db.Set<SiteSettingsDocument>().FirstOrDefaultAsync(d => d.Key == SiteSettingsDocument.DefaultKey, ct);
        if (doc is not null) return doc;
        doc = new SiteSettingsDocument { Json = JsonSerializer.Serialize(Defaults, Json) };
        db.Set<SiteSettingsDocument>().Add(doc);
        await db.SaveChangesAsync(ct);
        return doc;
    }

    public static SiteSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Defaults;
        try
        {
            return JsonSerializer.Deserialize<SiteSettings>(json, Json) ?? Defaults;
        }
        catch (JsonException)
        {
            return Defaults;
        }
    }

    /// <summary>Validates every field (links, images, ids) and returns a trimmed copy. Throws 400 with field errors.</summary>
    public SiteSettings Validate(SiteSettings s)
    {
        var e = new FieldErrors();
        string Req(string? v, string field, int max)
        {
            var c = WebsiteRules.Clean(v);
            if (c is null) e.Add(field, "Required.");
            else if (c.Length > max) e.Add(field, $"At most {max} characters.");
            return c ?? string.Empty;
        }
        string? Opt(string? v, string field, int max)
        {
            var c = WebsiteRules.Clean(v);
            if (c is not null && c.Length > max) e.Add(field, $"At most {max} characters.");
            return c;
        }
        SiteLink? Link(SiteLink? l, string field, bool required = false)
        {
            if (l is null || (WebsiteRules.Clean(l.Label) is null && WebsiteRules.Clean(l.Url) is null))
            {
                if (required) e.Add(field, "Add a label and a link.");
                return null;
            }
            var label = Req(l.Label, field + ".label", 60);
            var url = WebsiteRules.Link(l.Url, field + ".url", e);
            if (url is null) e.Add(field + ".url", "Add the link.");
            return new SiteLink(label, url ?? string.Empty);
        }

        var menu = new List<MenuItem>();
        var items = s.Header?.Menu ?? Array.Empty<MenuItem>();
        if (items.Count > 10) e.Add("header.menu", "Use at most 10 top-level menu items.");
        for (var i = 0; i < items.Count; i++)
        {
            var m = items[i];
            var f = $"header.menu[{i}]";
            var children = new List<MenuItem>();
            var kids = m.Children ?? Array.Empty<MenuItem>();
            if (kids.Count > 40) e.Add(f + ".children", "Use at most 40 sub-items.");
            for (var j = 0; j < kids.Count; j++)
            {
                var c = kids[j];
                var cf = $"{f}.children[{j}]";
                var url = WebsiteRules.Link(c.Url, cf + ".url", e);
                if (url is null) e.Add(cf + ".url", "Sub-items need a link.");
                children.Add(new MenuItem(Req(c.Label, cf + ".label", 60), url, Opt(c.Description, cf + ".description", 160), null));
            }
            var topUrl = WebsiteRules.Link(m.Url, f + ".url", e);
            if (topUrl is null && children.Count == 0) e.Add(f + ".url", "Add a link or sub-items.");
            menu.Add(new MenuItem(Req(m.Label, f + ".label", 40), topUrl, Opt(m.Description, f + ".description", 160), children));
        }

        var columns = new List<FooterColumn>();
        var cols = s.Footer?.Columns ?? Array.Empty<FooterColumn>();
        if (cols.Count > 6) e.Add("footer.columns", "Use at most 6 footer columns.");
        for (var i = 0; i < cols.Count; i++)
        {
            var links = (cols[i].Links ?? Array.Empty<SiteLink>()).Select((l, j) => Link(l, $"footer.columns[{i}].links[{j}]", true)!).Where(l => l is not null).ToList();
            if (links.Count > 15) e.Add($"footer.columns[{i}].links", "Use at most 15 links per column.");
            columns.Add(new FooterColumn(Req(cols[i].Title, $"footer.columns[{i}].title", 40), links));
        }
        var legal = (s.Footer?.LegalLinks ?? Array.Empty<SiteLink>()).Select((l, j) => Link(l, $"footer.legalLinks[{j}]", true)!).Where(l => l is not null).ToList();

        var c0 = s.Contact ?? new ContactSettings(null, null, null, null, null);
        var email = Opt(c0.Email, "contact.email", 254);
        if (email is not null && !FieldRules.IsEmail(email)) e.Add("contact.email", "Enter a valid email address.");
        var phone = Opt(c0.Phone, "contact.phone", 32);
        if (phone is not null && !PhoneRegex().IsMatch(phone)) e.Add("contact.phone", "Enter a phone number such as +1 415 555 0100.");
        var whatsapp = Opt(c0.WhatsApp, "contact.whatsApp", 16)?.Replace(" ", string.Empty);
        if (whatsapp is not null && !FieldRules.IsE164(whatsapp)) e.Add("contact.whatsApp", "Use the international format, e.g. +14155550100.");

        var social = new List<SocialProfile>();
        var socialIn = s.Social ?? Array.Empty<SocialProfile>();
        for (var i = 0; i < socialIn.Count; i++)
        {
            var platform = SocialPlatforms.FirstOrDefault(p => string.Equals(p, socialIn[i].Platform?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (platform is null) { e.Add($"social[{i}].platform", "Unknown platform."); continue; }
            var url = WebsiteRules.Clean(socialIn[i].Url);
            if (url is null || !url.StartsWith("https://", StringComparison.Ordinal) || !FieldRules.IsSafeContentUrl(url))
                e.Add($"social[{i}].url", "Use the https:// link to the profile.");
            social.Add(new SocialProfile(platform, url ?? string.Empty));
        }

        var logos = new List<TrustLogo>();
        var logosIn = s.TrustLogos ?? Array.Empty<TrustLogo>();
        if (logosIn.Count > 24) e.Add("trustLogos", "Use at most 24 logos.");
        for (var i = 0; i < logosIn.Count; i++)
        {
            var img = rules.Image(logosIn[i].ImageUrl, $"trustLogos[{i}].imageUrl", e);
            if (img is null) e.Add($"trustLogos[{i}].imageUrl", "Upload the logo image.");
            logos.Add(new TrustLogo(Req(logosIn[i].Name, $"trustLogos[{i}].name", 80), img ?? string.Empty,
                WebsiteRules.Link(logosIn[i].Url, $"trustLogos[{i}].url", e)));
        }

        var a0 = s.Announcement ?? new AnnouncementBar(false, null, null, null);
        var aText = Opt(a0.Text, "announcement.text", 200);
        if (a0.Enabled && aText is null) e.Add("announcement.text", "Add the announcement text or turn the bar off.");
        var aUrl = WebsiteRules.Link(a0.LinkUrl, "announcement.linkUrl", e);
        var aLabel = Opt(a0.LinkLabel, "announcement.linkLabel", 40);
        if ((aUrl is null) != (aLabel is null)) e.Add("announcement.linkLabel", "Link label and link go together.");

        var seo0 = s.Seo ?? Defaults.Seo;
        var siteUrl = Opt(seo0.SiteUrl, "seo.siteUrl", 200)?.TrimEnd('/');
        if (siteUrl is not null && (!Uri.TryCreate(siteUrl, UriKind.Absolute, out var su) || su.Scheme != Uri.UriSchemeHttps || su.AbsolutePath != "/"))
            e.Add("seo.siteUrl", "Use the site's https:// origin, e.g. https://www.optimizeall.com.");
        var template = Req(seo0.TitleTemplate, "seo.titleTemplate", 80);
        if (!template.Contains("%s", StringComparison.Ordinal)) e.Add("seo.titleTemplate", "Include %s where the page title goes.");
        var twitter = Opt(seo0.TwitterHandle, "seo.twitterHandle", 16);
        if (twitter is not null && !TwitterRegex().IsMatch(twitter)) e.Add("seo.twitterHandle", "Use a handle such as @optimizeall.");

        var o0 = s.Organization ?? Defaults.Organization;
        var country = Opt(o0.CountryCode, "organization.countryCode", 2)?.ToUpperInvariant();
        if (country is not null && !FieldRules.IsCountryCode(country)) e.Add("organization.countryCode", "Use a two-letter country code.");
        if (o0.FoundingYear is { } year && (year < 1900 || year > 2100)) e.Add("organization.foundingYear", "Enter a valid year.");

        var an = s.Analytics ?? new AnalyticsSettings(null, null, null);
        var ga4 = Opt(an.Ga4MeasurementId, "analytics.ga4MeasurementId", 20)?.ToUpperInvariant();
        if (ga4 is not null && !Ga4Regex().IsMatch(ga4)) e.Add("analytics.ga4MeasurementId", "A GA4 measurement ID looks like G-ABC123XYZ9.");
        var gtm = Opt(an.GtmContainerId, "analytics.gtmContainerId", 20)?.ToUpperInvariant();
        if (gtm is not null && !GtmRegex().IsMatch(gtm)) e.Add("analytics.gtmContainerId", "A GTM container ID looks like GTM-ABC1234.");
        var pixel = Opt(an.MetaPixelId, "analytics.metaPixelId", 20);
        if (pixel is not null && !PixelRegex().IsMatch(pixel)) e.Add("analytics.metaPixelId", "A Meta Pixel ID is 10–20 digits.");

        var stats = new List<HomeStat>();
        var statsIn = s.HomeStats ?? Array.Empty<HomeStat>();
        if (statsIn.Count > 8) e.Add("homeStats", "Use at most 8 stats.");
        for (var i = 0; i < statsIn.Count; i++)
        {
            if (!Enum.IsDefined(statsIn[i].Measurement)) e.Add($"homeStats[{i}].measurement", "Say whether the figure is measured or estimated.");
            stats.Add(new HomeStat(Req(statsIn[i].Label, $"homeStats[{i}].label", 80), Req(statsIn[i].Value, $"homeStats[{i}].value", 20),
                statsIn[i].Measurement, Opt(statsIn[i].Context, $"homeStats[{i}].context", 160)));
        }

        var result = new SiteSettings(
            Req(s.SiteName, "siteName", 80),
            Req(s.Tagline, "tagline", 120),
            new HeaderSettings(menu, Link(s.Header?.Cta, "header.cta")),
            new FooterSettings(Opt(s.Footer?.Blurb, "footer.blurb", 400), columns, legal),
            new ContactSettings(email, phone, whatsapp, Opt(c0.Address, "contact.address", 300), Opt(c0.Hours, "contact.hours", 120)),
            social,
            logos,
            new AnnouncementBar(a0.Enabled, aText, aLabel, aUrl),
            new DefaultSeo(siteUrl, template, Req(seo0.DefaultTitle, "seo.defaultTitle", 70), Opt(seo0.DefaultDescription, "seo.defaultDescription", 200),
                rules.Image(seo0.DefaultOgImageUrl, "seo.defaultOgImageUrl", e), twitter),
            new OrganizationSchema(Opt(o0.LegalName, "organization.legalName", 150), rules.Image(o0.LogoUrl, "organization.logoUrl", e), o0.FoundingYear,
                Opt(o0.StreetAddress, "organization.streetAddress", 200), Opt(o0.Locality, "organization.locality", 100),
                Opt(o0.Region, "organization.region", 100), Opt(o0.PostalCode, "organization.postalCode", 20), country,
                WebsiteRules.Lines(o0.AreaServed, "organization.areaServed", e, 30, 80)),
            new AnalyticsSettings(ga4, gtm, pixel),
            stats);
        e.ThrowIfAny();
        return result;
    }

    /// <summary>Defaults used before an administrator saves settings (and by the baseline seed).</summary>
    public static readonly SiteSettings Defaults = new(
        "Optimize All",
        "Discover the world of solution",
        new HeaderSettings(
            new MenuItem[]
            {
                new("Services", "/services", "Everything we do to grow your brand.", Array.Empty<MenuItem>()),
                new("Industries", "/industries", null, null),
                new("Case studies", "/case-studies", null, null),
                new("Pricing", "/pricing", null, null),
                new("Academy", "/learn", "Free courses with certificates.", null),
                new("About", "/about", null, new MenuItem[]
                {
                    new("About us", "/about", "Who we are and how we work.", null),
                    new("Team", "/team", "The people behind your results.", null),
                    new("Careers", "/careers", "Join the agency.", null),
                    new("Blog", "/blog", "Playbooks, research and news.", null),
                }),
                new("Creators", "/creators", "Get paid to share brands you believe in.", null),
            },
            new SiteLink("Get a free audit", "/free-audit")),
        new FooterSettings(
            "A full-service digital marketing agency: search, social, paid media, content, email, brand and web — measured in revenue, not vanity metrics.",
            new FooterColumn[]
            {
                new("Services", new SiteLink[]
                {
                    new("SEO", "/services/seo"), new("Google Ads / PPC", "/services/google-ads-ppc"),
                    new("Social media management", "/services/social-media-management"),
                    new("Influencer & UGC marketing", "/services/influencer-ugc-marketing"),
                    new("Web design & development", "/services/web-design-development"), new("All services", "/services"),
                }),
                new("Company", new SiteLink[]
                {
                    new("About", "/about"), new("How we work", "/how-we-work"), new("Team", "/team"), new("Careers", "/careers"),
                    new("Case studies", "/case-studies"), new("Blog", "/blog"), new("Free courses", "/learn"),
                }),
                new("Get started", new SiteLink[]
                {
                    new("Free marketing audit", "/free-audit"), new("Get a quote", "/get-a-quote"), new("Book a consultation", "/book-a-consultation"),
                    new("Pricing", "/pricing"), new("Contact", "/contact"), new("Become a creator", "/creators"),
                }),
            },
            new SiteLink[]
            {
                new("Privacy policy", "/privacy-policy"), new("Terms of service", "/terms-of-service"), new("Cookie policy", "/cookie-policy"),
                new("Accessibility", "/accessibility"), new("Refund policy", "/refund-policy"),
            }),
        new ContactSettings("hello@optimizeall.com", null, null, null, "Monday–Friday, 9:00–18:00"),
        Array.Empty<SocialProfile>(),
        Array.Empty<TrustLogo>(),
        new AnnouncementBar(false, null, null, null),
        new DefaultSeo(null, "%s | Optimize All", "Optimize All — Full-service digital marketing agency",
            "Search, social, paid media, content, email and web — one accountable team focused on measurable growth.", null, null),
        new OrganizationSchema("Optimize All", null, null, null, null, null, null, null, Array.Empty<string>()),
        new AnalyticsSettings(null, null, null),
        Array.Empty<HomeStat>());

    [GeneratedRegex(@"^\+?[0-9][0-9 ().-]{5,30}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^@[A-Za-z0-9_]{1,15}$")]
    private static partial Regex TwitterRegex();

    [GeneratedRegex(@"^G-[A-Z0-9]{4,15}$")]
    private static partial Regex Ga4Regex();

    [GeneratedRegex(@"^GTM-[A-Z0-9]{4,12}$")]
    private static partial Regex GtmRegex();

    [GeneratedRegex(@"^[0-9]{10,20}$")]
    private static partial Regex PixelRegex();
}

using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Content.Copy;
using OptimizeAll.Api.Modules.LandingPages;
using OptimizeAll.Api.Modules.Website.Careers;
using OptimizeAll.Api.Modules.Website.Public;
using OptimizeAll.Api.Modules.Website.Settings;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.SiteSeo;

/// <summary>
/// A server-side redirect for a public path (the Website module's slug-redirect manager plugs in here). Status is 301,
/// 308 or 410 (Gone: removed content, no target).
/// </summary>
public sealed record SeoRedirect(int Status, string? Location);

/// <summary>
/// Looks up a managed redirect for a request path as received (not yet normalized) and its query string (without "?").
/// The returned Location already carries the request's other query parameters (e.g. UTM tags). The Website module's
/// redirect manager (<see cref="Redirects.WebsiteRedirectLookup"/>) is the production implementation.
/// </summary>
public interface ISeoRedirectLookup
{
    Task<SeoRedirect?> FindAsync(string path, string query, CancellationToken ct);
}

public sealed class NoSeoRedirects : ISeoRedirectLookup
{
    public Task<SeoRedirect?> FindAsync(string path, string query, CancellationToken ct) => Task.FromResult<SeoRedirect?>(null);
}

/// <summary>
/// Resolves any public URL to its <see cref="SeoPage"/>: HTTP status (200 / 301 / 404 / 410), title, description,
/// canonical, robots, Open Graph, JSON-LD and crawlable content. It is the single source of truth for the
/// server-rendered HTML (<see cref="SeoDocumentWriter"/>), the sitemaps, llms.txt, the Markdown page versions and the
/// admin SEO overview. It reads the same services and page copy as the public API, so the server HTML and the web app's
/// head manager agree.
/// </summary>
public sealed partial class SeoPageResolver(
    PublicSiteService site, AppDbContext db, SiteCopyService copyService, CareersService careers, LandingPageService landing,
    ISeoRedirectLookup redirects, TimeProvider clock)
{
    /// <summary>Signed-in areas (portals). Never indexed; disallowed in robots.txt.</summary>
    public static readonly string[] PortalPrefixes = { "/app", "/admin", "/agency", "/client", "/review", "/finance", "/manage" };

    /// <summary>Sign-in and account pages: noindex, nofollow.</summary>
    public static readonly string[] AuthPaths =
        { "/login", "/register", "/check-email", "/verify-email", "/forgot-password", "/reset-password", "/auth", "/design-system" };

    /// <summary>Public pages reached through personal or tokenized links: noindex, nofollow (their case is preserved).</summary>
    public static readonly string[] TokenPrefixes = { "/p/", "/i/", "/email/", "/join/", "/f/" };

    /// <summary>Public utility pages that must not be indexed but whose links may be followed.</summary>
    public static readonly string[] UtilityPaths = { "/search", "/newsletter/confirm", "/newsletter/unsubscribe" };

    /// <summary>Built-in listing and form pages: path → page-copy prefix (titles and descriptions are editable page texts).</summary>
    public static readonly IReadOnlyDictionary<string, string> CopyPages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["/services"] = "services", ["/pricing"] = "pricing", ["/industries"] = "industries", ["/case-studies"] = "caseStudies",
        ["/blog"] = "blog", ["/team"] = "team", ["/careers"] = "careers", ["/contact"] = "contact", ["/free-audit"] = "audit",
        ["/get-a-quote"] = "quote", ["/book-a-consultation"] = "booking",
    };

    private SiteSettings _settings = null!;
    private string _baseUrl = string.Empty;
    private JsonLd _ld = null!;
    private SeoJsonLd _seoLd = null!;
    private CopyReader _copy = null!;
    private DateTime? _copyUpdatedAt;
    private DateTime? _settingsUpdatedAt;
    private bool _loaded;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public string BaseUrl => _baseUrl;

    public SiteSettings Settings => _settings;

    public CopyReader Copy => _copy;

    public string Absolute(string path) => _ld.Url(path);

    public async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded) return;
        _settings = await site.SettingsAsync(ct);
        _baseUrl = await site.BaseUrlAsync(ct);
        _ld = new JsonLd(_baseUrl, _settings);
        _seoLd = new SeoJsonLd(_ld, _settings.SiteName);
        var copy = await copyService.GetPublicAsync(ct);
        _copy = new CopyReader(copy.Values);
        _copyUpdatedAt = copy.UpdatedAt;
        _settingsUpdatedAt = await db.Set<SiteSettingsDocument>().AsNoTracking().Where(d => d.Key == SiteSettingsDocument.DefaultKey)
            .Select(d => (DateTime?)d.UpdatedAt).FirstOrDefaultAsync(ct);
        _loaded = true;
    }

    public static bool IsUnder(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);

    public static bool IsPrivatePath(string path) =>
        PortalPrefixes.Any(p => IsUnder(path, p)) || AuthPaths.Any(p => IsUnder(path, p));

    public static bool IsTokenPath(string path) => TokenPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// One URL form per page: no duplicate or trailing slashes, lower case (except personal token links, whose case is
    /// significant). Returns null when <paramref name="path"/> is already canonical.
    /// </summary>
    public static string? NormalizePath(string path)
    {
        var p = string.IsNullOrEmpty(path) ? "/" : path;
        while (p.Contains("//", StringComparison.Ordinal)) p = p.Replace("//", "/", StringComparison.Ordinal);
        if (p.Length > 1) p = p.TrimEnd('/');
        if (p.Length == 0) p = "/";
        if (p.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase)) p = p[..^"/index.html".Length] is { Length: > 0 } rest ? rest : "/";
        if (!IsTokenPath(p) && !IsPrivatePath(p)) p = p.ToLowerInvariant();
        return p == path ? null : p;
    }

    /// <summary>Resolves a request path (+ query string) to the page the server renders.</summary>
    public async Task<SeoPage> ResolveAsync(string path, string? queryString, CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        var qs = string.IsNullOrEmpty(queryString) || queryString == "?" ? string.Empty : queryString.StartsWith('?') ? queryString : "?" + queryString;
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(qs)
            .Where(kv => kv.Value.Count > 0 && !string.IsNullOrEmpty(kv.Value[0]))
            .ToDictionary(kv => kv.Key, kv => kv.Value[0]!, StringComparer.Ordinal);
        // Managed redirects first, on the path as requested: the redirect manager normalizes addresses itself
        // ("/Old-Page/" finds "/old-page"), so a moved address is one hop to its new home, not a normalization 301 first.
        if (await redirects.FindAsync(path, qs.TrimStart('?'), ct) is { } managed)
        {
            if (managed.Status == 410 || managed.Location is null) return Gone(path);
            return Redirect(path, managed.Location, managed.Status is 301 or 302 or 307 or 308 ? managed.Status : 301);
        }
        if (NormalizePath(path) is { } normalized)
            return Redirect(path, normalized + qs, 301);

        try
        {
            return await DispatchAsync(path, query, ct) ?? NotFound(path);
        }
        catch (DomainException ex) when (ex.Kind == DomainErrorKind.NotFound)
        {
            return NotFound(path);
        }
    }

    private async Task<SeoPage?> DispatchAsync(string path, IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        if (IsPrivatePath(path)) return Private(path);
        if (IsTokenPath(path)) return TokenPage(path);
        if (UtilityPaths.Contains(path)) return Utility(path, query);

        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        switch (segments.Length)
        {
            case 0:
                return await HomeAsync(ct);
            case 1:
                return segments[0] switch
                {
                    "services" => await ServicesAsync(ct),
                    "pricing" => await PricingAsync(ct),
                    "industries" => await IndustriesAsync(ct),
                    "case-studies" => await CaseStudiesAsync(query, ct),
                    "blog" => await BlogAsync(query, ct),
                    "team" => await TeamAsync(ct),
                    "careers" => await CareersAsync(ct),
                    "contact" or "free-audit" or "get-a-quote" or "book-a-consultation" => await FormPageAsync(path, ct),
                    "creators" => CreatorsPage(),
                    "faq" => await FaqAsync(ct),
                    _ => await CmsPageAsync(segments[0], ct),
                };
            case 2:
                return segments[0] switch
                {
                    "services" => await ServiceAsync(segments[1], ct),
                    "industries" => await IndustryAsync(segments[1], ct),
                    "case-studies" => await CaseStudyAsync(segments[1], ct),
                    "blog" => await PostAsync(segments[1], ct),
                    "careers" => await JobAsync(segments[1], ct),
                    "c" => await CampaignAsync(segments[1], ct),
                    _ => null,
                };
            case 3 when segments[0] == "lp":
                return await LandingAsync(segments[1], segments[2], ct);
            default:
                return null;
        }
    }

    // ---------------------------------------------------------------- Page scaffolding

    /// <summary>A content page with the defaults every page shares (title template, canonical = self, default OG image).</summary>
    public SeoPage NewPage(string path, string title, string? description, bool fullTitle = false)
    {
        var page = new SeoPage
        {
            Path = path,
            Title = fullTitle ? title : SeoText.ApplyTemplate(title, _settings.Seo.TitleTemplate, _settings.SiteName),
            Description = SeoText.Clamp(description ?? _settings.Seo.DefaultDescription),
            Canonical = _ld.Url(path),
        };
        SetImage(page, null, null);
        return page;
    }

    /// <summary>The built-in social image (frontend/public/og-default.png), used when neither the page nor the settings set one.</summary>
    public const string DefaultOgImagePath = "/og-default.png";

    /// <summary>Per-page social image, falling back to the site default and then /og-default.png (1200×630, with its size).</summary>
    public void SetImage(SeoPage page, string? image, string? alt)
    {
        var chosen = image ?? _settings.Seo.DefaultOgImageUrl;
        page.OgImage = chosen is null ? _ld.Url(DefaultOgImagePath) : _ld.Url(chosen);
        page.OgImageAlt = chosen is null ? $"{_settings.SiteName}: full-service digital marketing agency" : alt ?? page.Title;
        (page.OgImageWidth, page.OgImageHeight) = chosen is null ? (1200, 630) : ((int?)null, (int?)null);
        if (image is not null) page.Images.Add(new SeoImage(_ld.Url(image), alt ?? page.Title));
    }

    private void ApplySeo(SeoPage page, PublicSeoDto seo, IEnumerable<JsonElement> jsonLd, string? imageAlt = null)
    {
        page.Title = SeoText.ApplyTemplate(seo.Title, _settings.Seo.TitleTemplate, _settings.SiteName);
        page.Description = SeoText.Clamp(seo.Description);
        page.Canonical = seo.CanonicalUrl;
        page.NoIndex = seo.NoIndex;
        page.NoFollow = seo.NoIndex; // an editor's "noindex" hides the page completely, as the web app's head manager does
        page.Images.Clear();
        SetImage(page, seo.OgImageUrl == _settings.Seo.DefaultOgImageUrl ? null : seo.OgImageUrl, imageAlt);
        page.JsonLd.AddRange(jsonLd);
    }

    private void Crumbs(SeoPage page, params (string Name, string Path)[] items)
    {
        page.Breadcrumbs.Add(new Crumb("Home", "/"));
        foreach (var (name, p) in items) page.Breadcrumbs.Add(new Crumb(name, p));
    }

    /// <summary>Adds the BreadcrumbList node (pages whose payload JSON-LD already has one skip this).</summary>
    private void CrumbsLd(SeoPage page, params (string Name, string Path)[] items)
    {
        Crumbs(page, items);
        page.JsonLd.Add(_ld.Breadcrumbs(new[] { ("Home", "/") }.Concat(items).ToArray()));
    }

    private DateTime? Latest(params DateTime?[] values) => values.Where(v => v is not null).Max();

    private static string Money(decimal amount, string currency) =>
        currency switch
        {
            "USD" => "$" + amount.ToString("#,0.##", CultureInfo.InvariantCulture),
            "EUR" => "€" + amount.ToString("#,0.##", CultureInfo.InvariantCulture),
            "GBP" => "£" + amount.ToString("#,0.##", CultureInfo.InvariantCulture),
            _ => currency + " " + amount.ToString("#,0.##", CultureInfo.InvariantCulture),
        };

    private static string Period(PackageBillingPeriod p) => p switch
    {
        PackageBillingPeriod.OneTime => "one-time",
        PackageBillingPeriod.Monthly => "per month",
        PackageBillingPeriod.Quarterly => "per quarter",
        PackageBillingPeriod.Yearly => "per year",
        _ => string.Empty,
    };

    public static string PackagePrice(PublicPackageDto p) =>
        p.IsCustomQuote || p.Price is null ? "Custom quote" : $"{Money(p.Price.Value, p.Currency)} {Period(p.BillingPeriod)}".Trim()
            + (p.SetupFee is { } fee and > 0 ? $" + {Money(fee, p.Currency)} setup" : string.Empty);

    // ---------------------------------------------------------------- Non-content pages

    public SeoPage Private(string path)
    {
        var label = path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() switch
        {
            "login" => "Sign in",
            "register" => "Create your account",
            "forgot-password" or "reset-password" => "Reset your password",
            "check-email" or "verify-email" => "Verify your email",
            _ => "Your account",
        };
        var page = NewPage(path, label, null);
        page.Kind = SeoPageKind.Private;
        page.NoIndex = true;
        page.NoFollow = true;
        page.Canonical = null;
        page.Source = "App";
        page.Content.Add(new HeadingNode(1, label));
        page.Content.Add(new ParagraphNode($"This part of {_settings.SiteName} needs JavaScript and, for most pages, signing in."));
        page.Content.Add(new ActionNode($"Go to the {_settings.SiteName} home page", "/"));
        return page;
    }

    private SeoPage TokenPage(string path)
    {
        var page = NewPage(path, path.StartsWith("/join/", StringComparison.Ordinal) ? "You're invited" : _settings.SiteName, null);
        page.Kind = SeoPageKind.Utility;
        page.NoIndex = true;
        page.NoFollow = true;
        page.Canonical = null;
        page.Source = "Personal link";
        page.Content.Add(new HeadingNode(1, page.Title));
        page.Content.Add(new ParagraphNode("This page is opened from a personal link and needs JavaScript."));
        return page;
    }

    private SeoPage Utility(string path, IReadOnlyDictionary<string, string> query)
    {
        var title = path switch
        {
            "/search" => "Search",
            "/newsletter/confirm" => "Confirm your subscription",
            _ => "Unsubscribe",
        };
        var page = NewPage(path, title, null);
        page.Kind = SeoPageKind.Utility;
        page.NoIndex = true;
        page.NoFollow = path != "/search"; // newsletter links carry personal tokens
        page.Source = "Built-in page";
        page.Canonical = _ld.Url(path);
        CrumbsLd(page, (title, path));
        page.Content.Add(new HeadingNode(1, title));
        if (path == "/search")
            page.Content.Add(new ParagraphNode(query.TryGetValue("q", out var q) ? $"Search results for “{q}” need JavaScript." : "Search our services, articles and case studies."));
        return page;
    }

    public SeoPage NotFound(string path)
    {
        var title = _copy.Text("shared.page404.title");
        var page = NewPage(path, title.Length > 0 ? title : "Page not found", _copy.Text("shared.page404.description"));
        page.Status = 404;
        page.Kind = SeoPageKind.NotFound;
        page.NoIndex = true;
        page.Canonical = null;
        page.Content.Add(new HeadingNode(1, page.Title.Split(" | ")[0]));
        page.Content.Add(new ParagraphNode(_copy.Text("shared.page404.description")));
        page.Content.Add(new HeadingNode(2, "Helpful links"));
        page.Content.Add(new LinkListNode(new[]
        {
            new LinkItem("Home", "/"), new LinkItem("Services", "/services"), new LinkItem("Case studies", "/case-studies"),
            new LinkItem("Pricing", "/pricing"), new LinkItem("Blog", "/blog"), new LinkItem("Contact us", "/contact"),
            new LinkItem("Search the site", "/search"),
        }));
        return page;
    }

    private SeoPage Gone(string path)
    {
        var page = NotFound(path);
        page.Status = 410;
        page.Title = SeoText.ApplyTemplate("This page has been removed", _settings.Seo.TitleTemplate, _settings.SiteName);
        page.Content[0] = new HeadingNode(1, "This page has been removed");
        return page;
    }

    private SeoPage Redirect(string path, string location, int status) => new()
    {
        Path = path, Status = status, RedirectTo = location, Kind = SeoPageKind.Redirect, NoIndex = true,
        Title = SeoText.ApplyTemplate("Moved", _settings.Seo.TitleTemplate, _settings.SiteName),
    };

    // ---------------------------------------------------------------- Chrome

    /// <summary>Header, footer and legal links from the site settings (+ the services menu).</summary>
    public async Task<SiteChromeLinks> ChromeAsync(CancellationToken ct)
    {
        await EnsureLoadedAsync(ct);
        var header = new List<LinkItem>();
        foreach (var item in _settings.Header.Menu)
        {
            if (item.Url is not null) header.Add(new LinkItem(item.Label, item.Url));
            foreach (var child in item.Children ?? Array.Empty<MenuItem>())
                if (child.Url is not null && child.Url != item.Url) header.Add(new LinkItem(child.Label, child.Url));
        }
        if (_settings.Header.Cta is { } cta) header.Add(new LinkItem(cta.Label, cta.Url));
        var footer = _settings.Footer.Columns.Select(c => (c.Title, (IReadOnlyList<LinkItem>)c.Links.Select(l => new LinkItem(l.Label, l.Url)).ToList())).ToList();
        return new SiteChromeLinks(_settings.SiteName, header.DistinctBy(l => l.Href).ToList(), footer,
            _settings.Footer.LegalLinks.Select(l => new LinkItem(l.Label, l.Url)).ToList());
    }
}

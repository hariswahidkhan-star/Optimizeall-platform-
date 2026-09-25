using System.Globalization;
using OptimizeAll.Api.Common.Settings;
using OptimizeAll.Api.Modules.Website.Public;
using OptimizeAll.Domain.Learning;
using OptimizeAll.Domain.Settings;

namespace OptimizeAll.Api.Modules.Learning.Certificates;

/// <summary>The issuing organisation (admin settings <c>learning.issuerName</c>, <c>learning.linkedInOrganizationId</c>).</summary>
public sealed record LearningIssuer(string Name, string? LinkedInOrganizationId, string BaseUrl);

/// <summary>
/// Public URLs of the academy, certificates, badges and Open Badges documents. Links that leave the site (LinkedIn, Open
/// Badges documents, JSON-LD, Open Graph, sitemaps, emails, canonical URLs) are absolute on <see cref="BaseUrl"/>; images
/// and files our own web app shows or downloads are root-relative (<c>/api/v1/public/learning/…</c>) so they are always
/// same-origin for the web app's CSP (<c>img-src 'self'</c>), whatever the configured public URL.
/// </summary>
public sealed class LearningLinks(string baseUrl)
{
    public string BaseUrl { get; } = baseUrl.TrimEnd('/');

    public string Absolute(string path) => path.StartsWith("https://", StringComparison.Ordinal) || path.StartsWith("http://", StringComparison.Ordinal)
        ? path : BaseUrl + (path.StartsWith('/') ? path : "/" + path);

    public static string CoursePath(string slug) => $"/learn/{Uri.EscapeDataString(slug)}";
    public static string LessonPath(string slug, string lesson) => $"/learn/{Uri.EscapeDataString(slug)}/{Uri.EscapeDataString(lesson)}";
    public const string PathsPath = "/learn/paths";
    public static string PathPath(string slug) => $"/learn/paths/{Uri.EscapeDataString(slug)}";
    public static string VerifyPath(Guid certificateId) => $"/verify/certificates/{certificateId}";
    public static string BadgeImagePath(string slug) => $"/api/v1/public/learning/courses/{Uri.EscapeDataString(slug)}/badge.svg";
    public static string PdfPath(Guid id) => $"/api/v1/public/learning/certificates/{id}/certificate.pdf";
    public static string ImagePath(Guid id) => $"/api/v1/public/learning/certificates/{id}/certificate.svg";

    public string Course(string slug) => Absolute(CoursePath(slug));
    public string Verify(Guid certificateId) => Absolute(VerifyPath(certificateId));
    public string Pdf(Guid id) => Absolute(PdfPath(id));
    public string Image(Guid id) => Absolute(ImagePath(id));
    public string BadgeImage(string slug) => Absolute(BadgeImagePath(slug));
    public string OpenBadgesIssuer => Absolute("/api/v1/public/learning/openbadges/issuer");
    public string OpenBadgeClass(string slug) => Absolute($"/api/v1/public/learning/openbadges/badges/{Uri.EscapeDataString(slug)}");
    public string OpenBadgeAssertion(Guid id) => Absolute($"/api/v1/public/learning/openbadges/assertions/{id}");

    /// <summary>Links for the web app: verification, Open Badges and LinkedIn absolute; PDF, certificate and badge images root-relative.</summary>
    public CertificateLinksDto For(Certificate c, LearningIssuer issuer) => new(
        Verify(c.Id), PdfPath(c.Id), ImagePath(c.Id), BadgeImagePath(c.CourseSlug), OpenBadgeAssertion(c.Id),
        LinkedIn.AddToProfile(c.BadgeName, issuer, c.IssuedAt, Verify(c.Id), c.VerificationCode),
        LinkedIn.Share(Verify(c.Id)));
}

/// <summary>
/// LinkedIn URLs (no API access or credentials needed):
/// <list type="bullet">
/// <item>"Add to profile" for a certification — LinkedIn's documented Add-to-Profile URL:
/// <c>https://www.linkedin.com/profile/add?startTask=CERTIFICATION_NAME&amp;name=…&amp;organizationId=…&amp;issueYear=…&amp;issueMonth=…&amp;certUrl=…&amp;certId=…</c>
/// (<c>organizationName</c> instead of <c>organizationId</c> when no LinkedIn company page id is configured; the optional
/// <c>expirationYear</c>/<c>expirationMonth</c> are omitted because certificates do not expire).</item>
/// <item>"Share" — the share-offsite dialog: <c>https://www.linkedin.com/sharing/share-offsite/?url=…</c>; LinkedIn reads the
/// verification page's Open Graph tags for the preview.</item>
/// </list>
/// </summary>
public static class LinkedIn
{
    public const string AddToProfileBase = "https://www.linkedin.com/profile/add";
    public const string ShareBase = "https://www.linkedin.com/sharing/share-offsite/";

    public static string AddToProfile(string certificationName, LearningIssuer issuer, DateTime issuedAt, string certUrl, string certId)
    {
        var query = new List<(string, string)>
        {
            ("startTask", "CERTIFICATION_NAME"),
            ("name", certificationName),
        };
        query.Add(string.IsNullOrWhiteSpace(issuer.LinkedInOrganizationId)
            ? ("organizationName", issuer.Name)
            : ("organizationId", issuer.LinkedInOrganizationId.Trim()));
        query.Add(("issueYear", issuedAt.Year.ToString(CultureInfo.InvariantCulture)));
        query.Add(("issueMonth", issuedAt.Month.ToString(CultureInfo.InvariantCulture)));
        query.Add(("certUrl", certUrl));
        query.Add(("certId", certId));
        return AddToProfileBase + "?" + string.Join('&', query.Select(q => $"{q.Item1}={Uri.EscapeDataString(q.Item2)}"));
    }

    public static string Share(string url) => ShareBase + "?url=" + Uri.EscapeDataString(url);
}

/// <summary>Loads the issuer settings and the public base URL (<see cref="PublicSiteService.BaseUrlAsync"/>: IPublicOrigin).</summary>
public sealed class LearningIssuerProvider(ISettingsService settings, PublicSiteService site)
{
    private LearningIssuer? _issuer;

    public async Task<LearningIssuer> GetAsync(CancellationToken ct)
    {
        if (_issuer is not null) return _issuer;
        var name = await settings.GetAsync(SettingKeys.LearningIssuerName, "Optimize All Academy", ct);
        var linkedIn = await settings.GetAsync(SettingKeys.LearningLinkedInOrganizationId, string.Empty, ct);
        var baseUrl = await site.BaseUrlAsync(ct);
        return _issuer = new LearningIssuer(string.IsNullOrWhiteSpace(name) ? "Optimize All Academy" : name.Trim(),
            string.IsNullOrWhiteSpace(linkedIn) ? null : linkedIn.Trim(), baseUrl);
    }

    public async Task<LearningLinks> LinksAsync(CancellationToken ct) => new((await GetAsync(ct)).BaseUrl);
}

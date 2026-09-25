using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Learning.Certificates;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Learning;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Learning;

/// <summary>
/// The free public academy (anonymous, rate-limited per IP): catalog, course pages and lessons of published courses, and
/// course badge images. Final-exam questions are never served here (exams need an account). Documented in docs/api/learning.md.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/v1/public/learning")]
public sealed class PublicLearningController(PublicLearningService learning, LearningIssuerProvider issuers) : ControllerBase
{
    /// <summary>Published courses (featured first by default; sort=title|newest|duration|level).</summary>
    [HttpGet("courses")]
    public Task<PagedResult<CourseCardDto>> Courses([FromQuery] CatalogQuery query, CancellationToken ct) => learning.CatalogAsync(query, ct);

    /// <summary>
    /// The academy in numbers (counts, subjects, featured slugs, eight highlight cards, skills) for the marketing pages and
    /// the header: a few KB instead of the whole catalog. Cacheable for 5 minutes.
    /// </summary>
    [HttpGet("summary")]
    public async Task<LearningSummaryDto> Summary(CancellationToken ct)
    {
        Response.Headers.CacheControl = "public, max-age=300";
        return await learning.SummaryAsync(ct);
    }

    /// <summary>Categories that have published courses, with counts.</summary>
    [HttpGet("categories")]
    public Task<IReadOnlyList<CategorySummaryDto>> Categories(CancellationToken ct) => learning.CategoriesAsync(ct);

    /// <summary>A course page: outcomes, skills, syllabus, badge, exam rules, SEO block and schema.org JSON-LD.</summary>
    [HttpGet("courses/{slug}")]
    public Task<CourseDetailDto> Course(string slug, CancellationToken ct) => learning.CourseAsync(slug, ct);

    /// <summary>A lesson (Markdown body, takeaways, knowledge check with answers — it is not graded —, activity, video).</summary>
    [HttpGet("courses/{slug}/lessons/{lessonSlug}")]
    public Task<LessonDto> Lesson(string slug, string lessonSlug, CancellationToken ct) => learning.LessonAsync(slug, lessonSlug, ct);

    /// <summary>The course's badge as an SVG image (Open Badges image, cards, certificates).</summary>
    [HttpGet("courses/{slug}/badge.svg")]
    [Produces("image/svg+xml")]
    public async Task<IActionResult> Badge(string slug, CancellationToken ct)
    {
        var (course, doc) = await learning.LoadPublishedAsync(slug, ct);
        var issuer = await issuers.GetAsync(ct);
        Response.Headers.CacheControl = "public, max-age=3600";
        Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'";
        return Content(CertificateArt.BadgeSvg(doc.Pack.Badge!.Name, course.Category, course.Level, issuer.Name), "image/svg+xml; charset=utf-8", Encoding.UTF8);
    }
}

/// <summary>
/// Public credential verification (anonymous, rate-limited): certificate verification by id or code, the server-rendered
/// verification page, the certificate PDF and SVG, and the Open Badges 2.0 hosted documents (issuer, badge classes,
/// assertions). Revoked certificates stay verifiable (status "revoked"); their Open Badges assertion answers 410 Gone.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/v1/public/learning")]
public sealed class PublicCredentialsController(
    CertificateService certificates, PublicLearningService learning, LearningIssuerProvider issuers, AppDbContext db) : ControllerBase
{
    [HttpGet("certificates/{id:guid}")]
    public Task<CertificateVerificationDto> Verify(Guid id, CancellationToken ct) => certificates.VerifyAsync(id, ct);

    /// <summary>Looks a certificate up by its verification code ("OA-XXXX-XXXX", as printed and on LinkedIn).</summary>
    [HttpGet("certificates/verify")]
    public Task<CertificateVerificationDto> VerifyByCode([FromQuery] string? code, CancellationToken ct) => certificates.VerifyByCodeAsync(code, ct);

    [HttpGet("certificates/{id:guid}/certificate.pdf")]
    [Produces("application/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var (certificate, text) = await CertificateTextAsync(id, ct);
        Response.Headers.CacheControl = "no-cache";
        return File(CertificatePdf.Render(text), "application/pdf", $"certificate-{certificate.VerificationCode}.pdf");
    }

    [HttpGet("certificates/{id:guid}/certificate.svg")]
    [Produces("image/svg+xml")]
    public async Task<IActionResult> Image(Guid id, CancellationToken ct)
    {
        var (_, text) = await CertificateTextAsync(id, ct);
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'";
        Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        return Content(CertificateArt.CertificateSvg(text), "image/svg+xml; charset=utf-8", Encoding.UTF8);
    }

    private async Task<(Certificate Certificate, CertificateArt.CertificateText Text)> CertificateTextAsync(Guid id, CancellationToken ct)
    {
        var c = await certificates.FindAsync(id, ct);
        var issuer = await issuers.GetAsync(ct);
        var links = new LearningLinks(issuer.BaseUrl);
        var course = await db.Set<Course>().AsNoTracking().Where(x => x.Id == c.CourseId).Select(x => new { x.Category, x.Level }).FirstAsync(ct);
        return (c, new CertificateArt.CertificateText(c.HolderName, c.CourseTitle, c.BadgeName, course.Category, course.Level, c.Skills, c.IssuedAt,
            c.VerificationCode, links.Verify(c.Id), issuer.Name, c.IsRevoked));
    }

    // ---------------------------------------------------------------- Open Badges 2.0 (hosted)

    private void OpenBadgeHeaders()
    {
        Response.Headers.AccessControlAllowOrigin = "*";
        Response.Headers["Cross-Origin-Resource-Policy"] = "cross-origin";
        Response.Headers.CacheControl = "no-cache";
    }

    private ContentResult JsonLd(JsonNode node, int status = StatusCodes.Status200OK) =>
        new() { Content = node.ToJsonString(), ContentType = "application/ld+json; charset=utf-8", StatusCode = status };

    /// <summary>Open Badges 2.0 issuer profile.</summary>
    [HttpGet("openbadges/issuer")]
    [Produces("application/ld+json")]
    public async Task<IActionResult> Issuer(CancellationToken ct)
    {
        var issuer = await issuers.GetAsync(ct);
        OpenBadgeHeaders();
        return JsonLd(OpenBadges.Issuer(issuer, new LearningLinks(issuer.BaseUrl), null));
    }

    /// <summary>Open Badges 2.0 BadgeClass of a course.</summary>
    [HttpGet("openbadges/badges/{slug}")]
    [Produces("application/ld+json")]
    public async Task<IActionResult> BadgeClass(string slug, CancellationToken ct)
    {
        var (_, doc) = await learning.LoadPublishedAsync(slug, ct);
        var issuer = await issuers.GetAsync(ct);
        OpenBadgeHeaders();
        return JsonLd(OpenBadges.BadgeClass(doc.Pack, new LearningLinks(issuer.BaseUrl)));
    }

    /// <summary>
    /// Open Badges 2.0 hosted Assertion of a certificate. A revoked certificate answers 410 Gone with the spec's revoked
    /// stub (id, revoked: true) in a problem document (code learning.certificate_revoked).
    /// </summary>
    [HttpGet("openbadges/assertions/{id:guid}")]
    [Produces("application/ld+json")]
    public async Task<IActionResult> Assertion(Guid id, CancellationToken ct)
    {
        var c = await certificates.FindAsync(id, ct);
        var issuer = await issuers.GetAsync(ct);
        var links = new LearningLinks(issuer.BaseUrl);
        OpenBadgeHeaders();
        if (c.IsRevoked)
        {
            var stub = OpenBadges.Revoked(c, links);
            stub["type"] = "Assertion";
            stub["title"] = "Gone";
            stub["status"] = StatusCodes.Status410Gone;
            stub["detail"] = "This credential has been revoked by the issuer.";
            stub["code"] = "learning.certificate_revoked";
            stub["traceId"] = HttpContext.TraceIdentifier;
            return new ContentResult { Content = stub.ToJsonString(), ContentType = "application/problem+json", StatusCode = StatusCodes.Status410Gone };
        }
        var email = await db.Set<User>().AsNoTracking().Where(u => u.Id == c.UserId).Select(u => u.Email).FirstAsync(ct);
        return JsonLd(OpenBadges.Assertion(c, email, links));
    }
}

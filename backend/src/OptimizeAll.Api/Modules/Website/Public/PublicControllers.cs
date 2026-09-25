using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Website.Blog;
using OptimizeAll.Api.Modules.Website.Careers;
using OptimizeAll.Api.Modules.Website.Leads;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Public;

/// <summary>
/// The public agency website API (anonymous, rate-limited per IP). Only published content is returned. Documented in
/// docs/api/website.md.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/v1/public")]
public sealed class PublicWebsiteController(PublicSiteService site, AppDbContext db, TimeProvider clock) : ControllerBase
{
    /// <summary>Site settings for the header, footer, consent banner and head manager (+ the services mega-menu).</summary>
    [HttpGet("site")]
    public Task<PublicSiteDto> Site(CancellationToken ct) => site.SiteAsync(ct);

    [HttpGet("home")]
    public Task<HomeDto> Home(CancellationToken ct) => site.HomeAsync(ct);

    [HttpGet("services")]
    public Task<IReadOnlyList<ServiceCategoryGroupDto>> Services(CancellationToken ct) => site.ServicesAsync(ct);

    /// <summary>A published service with its active packages (ids are stable references for CRM/billing), FAQs and JSON-LD.</summary>
    [HttpGet("services/{slug}")]
    public Task<PublicServiceDto> Service(string slug, CancellationToken ct) => site.ServiceAsync(slug, ct);

    [HttpGet("pricing")]
    public Task<PricingDto> Pricing(CancellationToken ct) => site.PricingAsync(ct);

    [HttpGet("industries")]
    public Task<IReadOnlyList<IndustryCardDto>> Industries(CancellationToken ct) => site.IndustriesAsync(ct);

    [HttpGet("industries/{slug}")]
    public Task<PublicIndustryDto> Industry(string slug, CancellationToken ct) => site.IndustryAsync(slug, ct);

    /// <summary>Published case studies, optionally filtered by service slug and/or industry slug.</summary>
    [HttpGet("case-studies")]
    public Task<IReadOnlyList<CaseStudyCardDto>> CaseStudies([FromQuery] string? service, [FromQuery] string? industry, CancellationToken ct) =>
        site.CaseStudiesAsync(service, industry, ct);

    [HttpGet("case-studies/{slug}")]
    public Task<PublicCaseStudyDto> CaseStudy(string slug, CancellationToken ct) => site.CaseStudyAsync(slug, ct);

    [HttpGet("testimonials")]
    public Task<IReadOnlyList<PublicTestimonialDto>> Testimonials(CancellationToken ct) => site.TestimonialsAsync(true, 50, ct);

    [HttpGet("team")]
    public Task<IReadOnlyList<PublicTeamMemberDto>> Team(CancellationToken ct) => site.TeamAsync(ct);

    [HttpGet("pages/{slug}")]
    public Task<PublicPageDto> Page(string slug, CancellationToken ct) => site.PageAsync(slug, ct);

    /// <summary>Search across published services, blog posts and case studies (min. 2 characters).</summary>
    [HttpGet("search")]
    public Task<SearchResultDto> Search([FromQuery] string? q, CancellationToken ct) => site.SearchAsync(q, ct);

    // ---------------------------------------------------------------- Blog

    [HttpGet("blog")]
    public Task<BlogIndexDto> Blog([FromQuery] PublicBlogQuery query, CancellationToken ct) =>
        PublicBlogQueries.IndexAsync(db, clock.GetUtcNow().UtcDateTime, query, ct);

    [HttpGet("blog/{slug}")]
    public Task<PublicPostDto> Post(string slug, CancellationToken ct) =>
        PublicBlogQueries.PostAsync(db, site, clock.GetUtcNow().UtcDateTime, slug, ct);

    [HttpGet("blog/rss.xml")]
    public async Task<ContentResult> Rss(CancellationToken ct)
    {
        Response.Headers.CacheControl = "public, max-age=300";
        return Content(await PublicBlogQueries.RssAsync(db, site, clock.GetUtcNow().UtcDateTime, ct), "application/rss+xml; charset=utf-8");
    }

    // The sitemaps (/sitemap.xml index, /sitemaps/*.xml and the legacy flat /api/v1/public/sitemap.xml) and robots.txt
    // are served by SiteSeo.SeoFilesController from the SEO page resolver.
}

/// <summary>Public forms: contact, free audit, quote, consultation booking, newsletter and job applications.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/v1/public")]
public sealed class PublicFormsController(
    FormGuard guard, InquiryService inquiries, BookingService bookings, NewsletterService newsletter, CareersService careers) : ControllerBase
{
    /// <summary>A signed form token (proves minimum fill time) plus the budget/timeline options. Fetch when a form opens.</summary>
    [HttpGet("forms/token")]
    public FormTokenDto Token() => guard.Issue();

    [HttpPost("inquiries/contact")]
    [ProducesResponseType(typeof(InquiryAcceptedDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Contact(ContactInquiryInput input, CancellationToken ct) => Accepted(await inquiries.ContactAsync(input, ct));

    [HttpPost("inquiries/audit")]
    [ProducesResponseType(typeof(InquiryAcceptedDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Audit(AuditInquiryInput input, CancellationToken ct) => Accepted(await inquiries.AuditAsync(input, ct));

    [HttpPost("inquiries/quote")]
    [ProducesResponseType(typeof(InquiryAcceptedDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Quote(QuoteInquiryInput input, CancellationToken ct) => Accepted(await inquiries.QuoteAsync(input, ct));

    /// <summary>Free consultation slots (UTC instants) between <paramref name="from"/> and <paramref name="days"/> later.</summary>
    [HttpGet("consultations/slots")]
    public Task<SlotsDto> Slots([FromQuery] DateOnly? from, [FromQuery] int days = 14, CancellationToken ct = default) =>
        bookings.SlotsAsync(from, days, ct);

    [HttpPost("consultations")]
    [ProducesResponseType(typeof(BookingConfirmationDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Book(BookConsultationInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await bookings.BookAsync(input, ct));

    [HttpPost("newsletter/subscribe")]
    [ProducesResponseType(typeof(NewsletterResultDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Subscribe(NewsletterSubscribeInput input, CancellationToken ct) => Accepted(await newsletter.SubscribeAsync(input, ct));

    [HttpPost("newsletter/confirm")]
    public Task<NewsletterResultDto> Confirm(NewsletterTokenInput input, CancellationToken ct) => newsletter.ConfirmAsync(input, ct);

    [HttpPost("newsletter/unsubscribe")]
    public Task<NewsletterResultDto> Unsubscribe(NewsletterTokenInput input, CancellationToken ct) => newsletter.UnsubscribeAsync(input, ct);

    [HttpGet("careers")]
    public Task<IReadOnlyList<JobCardDto>> Jobs(CancellationToken ct) => careers.PublicJobsAsync(ct);

    [HttpGet("careers/{slug}")]
    public Task<PublicJobDto> Job(string slug, CancellationToken ct) => careers.PublicJobAsync(slug, ct);

    /// <summary>Multipart application with a PDF CV (max 5 MB; verified by content, not by name or MIME type).</summary>
    [HttpPost("careers/{slug}/applications")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [ProducesResponseType(typeof(ApplicationReceivedDto), StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Apply(string slug, [FromForm] JobApplicationForm form, CancellationToken ct) =>
        Accepted(await careers.ApplyAsync(slug, form, ct));
}

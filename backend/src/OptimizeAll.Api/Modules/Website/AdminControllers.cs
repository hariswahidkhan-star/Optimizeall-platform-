using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Api.Modules.Website.Blog;
using OptimizeAll.Api.Modules.Website.Careers;
using OptimizeAll.Api.Modules.Website.Catalog;
using OptimizeAll.Api.Modules.Website.Leads;
using OptimizeAll.Api.Modules.Website.Pages;
using OptimizeAll.Api.Modules.Website.Settings;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website;

/// <summary>Service categories, services and their packages (<c>site.manage</c>).</summary>
[ApiController]
[HasPermission(Permissions.SiteManage)]
[Route("api/v1/agency/website")]
public sealed class WebsiteServicesController(CatalogAdminService catalog) : ControllerBase
{
    [HttpGet("service-categories")]
    public Task<IReadOnlyList<ServiceCategoryDto>> Categories(CancellationToken ct) => catalog.ListCategoriesAsync(ct);

    [HttpPost("service-categories")]
    public async Task<IActionResult> CreateCategory(ServiceCategoryInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreateCategoryAsync(input, ct));

    [HttpPut("service-categories/{id:guid}")]
    public Task<ServiceCategoryDto> UpdateCategory(Guid id, ServiceCategoryInput input, CancellationToken ct) => catalog.UpdateCategoryAsync(id, input, ct);

    [HttpDelete("service-categories/{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct)
    {
        await catalog.DeleteCategoryAsync(id, ct);
        return NoContent();
    }

    [HttpPost("service-categories/reorder")]
    public Task<ReorderResult> ReorderCategories(ReorderInput input, CancellationToken ct) => catalog.ReorderCategoriesAsync(input, ct);

    [HttpGet("services")]
    public Task<PagedResult<ServiceSummaryDto>> Services([FromQuery] ServiceQuery query, CancellationToken ct) => catalog.ListServicesAsync(query, ct);

    [HttpGet("services/{id:guid}")]
    public Task<ServiceDto> Service(Guid id, CancellationToken ct) => catalog.GetServiceAsync(id, ct);

    [HttpPost("services")]
    public async Task<IActionResult> CreateService(ServiceInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreateServiceAsync(input, ct));

    [HttpPut("services/{id:guid}")]
    public Task<ServiceDto> UpdateService(Guid id, ServiceInput input, CancellationToken ct) => catalog.UpdateServiceAsync(id, input, ct);

    [HttpDelete("services/{id:guid}")]
    public async Task<IActionResult> DeleteService(Guid id, CancellationToken ct)
    {
        await catalog.DeleteServiceAsync(id, ct);
        return NoContent();
    }

    [HttpPost("services/reorder")]
    public Task<ReorderResult> ReorderServices(ReorderInput input, CancellationToken ct) => catalog.ReorderServicesAsync(input, ct);

    [HttpPost("services/{serviceId:guid}/packages")]
    public async Task<IActionResult> CreatePackage(Guid serviceId, PackageInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreatePackageAsync(serviceId, input, ct));

    [HttpPut("services/{serviceId:guid}/packages/{id:guid}")]
    public Task<PackageDto> UpdatePackage(Guid serviceId, Guid id, PackageInput input, CancellationToken ct) =>
        catalog.UpdatePackageAsync(serviceId, id, input, ct);

    [HttpDelete("services/{serviceId:guid}/packages/{id:guid}")]
    public async Task<IActionResult> DeletePackage(Guid serviceId, Guid id, CancellationToken ct)
    {
        await catalog.DeletePackageAsync(serviceId, id, ct);
        return NoContent();
    }
}

/// <summary>Service/package picker for other staff areas (CRM proposals, billing): any of site.manage, crm.view, proposals.manage, billing.view.</summary>
[ApiController]
[Authorize]
[RequireAnyPermission(Permissions.SiteManage, Permissions.CrmView, Permissions.ProposalsManage, Permissions.BillingView)]
[Route("api/v1/agency/website/catalog")]
public sealed class WebsiteCatalogController(IServiceCatalog catalog, ICurrentUser user) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ServiceInfo>> Get([FromQuery] bool includeUnpublished, CancellationToken ct)
    {
        user.RequireAny(Permissions.SiteManage, Permissions.CrmView, Permissions.ProposalsManage, Permissions.BillingView);
        return catalog.ListServicesAsync(includeUnpublished, ct);
    }
}

/// <summary>Industries, case studies, testimonials, team, pages and site settings (<c>site.manage</c>).</summary>
[ApiController]
[HasPermission(Permissions.SiteManage)]
[Route("api/v1/agency/website")]
public sealed class WebsiteContentController(CatalogAdminService catalog, SiteSettingsService settings) : ControllerBase
{
    [HttpGet("industries")]
    public Task<PagedResult<IndustryDto>> Industries([FromQuery] CmsQuery query, CancellationToken ct) => catalog.ListIndustriesAsync(query, ct);

    [HttpGet("industries/{id:guid}")]
    public Task<IndustryDto> Industry(Guid id, CancellationToken ct) => catalog.GetIndustryAsync(id, ct);

    [HttpPost("industries")]
    public async Task<IActionResult> CreateIndustry(IndustryInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreateIndustryAsync(input, ct));

    [HttpPut("industries/{id:guid}")]
    public Task<IndustryDto> UpdateIndustry(Guid id, IndustryInput input, CancellationToken ct) => catalog.UpdateIndustryAsync(id, input, ct);

    [HttpDelete("industries/{id:guid}")]
    public async Task<IActionResult> DeleteIndustry(Guid id, CancellationToken ct)
    {
        await catalog.DeleteIndustryAsync(id, ct);
        return NoContent();
    }

    [HttpPost("industries/reorder")]
    public Task<ReorderResult> ReorderIndustries(ReorderInput input, CancellationToken ct) => catalog.ReorderIndustriesAsync(input, ct);

    [HttpGet("case-studies")]
    public Task<PagedResult<CaseStudyDto>> CaseStudies([FromQuery] CmsQuery query, CancellationToken ct) => catalog.ListCaseStudiesAsync(query, ct);

    [HttpGet("case-studies/{id:guid}")]
    public Task<CaseStudyDto> CaseStudy(Guid id, CancellationToken ct) => catalog.GetCaseStudyAsync(id, ct);

    [HttpPost("case-studies")]
    public async Task<IActionResult> CreateCaseStudy(CaseStudyInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreateCaseStudyAsync(input, ct));

    [HttpPut("case-studies/{id:guid}")]
    public Task<CaseStudyDto> UpdateCaseStudy(Guid id, CaseStudyInput input, CancellationToken ct) => catalog.UpdateCaseStudyAsync(id, input, ct);

    [HttpDelete("case-studies/{id:guid}")]
    public async Task<IActionResult> DeleteCaseStudy(Guid id, CancellationToken ct)
    {
        await catalog.DeleteCaseStudyAsync(id, ct);
        return NoContent();
    }

    [HttpPost("case-studies/reorder")]
    public Task<ReorderResult> ReorderCaseStudies(ReorderInput input, CancellationToken ct) => catalog.ReorderCaseStudiesAsync(input, ct);

    [HttpGet("testimonials")]
    public Task<PagedResult<TestimonialDto>> Testimonials([FromQuery] CmsQuery query, CancellationToken ct) => catalog.ListTestimonialsAsync(query, ct);

    [HttpPost("testimonials")]
    public async Task<IActionResult> CreateTestimonial(TestimonialInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreateTestimonialAsync(input, ct));

    [HttpPut("testimonials/{id:guid}")]
    public Task<TestimonialDto> UpdateTestimonial(Guid id, TestimonialInput input, CancellationToken ct) => catalog.UpdateTestimonialAsync(id, input, ct);

    [HttpDelete("testimonials/{id:guid}")]
    public async Task<IActionResult> DeleteTestimonial(Guid id, CancellationToken ct)
    {
        await catalog.DeleteTestimonialAsync(id, ct);
        return NoContent();
    }

    [HttpPost("testimonials/reorder")]
    public Task<ReorderResult> ReorderTestimonials(ReorderInput input, CancellationToken ct) => catalog.ReorderTestimonialsAsync(input, ct);

    [HttpGet("team")]
    public Task<IReadOnlyList<TeamMemberDto>> Team(CancellationToken ct) => catalog.ListTeamAsync(ct);

    [HttpPost("team")]
    public async Task<IActionResult> CreateTeamMember(TeamMemberInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreateTeamMemberAsync(input, ct));

    [HttpPut("team/{id:guid}")]
    public Task<TeamMemberDto> UpdateTeamMember(Guid id, TeamMemberInput input, CancellationToken ct) => catalog.UpdateTeamMemberAsync(id, input, ct);

    [HttpDelete("team/{id:guid}")]
    public async Task<IActionResult> DeleteTeamMember(Guid id, CancellationToken ct)
    {
        await catalog.DeleteTeamMemberAsync(id, ct);
        return NoContent();
    }

    [HttpPost("team/reorder")]
    public Task<ReorderResult> ReorderTeam(ReorderInput input, CancellationToken ct) => catalog.ReorderTeamAsync(input, ct);

    [HttpGet("pages")]
    public Task<IReadOnlyList<SitePageSummaryDto>> Pages(CancellationToken ct) => catalog.ListPagesAsync(ct);

    [HttpGet("pages/{id:guid}")]
    public Task<SitePageDto> Page(Guid id, CancellationToken ct) => catalog.GetPageAsync(id, ct);

    [HttpPost("pages")]
    public async Task<IActionResult> CreatePage(SitePageInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await catalog.CreatePageAsync(input, ct));

    [HttpPut("pages/{id:guid}")]
    public Task<SitePageDto> UpdatePage(Guid id, SitePageInput input, CancellationToken ct) => catalog.UpdatePageAsync(id, input, ct);

    [HttpDelete("pages/{id:guid}")]
    public async Task<IActionResult> DeletePage(Guid id, CancellationToken ct)
    {
        await catalog.DeletePageAsync(id, ct);
        return NoContent();
    }

    [HttpGet("pages/{id:guid}/revisions")]
    public Task<IReadOnlyList<SitePageRevisionSummaryDto>> PageRevisions(Guid id, CancellationToken ct) => catalog.ListRevisionsAsync(id, ct);

    [HttpGet("pages/{id:guid}/revisions/{version:int}")]
    public Task<SitePageRevisionDto> PageRevision(Guid id, int version, CancellationToken ct) => catalog.GetRevisionAsync(id, version, ct);

    [HttpPost("pages/{id:guid}/revisions/{version:int}/restore")]
    public Task<SitePageDto> RestorePageRevision(Guid id, int version, RestorePageRevisionInput input, CancellationToken ct) =>
        catalog.RestoreRevisionAsync(id, version, input, ct);

    /// <summary>Validates and normalizes blocks without saving (editor live preview).</summary>
    [HttpPost("pages/preview")]
    public IReadOnlyList<PageBlock> PreviewBlocks(List<PageBlockInput> blocks) => catalog.PreviewBlocks(blocks);

    [HttpGet("settings")]
    public Task<SiteSettingsDto> Settings(CancellationToken ct) => settings.GetForEditAsync(ct);

    /// <summary>
    /// Denied while impersonating: the settings choose the tag-manager / analytics scripts loaded on every public page and
    /// the canonical site URL used by links, the sitemap and robots.txt (platform-wide configuration, like admin settings).
    /// </summary>
    [HttpPut("settings")]
    [DeniedWhileImpersonating]
    public Task<SiteSettingsDto> UpdateSettings(UpdateSiteSettingsRequest request, CancellationToken ct) => settings.UpdateAsync(request, ct);
}

/// <summary>Blog posts and categories. Authorization per action inside <see cref="BlogService"/> (blog.write / blog.publish).</summary>
[ApiController]
[Authorize]
[RequireAnyPermission(Permissions.BlogWrite, Permissions.BlogPublish)]
[Route("api/v1/agency/website/blog")]
public sealed class WebsiteBlogController(BlogService blog) : ControllerBase
{
    [HttpGet("posts")]
    public Task<PagedResult<BlogPostSummaryDto>> Posts([FromQuery] BlogPostQuery query, CancellationToken ct) => blog.ListPostsAsync(query, ct);

    [HttpGet("posts/{id:guid}")]
    public Task<BlogPostDto> Post(Guid id, CancellationToken ct) => blog.GetPostAsync(id, ct);

    [HttpPost("posts")]
    public async Task<IActionResult> Create(BlogPostInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await blog.CreatePostAsync(input, ct));

    [HttpPut("posts/{id:guid}")]
    public Task<BlogPostDto> Update(Guid id, BlogPostInput input, CancellationToken ct) => blog.UpdatePostAsync(id, input, ct);

    [HttpDelete("posts/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await blog.DeletePostAsync(id, ct);
        return NoContent();
    }

    [HttpPost("posts/{id:guid}/submit")]
    public Task<BlogPostDto> Submit(Guid id, BlogActionInput input, CancellationToken ct) => blog.SubmitAsync(id, input, ct);

    [HttpPost("posts/{id:guid}/publish")]
    [HasPermission(Permissions.BlogPublish)]
    public Task<BlogPostDto> Publish(Guid id, BlogActionInput input, CancellationToken ct) => blog.PublishAsync(id, input, ct);

    [HttpPost("posts/{id:guid}/schedule")]
    [HasPermission(Permissions.BlogPublish)]
    public Task<BlogPostDto> Schedule(Guid id, BlogActionInput input, CancellationToken ct) => blog.ScheduleAsync(id, input, ct);

    [HttpPost("posts/{id:guid}/unpublish")]
    [HasPermission(Permissions.BlogPublish)]
    public Task<BlogPostDto> Unpublish(Guid id, BlogActionInput input, CancellationToken ct) => blog.UnpublishAsync(id, input, ct);

    [HttpPost("posts/{id:guid}/return-to-draft")]
    [HasPermission(Permissions.BlogPublish)]
    public Task<BlogPostDto> ReturnToDraft(Guid id, BlogActionInput input, CancellationToken ct) => blog.ReturnToDraftAsync(id, input, ct);

    [HttpGet("categories")]
    public Task<IReadOnlyList<BlogCategoryDto>> Categories(CancellationToken ct) => blog.ListCategoriesAsync(ct);

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory(BlogCategoryInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await blog.CreateCategoryAsync(input, ct));

    [HttpPost("categories/reorder")]
    public Task<ReorderResult> ReorderCategories(ReorderInput input, CancellationToken ct) => blog.ReorderCategoriesAsync(input, ct);

    [HttpPut("categories/{id:guid}")]
    public Task<BlogCategoryDto> UpdateCategory(Guid id, BlogCategoryInput input, CancellationToken ct) => blog.UpdateCategoryAsync(id, input, ct);

    [HttpDelete("categories/{id:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid id, CancellationToken ct)
    {
        await blog.DeleteCategoryAsync(id, ct);
        return NoContent();
    }

    /// <summary>Team members who can be bylined as authors (writers need this without site.manage).</summary>
    [HttpGet("authors")]
    public async Task<IReadOnlyList<AuthorOptionDto>> Authors([FromServices] AppDbContext db, CancellationToken ct) =>
        await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            db.Set<TeamMember>().OrderBy(m => m.SortOrder).ThenBy(m => m.Name).Select(m => new AuthorOptionDto(m.Id, m.Name, m.Role)), ct);
}

/// <summary>Job openings and the applications pipeline (<c>careers.manage</c>).</summary>
[ApiController]
[HasPermission(Permissions.CareersManage)]
[Route("api/v1/agency/website/careers")]
public sealed class WebsiteCareersController(CareersService careers) : ControllerBase
{
    [HttpGet("jobs")]
    public Task<IReadOnlyList<JobDto>> Jobs(CancellationToken ct) => careers.ListJobsAsync(ct);

    [HttpGet("jobs/{id:guid}")]
    public Task<JobDto> Job(Guid id, CancellationToken ct) => careers.GetJobAsync(id, ct);

    [HttpPost("jobs")]
    public async Task<IActionResult> CreateJob(JobInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await careers.CreateJobAsync(input, ct));

    [HttpPut("jobs/{id:guid}")]
    public Task<JobDto> UpdateJob(Guid id, JobInput input, CancellationToken ct) => careers.UpdateJobAsync(id, input, ct);

    [HttpDelete("jobs/{id:guid}")]
    public async Task<IActionResult> DeleteJob(Guid id, CancellationToken ct)
    {
        await careers.DeleteJobAsync(id, ct);
        return NoContent();
    }

    [HttpGet("applications")]
    public Task<PagedResult<ApplicationSummaryDto>> Applications([FromQuery] ApplicationQuery query, CancellationToken ct) =>
        careers.ListApplicationsAsync(query, ct);

    [HttpGet("applications/{id:guid}")]
    public Task<ApplicationDto> Application(Guid id, CancellationToken ct) => careers.GetApplicationAsync(id, ct);

    [HttpPost("applications/{id:guid}/move")]
    public Task<ApplicationDto> Move(Guid id, MoveApplicationInput input, CancellationToken ct) => careers.MoveAsync(id, input, ct);

    /// <summary>Erases the application, its notes and its CV.</summary>
    [HttpDelete("applications/{id:guid}")]
    public async Task<IActionResult> DeleteApplication(Guid id, CancellationToken ct)
    {
        await careers.DeleteApplicationAsync(id, ct);
        return NoContent();
    }

    [HttpPost("applications/{id:guid}/notes")]
    public Task<ApplicationDto> AddNote(Guid id, ApplicationNoteInput input, CancellationToken ct) => careers.AddNoteAsync(id, input, ct);

    /// <summary>Downloads the applicant's CV (PDF, attachment, audited).</summary>
    [HttpGet("applications/{id:guid}/cv")]
    public async Task<IActionResult> Cv(Guid id, CancellationToken ct)
    {
        var file = await careers.DownloadCvAsync(id, ct);
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        return File(file.Content, "application/pdf", file.FileName);
    }
}

/// <summary>Leads inbox (site.manage; crm.view may read), consultations and newsletter (site.manage), overview KPIs.</summary>
[ApiController]
[Authorize]
[Route("api/v1/agency/website")]
public sealed class WebsiteLeadsController(
    InquiryService inquiries, BookingService bookings, NewsletterService newsletter, OverviewService overview) : ControllerBase
{
    [HttpGet("overview")]
    [RequireAnyPermission(Permissions.SiteManage, Permissions.CrmView, Permissions.BlogWrite, Permissions.BlogPublish, Permissions.CareersManage)]
    public Task<WebsiteOverviewDto> Overview(CancellationToken ct)
    {
        return overview.GetAsync(ct);
    }

    [HttpGet("inquiries")]
    [RequireAnyPermission(Permissions.SiteManage, Permissions.CrmView)]
    public Task<PagedResult<InquirySummaryDto>> Inquiries([FromQuery] InquiryQuery query, CancellationToken ct) => inquiries.ListAsync(query, ct);

    [HttpGet("inquiries/export.csv")]
    [RequireAnyPermission(Permissions.SiteManage, Permissions.CrmView)]
    public async Task<IActionResult> ExportInquiries([FromQuery] InquiryQuery query, CancellationToken ct)
    {
        var rows = await inquiries.ExportRowsAsync(query, ct);
        return Csv.File("website-inquiries.csv",
            new[] { "reference", "type", "status", "name", "email", "phone", "company", "website", "services", "budget", "timeline", "utm_source", "utm_medium", "utm_campaign", "created_at" },
            rows.Select(i => new object?[]
            {
                LeadReference.For(i.Id), i.Type, i.Status, i.Name, i.Email, i.Phone, i.Company, i.Website, string.Join(" ", i.ServiceSlugs),
                i.BudgetRange, i.Timeline, i.UtmSource, i.UtmMedium, i.UtmCampaign, i.CreatedAt,
            }));
    }

    [HttpGet("inquiries/{id:guid}")]
    [RequireAnyPermission(Permissions.SiteManage, Permissions.CrmView)]
    public Task<InquiryDto> Inquiry(Guid id, CancellationToken ct) => inquiries.GetAsync(id, ct);

    [HttpPut("inquiries/{id:guid}")]
    [HasPermission(Permissions.SiteManage)]
    public Task<InquiryDto> UpdateInquiry(Guid id, UpdateInquiryInput input, CancellationToken ct) => inquiries.UpdateAsync(id, input, ct);

    [HttpDelete("inquiries/{id:guid}")]
    [HasPermission(Permissions.SiteManage)]
    public async Task<IActionResult> DeleteInquiry(Guid id, CancellationToken ct)
    {
        await inquiries.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpGet("bookings/settings")]
    [HasPermission(Permissions.SiteManage)]
    public Task<ConsultationSettingsDto> BookingSettings(CancellationToken ct) => bookings.GetSettingsAsync(ct);

    [HttpPut("bookings/settings")]
    [HasPermission(Permissions.SiteManage)]
    public Task<ConsultationSettingsDto> UpdateBookingSettings(ConsultationSettingsInput input, CancellationToken ct) => bookings.UpdateSettingsAsync(input, ct);

    [HttpPost("bookings/blackouts")]
    [HasPermission(Permissions.SiteManage)]
    public async Task<IActionResult> AddBlackout(BlackoutInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await bookings.AddBlackoutAsync(input, ct));

    [HttpDelete("bookings/blackouts/{id:guid}")]
    [HasPermission(Permissions.SiteManage)]
    public async Task<IActionResult> DeleteBlackout(Guid id, CancellationToken ct)
    {
        await bookings.DeleteBlackoutAsync(id, ct);
        return NoContent();
    }

    [HttpGet("bookings")]
    [HasPermission(Permissions.SiteManage)]
    public Task<PagedResult<BookingDto>> Bookings([FromQuery] BookingQuery query, CancellationToken ct) => bookings.ListBookingsAsync(query, ct);

    /// <summary>Free slots for rescheduling (same availability as the public page).</summary>
    [HttpGet("bookings/slots")]
    [HasPermission(Permissions.SiteManage)]
    public Task<SlotsDto> Slots([FromQuery] DateOnly? from, [FromQuery] int days = 14, CancellationToken ct = default) => bookings.SlotsAsync(from, days, ct);

    [HttpPost("bookings/{id:guid}/cancel")]
    [HasPermission(Permissions.SiteManage)]
    public Task<BookingDto> Cancel(Guid id, CancelBookingInput input, CancellationToken ct) => bookings.CancelAsync(id, input, ct);

    [HttpPost("bookings/{id:guid}/reschedule")]
    [HasPermission(Permissions.SiteManage)]
    public Task<BookingDto> Reschedule(Guid id, RescheduleBookingInput input, CancellationToken ct) => bookings.RescheduleAsync(id, input, ct);

    [HttpPost("bookings/{id:guid}/status")]
    [HasPermission(Permissions.SiteManage)]
    public Task<BookingDto> SetStatus(Guid id, BookingStatusInput input, CancellationToken ct) => bookings.SetStatusAsync(id, input, ct);

    [HttpGet("newsletter/subscribers")]
    [HasPermission(Permissions.SiteManage)]
    public Task<PagedResult<SubscriberDto>> Subscribers([FromQuery] SubscriberQuery query, CancellationToken ct) => newsletter.ListAsync(query, ct);

    [HttpPost("newsletter/subscribers/{id:guid}/unsubscribe")]
    [HasPermission(Permissions.SiteManage)]
    public Task<SubscriberDto> UnsubscribeSubscriber(Guid id, CancellationToken ct) => newsletter.UnsubscribeByStaffAsync(id, ct);

    [HttpDelete("newsletter/subscribers/{id:guid}")]
    [HasPermission(Permissions.SiteManage)]
    public async Task<IActionResult> DeleteSubscriber(Guid id, CancellationToken ct)
    {
        await newsletter.DeleteSubscriberAsync(id, ct);
        return NoContent();
    }

    [HttpGet("newsletter/subscribers/export.csv")]
    [HasPermission(Permissions.SiteManage)]
    public async Task<IActionResult> ExportSubscribers([FromQuery] SubscriberQuery query, CancellationToken ct)
    {
        var rows = await newsletter.ExportAsync(query, ct);
        return Csv.File("newsletter-subscribers.csv",
            new[] { "email", "status", "source", "consent_version", "consent_at", "confirmed_at", "unsubscribed_at", "utm_source", "created_at" },
            rows.Select(s => new object?[] { s.Email, s.Status, s.Source, s.ConsentVersion, s.ConsentAt, s.ConfirmedAt, s.UnsubscribedAt, s.UtmSource, s.CreatedAt }));
    }
}

/// <summary>
/// Image uploads for website content (service heroes, case-study galleries, blog covers, team photos, logos). Mirrors
/// <c>POST /admin/files</c> but is open to website editors (site.manage, blog.write, blog.publish, careers.manage), who do not
/// hold content.manage. Stored as public content images through the Files module (magic bytes, metadata stripped).
/// </summary>
[ApiController]
[Authorize]
[RequireAnyPermission(Permissions.SiteManage, Permissions.BlogWrite, Permissions.BlogPublish, Permissions.CareersManage)]
[Route("api/v1/agency/website/images")]
public sealed class WebsiteImagesController(IFileService files, ICurrentUser user, AppDbContext db, IAuditLogger audit) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<StoredFileDto>> Upload([FromForm] WebsiteImageForm form, CancellationToken ct)
    {
        user.RequireAny(Permissions.SiteManage, Permissions.BlogWrite, Permissions.BlogPublish, Permissions.CareersManage);
        if (form.File is null) throw new DomainException("file.required", "Choose an image to upload.");
        var file = await files.SaveImageAsync(form.File, FilePurpose.ContentImage, user.Id, isPublic: true, ct);
        audit.Record("website.image_uploaded", nameof(StoredFile), file.Id, after: new { file.ContentType, file.SizeBytes, file.Width, file.Height, file.Sha256 });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            files.Discard(file);
            throw;
        }
        return StatusCode(StatusCodes.Status201Created, StoredFileDto.From(file));
    }
}

public sealed record AuthorOptionDto(Guid Id, string Name, string Role);

public sealed class WebsiteImageForm
{
    public IFormFile? File { get; set; }
}

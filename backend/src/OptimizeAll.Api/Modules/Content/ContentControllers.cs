using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Content;

[ApiController]
[HasPermission(Permissions.ContentManage)]
[Route("api/v1/admin/content/banners")]
public sealed class AdminBannersController(ContentService content) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<BannerDto>> List([FromQuery] BannerQuery query, CancellationToken ct) => content.ListBannersAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<BannerDto> Get(Guid id, CancellationToken ct) => content.GetBannerAsync(id, ct);

    [HttpPost]
    [ProducesResponseType(typeof(BannerDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(BannerRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await content.CreateBannerAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<BannerDto> Update(Guid id, UpdateBannerRequest request, CancellationToken ct) => content.UpdateBannerAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await content.DeleteBannerAsync(id, ct);
        return NoContent();
    }

    [HttpPost("reorder")]
    public Task<ReorderResponse> Reorder(ReorderRequest request, CancellationToken ct) => content.ReorderBannersAsync(request, ct);
}

[ApiController]
[HasPermission(Permissions.ContentManage)]
[Route("api/v1/admin/content/announcements")]
public sealed class AdminAnnouncementsController(ContentService content) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AnnouncementDto>> List([FromQuery] AnnouncementQuery query, CancellationToken ct) =>
        content.ListAnnouncementsAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<AnnouncementDto> Get(Guid id, CancellationToken ct) => content.GetAnnouncementAsync(id, ct);

    [HttpPost]
    [ProducesResponseType(typeof(AnnouncementDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(AnnouncementRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await content.CreateAnnouncementAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<AnnouncementDto> Update(Guid id, UpdateAnnouncementRequest request, CancellationToken ct) =>
        content.UpdateAnnouncementAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await content.DeleteAnnouncementAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[HasPermission(Permissions.ContentManage)]
[Route("api/v1/admin/content/faqs")]
public sealed class AdminFaqsController(ContentService content) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<FaqDto>> List([FromQuery] FaqQuery query, CancellationToken ct) => content.ListFaqsAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<FaqDto> Get(Guid id, CancellationToken ct) => content.GetFaqAsync(id, ct);

    [HttpPost]
    [ProducesResponseType(typeof(FaqDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(FaqRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await content.CreateFaqAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<FaqDto> Update(Guid id, UpdateFaqRequest request, CancellationToken ct) => content.UpdateFaqAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await content.DeleteFaqAsync(id, ct);
        return NoContent();
    }

    [HttpPost("reorder")]
    public Task<ReorderResponse> Reorder(ReorderRequest request, CancellationToken ct) => content.ReorderFaqsAsync(request, ct);
}

[ApiController]
[HasPermission(Permissions.ContentManage)]
[Route("api/v1/admin/content/onboarding-steps")]
public sealed class AdminOnboardingStepsController(ContentService content) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<OnboardingStepDto>> List([FromQuery] OnboardingStepQuery query, CancellationToken ct) =>
        content.ListStepsAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<OnboardingStepDto> Get(Guid id, CancellationToken ct) => content.GetStepAsync(id, ct);

    [HttpPost]
    [ProducesResponseType(typeof(OnboardingStepDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(OnboardingStepRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await content.CreateStepAsync(request, ct));

    [HttpPut("{id:guid}")]
    public Task<OnboardingStepDto> Update(Guid id, UpdateOnboardingStepRequest request, CancellationToken ct) =>
        content.UpdateStepAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await content.DeleteStepAsync(id, ct);
        return NoContent();
    }

    [HttpPost("reorder")]
    public Task<ReorderResponse> Reorder(ReorderRequest request, CancellationToken ct) => content.ReorderStepsAsync(request, ct);
}

/// <summary>Public and participant-facing content reads.</summary>
[ApiController]
[Route("api/v1/content")]
public sealed class PublicContentController(
    ContentService content, IParticipantStateService state, AppDbContext db, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Published FAQ grouped by category. Anonymous.</summary>
    [HttpGet("faqs")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Public)]
    public Task<PublicFaqDto> Faqs(CancellationToken ct) => content.PublicFaqAsync(ct);

    /// <summary>Active announcements whose audience matches the signed-in user.</summary>
    [HttpGet("announcements")]
    [Authorize]
    public async Task<IReadOnlyList<AnnouncementViewDto>> Announcements(CancellationToken ct)
    {
        var snapshot = await state.LoadAsync(currentUser.Id, ct);
        return await HomeService.VisibleAnnouncementsAsync(db, snapshot, ct);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Modules.Campaigns;

/// <summary>Participant campaign browsing and detail.</summary>
[ApiController]
public sealed class CampaignsController(ICampaignCatalogService catalog, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Active campaign categories (anonymous).</summary>
    [AllowAnonymous]
    [HttpGet("api/v1/campaign-categories")]
    public Task<IReadOnlyList<CategoryDto>> Categories(CancellationToken ct) => catalog.ActiveCategoriesAsync(ct);

    /// <summary>Browse Active (and upcoming Scheduled) public campaigns with eligibility for the caller.</summary>
    [HasPermission(Permissions.ParticipantPortal)]
    [HttpGet("api/v1/campaigns")]
    public Task<PagedResult<CampaignCardDto>> Browse([FromQuery] CampaignBrowseQuery query, CancellationToken ct) =>
        catalog.BrowseAsync(currentUser.Id, query, ct);

    /// <summary>Campaigns the caller is eligible for, ranked by interests, platforms, reward and deadline.</summary>
    [HasPermission(Permissions.ParticipantPortal)]
    [HttpGet("api/v1/campaigns/recommended")]
    public Task<IReadOnlyList<RecommendedCampaignDto>> Recommended([FromQuery] int limit = 6, CancellationToken ct = default) =>
        catalog.RecommendedAsync(currentUser.Id, limit, ct);

    /// <summary>Campaign detail by slug. InviteOnly campaigns are unlisted but reachable here; Draft/Archived are 404.</summary>
    [HasPermission(Permissions.ParticipantPortal)]
    [HttpGet("api/v1/campaigns/{slug}")]
    public Task<CampaignDetailDto> Detail(string slug, CancellationToken ct) => catalog.DetailAsync(currentUser.Id, slug, ct);
}

/// <summary>Campaign id/title/status options for staff filters and pickers (reviewers, finance, managers).</summary>
[ApiController]
public sealed class CampaignOptionsController(ICampaignAdminService campaigns) : ControllerBase
{
    /// <summary>Newest first, at most 500; <paramref name="search"/> matches title or slug.</summary>
    [HasPermission(Permissions.CampaignsView)]
    [HttpGet("api/v1/campaigns/options")]
    public Task<IReadOnlyList<CampaignOptionDto>> Options([FromQuery] string? search, CancellationToken ct) =>
        campaigns.OptionsAsync(search, ct);
}

/// <summary>Staff campaign management.</summary>
[ApiController]
[Route("api/v1/admin/campaigns")]
[HasPermission(Permissions.CampaignsManage)]
public sealed class AdminCampaignsController(ICampaignAdminService campaigns) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AdminCampaignListItemDto>> List([FromQuery] AdminCampaignQuery query, CancellationToken ct) =>
        campaigns.ListAsync(query, ct);

    /// <summary>Creates a Draft with reward rules v1 (also requires rewards.edit).</summary>
    [HttpPost]
    public async Task<ActionResult<AdminCampaignDto>> Create(CreateCampaignRequest request, CancellationToken ct)
    {
        var created = await campaigns.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    public Task<AdminCampaignDto> Get(Guid id, CancellationToken ct) => campaigns.GetAsync(id, ct);

    [HttpPut("{id:guid}")]
    public Task<AdminCampaignDto> Update(Guid id, UpdateCampaignRequest request, CancellationToken ct) =>
        campaigns.UpdateAsync(id, request, ct);

    [HttpPost("{id:guid}/publish")]
    [HasPermission(Permissions.CampaignsPublish)]
    public Task<AdminCampaignDto> Publish(Guid id, CancellationToken ct) => campaigns.PublishAsync(id, ct);

    [HttpPost("{id:guid}/pause")]
    public Task<AdminCampaignDto> Pause(Guid id, StatusChangeRequest request, CancellationToken ct) =>
        campaigns.PauseAsync(id, request.Reason, ct);

    [HttpPost("{id:guid}/resume")]
    public Task<AdminCampaignDto> Resume(Guid id, CancellationToken ct) => campaigns.ResumeAsync(id, ct);

    [HttpPost("{id:guid}/end")]
    public Task<AdminCampaignDto> End(Guid id, StatusChangeRequest request, CancellationToken ct) =>
        campaigns.EndAsync(id, request.Reason, ct);

    [HttpPost("{id:guid}/archive")]
    public Task<AdminCampaignDto> Archive(Guid id, CancellationToken ct) => campaigns.ArchiveAsync(id, ct);

    [HttpPost("{id:guid}/duplicate")]
    public async Task<ActionResult<AdminCampaignDto>> Duplicate(Guid id, CancellationToken ct)
    {
        var copy = await campaigns.DuplicateAsync(id, ct);
        return CreatedAtAction(nameof(Get), new { id = copy.Id }, copy);
    }

    [HttpPost("{id:guid}/assets")]
    public async Task<ActionResult<CampaignAssetDto>> AddAsset(Guid id, AssetInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await campaigns.AddAssetAsync(id, input, ct));

    [HttpPut("{id:guid}/assets/{assetId:guid}")]
    public Task<CampaignAssetDto> UpdateAsset(Guid id, Guid assetId, AssetInput input, CancellationToken ct) =>
        campaigns.UpdateAssetAsync(id, assetId, input, ct);

    [HttpDelete("{id:guid}/assets/{assetId:guid}")]
    public async Task<IActionResult> DeleteAsset(Guid id, Guid assetId, CancellationToken ct)
    {
        await campaigns.DeleteAssetAsync(id, assetId, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/assets/reorder")]
    public Task<IReadOnlyList<CampaignAssetDto>> ReorderAssets(Guid id, ReorderAssetsRequest request, CancellationToken ct) =>
        campaigns.ReorderAssetsAsync(id, request, ct);

    [HttpGet("{id:guid}/disclosures")]
    public Task<IReadOnlyList<DisclosureDto>> Disclosures(Guid id, CancellationToken ct) => campaigns.GetDisclosuresAsync(id, ct);

    [HttpPut("{id:guid}/disclosures")]
    public Task<IReadOnlyList<DisclosureDto>> ReplaceDisclosures(Guid id, ReplaceDisclosuresRequest request, CancellationToken ct) =>
        campaigns.ReplaceDisclosuresAsync(id, request, ct);
}

[ApiController]
[Route("api/v1/admin/campaign-categories")]
[HasPermission(Permissions.CampaignsManage)]
public sealed class AdminCampaignCategoriesController(ICampaignAdminService campaigns) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AdminCategoryDto>> List(CancellationToken ct) => campaigns.ListCategoriesAsync(ct);

    [HttpPost]
    public async Task<ActionResult<AdminCategoryDto>> Create(CategoryInput input, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await campaigns.CreateCategoryAsync(input, ct));

    [HttpPut("{id:guid}")]
    public Task<AdminCategoryDto> Update(Guid id, CategoryInput input, CancellationToken ct) => campaigns.UpdateCategoryAsync(id, input, ct);

    /// <summary>Deletes an unused category; a category referenced by campaigns is deactivated instead.</summary>
    [HttpDelete("{id:guid}")]
    public Task<CategoryDeleteResultDto> Delete(Guid id, CancellationToken ct) => campaigns.DeleteCategoryAsync(id, ct);
}

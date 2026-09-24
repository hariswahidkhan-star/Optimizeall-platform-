using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Modules.Rewards;

[DeniedWhileImpersonating(WritesOnly = true)] // reward rates are money
[ApiController]
[Route("api/v1/admin/campaigns/{campaignId:guid}/reward-rules")]
public sealed class RewardRulesController(IRewardRulesService rules) : ControllerBase
{
    /// <summary>All rule set versions (newest first) with their rules and how many submissions reference each.</summary>
    [HttpGet]
    [HasPermission(Permissions.CampaignsView)]
    public Task<IReadOnlyList<RewardRuleSetDto>> List(Guid campaignId, CancellationToken ct) => rules.ListAsync(campaignId, ct);

    /// <summary>Saves a new version (sensitive: rewards.edit + confirm + reason). Existing submissions keep their version.</summary>
    [HttpPost]
    [HasPermission(Permissions.RewardsEdit)]
    public async Task<ActionResult<RewardRuleSetDto>> Create(Guid campaignId, CreateRewardRuleSetRequest request, CancellationToken ct)
    {
        var created = await rules.CreateVersionAsync(campaignId, request, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    /// <summary>Prices a hypothetical post with a saved version or unsaved draft rules.</summary>
    [HttpPost("preview")]
    [HasPermission(Permissions.CampaignsManage)]
    public Task<RewardQuoteDto> Preview(Guid campaignId, RewardPreviewRequest request, CancellationToken ct) =>
        rules.PreviewAsync(campaignId, request, ct);
}

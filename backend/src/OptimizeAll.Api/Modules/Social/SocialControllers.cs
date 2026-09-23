using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Modules.Social;

/// <summary>The participant's own social media profiles with their current global qualification.</summary>
[ApiController]
[HasPermission(Permissions.ParticipantPortal)]
[Route("api/v1/me/social-accounts")]
public sealed class MySocialAccountsController(ISocialAccountService social, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public Task<SocialAccountListDto> List(CancellationToken ct) => social.ListMineAsync(currentUser.Id, ct);

    [HttpPost]
    [ProducesResponseType(typeof(SocialAccountDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateSocialAccountRequest request, CancellationToken ct)
    {
        var dto = await social.CreateAsync(currentUser.Id, request, ct);
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    /// <summary>
    /// Edits a profile. Changing the handle, creation date or follower count of a Verified or PendingReview
    /// profile resets it to Unverified (the response says so in <c>verificationReset</c> and <c>message</c>).
    /// </summary>
    [HttpPut("{id:guid}")]
    public Task<SocialAccountChangeResponse> Update(Guid id, UpdateSocialAccountRequest request, CancellationToken ct) =>
        social.UpdateAsync(currentUser.Id, id, request, ct);

    /// <summary>Deactivates (never deletes: past submissions reference the profile).</summary>
    [HttpDelete("{id:guid}")]
    public Task<SocialAccountDto> Deactivate(Guid id, CancellationToken ct) => social.DeactivateAsync(currentUser.Id, id, ct);

    [HttpPost("{id:guid}/reactivate")]
    public Task<SocialAccountDto> Reactivate(Guid id, CancellationToken ct) => social.ReactivateAsync(currentUser.Id, id, ct);

    [HttpPost("{id:guid}/request-verification")]
    public Task<SocialAccountDto> RequestVerification(Guid id, CancellationToken ct) =>
        social.RequestVerificationAsync(currentUser.Id, id, ct);
}

/// <summary>Staff verification of participant social profiles.</summary>
[ApiController]
[HasPermission(Permissions.SocialAccountsVerify)]
[Route("api/v1/review/social-accounts")]
public sealed class SocialAccountReviewController(ISocialAccountService social, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ReviewSocialAccountDto>> List([FromQuery] ReviewSocialAccountQuery query, CancellationToken ct) =>
        social.ReviewListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<ReviewSocialAccountDetailDto> Get(Guid id, CancellationToken ct) => social.ReviewGetAsync(id, ct);

    [HttpPost("{id:guid}/decision")]
    public Task<ReviewSocialAccountDetailDto> Decide(Guid id, SocialAccountDecisionRequest request, CancellationToken ct) =>
        social.DecideAsync(currentUser.Id, id, request, ct);
}

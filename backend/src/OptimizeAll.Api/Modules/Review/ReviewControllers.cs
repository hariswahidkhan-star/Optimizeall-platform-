using Microsoft.AspNetCore.Mvc;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Modules.Review;

/// <summary>Reviewer workspace.</summary>
[ApiController]
[Route("api/v1/review")]
public sealed class ReviewController(IReviewQueryService queries, IReviewService review) : ControllerBase
{
    [HttpGet("queue")]
    [HasPermission(Permissions.SubmissionsReview)]
    public Task<PagedResult<ReviewQueueItemDto>> Queue([FromQuery] ReviewQueueQuery query, CancellationToken ct) => queries.QueueAsync(query, ct);

    [HttpPost("submissions/{id:guid}/claim")]
    [HasPermission(Permissions.SubmissionsReview)]
    public Task<ClaimDto> Claim(Guid id, CancellationToken ct) => review.ClaimAsync(id, ct);

    [HttpPost("submissions/{id:guid}/release")]
    [HasPermission(Permissions.SubmissionsReview)]
    public async Task<IActionResult> Release(Guid id, CancellationToken ct)
    {
        await review.ReleaseAsync(id, ct);
        return NoContent();
    }

    /// <summary>Everything needed to review one submission side by side with the campaign requirements.</summary>
    [HttpGet("submissions/{id:guid}")]
    [HasPermission(Permissions.SubmissionsReview)]
    public Task<ReviewDetailDto> Detail(Guid id, CancellationToken ct) => queries.DetailAsync(id, ct);

    [HttpPost("submissions/{id:guid}/decision")]
    [HasPermission(Permissions.SubmissionsReview)]
    public Task<DecisionResultDto> Decide(Guid id, DecisionRequest request, CancellationToken ct) => review.DecideAsync(id, request, ct);

    [HttpGet("live-checks")]
    [HasPermission(Permissions.SubmissionsReview)]
    public Task<PagedResult<LiveCheckItemDto>> LiveChecks([FromQuery] LiveCheckQuery query, CancellationToken ct) => queries.LiveChecksAsync(query, ct);

    [HttpPost("submissions/{id:guid}/live-check")]
    [HasPermission(Permissions.SubmissionsReview)]
    public Task<LiveCheckResultDto> LiveCheck(Guid id, LiveCheckRequest request, CancellationToken ct) => review.LiveCheckAsync(id, request, ct);

    [HttpPost("submissions/{id:guid}/reverse")]
    [HasPermission(Permissions.SubmissionsReverse)]
    public Task<ReverseResultDto> Reverse(Guid id, ReverseRequest request, CancellationToken ct) => review.ReverseAsync(id, request, ct);

    [HttpGet("appeals")]
    [HasPermission(Permissions.AppealsResolve)]
    public Task<PagedResult<AppealListItemDto>> Appeals([FromQuery] AppealQuery query, CancellationToken ct) => queries.AppealsAsync(query, ct);

    [HttpGet("appeals/{id:guid}")]
    [HasPermission(Permissions.AppealsResolve)]
    public Task<AppealDetailDto> Appeal(Guid id, CancellationToken ct) => queries.AppealAsync(id, ct);

    [HttpPost("appeals/{id:guid}/resolve")]
    [HasPermission(Permissions.AppealsResolve)]
    public Task<AppealResolutionDto> ResolveAppeal(Guid id, ResolveAppealRequest request, CancellationToken ct) =>
        review.ResolveAppealAsync(id, request, ct);

    [HttpGet("reviewers")]
    [HasPermission(Permissions.ReviewAssign)]
    public Task<IReadOnlyList<ReviewerDto>> Reviewers(CancellationToken ct) => queries.ReviewersAsync(ct);

    [HttpPost("assign")]
    [HasPermission(Permissions.ReviewAssign)]
    public Task<AssignResultDto> Assign(AssignRequest request, CancellationToken ct) => review.AssignAsync(request, ct);

    [HttpGet("stats")]
    [HasPermission(Permissions.SubmissionsReview)]
    public Task<ReviewStatsDto> Stats(CancellationToken ct) => queries.StatsAsync(ct);
}

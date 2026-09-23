using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;

namespace OptimizeAll.Api.Modules.Submissions;

[ApiController]
[Route("api/v1/me/submissions")]
[HasPermission(Permissions.ParticipantPortal)]
public sealed class MeSubmissionsController(ISubmissionService submissions) : ControllerBase
{
    /// <summary>Submits proof of a post (multipart/form-data with an optional screenshot image).</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Submissions)]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<MySubmissionDetailDto>> Create([FromForm] CreateSubmissionForm form, CancellationToken ct)
    {
        var created = await submissions.CreateAsync(form, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet]
    public Task<PagedResult<MySubmissionListItemDto>> List([FromQuery] MySubmissionsQuery query, CancellationToken ct) =>
        submissions.ListMineAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<MySubmissionDetailDto> Get(Guid id, CancellationToken ct) => submissions.GetMineAsync(id, ct);

    /// <summary>Corrects a submission in NeedsCorrection and sends it back to review (multipart/form-data).</summary>
    [HttpPut("{id:guid}")]
    [EnableRateLimiting(RateLimitPolicies.Submissions)]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public Task<MySubmissionDetailDto> Resubmit(Guid id, [FromForm] UpdateSubmissionForm form, CancellationToken ct) =>
        submissions.ResubmitAsync(id, form, ct);

    /// <summary>Appeals a rejection or reversal (once per decision, within the appeal window).</summary>
    [HttpPost("{id:guid}/appeal")]
    [EnableRateLimiting(RateLimitPolicies.Submissions)]
    public Task<MySubmissionDetailDto> Appeal(Guid id, AppealRequest request, CancellationToken ct) =>
        submissions.AppealAsync(id, request, ct);
}

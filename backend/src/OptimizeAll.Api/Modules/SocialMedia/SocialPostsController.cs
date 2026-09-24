using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.SocialMedia;

public sealed class PostListQuery : PageQuery
{
    public Guid? ClientId { get; set; }
    public SocialPostStatus? Status { get; set; }
    public SocialNetwork? Network { get; set; }
}

public sealed class CalendarQuery
{
    public Guid? ClientId { get; set; }
    [Required] public DateTime? From { get; set; }
    [Required] public DateTime? To { get; set; }
}

public sealed record AwarenessDayDto(DateOnly Date, string Name, IReadOnlyList<string> Countries, string SourceUrl);

public sealed record BestTimeDto(SocialNetwork Network, IReadOnlyList<string> Times, string Source);

public sealed record CalendarDto(IReadOnlyList<PostSummaryDto> Posts, IReadOnlyList<AwarenessDayDto> AwarenessDays, IReadOnlyList<BestTimeDto> BestTimes);

public sealed record PresetDto(
    SocialNetwork Network, string Label, int MaxTextLength, int? MaxTitleLength, bool RequiresTitle, int MaxHashtags,
    int? RecommendedHashtags, int MaxMentions, int MaxMedia, int MaxVideos, bool RequiresMedia, bool RequiresVideo, bool AllowsMixedMedia,
    decimal? MinAspectRatio, decimal? MaxAspectRatio, int? MinVideoSeconds, int? MaxVideoSeconds, long? MaxImageBytes, int MaxAltTextLength,
    bool SupportsFirstComment, LinkHandling LinkHandling, int? UrlWeight, IReadOnlyList<string> RecommendedTimes, string Source)
{
    public static PresetDto From(NetworkRules r) => new(r.Network, PostValidator.Label(r.Network), r.MaxTextLength, r.MaxTitleLength, r.RequiresTitle,
        r.MaxHashtags, r.RecommendedHashtags, r.MaxMentions, r.MaxMedia, r.MaxVideos, r.RequiresMedia, r.RequiresVideo, r.AllowsMixedMedia,
        r.MinAspectRatio, r.MaxAspectRatio, r.MinVideoSeconds, r.MaxVideoSeconds, r.MaxImageBytes, r.MaxAltTextLength, r.SupportsFirstComment,
        r.LinkHandling, r.UrlWeight, r.RecommendedTimes, r.Source);
}

public sealed record PublishAttemptDto(
    Guid Id, Guid PostId, Guid VariantId, SocialNetwork Network, int AttemptNumber, string Outcome, PublishFailureKind FailureKind,
    string? Message, string? ExternalPostId, DateTime StartedAt, DateTime FinishedAt, string? ActorName);

public sealed record PublishingRowDto(PostSummaryDto Post, IReadOnlyList<VariantStateDto> Variants);

public sealed record VariantStateDto(
    Guid Id, SocialNetwork Network, string ProfileHandle, VariantPublishStatus PublishStatus, int Attempts, DateTime? NextAttemptAt,
    PublishFailureKind FailureKind, string? FailureReason, string? PublishedUrl, DateTime? PublishedAt, bool PublishedManually);

[ApiController]
[Route("api/v1/agency/social")]
public sealed class SocialPostsController(
    AppDbContext db, SocialAccess access, SocialPostService posts, NetworkPresetProvider presets) : ControllerBase
{
    [HttpGet("presets")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IReadOnlyList<PresetDto>> Presets(CancellationToken ct) =>
        (await presets.AllAsync(ct)).Values.OrderBy(r => r.Network).Select(PresetDto.From).ToList();

    [HttpPost("validate")]
    [HasPermission(Permissions.SocialManage)]
    public Task<ValidationResultDto> Validate(ValidateInput input, CancellationToken ct) => posts.ValidateAsync(input, ct);

    [HttpGet("posts")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<PagedResult<PostSummaryDto>> List([FromQuery] PostListQuery query, CancellationToken ct)
    {
        var q = (await access.ScopedAsync<SocialPost>(p => p.ClientAccountId, ct)).AsNoTracking().Include(p => p.Variants).AsQueryable();
        if (query.ClientId is { } c) q = q.Where(p => p.ClientAccountId == c);
        if (query.Status is { } s) q = q.Where(p => p.Status == s);
        if (query.Network is { } n) q = q.Where(p => p.Variants.Any(v => v.Network == n));
        if (!string.IsNullOrWhiteSpace(query.Search)) q = q.Where(p => EF.Functions.Like(p.Title, PagingExtensions.LikePattern(query.Search)));
        var page = await q.OrderByDescending(p => p.ScheduledAt ?? p.CreatedAt).ToPagedAsync(query, ct);
        var names = await ClientNamesAsync(page.Items.Select(p => p.ClientAccountId), ct);
        return new PagedResult<PostSummaryDto>(page.Items.Select(p => SocialPostService.Summary(p, names.GetValueOrDefault(p.ClientAccountId, ""))).ToList(),
            page.Total, page.Page, page.PageSize);
    }

    [HttpGet("posts/{id:guid}")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<PostDto> Get(Guid id, CancellationToken ct) => await posts.ToDtoAsync(await access.PostAsync(id, ct, track: false), false, ct);

    [HttpPost("posts")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<ActionResult<PostDto>> Create(PostInput input, CancellationToken ct)
    {
        var post = await posts.CreateAsync(input, ct);
        return CreatedAtAction(nameof(Get), new { id = post.Id }, await posts.ToDtoAsync(post, false, ct));
    }

    [HttpPut("posts/{id:guid}")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<PostDto> Update(Guid id, PostInput input, CancellationToken ct) =>
        await posts.ToDtoAsync(await posts.UpdateAsync(id, input, ct), false, ct);

    [HttpDelete("posts/{id:guid}")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await posts.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("posts/{id:guid}/submit")]
    [HasPermission(Permissions.SocialManage)]
    public Task<PostDto> Submit(Guid id, TransitionInput input, CancellationToken ct) => Transition(id, WorkflowAction.Submit, input, ct);

    [HttpPost("posts/{id:guid}/approve")]
    [HasPermission(Permissions.SocialPublish)]
    public Task<PostDto> Approve(Guid id, TransitionInput input, CancellationToken ct) => Transition(id, WorkflowAction.ApproveInternal, input, ct);

    [HttpPost("posts/{id:guid}/request-changes")]
    [HasPermission(Permissions.SocialManage)]
    public Task<PostDto> RequestChanges(Guid id, TransitionInput input, CancellationToken ct) => Transition(id, WorkflowAction.RequestChanges, input, ct);

    [HttpPost("posts/{id:guid}/unschedule")]
    [HasPermission(Permissions.SocialPublish)]
    public Task<PostDto> Unschedule(Guid id, TransitionInput input, CancellationToken ct) => Transition(id, WorkflowAction.Unschedule, input, ct);

    [HttpPost("posts/{id:guid}/schedule")]
    [HasPermission(Permissions.SocialPublish)]
    public async Task<PostDto> Schedule(Guid id, ScheduleInput input, CancellationToken ct) =>
        await posts.ToDtoAsync(await posts.ScheduleAsync(id, input, ct), false, ct);

    [HttpPost("posts/{id:guid}/queue")]
    [HasPermission(Permissions.SocialPublish)]
    public async Task<PostDto> Queue(Guid id, CancellationToken ct) => await posts.ToDtoAsync(await posts.QueueAsync(id, ct), false, ct);

    /// <summary>Moves the planned/scheduled time (calendar drag or its keyboard alternative).</summary>
    [HttpPost("posts/{id:guid}/reschedule")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<PostDto> Reschedule(Guid id, RescheduleInput input, CancellationToken ct) =>
        await posts.ToDtoAsync(await posts.RescheduleAsync(id, input, ct), false, ct);

    [HttpPost("posts/{id:guid}/mark-published")]
    [HasPermission(Permissions.SocialPublish)]
    public async Task<PostDto> MarkPublished(Guid id, MarkPublishedInput input, CancellationToken ct) =>
        await posts.ToDtoAsync(await posts.MarkPublishedAsync(id, input, ct), false, ct);

    [HttpPost("posts/{id:guid}/retry")]
    [HasPermission(Permissions.SocialPublish)]
    public async Task<PostDto> Retry(Guid id, RetryInput input, CancellationToken ct) =>
        await posts.ToDtoAsync(await posts.RetryAsync(id, input, ct), false, ct);

    [HttpPost("posts/{id:guid}/comments")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<PostDto> Comment(Guid id, CommentInput input, CancellationToken ct) =>
        await posts.ToDtoAsync(await posts.CommentAsync(id, input, fromClient: false, ct), false, ct);

    [HttpGet("posts/{id:guid}/attempts")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IReadOnlyList<PublishAttemptDto>> Attempts(Guid id, CancellationToken ct)
    {
        await access.PostAsync(id, ct, track: false);
        var rows = await db.Set<SocialPublishAttempt>().AsNoTracking().Where(a => a.PostId == id).OrderByDescending(a => a.StartedAt).ToListAsync(ct);
        var names = await UserNames.LookupAsync(db, rows.Select(r => r.ActorUserId), ct);
        return rows.Select(a => new PublishAttemptDto(a.Id, a.PostId, a.VariantId, a.Network, a.AttemptNumber, a.Outcome, a.FailureKind,
            a.Message, a.ExternalPostId, a.StartedAt, a.FinishedAt, a.ActorUserId is { } u ? names.GetValueOrDefault(u) : null)).ToList();
    }

    [HttpGet("calendar")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<CalendarDto> Calendar([FromQuery] CalendarQuery query, CancellationToken ct)
    {
        var (from, to) = Range(query.From!.Value, query.To!.Value, 100);
        var q = (await access.ScopedAsync<SocialPost>(p => p.ClientAccountId, ct)).AsNoTracking().Include(p => p.Variants).AsQueryable();
        string? country = null;
        if (query.ClientId is { } clientId)
        {
            var client = await access.ClientAsync(clientId, ct);
            country = client.CountryCode;
            q = q.Where(p => p.ClientAccountId == clientId);
        }
        var rows = await q.Where(p => p.ScheduledAt >= from && p.ScheduledAt < to).OrderBy(p => p.ScheduledAt).Take(1000).ToListAsync(ct);
        var names = await ClientNamesAsync(rows.Select(p => p.ClientAccountId), ct);
        return new CalendarDto(
            rows.Select(p => SocialPostService.Summary(p, names.GetValueOrDefault(p.ClientAccountId, ""))).ToList(),
            await AwarenessDaysAsync(db, from, to, country, ct),
            (await presets.AllAsync(ct)).Values.OrderBy(r => r.Network)
                .Select(r => new BestTimeDto(r.Network, r.RecommendedTimes, NetworkPresets.TimesSource)).ToList());
    }

    [HttpGet("approvals")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IReadOnlyList<PostSummaryDto>> Approvals([FromQuery] Guid? clientId, CancellationToken ct)
    {
        var q = (await access.ScopedAsync<SocialPost>(p => p.ClientAccountId, ct)).AsNoTracking().Include(p => p.Variants)
            .Where(p => p.Status == SocialPostStatus.InternalReview || p.Status == SocialPostStatus.ClientApproval);
        if (clientId is { } c) q = q.Where(p => p.ClientAccountId == c);
        var rows = await q.OrderBy(p => p.ScheduledAt ?? p.UpdatedAt).Take(500).ToListAsync(ct);
        var names = await ClientNamesAsync(rows.Select(p => p.ClientAccountId), ct);
        return rows.Select(p => SocialPostService.Summary(p, names.GetValueOrDefault(p.ClientAccountId, ""))).ToList();
    }

    /// <summary>Publishing log: scheduled, publishing, published and failed posts with each variant's state.</summary>
    [HttpGet("publishing")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<PagedResult<PublishingRowDto>> Publishing([FromQuery] PostListQuery query, CancellationToken ct)
    {
        var q = (await access.ScopedAsync<SocialPost>(p => p.ClientAccountId, ct)).AsNoTracking().Include(p => p.Variants)
            .Where(p => p.Status == SocialPostStatus.Scheduled || p.Status == SocialPostStatus.Publishing
                        || p.Status == SocialPostStatus.Published || p.Status == SocialPostStatus.Failed);
        if (query.ClientId is { } c) q = q.Where(p => p.ClientAccountId == c);
        if (query.Status is { } s) q = q.Where(p => p.Status == s);
        var page = await q.OrderByDescending(p => p.ScheduledAt).ToPagedAsync(query, ct);
        var names = await ClientNamesAsync(page.Items.Select(p => p.ClientAccountId), ct);
        var profileIds = page.Items.SelectMany(p => p.Variants).Select(v => v.ProfileId).Distinct().ToList();
        var handles = await db.Set<BrandProfile>().AsNoTracking().Where(p => profileIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Handle, ct);
        return new PagedResult<PublishingRowDto>(page.Items.Select(p => new PublishingRowDto(
            SocialPostService.Summary(p, names.GetValueOrDefault(p.ClientAccountId, "")),
            p.Variants.OrderBy(v => v.Network).Select(v => new VariantStateDto(v.Id, v.Network, handles.GetValueOrDefault(v.ProfileId, ""),
                v.PublishStatus, v.Attempts, v.NextAttemptAt, v.FailureKind, v.FailureReason, v.PublishedUrl, v.PublishedAt, v.PublishedManually)).ToList()))
            .ToList(), page.Total, page.Page, page.PageSize);
    }

    private Task<PostDto> Transition(Guid id, WorkflowAction action, TransitionInput input, CancellationToken ct) =>
        TransitionCore(id, action, input, ct);

    private async Task<PostDto> TransitionCore(Guid id, WorkflowAction action, TransitionInput input, CancellationToken ct) =>
        await posts.ToDtoAsync(await posts.TransitionAsync(id, action, input, ct), false, ct);

    private async Task<Dictionary<Guid, string>> ClientNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Set<ClientAccount>().AsNoTracking().Where(c => list.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
    }

    public static (DateTime From, DateTime To) Range(DateTime from, DateTime to, int maxDays)
    {
        var f = SocialPostService.Utc(from);
        var t = SocialPostService.Utc(to);
        if (t <= f) throw new DomainException("social.invalid_range", "'to' must be after 'from'.");
        if ((t - f).TotalDays > maxDays) throw new DomainException("social.range_too_long", $"Choose a range of at most {maxDays} days.");
        return (f, t);
    }

    public static async Task<IReadOnlyList<AwarenessDayDto>> AwarenessDaysAsync(AppDbContext db, DateTime from, DateTime to, string? country, CancellationToken ct)
    {
        var all = await db.Set<SocialAwarenessDay>().AsNoTracking().Where(d => d.IsActive).ToListAsync(ct);
        var result = new List<AwarenessDayDto>();
        for (var year = from.Year; year <= to.Year; year++)
        {
            foreach (var d in all.Where(d => d.Year is null || d.Year == year))
            {
                if (d.Day > DateTime.DaysInMonth(year, d.Month)) continue;
                var date = new DateOnly(year, d.Month, d.Day);
                var start = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                if (start.AddDays(1) <= from || start >= to) continue;
                if (country is not null && d.Countries.Count > 0 && !d.Countries.Contains(country, StringComparer.OrdinalIgnoreCase)) continue;
                result.Add(new AwarenessDayDto(date, d.Name, d.Countries, d.SourceUrl));
            }
        }
        return result.OrderBy(r => r.Date).ToList();
    }
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Ads;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.SocialMedia;

public sealed record ClientOrganizationDto(Guid Id, string Name, string Currency, string TimeZone, ClientMemberRole Role, bool CanApprove);

public sealed record ClientPerformanceDto(SocialKpisDto Social, IReadOnlyList<SeriesPointDto> SocialSeries, IReadOnlyList<TopPostDto> TopPosts,
    ClientAdsKpisDto Ads);

/// <summary>
/// Client portal: a preview of the social calendar, approving or requesting changes on posts awaiting client approval
/// (Approver or Owner duty), and a social + ads performance summary. Drafts, internal review and internal notes are never shown.
/// </summary>
[ApiController]
[Route("api/v1/client/social")]
[HasPermission(Permissions.ClientPortal)]
public sealed class ClientSocialController(
    AppDbContext db, SocialAccess access, SocialPostService posts, SocialAnalyticsService analytics, AdsKpiService adsKpis,
    IFileStorage storage, TimeProvider clock) : ControllerBase
{
    private static readonly SocialPostStatus[] Visible =
    {
        SocialPostStatus.ClientApproval, SocialPostStatus.Approved, SocialPostStatus.Scheduled, SocialPostStatus.Publishing,
        SocialPostStatus.Published, SocialPostStatus.Failed,
    };

    [HttpGet("organizations")]
    public async Task<IReadOnlyList<ClientOrganizationDto>> Organizations(CancellationToken ct)
    {
        var ids = await access.Scope.MemberClientIdsAsync(ct);
        var clients = await db.Set<ClientAccount>().AsNoTracking().Where(c => ids.Contains(c.Id)).OrderBy(c => c.Name).ToListAsync(ct);
        var result = new List<ClientOrganizationDto>();
        foreach (var c in clients)
        {
            var role = await access.Scope.MemberRoleAsync(c.Id, ct) ?? ClientMemberRole.Viewer;
            result.Add(new ClientOrganizationDto(c.Id, c.Name, c.Currency, c.TimeZone, role, role is ClientMemberRole.Approver or ClientMemberRole.Owner));
        }
        return result;
    }

    [HttpGet("calendar")]
    public async Task<CalendarDto> Calendar([FromQuery] Guid clientId, [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
    {
        var client = await access.ClientAsync(clientId, ct);
        var (f, t) = SocialPostsController.Range(from, to, 100);
        var rows = await db.Set<SocialPost>().AsNoTracking().Include(p => p.Variants)
            .Where(p => p.ClientAccountId == clientId && Visible.Contains(p.Status) && p.ScheduledAt >= f && p.ScheduledAt < t)
            .OrderBy(p => p.ScheduledAt).Take(500).ToListAsync(ct);
        return new CalendarDto(rows.Select(p => ClientSummary(p, client.Name)).ToList(),
            await SocialPostsController.AwarenessDaysAsync(db, f, t, client.CountryCode, ct), Array.Empty<BestTimeDto>());
    }

    [HttpGet("approvals")]
    public async Task<IReadOnlyList<PostSummaryDto>> Approvals([FromQuery] Guid clientId, CancellationToken ct)
    {
        var client = await access.ClientAsync(clientId, ct);
        var rows = await db.Set<SocialPost>().AsNoTracking().Include(p => p.Variants)
            .Where(p => p.ClientAccountId == clientId && p.Status == SocialPostStatus.ClientApproval)
            .OrderBy(p => p.ScheduledAt).Take(200).ToListAsync(ct);
        return rows.Select(p => ClientSummary(p, client.Name)).ToList();
    }

    [HttpGet("posts/{id:guid}")]
    public async Task<PostDto> Get(Guid id, CancellationToken ct) => await posts.ToDtoAsync(await VisiblePostAsync(id, ct), forClient: true, ct);

    [HttpPost("posts/{id:guid}/approve")]
    public async Task<PostDto> Approve(Guid id, TransitionInput input, CancellationToken ct)
    {
        var post = await VisiblePostAsync(id, ct);
        await access.ClientAsync(post.ClientAccountId, ct, ClientMemberRole.Approver);
        return await posts.ToDtoAsync(await posts.TransitionAsync(id, WorkflowAction.ClientApprove, input, ct), forClient: true, ct);
    }

    [HttpPost("posts/{id:guid}/request-changes")]
    public async Task<PostDto> RequestChanges(Guid id, TransitionInput input, CancellationToken ct)
    {
        var post = await VisiblePostAsync(id, ct);
        await access.ClientAsync(post.ClientAccountId, ct, ClientMemberRole.Approver);
        return await posts.ToDtoAsync(await posts.TransitionAsync(id, WorkflowAction.ClientRequestChanges, input, ct), forClient: true, ct);
    }

    [HttpPost("posts/{id:guid}/comments")]
    public async Task<PostDto> Comment(Guid id, CommentInput input, CancellationToken ct)
    {
        await VisiblePostAsync(id, ct);
        return await posts.ToDtoAsync(await posts.CommentAsync(id, input, fromClient: true, ct), forClient: true, ct);
    }

    [HttpGet("media/{id:guid}/content")]
    public async Task<IActionResult> Media(Guid id, CancellationToken ct)
    {
        var asset = await access.OwnedAsync<SocialMediaAsset>(id, m => m.ClientAccountId, "Media", ct);
        // Only media of posts the client may see: not the agency's library, drafts or posts in internal review.
        var visibleMedia = await db.Set<SocialPostVariant>().AsNoTracking()
            .Where(v => v.ClientAccountId == asset.ClientAccountId)
            .Join(db.Set<SocialPost>().Where(p => Visible.Contains(p.Status)), v => v.PostId, p => p.Id, (v, _) => v.MediaIds)
            .ToListAsync(ct);
        if (!visibleMedia.Any(ids => ids.Contains(asset.Id))) throw DomainException.NotFound("Media");
        return await SocialLibraryController.StreamAsync(this, db, storage, asset, ct);
    }

    [HttpGet("media")]
    public async Task<IReadOnlyList<MediaDto>> MediaForPost([FromQuery] Guid postId, CancellationToken ct)
    {
        var post = await VisiblePostAsync(postId, ct);
        var ids = post.Variants.SelectMany(v => v.MediaIds).Distinct().ToList();
        var assets = await db.Set<SocialMediaAsset>().AsNoTracking().Where(m => m.ClientAccountId == post.ClientAccountId && ids.Contains(m.Id)).ToListAsync(ct);
        return assets.Select(m => SocialLibraryController.Map(m, null, staff: false)).ToList();
    }

    [HttpGet("performance")]
    public async Task<ClientPerformanceDto> Performance([FromQuery] Guid clientId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var t = to ?? today;
        var f = from ?? t.AddDays(-29);
        if (f > t || t.DayNumber - f.DayNumber > 400) throw new DomainException("social.invalid_range", "Choose a valid range of at most 400 days.");
        return new ClientPerformanceDto(
            await analytics.KpisAsync(clientId, f, t, ct),
            await analytics.SeriesAsync(clientId, f, t, null, ct),
            (await analytics.TopPostsAsync(clientId, f, t, 5, ct)).Select(p => p with { PostKey = string.Empty }).ToList(),
            await adsKpis.ClientSummaryAsync(clientId, f, t, ct));
    }

    private async Task<SocialPost> VisiblePostAsync(Guid id, CancellationToken ct)
    {
        var post = await access.PostAsync(id, ct, track: false);
        if (!Visible.Contains(post.Status)) throw DomainException.NotFound("Post");
        return post;
    }

    /// <summary>Clients see "Scheduled" for a failed post (the agency is handling it) and no failure details.</summary>
    private static PostSummaryDto ClientSummary(SocialPost p, string clientName) =>
        SocialPostService.Summary(p, clientName) with
        {
            Status = p.Status == SocialPostStatus.Failed ? SocialPostStatus.Scheduled : p.Status,
            FailureReason = null,
        };
}

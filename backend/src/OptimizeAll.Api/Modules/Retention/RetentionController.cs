using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Marketing.Shared;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Retention;

public sealed record RetentionKindCountDto(string Kind, int Sent);

public sealed record RetentionSummaryDto(DateTime From, DateTime To, int Total, IReadOnlyList<RetentionKindCountDto> Items);

public sealed record RetentionLogDto(Guid Id, Guid UserId, string DisplayName, string Email, string Kind, string DedupKey, DateTime SentAt);

public sealed class RetentionLogQuery : PageQuery
{
    public string? Kind { get; set; }
    public Guid? UserId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

[ApiController]
[Route("api/v1/marketing/retention")]
[HasPermission(Permissions.MarketingManage)]
public sealed class RetentionController(AppDbContext db, TimeProvider clock) : ControllerBase
{
    /// <summary>Messages sent per kind in [from, to] (default last 30 days).</summary>
    [HttpGet("summary")]
    public async Task<RetentionSummaryDto> Summary([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var range = DateRange.Resolve(from, to, clock.GetUtcNow().UtcDateTime);
        var counts = await db.Set<RetentionMessageLog>().AsNoTracking()
            .Where(l => l.SentAt >= range.From && l.SentAt <= range.To)
            .GroupBy(l => l.Kind).Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Kind, x => x.Count, ct);
        var items = RetentionKinds.All.Concat(counts.Keys.Except(RetentionKinds.All))
            .Select(k => new RetentionKindCountDto(k, counts.TryGetValue(k, out var c) ? c : 0)).ToList();
        return new RetentionSummaryDto(range.From, range.To, items.Sum(i => i.Sent), items);
    }

    /// <summary>Sent retention messages, newest first. Search matches the participant's name or email.</summary>
    [HttpGet("log")]
    public async Task<PagedResult<RetentionLogDto>> Log([FromQuery] RetentionLogQuery query, CancellationToken ct)
    {
        var q = from l in db.Set<RetentionMessageLog>().AsNoTracking()
                join u in db.Set<User>() on l.UserId equals u.Id
                select new { l, u };
        if (!string.IsNullOrWhiteSpace(query.Kind)) q = q.Where(x => x.l.Kind == query.Kind);
        if (query.UserId is { } userId) q = q.Where(x => x.l.UserId == userId);
        if (query.From is { } from) { var f = DateRange.AsUtc(from); q = q.Where(x => x.l.SentAt >= f); }
        if (query.To is { } to) { var t = DateRange.AsUtc(to); q = q.Where(x => x.l.SentAt <= t); }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search);
            q = q.Where(x => EF.Functions.Like(x.u.DisplayName, like, "\\") || EF.Functions.Like(x.u.Email, like, "\\"));
        }
        return await q.OrderByDescending(x => x.l.SentAt)
            .Select(x => new RetentionLogDto(x.l.Id, x.l.UserId, x.u.DisplayName, x.u.Email, x.l.Kind, x.l.DedupKey, x.l.SentAt))
            .ToPagedAsync(query, ct);
    }
}

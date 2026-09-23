using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Support;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Admin;

public sealed class AuditLogService(AppDbContext db)
{
    public const int MaxExportRows = 50_000;

    private sealed class Row
    {
        public required AuditLog Log { get; init; }
        public string? ActorEmail { get; init; }
        public string? ActorDisplayName { get; init; }
    }

    private IQueryable<Row> Query(AuditLogQuery q)
    {
        var logs = db.Set<AuditLog>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Action)) logs = logs.Where(l => l.Action.StartsWith(q.Action.Trim()));
        if (!string.IsNullOrWhiteSpace(q.EntityType)) logs = logs.Where(l => l.EntityType == q.EntityType.Trim());
        if (!string.IsNullOrWhiteSpace(q.EntityId)) logs = logs.Where(l => l.EntityId == q.EntityId.Trim());
        if (q.ActorUserId is { } actor) logs = logs.Where(l => l.ActorUserId == actor);
        if (q.From is { } from) { var f = ToUtc(from); logs = logs.Where(l => l.CreatedAt >= f); }
        if (q.To is { } to) { var t = ToUtc(to); logs = logs.Where(l => l.CreatedAt <= t); }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var p = PagingExtensions.LikePattern(q.Search);
            logs = logs.Where(l => EF.Functions.Like(l.Action, p, "\\") || EF.Functions.Like(l.EntityId, p, "\\") || (l.Reason != null && EF.Functions.Like(l.Reason, p, "\\")));
        }

        return from l in logs
               join u in db.Set<User>().AsNoTracking() on l.ActorUserId equals u.Id into actors
               from u in actors.DefaultIfEmpty()
               orderby l.Id descending
               select new Row { Log = l, ActorEmail = u == null ? null : u.Email, ActorDisplayName = u == null ? null : u.DisplayName };
    }

    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditLogQuery query, CancellationToken ct)
    {
        var page = await Query(query).ToPagedAsync(query, ct);
        return new PagedResult<AuditLogDto>(page.Items.Select(ToDto).ToList(), page.Total, page.Page, page.PageSize);
    }

    /// <summary>
    /// Most recent entries about a user: the user record itself and the user's own social accounts and support tickets.
    /// Entries the user performed on other entities (actor-side) are included only when <paramref name="includeActorEntries"/>
    /// is set, and IP address / correlation id only when <paramref name="includeNetworkDetails"/> is set; both are
    /// <c>audit.view</c> data and must not leak through <c>users.view</c>.
    /// </summary>
    public async Task<List<AuditLogDto>> RecentForUserAsync(Guid userId, int take, bool includeActorEntries, bool includeNetworkDetails,
        CancellationToken ct)
    {
        var id = userId.ToString();
        var accountIds = (await db.Set<SocialAccount>().AsNoTracking().Where(a => a.UserId == userId).Select(a => a.Id).ToListAsync(ct))
            .Select(g => g.ToString()).ToList();
        var ticketIds = (await db.Set<SupportTicket>().AsNoTracking().Where(t => t.UserId == userId).Select(t => t.Id).ToListAsync(ct))
            .Select(g => g.ToString()).ToList();

        var logs = db.Set<AuditLog>().AsNoTracking().Where(l =>
            (l.EntityType == nameof(User) && l.EntityId == id) ||
            (l.EntityType == nameof(SocialAccount) && accountIds.Contains(l.EntityId)) ||
            (l.EntityType == nameof(SupportTicket) && ticketIds.Contains(l.EntityId)) ||
            (includeActorEntries && l.ActorUserId == userId));

        var rows = await (from l in logs
                          join u in db.Set<User>().AsNoTracking() on l.ActorUserId equals u.Id into actors
                          from u in actors.DefaultIfEmpty()
                          orderby l.Id descending
                          select new Row { Log = l, ActorEmail = u == null ? null : u.Email, ActorDisplayName = u == null ? null : u.DisplayName })
            .Take(take).ToListAsync(ct);
        return rows.Select(r =>
        {
            var dto = ToDto(r);
            return includeNetworkDetails ? dto : dto with { IpAddress = null, CorrelationId = null };
        }).ToList();
    }

    public async Task<FileContentResult> ExportCsvAsync(AuditLogQuery query, CancellationToken ct)
    {
        var rows = await Query(query).Take(MaxExportRows).ToListAsync(ct);
        return Csv.File($"audit-log-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv",
            new[] { "id", "createdAt", "actorUserId", "actorEmail", "actorType", "action", "entityType", "entityId", "reason", "ipAddress", "correlationId", "before", "after" },
            rows.Select(r => new object?[]
            {
                r.Log.Id, r.Log.CreatedAt, r.Log.ActorUserId, r.ActorEmail, r.Log.ActorType, r.Log.Action, r.Log.EntityType,
                r.Log.EntityId, r.Log.Reason, r.Log.IpAddress, r.Log.CorrelationId, r.Log.BeforeJson, r.Log.AfterJson,
            }));
    }

    private static AuditLogDto ToDto(Row r) => new(r.Log.Id, r.Log.CreatedAt, r.Log.ActorUserId, r.ActorEmail, r.ActorDisplayName,
        r.Log.ActorType, r.Log.Action, r.Log.EntityType, r.Log.EntityId, Parse(r.Log.BeforeJson), Parse(r.Log.AfterJson), r.Log.Reason,
        r.Log.IpAddress, r.Log.CorrelationId);

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonSerializer.SerializeToElement(json);
        }
    }

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
}

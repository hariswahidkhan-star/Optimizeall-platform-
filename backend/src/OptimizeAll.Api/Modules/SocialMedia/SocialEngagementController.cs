using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.SocialMedia;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.SocialMedia;

// ---------------------------------------------------------------- adapters

public sealed record IngestedMention(SocialNetwork Network, string ExternalId, string AuthorHandle, string Text, string? Url, DateTime PostedAt);

public sealed record IngestedInboxItem(SocialNetwork Network, InboxItemKind Kind, string ExternalId, string AuthorHandle, string Text, string? Url, DateTime ReceivedAt);

public sealed record AdapterResult<T>(bool Configured, IReadOnlyList<T> Items, string Message);

/// <summary>Fetches mentions for listening queries. The default reports "not configured" (manual entry remains available).</summary>
public interface ISocialListeningProvider
{
    Task<AdapterResult<IngestedMention>> FetchAsync(Guid clientId, IReadOnlyList<SocialListeningQuery> queries, DateTime since, CancellationToken ct);
}

/// <summary>Fetches comments/DMs and sends replies. The default reports "not configured".</summary>
public interface ISocialInboxProvider
{
    Task<AdapterResult<IngestedInboxItem>> FetchAsync(BrandProfile profile, DateTime since, CancellationToken ct);
    Task<(bool Sent, string? ExternalId, string Message)> ReplyAsync(BrandProfile profile, SocialInboxItem item, string body, CancellationToken ct);
}

public sealed class NotConfiguredListeningProvider : ISocialListeningProvider
{
    public Task<AdapterResult<IngestedMention>> FetchAsync(Guid clientId, IReadOnlyList<SocialListeningQuery> queries, DateTime since, CancellationToken ct) =>
        Task.FromResult(new AdapterResult<IngestedMention>(false, Array.Empty<IngestedMention>(),
            "Social listening data provider is not configured. Log mentions manually or connect a provider."));
}

public sealed class NotConfiguredInboxProvider : ISocialInboxProvider
{
    public Task<AdapterResult<IngestedInboxItem>> FetchAsync(BrandProfile profile, DateTime since, CancellationToken ct) =>
        Task.FromResult(new AdapterResult<IngestedInboxItem>(false, Array.Empty<IngestedInboxItem>(),
            $"Inbox sync for {PostValidator.Label(profile.Network)} is not configured. Log comments and messages manually."));

    public Task<(bool Sent, string? ExternalId, string Message)> ReplyAsync(BrandProfile profile, SocialInboxItem item, string body, CancellationToken ct) =>
        Task.FromResult((false, (string?)null,
            $"Replying through the {PostValidator.Label(profile.Network)} API is not configured. Reply on the network, then log the reply here."));
}

// ---------------------------------------------------------------- DTOs

public sealed record ListeningQueryDto(Guid Id, Guid ClientAccountId, ListeningQueryKind Kind, string Term, IReadOnlyList<SocialNetwork> Networks, bool IsActive, Guid ConcurrencyStamp);

public sealed class ListeningQueryInput
{
    [Required] public ListeningQueryKind? Kind { get; set; }
    [Required, MinLength(2), MaxLength(150)] public string Term { get; set; } = string.Empty;
    [MaxLength(8)] public List<SocialNetwork> Networks { get; set; } = new();
    public bool IsActive { get; set; } = true;
}

public sealed record MentionDto(
    Guid Id, Guid ClientAccountId, Guid? QueryId, SocialNetwork Network, string AuthorHandle, string Text, string? Url, DateTime PostedAt,
    Sentiment? Sentiment, SentimentSource? SentimentSource, decimal? SentimentScore, string? SentimentLabel, IngestSource Source);

public sealed class MentionInput
{
    [Required] public SocialNetwork? Network { get; set; }
    public Guid? QueryId { get; set; }
    [Required, MaxLength(150)] public string AuthorHandle { get; set; } = string.Empty;
    [Required, MaxLength(4000)] public string Text { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Url { get; set; }
    [Required] public DateTime? PostedAt { get; set; }

    /// <summary>Manual sentiment; when omitted and <see cref="AutoSentiment"/> is true the lexicon estimate is stored.</summary>
    public Sentiment? Sentiment { get; set; }
    public bool AutoSentiment { get; set; } = true;
}

public sealed class SentimentInput
{
    [Required] public Sentiment? Sentiment { get; set; }
}

public sealed record MentionSummaryDto(int Total, int Positive, int Neutral, int Negative, int Untagged, int Automatic);

public sealed record InboxItemDto(
    Guid Id, Guid ClientAccountId, Guid? ProfileId, SocialNetwork Network, InboxItemKind Kind, string AuthorHandle, string Text, string? Url,
    DateTime ReceivedAt, InboxItemStatus Status, Guid? AssignedToUserId, string? AssignedToName, Sentiment? Sentiment, IngestSource Source,
    IReadOnlyList<InboxReplyDto> Replies, Guid ConcurrencyStamp);

public sealed record InboxReplyDto(Guid Id, string Body, bool SentViaApi, string ByName, DateTime CreatedAt);

public sealed class InboxItemInput
{
    public Guid? ProfileId { get; set; }
    [Required] public SocialNetwork? Network { get; set; }
    [Required] public InboxItemKind? Kind { get; set; }
    [Required, MaxLength(150)] public string AuthorHandle { get; set; } = string.Empty;
    [Required, MaxLength(4000)] public string Text { get; set; } = string.Empty;
    [MaxLength(1000)] public string? Url { get; set; }
    public DateTime? ReceivedAt { get; set; }
}

public sealed class InboxUpdateInput
{
    public InboxItemStatus? Status { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public bool Unassign { get; set; }
    public Sentiment? Sentiment { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class InboxReplyInput
{
    [Required, MinLength(1), MaxLength(4000)] public string Body { get; set; } = string.Empty;

    /// <summary>True: send through the network API (fails with 409 when not configured). False: log a reply sent by hand.</summary>
    public bool SendViaApi { get; set; }
}

public sealed record CompetitorDto(
    Guid Id, Guid ClientAccountId, string Name, SocialNetwork Network, string Handle, string? ProfileUrl,
    IReadOnlyList<CompetitorSnapshotDto> Snapshots, Guid ConcurrencyStamp);

public sealed record CompetitorSnapshotDto(Guid Id, DateOnly Date, long Followers, decimal? EngagementRate, int? PostsLast30Days, MetricSource Source, string SourceLabel);

public sealed class CompetitorInput
{
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required] public SocialNetwork? Network { get; set; }
    [Required, MaxLength(150)] public string Handle { get; set; } = string.Empty;
    [MaxLength(500)] public string? ProfileUrl { get; set; }
}

public sealed class CompetitorSnapshotInput
{
    [Required] public DateOnly? Date { get; set; }
    [Range(0, long.MaxValue)] public long Followers { get; set; }
    [Range(0, 1)] public decimal? EngagementRate { get; set; }
    [Range(0, 10000)] public int? PostsLast30Days { get; set; }
    public MetricSource Source { get; set; } = MetricSource.Manual;
}

public sealed record SyncResultDto(bool Configured, int Imported, string Message);

/// <summary>Social listening (lite), social inbox (lite) and competitor benchmarking.</summary>
[ApiController]
[Route("api/v1/agency/social")]
public sealed class SocialEngagementController(
    AppDbContext db, SocialAccess access, ISocialListeningProvider listening, ISocialInboxProvider inbox, IDatabaseDialect dialect,
    ICurrentUser currentUser, IAuditLogger audit, IPermissionDirectory directory, TimeProvider clock) : ControllerBase
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ------------------------------ listening

    [HttpGet("clients/{clientId:guid}/listening/queries")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IReadOnlyList<ListeningQueryDto>> Queries(Guid clientId, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        return await db.Set<SocialListeningQuery>().AsNoTracking().Where(q => q.ClientAccountId == clientId).OrderBy(q => q.Kind).ThenBy(q => q.Term)
            .Select(q => new ListeningQueryDto(q.Id, q.ClientAccountId, q.Kind, q.Term, q.Networks, q.IsActive, q.ConcurrencyStamp)).ToListAsync(ct);
    }

    [HttpPost("clients/{clientId:guid}/listening/queries")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<ListeningQueryDto> CreateQuery(Guid clientId, ListeningQueryInput input, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var term = input.Kind switch
        {
            ListeningQueryKind.Hashtag => PostValidator.NormalizeHashtag(input.Term),
            ListeningQueryKind.CompetitorHandle => "@" + Normalization.Handle(input.Term),
            _ => input.Term.Trim(),
        };
        var q = new SocialListeningQuery { ClientAccountId = clientId, Kind = input.Kind!.Value, Term = term, Networks = input.Networks.Distinct().ToList(), IsActive = input.IsActive };
        db.Set<SocialListeningQuery>().Add(q);
        await db.SaveChangesAsync(ct);
        return new ListeningQueryDto(q.Id, clientId, q.Kind, q.Term, q.Networks, q.IsActive, q.ConcurrencyStamp);
    }

    [HttpDelete("listening/queries/{id:guid}")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IActionResult> DeleteQuery(Guid id, CancellationToken ct)
    {
        var q = await access.OwnedAsync<SocialListeningQuery>(id, x => x.ClientAccountId, "Query", ct);
        db.Remove(q);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("clients/{clientId:guid}/listening/mentions")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<PagedResult<MentionDto>> Mentions(Guid clientId, [FromQuery] PageQuery query, [FromQuery] Sentiment? sentiment,
        [FromQuery] Guid? queryId, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var q = db.Set<SocialMention>().AsNoTracking().Where(m => m.ClientAccountId == clientId);
        if (sentiment is { } s) q = q.Where(m => m.Sentiment == s);
        if (queryId is { } qid) q = q.Where(m => m.QueryId == qid);
        if (!string.IsNullOrWhiteSpace(query.Search)) q = q.Where(m => EF.Functions.Like(m.Text, PagingExtensions.LikePattern(query.Search)));
        var page = await q.OrderByDescending(m => m.PostedAt).ToPagedAsync(query, ct);
        return new PagedResult<MentionDto>(page.Items.Select(ToDto).ToList(), page.Total, page.Page, page.PageSize);
    }

    [HttpGet("clients/{clientId:guid}/listening/summary")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<MentionSummaryDto> MentionSummary(Guid clientId, [FromQuery] int days = 30, CancellationToken ct = default)
    {
        await access.ClientAsync(clientId, ct);
        var since = Now.AddDays(-Math.Clamp(days, 1, 365));
        var rows = await db.Set<SocialMention>().AsNoTracking().Where(m => m.ClientAccountId == clientId && m.PostedAt >= since)
            .Select(m => new { m.Sentiment, m.SentimentSource }).ToListAsync(ct);
        return new MentionSummaryDto(rows.Count, rows.Count(r => r.Sentiment == Sentiment.Positive), rows.Count(r => r.Sentiment == Sentiment.Neutral),
            rows.Count(r => r.Sentiment == Sentiment.Negative), rows.Count(r => r.Sentiment == null), rows.Count(r => r.SentimentSource == SentimentSource.Automatic));
    }

    [HttpPost("clients/{clientId:guid}/listening/mentions")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<MentionDto> AddMention(Guid clientId, MentionInput input, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        if (input.QueryId is { } qid && !await db.Set<SocialListeningQuery>().AnyAsync(q => q.Id == qid && q.ClientAccountId == clientId, ct))
            throw DomainException.NotFound("Query");
        var url = SafeUrl(input.Url);
        var m = new SocialMention
        {
            ClientAccountId = clientId, QueryId = input.QueryId, Network = input.Network!.Value, AuthorHandle = Normalization.Handle(input.AuthorHandle),
            Text = input.Text.Trim(), Url = url, PostedAt = SocialPostService.Utc(input.PostedAt!.Value), Source = IngestSource.Manual,
            DedupeKey = "manual:" + Guid.NewGuid().ToString("N"), CreatedByUserId = currentUser.Id,
        };
        if (url is not null) m.DedupeKey = "url:" + Normalization.Sha256Hex(url)[..40];
        ApplySentiment(m, input.Sentiment, input.AutoSentiment);
        db.Set<SocialMention>().Add(m);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("social.mention_exists", "This mention was already logged.");
        }
        return ToDto(m);
    }

    [HttpPut("listening/mentions/{id:guid}/sentiment")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<MentionDto> TagMention(Guid id, SentimentInput input, CancellationToken ct)
    {
        var m = await access.OwnedAsync<SocialMention>(id, x => x.ClientAccountId, "Mention", ct);
        ApplySentiment(m, input.Sentiment, auto: false);
        await db.SaveChangesAsync(ct);
        return ToDto(m);
    }

    /// <summary>Pulls mentions from the configured listening provider (default: not configured, nothing imported).</summary>
    [HttpPost("clients/{clientId:guid}/listening/sync")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<SyncResultDto> SyncMentions(Guid clientId, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var queries = await db.Set<SocialListeningQuery>().AsNoTracking().Where(q => q.ClientAccountId == clientId && q.IsActive).ToListAsync(ct);
        var result = await listening.FetchAsync(clientId, queries, Now.AddDays(-7), ct);
        if (!result.Configured) return new SyncResultDto(false, 0, result.Message);
        var imported = 0;
        foreach (var item in result.Items)
        {
            var key = $"{item.Network}:{item.ExternalId}";
            if (await db.Set<SocialMention>().AnyAsync(m => m.ClientAccountId == clientId && m.DedupeKey == key, ct)) continue;
            var m = new SocialMention
            {
                ClientAccountId = clientId, Network = item.Network, AuthorHandle = Normalization.Handle(item.AuthorHandle), Text = item.Text,
                Url = SafeUrl(item.Url), PostedAt = item.PostedAt, Source = IngestSource.Api, DedupeKey = key,
            };
            ApplySentiment(m, null, auto: true);
            db.Set<SocialMention>().Add(m);
            imported++;
        }
        await db.SaveChangesAsync(ct);
        return new SyncResultDto(true, imported, result.Message);
    }

    // ------------------------------ inbox

    [HttpGet("clients/{clientId:guid}/inbox")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<PagedResult<InboxItemDto>> Inbox(Guid clientId, [FromQuery] PageQuery query, [FromQuery] InboxItemStatus? status,
        [FromQuery] bool mine, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var q = db.Set<SocialInboxItem>().AsNoTracking().Where(i => i.ClientAccountId == clientId);
        if (status is { } s) q = q.Where(i => i.Status == s);
        if (mine) q = q.Where(i => i.AssignedToUserId == currentUser.Id);
        var page = await q.OrderByDescending(i => i.ReceivedAt).ToPagedAsync(query, ct);
        return new PagedResult<InboxItemDto>(await InboxDtosAsync(page.Items, ct), page.Total, page.Page, page.PageSize);
    }

    [HttpPost("clients/{clientId:guid}/inbox")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<InboxItemDto> LogInboxItem(Guid clientId, InboxItemInput input, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        if (input.ProfileId is { } pid && !await db.Set<BrandProfile>().AnyAsync(p => p.Id == pid && p.ClientAccountId == clientId, ct))
            throw DomainException.NotFound("Profile");
        var item = new SocialInboxItem
        {
            ClientAccountId = clientId, ProfileId = input.ProfileId, Network = input.Network!.Value, Kind = input.Kind!.Value,
            AuthorHandle = Normalization.Handle(input.AuthorHandle), Text = input.Text.Trim(), Url = SafeUrl(input.Url),
            ReceivedAt = input.ReceivedAt is { } r ? SocialPostService.Utc(r) : Now, Source = IngestSource.Manual,
            DedupeKey = "manual:" + Guid.NewGuid().ToString("N"), Sentiment = SentimentScorer.Score(input.Text).Sentiment,
        };
        db.Set<SocialInboxItem>().Add(item);
        await db.SaveChangesAsync(ct);
        return (await InboxDtosAsync(new[] { item }, ct))[0];
    }

    [HttpPatch("inbox/{id:guid}")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<InboxItemDto> UpdateInboxItem(Guid id, InboxUpdateInput input, CancellationToken ct)
    {
        var item = await access.OwnedAsync<SocialInboxItem>(id, x => x.ClientAccountId, "Inbox item", ct);
        if (input.ConcurrencyStamp is { } stamp)
        {
            if (stamp != item.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "Changed meanwhile; reload.");
            db.Entry(item).Property(x => x.ConcurrencyStamp).OriginalValue = stamp;
        }
        if (input.AssignedToUserId is { } assignee)
        {
            if (!await db.Set<OptimizeAll.Domain.Identity.User>().AnyAsync(u => u.Id == assignee, ct))
                throw DomainException.NotFound("User");
            // Built-in or custom-role holders of social.manage.
            if (!await directory.UserHasPermissionAsync(assignee, Permissions.SocialManage, ct))
                throw new DomainException("social.assignee_invalid", "Assign to someone who manages social media.");
            item.AssignedToUserId = assignee;
            if (item.Status == InboxItemStatus.Open) item.Status = InboxItemStatus.Assigned;
        }
        if (input.Unassign)
        {
            item.AssignedToUserId = null;
            if (item.Status == InboxItemStatus.Assigned) item.Status = InboxItemStatus.Open;
        }
        if (input.Status is { } status) item.Status = status;
        if (input.Sentiment is { } sentiment) item.Sentiment = sentiment;
        await db.SaveChangesAsync(ct);
        return (await InboxDtosAsync(new[] { item }, ct))[0];
    }

    [HttpPost("inbox/{id:guid}/replies")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<InboxItemDto> Reply(Guid id, InboxReplyInput input, CancellationToken ct)
    {
        var item = await access.OwnedAsync<SocialInboxItem>(id, x => x.ClientAccountId, "Inbox item", ct);
        var reply = new SocialInboxReply { ItemId = item.Id, ClientAccountId = item.ClientAccountId, Body = input.Body.Trim(), ByUserId = currentUser.Id, CreatedAt = Now };
        if (input.SendViaApi)
        {
            var profile = item.ProfileId is { } pid ? await db.Set<BrandProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid, ct) : null;
            if (profile is null) throw DomainException.Conflict("social.reply_not_configured", "This item has no connected profile; reply on the network and log it.");
            var (sent, externalId, message) = await inbox.ReplyAsync(profile, item, reply.Body, ct);
            if (!sent) throw DomainException.Conflict("social.reply_not_configured", message);
            reply.SentViaApi = true;
            reply.ExternalId = externalId;
        }
        db.Set<SocialInboxReply>().Add(reply);
        item.Status = InboxItemStatus.Replied;
        audit.Record("social.inbox.replied", nameof(SocialInboxItem), item.Id, after: new { reply.SentViaApi });
        await db.SaveChangesAsync(ct);
        return (await InboxDtosAsync(new[] { item }, ct))[0];
    }

    [HttpPost("clients/{clientId:guid}/inbox/sync")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<SyncResultDto> SyncInbox(Guid clientId, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var profiles = await db.Set<BrandProfile>().AsNoTracking().Where(p => p.ClientAccountId == clientId && p.IsActive).ToListAsync(ct);
        var imported = 0;
        var messages = new List<string>();
        var configured = false;
        foreach (var profile in profiles)
        {
            var result = await inbox.FetchAsync(profile, Now.AddDays(-7), ct);
            if (!result.Configured)
            {
                messages.Add(result.Message);
                continue;
            }
            configured = true;
            foreach (var i in result.Items)
            {
                var key = $"{i.Network}:{i.ExternalId}";
                if (await db.Set<SocialInboxItem>().AnyAsync(x => x.ClientAccountId == clientId && x.DedupeKey == key, ct)) continue;
                db.Set<SocialInboxItem>().Add(new SocialInboxItem
                {
                    ClientAccountId = clientId, ProfileId = profile.Id, Network = i.Network, Kind = i.Kind, AuthorHandle = Normalization.Handle(i.AuthorHandle),
                    Text = i.Text, Url = SafeUrl(i.Url), ReceivedAt = i.ReceivedAt, Source = IngestSource.Api, DedupeKey = key,
                    Sentiment = SentimentScorer.Score(i.Text).Sentiment,
                });
                imported++;
            }
        }
        await db.SaveChangesAsync(ct);
        return new SyncResultDto(configured, imported, messages.Count == 0 ? $"Imported {imported} item(s)." : string.Join(" ", messages.Distinct()));
    }

    // ------------------------------ competitors

    [HttpGet("clients/{clientId:guid}/competitors")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IReadOnlyList<CompetitorDto>> Competitors(Guid clientId, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var list = await db.Set<SocialCompetitor>().AsNoTracking().Where(c => c.ClientAccountId == clientId).OrderBy(c => c.Name).ToListAsync(ct);
        var ids = list.Select(c => c.Id).ToList();
        var snaps = await db.Set<SocialCompetitorSnapshot>().AsNoTracking().Where(s => ids.Contains(s.CompetitorId)).OrderBy(s => s.Date).ToListAsync(ct);
        return list.Select(c => new CompetitorDto(c.Id, c.ClientAccountId, c.Name, c.Network, c.Handle, c.ProfileUrl,
            snaps.Where(s => s.CompetitorId == c.Id).Select(SnapshotDto).ToList(), c.ConcurrencyStamp)).ToList();
    }

    [HttpPost("clients/{clientId:guid}/competitors")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<CompetitorDto> AddCompetitor(Guid clientId, CompetitorInput input, CancellationToken ct)
    {
        await access.ClientAsync(clientId, ct);
        var handle = Normalization.Handle(input.Handle);
        if (await db.Set<SocialCompetitor>().AnyAsync(c => c.ClientAccountId == clientId && c.Network == input.Network && c.Handle == handle, ct))
            throw DomainException.Conflict("social.competitor_exists", "This competitor profile is already tracked.");
        var c = new SocialCompetitor { ClientAccountId = clientId, Name = input.Name.Trim(), Network = input.Network!.Value, Handle = handle, ProfileUrl = SafeUrl(input.ProfileUrl) };
        db.Set<SocialCompetitor>().Add(c);
        await db.SaveChangesAsync(ct);
        return new CompetitorDto(c.Id, clientId, c.Name, c.Network, c.Handle, c.ProfileUrl, Array.Empty<CompetitorSnapshotDto>(), c.ConcurrencyStamp);
    }

    [HttpDelete("competitors/{id:guid}")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<IActionResult> DeleteCompetitor(Guid id, CancellationToken ct)
    {
        var c = await access.OwnedAsync<SocialCompetitor>(id, x => x.ClientAccountId, "Competitor", ct);
        db.Remove(c);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Records (or replaces) the snapshot of a day: idempotent by (competitor, date).</summary>
    [HttpPut("competitors/{id:guid}/snapshots")]
    [HasPermission(Permissions.SocialManage)]
    public async Task<CompetitorSnapshotDto> UpsertSnapshot(Guid id, CompetitorSnapshotInput input, CancellationToken ct)
    {
        var c = await access.OwnedAsync<SocialCompetitor>(id, x => x.ClientAccountId, "Competitor", ct);
        if (input.Source == MetricSource.Api)
            throw new DomainException("social.source_invalid", "Snapshots entered here are Manual or PlatformExport.");
        var date = input.Date!.Value;
        var snap = await db.Set<SocialCompetitorSnapshot>().FirstOrDefaultAsync(s => s.CompetitorId == c.Id && s.Date == date, ct);
        if (snap is null)
        {
            snap = new SocialCompetitorSnapshot { CompetitorId = c.Id, ClientAccountId = c.ClientAccountId, Date = date };
            db.Set<SocialCompetitorSnapshot>().Add(snap);
        }
        snap.Followers = input.Followers;
        snap.EngagementRate = input.EngagementRate;
        snap.PostsLast30Days = input.PostsLast30Days;
        snap.Source = input.Source;
        snap.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return SnapshotDto(snap);
    }

    // ------------------------------ helpers

    private static void ApplySentiment(SocialMention m, Sentiment? manual, bool auto)
    {
        if (manual is { } s)
        {
            m.Sentiment = s;
            m.SentimentSource = SentimentSource.Manual;
            m.SentimentScore = null;
        }
        else if (auto)
        {
            var estimate = SentimentScorer.Score(m.Text);
            m.Sentiment = estimate.Sentiment;
            m.SentimentSource = SentimentSource.Automatic;
            m.SentimentScore = estimate.Score;
        }
    }

    private static MentionDto ToDto(SocialMention m) => new(m.Id, m.ClientAccountId, m.QueryId, m.Network, m.AuthorHandle, m.Text, m.Url, m.PostedAt,
        m.Sentiment, m.SentimentSource, m.SentimentScore,
        m.Sentiment is null ? null : m.SentimentSource == SentimentSource.Automatic ? $"{m.Sentiment} (automatic estimate)" : $"{m.Sentiment} (tagged)",
        m.Source);

    private static CompetitorSnapshotDto SnapshotDto(SocialCompetitorSnapshot s) =>
        new(s.Id, s.Date, s.Followers, s.EngagementRate, s.PostsLast30Days, s.Source, MetricSources.Label(s.Source));

    private async Task<IReadOnlyList<InboxItemDto>> InboxDtosAsync(IReadOnlyList<SocialInboxItem> items, CancellationToken ct)
    {
        var ids = items.Select(i => i.Id).ToList();
        var replies = await db.Set<SocialInboxReply>().AsNoTracking().Where(r => ids.Contains(r.ItemId)).OrderBy(r => r.CreatedAt).ToListAsync(ct);
        var names = await UserNames.LookupAsync(db, items.Select(i => i.AssignedToUserId).Concat(replies.Select(r => (Guid?)r.ByUserId)), ct);
        return items.Select(i => new InboxItemDto(i.Id, i.ClientAccountId, i.ProfileId, i.Network, i.Kind, i.AuthorHandle, i.Text, i.Url, i.ReceivedAt,
            i.Status, i.AssignedToUserId, i.AssignedToUserId is { } a ? names.GetValueOrDefault(a) : null, i.Sentiment, i.Source,
            replies.Where(r => r.ItemId == i.Id).Select(r => new InboxReplyDto(r.Id, r.Body, r.SentViaApi, names.GetValueOrDefault(r.ByUserId, "Someone"), r.CreatedAt)).ToList(),
            i.ConcurrencyStamp)).ToList();
    }

    private static string? SafeUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) ? null
        : Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? u.ToString()
        : throw new DomainException("social.invalid_url", "Use an http(s) URL.");
}

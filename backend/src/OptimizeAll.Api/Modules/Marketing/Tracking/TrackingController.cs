using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Marketing.Shared;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Marketing.Tracking;

public sealed record TrackingLinkStatsDto(int Clicks, int UniqueClicks, int VerifiedConversions);

public sealed record TrackingLinkDto(
    Guid Id, Guid CampaignId, string CampaignTitle, string Code, string ShortUrl, string DestinationPreview,
    string UtmSource, string UtmMedium, string UtmCampaign, string? UtmContent, DateTime CreatedAt, TrackingLinkStatsDto Stats);

public sealed record TrackingParticipantRowDto(Guid? UserId, string? DisplayName, int Clicks, int UniqueClicks, int VerifiedConversions);

public sealed record TrackingSummaryDto(
    DateTime From, DateTime To, Guid? CampaignId, int Clicks, int UniqueClicks, int BotClicksExcluded, int VerifiedConversions,
    IReadOnlyList<MoneyAmount> ConversionValue, IReadOnlyList<TrackingParticipantRowDto> TopParticipants, string Note);

public sealed class TrackingService(AppDbContext db, MarketingUrls urls, TimeProvider clock)
{
    public const string UtmSource = "optimizeall";
    public const string UtmMedium = "social";

    /// <summary>The redirect target: the campaign's current destination (stored config) or the one captured at link creation.</summary>
    public static string EffectiveDestination(TrackingLink link, string? campaignDestination) =>
        TrackingUrl.IsValidDestination(campaignDestination) ? campaignDestination! : link.DestinationUrl;

    public static string BuildTarget(TrackingLink link, string? campaignDestination) =>
        TrackingUrl.MergeQuery(EffectiveDestination(link, campaignDestination),
            TrackingUrl.Utm(link.UtmSource, link.UtmMedium, link.UtmCampaign, link.UtmContent, link.UtmTerm));

    public async Task<TrackingLinkDto> GetOrCreateAsync(Guid userId, Guid campaignId, CancellationToken ct)
    {
        var campaign = await db.Set<Campaign>().AsNoTracking()
            .Where(c => c.Id == campaignId)
            .Select(c => new { c.Id, c.Slug, c.Title, c.Status, c.TrackingDestinationUrl, c.UtmCampaign })
            .FirstOrDefaultAsync(ct);
        if (campaign is null || campaign.Status is CampaignStatus.Draft or CampaignStatus.Archived)
            throw DomainException.NotFound("Campaign");

        var existing = await db.Set<TrackingLink>().AsNoTracking()
            .FirstOrDefaultAsync(l => l.CampaignId == campaignId && l.UserId == userId, ct);
        if (existing is not null) return (await ListAsync(userId, existing.Id, ct)).Single();

        if (!TrackingUrl.IsValidDestination(campaign.TrackingDestinationUrl))
            throw DomainException.Conflict("tracking.not_enabled", "Tracking links are not enabled for this campaign.");

        var referralCode = await db.Set<User>().Where(u => u.Id == userId).Select(u => u.ReferralCode).FirstAsync(ct);
        var utmCampaign = string.IsNullOrWhiteSpace(campaign.UtmCampaign) ? campaign.Slug : campaign.UtmCampaign.Trim();

        for (var attempt = 0; ; attempt++)
        {
            var link = new TrackingLink
            {
                Code = CodeGenerator.New(10),
                CampaignId = campaignId,
                UserId = userId,
                DestinationUrl = campaign.TrackingDestinationUrl!,
                UtmSource = UtmSource,
                UtmMedium = UtmMedium,
                UtmCampaign = utmCampaign.Length > 100 ? utmCampaign[..100] : utmCampaign,
                UtmContent = string.IsNullOrWhiteSpace(referralCode) ? null : referralCode,
                CreatedAt = clock.GetUtcNow().UtcDateTime,
            };
            db.Set<TrackingLink>().Add(link);
            try
            {
                await db.SaveChangesAsync(ct);
                return (await ListAsync(userId, link.Id, ct)).Single();
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex) && attempt < 5)
            {
                db.ChangeTracker.Clear();
                // Either a concurrent request created the participant's link (return it) or the code collided (retry).
                var raced = await db.Set<TrackingLink>().AsNoTracking()
                    .FirstOrDefaultAsync(l => l.CampaignId == campaignId && l.UserId == userId, ct);
                if (raced is not null) return (await ListAsync(userId, raced.Id, ct)).Single();
            }
        }
    }

    public async Task<IReadOnlyList<TrackingLinkDto>> ListAsync(Guid userId, Guid? linkId, CancellationToken ct)
    {
        var rows = await (
            from l in db.Set<TrackingLink>().AsNoTracking()
            join c in db.Set<Campaign>() on l.CampaignId equals c.Id
            where l.UserId == userId && (linkId == null || l.Id == linkId)
            orderby l.CreatedAt descending
            select new
            {
                Link = l, c.Title, c.TrackingDestinationUrl,
                Clicks = db.Set<TrackingClick>().Count(k => k.TrackingLinkId == l.Id && !k.IsSuspectedBot),
                Unique = db.Set<TrackingClick>().Count(k => k.TrackingLinkId == l.Id && !k.IsSuspectedBot && k.IsUnique),
                Conversions = db.Set<TrackingConversion>().Count(v => v.TrackingLinkId == l.Id && v.VerifiedAt != null),
            }).ToListAsync(ct);

        return rows.Select(r => new TrackingLinkDto(
            r.Link.Id, r.Link.CampaignId, r.Title, r.Link.Code, urls.TrackingShortUrl(r.Link.Code),
            BuildTarget(r.Link, r.TrackingDestinationUrl), r.Link.UtmSource, r.Link.UtmMedium, r.Link.UtmCampaign,
            r.Link.UtmContent, r.Link.CreatedAt, new TrackingLinkStatsDto(r.Clicks, r.Unique, r.Conversions))).ToList();
    }

    public async Task<TrackingSummaryDto> SummaryAsync(Guid? campaignId, DateRange range, CancellationToken ct)
    {
        var (start, end) = (range.From, range.To);
        var links = db.Set<TrackingLink>().AsNoTracking().Where(l => campaignId == null || l.CampaignId == campaignId);
        var clicks = from k in db.Set<TrackingClick>().AsNoTracking()
                     join l in links on k.TrackingLinkId equals l.Id
                     where k.ClickedAt >= start && k.ClickedAt <= end
                     select new { k.IsSuspectedBot, k.IsUnique, l.UserId };
        var conversions = from v in db.Set<TrackingConversion>().AsNoTracking()
                          join l in links on v.TrackingLinkId equals l.Id
                          where v.VerifiedAt != null && v.OccurredAt >= start && v.OccurredAt <= end
                          select new { v.Value, v.Currency, l.UserId };

        var clickTotals = await clicks.GroupBy(_ => 1).Select(g => new
        {
            Human = g.Count(x => !x.IsSuspectedBot),
            Unique = g.Count(x => !x.IsSuspectedBot && x.IsUnique),
            Bots = g.Count(x => x.IsSuspectedBot),
        }).FirstOrDefaultAsync(ct);
        var verified = await conversions.CountAsync(ct);
        var value = await conversions.Where(v => v.Value != null && v.Currency != null)
            .GroupBy(v => v.Currency!).Select(g => new MoneyAmount(g.Key, g.Sum(x => x.Value!.Value)))
            .ToListAsync(ct);

        var perUserClicks = await clicks.Where(x => !x.IsSuspectedBot).GroupBy(x => x.UserId)
            .Select(g => new { UserId = g.Key, Clicks = g.Count(), Unique = g.Count(x => x.IsUnique) })
            .OrderByDescending(x => x.Clicks).Take(10).ToListAsync(ct);
        var perUserConversions = await conversions.GroupBy(x => x.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() }).ToListAsync(ct);
        var userIds = perUserClicks.Select(x => x.UserId).Concat(perUserConversions.Select(x => x.UserId))
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var top = perUserClicks.Select(x => x.UserId)
            .Concat(perUserConversions.OrderByDescending(x => x.Count).Select(x => x.UserId))
            .Distinct()
            .Select(id => new TrackingParticipantRowDto(
                id, id is { } u && names.TryGetValue(u, out var n) ? n : null,
                perUserClicks.FirstOrDefault(x => x.UserId == id)?.Clicks ?? 0,
                perUserClicks.FirstOrDefault(x => x.UserId == id)?.Unique ?? 0,
                perUserConversions.FirstOrDefault(x => x.UserId == id)?.Count ?? 0))
            .OrderByDescending(r => r.Clicks).ThenByDescending(r => r.VerifiedConversions)
            .Take(10).ToList();

        return new TrackingSummaryDto(start, end, campaignId, clickTotals?.Human ?? 0, clickTotals?.Unique ?? 0, clickTotals?.Bots ?? 0,
            verified, value.OrderBy(v => v.Currency).ToList(), top,
            "Measured: clicks recorded by the redirect (suspected bots excluded) and conversions verified by signed postback.");
    }
}

[ApiController]
public sealed class TrackingController(TrackingService tracking, ICurrentUser currentUser, TimeProvider clock) : ControllerBase
{
    /// <summary>Returns (creating once) the caller's tracking link for a campaign. 409 tracking.not_enabled without a destination.</summary>
    [HttpPost("api/v1/me/campaigns/{campaignId:guid}/tracking-link")]
    [HasPermission(Permissions.ParticipantPortal)]
    public Task<TrackingLinkDto> Create(Guid campaignId, CancellationToken ct) =>
        tracking.GetOrCreateAsync(currentUser.Id, campaignId, ct);

    [HttpGet("api/v1/me/tracking-links")]
    [HasPermission(Permissions.ParticipantPortal)]
    public Task<IReadOnlyList<TrackingLinkDto>> Mine(CancellationToken ct) => tracking.ListAsync(currentUser.Id, null, ct);

    [HttpGet("api/v1/marketing/tracking/summary")]
    [HasPermission(Permissions.MarketingManage)]
    public Task<TrackingSummaryDto> Summary([FromQuery] Guid? campaignId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) =>
        tracking.SummaryAsync(campaignId, DateRange.Resolve(from, to, clock.GetUtcNow().UtcDateTime), ct);
}

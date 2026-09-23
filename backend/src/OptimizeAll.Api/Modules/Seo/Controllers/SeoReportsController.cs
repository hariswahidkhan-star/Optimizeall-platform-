using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Seo;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Seo.Controllers;

public sealed record ClientSeoOrganizationDto(
    Guid ClientAccountId, string ClientName, ClientSeoKpisDto Kpis, IReadOnlyList<ClientKeywordDto> TopKeywords);

public sealed record ClientKeywordDto(string Keyword, int? Position, int? Change, string SiteName);

public sealed record ClientSeoOverviewDto(DateOnly From, DateOnly To, IReadOnlyList<ClientSeoOrganizationDto> Organizations);

/// <summary>
/// SEO KPIs for client reports. The Projects module exposes no <c>IClientReportSection</c> in this build, so reporting
/// reads this endpoint (see docs/api/seo-pages-integrations.md). Requires seo.manage or reports.manage.
/// </summary>
[ApiController]
[Authorize]
public sealed class SeoReportsController(AppDbContext db, SeoAccess access, ICurrentUser currentUser, TimeProvider clock) : ControllerBase
{
    [HttpGet("api/v1/agency/seo/clients/{clientId:guid}/kpis")]
    public async Task<ClientSeoKpisDto> Kpis(Guid clientId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (!currentUser.HasPermission(Permissions.SeoManage) && !currentUser.HasPermission(Permissions.ReportsManage))
            throw DomainException.Forbidden("auth.forbidden", "You do not have permission to perform this action.");
        await access.EnsureAsync(clientId, "Client", ct);
        var (start, end) = Range(from, to);
        var name = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == clientId).Select(c => c.Name).FirstAsync(ct);
        return await SeoKpiService.ClientKpisAsync(db, clientId, name, start, end, ct);
    }

    /// <summary>Client portal: SEO health, rankings summary and landing-page leads for each organization the user belongs to.</summary>
    [HttpGet("api/v1/client/seo/overview")]
    [HasPermission(Permissions.ClientPortal)]
    public async Task<ClientSeoOverviewDto> ClientOverview([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var (start, end) = Range(from, to);
        var memberIds = await db.Set<ClientMember>().AsNoTracking().Where(m => m.UserId == currentUser.Id).Select(m => m.ClientAccountId).ToListAsync(ct);
        var clients = await db.Set<ClientAccount>().AsNoTracking().Where(c => memberIds.Contains(c.Id)).OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name }).ToListAsync(ct);
        var result = new List<ClientSeoOrganizationDto>();
        foreach (var client in clients)
        {
            var kpis = await SeoKpiService.ClientKpisAsync(db, client.Id, client.Name, start, end, ct);
            result.Add(new ClientSeoOrganizationDto(client.Id, client.Name, kpis, await TopKeywordsAsync(client.Id, ct)));
        }
        return new ClientSeoOverviewDto(start, end, result);
    }

    private async Task<List<ClientKeywordDto>> TopKeywordsAsync(Guid clientId, CancellationToken ct)
    {
        var sites = await db.Set<SeoSite>().AsNoTracking().Where(s => s.ClientAccountId == clientId && !s.IsArchived)
            .Select(s => new { s.Id, s.Name, s.Domain }).ToListAsync(ct);
        var result = new List<ClientKeywordDto>();
        foreach (var site in sites)
        {
            var own = RankMath.BareHost(site.Domain);
            var snaps = await db.Set<SeoRankSnapshot>().AsNoTracking().Where(s => s.SiteId == site.Id && s.Domain == own)
                .OrderByDescending(s => s.Date).Take(2000).Select(s => new { s.KeywordId, s.Date, s.Position }).ToListAsync(ct);
            var keywordIds = snaps.Select(s => s.KeywordId).Distinct().ToList();
            var names = await db.Set<SeoKeyword>().AsNoTracking().Where(k => keywordIds.Contains(k.Id) && k.IsTracked)
                .ToDictionaryAsync(k => k.Id, k => k.Keyword, ct);
            foreach (var g in snaps.Where(s => names.ContainsKey(s.KeywordId)).GroupBy(s => s.KeywordId))
            {
                var ordered = g.OrderByDescending(s => s.Date).ToList();
                var latest = ordered[0];
                var previous = ordered.Skip(1).FirstOrDefault();
                result.Add(new ClientKeywordDto(names[g.Key], latest.Position,
                    previous is null ? null : RankMath.Change(previous.Position, latest.Position), site.Name));
            }
        }
        return result.OrderBy(k => k.Position ?? int.MaxValue).ThenBy(k => k.Keyword).Take(10).ToList();
    }

    private (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to)
    {
        var end = to ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var start = from ?? end.AddDays(-30);
        if (start > end) throw new DomainException("range.invalid", "'from' must be before or equal to 'to'.");
        if (end.DayNumber - start.DayNumber > 366) throw new DomainException("range.too_long", "The date range may span at most 366 days.");
        return (start, end);
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Marketing.Shared;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Marketing.Invitations;

public sealed class InvitationRequest
{
    [Required, MinLength(2), MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Null = platform invitation; otherwise the campaign whose landing page the link opens.</summary>
    public Guid? CampaignId { get; set; }

    [MaxLength(100)] public string? UtmSource { get; set; }
    [MaxLength(100)] public string? UtmMedium { get; set; }
    [MaxLength(100)] public string? UtmCampaign { get; set; }

    public DateTime? ExpiresAt { get; set; }

    [Range(1, 10_000_000)]
    public int? MaxUses { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed record InvitationStatsDto(int Visits, int Registrations, int? RemainingUses);

public sealed record InvitationDto(
    Guid Id, string Code, string Url, string Name, Guid? CampaignId, string? CampaignTitle,
    string? UtmSource, string? UtmMedium, string? UtmCampaign, DateTime? ExpiresAt, int? MaxUses, bool IsActive,
    bool IsUsable, InvitationStatsDto Stats, DateTime CreatedAt, DateTime UpdatedAt);

public sealed class InvitationListQuery : PageQuery
{
    public Guid? CampaignId { get; set; }
    public bool? IsActive { get; set; }
}

public sealed record InvitationDeleteResult(bool Deleted, bool Deactivated);

[ApiController]
[Route("api/v1/marketing/invitations")]
[HasPermission(Permissions.MarketingManage)]
public sealed class InvitationsController(
    AppDbContext db, MarketingUrls urls, IAuditLogger audit, ICurrentUser currentUser, TimeProvider clock) : ControllerBase
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    [HttpGet]
    public async Task<PagedResult<InvitationDto>> List([FromQuery] InvitationListQuery query, CancellationToken ct)
    {
        var q = db.Set<InvitationLink>().AsNoTracking().AsQueryable();
        if (query.CampaignId is { } campaignId) q = q.Where(i => i.CampaignId == campaignId);
        if (query.IsActive is { } active) q = q.Where(i => i.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search);
            q = q.Where(i => EF.Functions.Like(i.Name, like) || EF.Functions.Like(i.Code, like));
        }
        var page = await q.OrderByDescending(i => i.CreatedAt).ToPagedAsync(query, ct);
        var titles = await CampaignTitlesAsync(page.Items.Select(i => i.CampaignId), ct);
        return new PagedResult<InvitationDto>(page.Items.Select(i => ToDto(i, titles)).ToList(), page.Total, page.Page, page.PageSize);
    }

    [HttpGet("{id:guid}")]
    public async Task<InvitationDto> Get(Guid id, CancellationToken ct)
    {
        var link = await db.Set<InvitationLink>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct)
                   ?? throw DomainException.NotFound("Invitation");
        return ToDto(link, await CampaignTitlesAsync(new[] { link.CampaignId }, ct));
    }

    [HttpPost]
    public async Task<ActionResult<InvitationDto>> Create(InvitationRequest request, CancellationToken ct)
    {
        await ValidateAsync(request, ct);
        for (var attempt = 0; ; attempt++)
        {
            var link = new InvitationLink { Code = CodeGenerator.New(8), CreatedByUserId = currentUser.Id };
            Apply(link, request);
            db.Set<InvitationLink>().Add(link);
            audit.Record("invitation.created", nameof(InvitationLink), link.Id, after: new { link.Code, request.Name, request.CampaignId, request.MaxUses, request.ExpiresAt });
            try
            {
                await db.SaveChangesAsync(ct);
                return CreatedAtAction(nameof(Get), new { id = link.Id }, await Get(link.Id, ct));
            }
            catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex) && attempt < 5)
            {
                db.ChangeTracker.Clear(); // code collision: retry with a new code
            }
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<InvitationDto> Update(Guid id, InvitationRequest request, CancellationToken ct)
    {
        var link = await db.Set<InvitationLink>().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw DomainException.NotFound("Invitation");
        await ValidateAsync(request, ct);
        var before = new { link.Name, link.CampaignId, link.ExpiresAt, link.MaxUses, link.IsActive };
        Apply(link, request);
        audit.Record("invitation.updated", nameof(InvitationLink), id, before, new { request.Name, request.CampaignId, request.ExpiresAt, request.MaxUses, request.IsActive });
        await db.SaveChangesAsync(ct);
        return await Get(id, ct);
    }

    /// <summary>Deletes an unused link; a link that has been visited or used is deactivated instead (keeps its stats).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<InvitationDeleteResult> Delete(Guid id, CancellationToken ct)
    {
        var link = await db.Set<InvitationLink>().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw DomainException.NotFound("Invitation");
        if (link.UseCount == 0 && link.VisitCount == 0)
        {
            db.Set<InvitationLink>().Remove(link);
            audit.Record("invitation.deleted", nameof(InvitationLink), id, before: new { link.Code, link.Name });
            await db.SaveChangesAsync(ct);
            return new InvitationDeleteResult(true, false);
        }
        link.IsActive = false;
        audit.Record("invitation.deactivated", nameof(InvitationLink), id, before: new { link.Code, link.Name });
        await db.SaveChangesAsync(ct);
        return new InvitationDeleteResult(false, true);
    }

    private async Task ValidateAsync(InvitationRequest request, CancellationToken ct)
    {
        if (request.CampaignId is { } campaignId)
        {
            var status = await db.Set<Campaign>().Where(c => c.Id == campaignId).Select(c => (CampaignStatus?)c.Status).FirstOrDefaultAsync(ct);
            if (status is null) throw new DomainException("invitation.campaign_not_found", "The campaign does not exist.");
            if (status is CampaignStatus.Archived or CampaignStatus.Ended)
                throw new DomainException("invitation.campaign_closed", "Invitations can only link to campaigns that have not ended.");
        }
        if (request.ExpiresAt is { } expires && expires.ToUniversalTime() <= Now)
            throw new DomainException("invitation.expiry_in_past", "The expiry must be in the future.");
    }

    private static void Apply(InvitationLink link, InvitationRequest r)
    {
        link.Name = r.Name.Trim();
        link.CampaignId = r.CampaignId;
        link.UtmSource = Clean(r.UtmSource);
        link.UtmMedium = Clean(r.UtmMedium);
        link.UtmCampaign = Clean(r.UtmCampaign);
        link.ExpiresAt = r.ExpiresAt?.ToUniversalTime();
        link.MaxUses = r.MaxUses;
        link.IsActive = r.IsActive;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<Dictionary<Guid, string>> CampaignTitlesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return new Dictionary<Guid, string>();
        return await db.Set<Campaign>().AsNoTracking().Where(c => list.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Title, ct);
    }

    private InvitationDto ToDto(InvitationLink i, IReadOnlyDictionary<Guid, string> titles) => new(
        i.Id, i.Code, urls.InvitationLink(i.Code), i.Name, i.CampaignId,
        i.CampaignId is { } c && titles.TryGetValue(c, out var t) ? t : null,
        i.UtmSource, i.UtmMedium, i.UtmCampaign, i.ExpiresAt, i.MaxUses, i.IsActive,
        IsUsable(i, Now),
        new InvitationStatsDto(i.VisitCount, i.UseCount, i.MaxUses is { } max ? Math.Max(0, max - i.UseCount) : null),
        i.CreatedAt, i.UpdatedAt);

    public static bool IsUsable(InvitationLink i, DateTime now) =>
        i.IsActive && (i.ExpiresAt is null || i.ExpiresAt > now) && (i.MaxUses is null || i.UseCount < i.MaxUses);
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Marketing.Shared;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Marketing.Content;

public sealed class CalendarEntryRequest
{
    [Required, MinLength(2), MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public Guid? CampaignId { get; set; }
    public Guid? TemplateId { get; set; }
    public SocialPlatform? Platform { get; set; }

    [Required]
    public DateTime? ScheduledFor { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public CalendarEntryStatus Status { get; set; } = CalendarEntryStatus.Planned;
}

public sealed record CalendarEntryDto(
    Guid Id, string Title, Guid? CampaignId, string? CampaignTitle, Guid? TemplateId, string? TemplateName,
    SocialPlatform? Platform, DateTime ScheduledFor, string? Notes, CalendarEntryStatus Status, DateTime CreatedAt, DateTime UpdatedAt);

public sealed class CalendarQuery
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? CampaignId { get; set; }
    public SocialPlatform? Platform { get; set; }
    public CalendarEntryStatus? Status { get; set; }
}

public sealed record CalendarDto(DateTime From, DateTime To, IReadOnlyList<CalendarEntryDto> Items);

[ApiController]
[Route("api/v1/marketing/calendar")]
[HasPermission(Permissions.MarketingManage)]
public sealed class CalendarController(AppDbContext db, IAuditLogger audit, ICurrentUser currentUser, TimeProvider clock) : ControllerBase
{
    /// <summary>Entries scheduled in [from, to] (default: the next 30 days from today 00:00 UTC; max range 366 days).</summary>
    [HttpGet]
    public async Task<CalendarDto> List([FromQuery] CalendarQuery query, CancellationToken ct)
    {
        var today = clock.GetUtcNow().UtcDateTime.Date;
        var from = query.From ?? (query.To.HasValue ? query.To.Value.AddDays(-30) : today);
        var to = query.To ?? from.AddDays(30);
        var range = DateRange.Resolve(from, to, clock.GetUtcNow().UtcDateTime);

        var q = db.Set<ContentCalendarEntry>().AsNoTracking().Where(e => e.ScheduledFor >= range.From && e.ScheduledFor <= range.To);
        if (query.CampaignId is { } c) q = q.Where(e => e.CampaignId == c);
        if (query.Platform is { } p) q = q.Where(e => e.Platform == p);
        if (query.Status is { } s) q = q.Where(e => e.Status == s);
        return new CalendarDto(range.From, range.To, await Project(q).Take(2000).ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<CalendarEntryDto> Get(Guid id, CancellationToken ct) =>
        await Project(db.Set<ContentCalendarEntry>().AsNoTracking().Where(e => e.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw DomainException.NotFound("CalendarEntry");

    [HttpPost]
    public async Task<ActionResult<CalendarEntryDto>> Create(CalendarEntryRequest request, CancellationToken ct)
    {
        await ValidateAsync(request, null, ct);
        var entry = new ContentCalendarEntry { CreatedByUserId = currentUser.Id };
        Apply(entry, request);
        db.Set<ContentCalendarEntry>().Add(entry);
        audit.Record("calendar.created", nameof(ContentCalendarEntry), entry.Id, after: new { entry.Title, entry.ScheduledFor, entry.CampaignId });
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = entry.Id }, await Get(entry.Id, ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<CalendarEntryDto> Update(Guid id, CalendarEntryRequest request, CancellationToken ct)
    {
        var entry = await db.Set<ContentCalendarEntry>().FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw DomainException.NotFound("CalendarEntry");
        await ValidateAsync(request, entry.TemplateId, ct);
        var before = new { entry.Title, entry.ScheduledFor, entry.Status };
        Apply(entry, request);
        audit.Record("calendar.updated", nameof(ContentCalendarEntry), id, before, new { entry.Title, entry.ScheduledFor, entry.Status });
        await db.SaveChangesAsync(ct);
        return await Get(id, ct);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var entry = await db.Set<ContentCalendarEntry>().FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw DomainException.NotFound("CalendarEntry");
        db.Set<ContentCalendarEntry>().Remove(entry);
        audit.Record("calendar.deleted", nameof(ContentCalendarEntry), id, before: new { entry.Title, entry.ScheduledFor });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private IQueryable<CalendarEntryDto> Project(IQueryable<ContentCalendarEntry> entries) =>
        from e in entries
        join c in db.Set<Campaign>() on e.CampaignId equals c.Id into cj
        from c in cj.DefaultIfEmpty()
        join t in db.Set<PostTemplate>() on e.TemplateId equals t.Id into tj
        from t in tj.DefaultIfEmpty()
        orderby e.ScheduledFor, e.Id
        select new CalendarEntryDto(e.Id, e.Title, e.CampaignId, c == null ? null : c.Title, e.TemplateId, t == null ? null : t.Name,
            e.Platform, e.ScheduledFor, e.Notes, e.Status, e.CreatedAt, e.UpdatedAt);

    private async Task ValidateAsync(CalendarEntryRequest request, Guid? currentTemplateId, CancellationToken ct)
    {
        if (request.CampaignId is { } campaignId && !await db.Set<Campaign>().AnyAsync(c => c.Id == campaignId, ct))
            throw new DomainException("calendar.campaign_not_found", "The campaign does not exist.");
        if (request.TemplateId is { } templateId)
        {
            var archived = await db.Set<PostTemplate>().Where(t => t.Id == templateId).Select(t => (bool?)t.IsArchived).FirstOrDefaultAsync(ct);
            if (archived is null) throw new DomainException("calendar.template_not_found", "The template does not exist.");
            if (archived == true && templateId != currentTemplateId)
                throw new DomainException("calendar.template_archived", "Archived templates cannot be scheduled.");
        }
    }

    private static void Apply(ContentCalendarEntry e, CalendarEntryRequest r)
    {
        e.Title = r.Title.Trim();
        e.CampaignId = r.CampaignId;
        e.TemplateId = r.TemplateId;
        e.Platform = r.Platform;
        e.ScheduledFor = r.ScheduledFor!.Value.ToUniversalTime();
        e.Notes = string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes.Trim();
        e.Status = r.Status;
    }
}

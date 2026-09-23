using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Marketing.Content;

public sealed class TemplateRequest
{
    [Required, MinLength(2), MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    public SocialPlatform? Platform { get; set; }

    [Required, MaxLength(5000)]
    public string Body { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Hashtags { get; set; }

    [MaxLength(10), RegularExpression("^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})?$", ErrorMessage = "Use a BCP 47 language code, e.g. en or en-GB.")]
    public string? LanguageCode { get; set; }

    public bool IsArchived { get; set; }
}

public sealed record TemplateDto(
    Guid Id, string Name, SocialPlatform? Platform, string Body, string? Hashtags, string? LanguageCode, bool IsArchived,
    int UsageCount, DateTime CreatedAt, DateTime UpdatedAt);

public sealed class TemplateListQuery : PageQuery
{
    public SocialPlatform? Platform { get; set; }
    public string? LanguageCode { get; set; }
    public bool IncludeArchived { get; set; }
}

public sealed record TemplateDeleteResult(bool Deleted, bool Archived);

[ApiController]
[Route("api/v1/marketing/templates")]
[HasPermission(Permissions.MarketingManage)]
public sealed class TemplatesController(AppDbContext db, IAuditLogger audit, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<TemplateDto>> List([FromQuery] TemplateListQuery query, CancellationToken ct)
    {
        var q = db.Set<PostTemplate>().AsNoTracking().AsQueryable();
        if (!query.IncludeArchived) q = q.Where(t => !t.IsArchived);
        if (query.Platform is { } p) q = q.Where(t => t.Platform == p || t.Platform == null);
        if (!string.IsNullOrWhiteSpace(query.LanguageCode)) q = q.Where(t => t.LanguageCode == query.LanguageCode);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search);
            q = q.Where(t => EF.Functions.Like(t.Name, like) || EF.Functions.Like(t.Body, like) || EF.Functions.Like(t.Hashtags!, like));
        }
        q = (query.Sort?.ToLowerInvariant(), query.Desc) switch
        {
            ("name", true) => q.OrderByDescending(t => t.Name),
            ("name", false) => q.OrderBy(t => t.Name),
            (_, false) => q.OrderBy(t => t.UpdatedAt),
            _ => q.OrderByDescending(t => t.UpdatedAt),
        };
        return await Project(q).ToPagedAsync(query, ct);
    }

    [HttpGet("{id:guid}")]
    public async Task<TemplateDto> Get(Guid id, CancellationToken ct) =>
        await Project(db.Set<PostTemplate>().AsNoTracking().Where(t => t.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw DomainException.NotFound("Template");

    [HttpPost]
    public async Task<ActionResult<TemplateDto>> Create(TemplateRequest request, CancellationToken ct)
    {
        var template = new PostTemplate { CreatedByUserId = currentUser.Id };
        Apply(template, request);
        db.Set<PostTemplate>().Add(template);
        audit.Record("template.created", nameof(PostTemplate), template.Id, after: new { template.Name, template.Platform });
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = template.Id }, await Get(template.Id, ct));
    }

    [HttpPut("{id:guid}")]
    public async Task<TemplateDto> Update(Guid id, TemplateRequest request, CancellationToken ct)
    {
        var template = await db.Set<PostTemplate>().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw DomainException.NotFound("Template");
        var before = new { template.Name, template.Platform, template.IsArchived };
        Apply(template, request);
        audit.Record("template.updated", nameof(PostTemplate), id, before, new { template.Name, template.Platform, template.IsArchived });
        await db.SaveChangesAsync(ct);
        return await Get(id, ct);
    }

    /// <summary>Deletes an unused template; a template referenced by campaign assets or calendar entries is archived instead.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<TemplateDeleteResult> Delete(Guid id, CancellationToken ct)
    {
        var template = await db.Set<PostTemplate>().FirstOrDefaultAsync(t => t.Id == id, ct) ?? throw DomainException.NotFound("Template");
        var referenced = await db.Set<CampaignAsset>().AnyAsync(a => a.TemplateId == id, ct) ||
                         await db.Set<ContentCalendarEntry>().AnyAsync(e => e.TemplateId == id, ct);
        if (referenced)
        {
            template.IsArchived = true;
            audit.Record("template.archived", nameof(PostTemplate), id, before: new { template.Name }, reason: "Referenced by campaign content");
            await db.SaveChangesAsync(ct);
            return new TemplateDeleteResult(false, true);
        }
        db.Set<PostTemplate>().Remove(template);
        audit.Record("template.deleted", nameof(PostTemplate), id, before: new { template.Name });
        await db.SaveChangesAsync(ct);
        return new TemplateDeleteResult(true, false);
    }

    private IQueryable<TemplateDto> Project(IQueryable<PostTemplate> q) => q.Select(t => new TemplateDto(
        t.Id, t.Name, t.Platform, t.Body, t.Hashtags, t.LanguageCode, t.IsArchived,
        db.Set<CampaignAsset>().Count(a => a.TemplateId == t.Id), t.CreatedAt, t.UpdatedAt));

    private static void Apply(PostTemplate t, TemplateRequest r)
    {
        t.Name = r.Name.Trim();
        t.Platform = r.Platform;
        t.Body = r.Body.Trim();
        t.Hashtags = string.IsNullOrWhiteSpace(r.Hashtags) ? null : r.Hashtags.Trim();
        t.LanguageCode = string.IsNullOrWhiteSpace(r.LanguageCode) ? null : r.LanguageCode.Trim();
        t.IsArchived = r.IsArchived;
    }
}

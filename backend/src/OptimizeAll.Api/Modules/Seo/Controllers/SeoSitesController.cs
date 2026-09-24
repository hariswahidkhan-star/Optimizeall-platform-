using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Seo.Http;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Seo;
using OptimizeAll.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace OptimizeAll.Api.Modules.Seo.Controllers;

public sealed class SiteRequest
{
    [Required]
    public Guid? ClientAccountId { get; set; }

    [Required, MinLength(2), MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Domain or URL (e.g. "www.example.com" or "https://example.com").</summary>
    [Required, MaxLength(300)]
    public string Domain { get; set; } = string.Empty;

    [RegularExpression("^https?$", ErrorMessage = "Protocol is http or https.")]
    public string? Protocol { get; set; }

    [MaxLength(1000)]
    public string? SitemapUrl { get; set; }

    [Required, RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a two-letter country code.")]
    public string TargetCountry { get; set; } = "US";

    [Required, RegularExpression("^[A-Za-z]{2,3}(-[A-Za-z]{2})?$", ErrorMessage = "Use a language code such as en or en-GB.")]
    public string TargetLanguage { get; set; } = "en";

    [MaxLength(10)]
    public List<string> Competitors { get; set; } = new();

    [Range(1, 2000)]
    public int? MaxPages { get; set; }

    [Range(1, 20)]
    public int? MaxDepth { get; set; }

    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record SiteDto(
    Guid Id, Guid ClientAccountId, string ClientName, string Name, string Domain, string Protocol, string BaseUrl, string? SitemapUrl,
    string TargetCountry, string TargetLanguage, IReadOnlyList<string> Competitors, int MaxPages, int MaxDepth, bool IsArchived,
    int? HealthScore, DateTime? LastAuditAt, SeoAuditStatus? LastAuditStatus, int KeywordCount, Guid ConcurrencyStamp,
    DateTime CreatedAt, DateTime UpdatedAt);

public sealed class SiteListQuery : PageQuery
{
    public Guid? ClientId { get; set; }
    public bool IncludeArchived { get; set; }
}

/// <summary>SEO sites per client (staff, seo.manage).</summary>
[ApiController]
[HasPermission(Permissions.SeoManage)]
[Route("api/v1/agency/seo")]
public sealed class SeoSitesController(
    AppDbContext db, SeoAccess access, IClientScope scope, IAuditLogger audit, IOptionsMonitor<SeoCrawlerOptions> crawlerOptions)
    : ControllerBase
{
    [HttpGet("client-options")]
    public Task<List<ClientOptionDto>> ClientOptions(CancellationToken ct) => access.ClientOptionsAsync(ct);

    [HttpGet("sites")]
    public async Task<PagedResult<SiteDto>> List([FromQuery] SiteListQuery query, CancellationToken ct)
    {
        var q = await scope.ApplyAsync(db.Set<SeoSite>().AsNoTracking(), s => s.ClientAccountId, ct);
        if (query.ClientId is { } clientId) q = q.Where(s => s.ClientAccountId == clientId);
        if (!query.IncludeArchived) q = q.Where(s => !s.IsArchived);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search);
            q = q.Where(s => EF.Functions.Like(s.Name, like, "\\") || EF.Functions.Like(s.Domain, like, "\\"));
        }
        var page = await q.OrderBy(s => s.Name).ThenBy(s => s.Id).ToPagedAsync(query, ct);
        return new PagedResult<SiteDto>(await ToDtosAsync(page.Items, ct), page.Total, page.Page, page.PageSize);
    }

    [HttpGet("sites/{id:guid}")]
    public async Task<SiteDto> Get(Guid id, CancellationToken ct) => (await ToDtosAsync(new[] { await access.SiteAsync(id, ct) }, ct))[0];

    [HttpPost("sites")]
    public async Task<ActionResult<SiteDto>> Create(SiteRequest request, CancellationToken ct)
    {
        await access.EnsureAsync(request.ClientAccountId!.Value, "Client", ct);
        var site = new SeoSite { ClientAccountId = request.ClientAccountId.Value };
        Apply(site, request);
        if (await db.Set<SeoSite>().AnyAsync(s => s.ClientAccountId == site.ClientAccountId && s.Domain == site.Domain, ct))
            throw DomainException.Conflict("seo.site_exists", "This client already has a site with that domain.");
        db.Set<SeoSite>().Add(site);
        audit.Record("seo.site_created", nameof(SeoSite), site.Id, after: Snapshot(site));
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = site.Id }, (await ToDtosAsync(new[] { site }, ct))[0]);
    }

    [HttpPut("sites/{id:guid}")]
    public async Task<SiteDto> Update(Guid id, SiteRequest request, CancellationToken ct)
    {
        var site = await access.SiteAsync(id, ct, tracked: true);
        if (request.ClientAccountId != site.ClientAccountId)
            throw new DomainException("seo.client_immutable", "A site cannot be moved to another client.");
        Stamp(site, request.ConcurrencyStamp);
        var before = Snapshot(site);
        Apply(site, request);
        if (await db.Set<SeoSite>().AnyAsync(s => s.Id != site.Id && s.ClientAccountId == site.ClientAccountId && s.Domain == site.Domain, ct))
            throw DomainException.Conflict("seo.site_exists", "This client already has a site with that domain.");
        audit.Record("seo.site_updated", nameof(SeoSite), site.Id, before, Snapshot(site));
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync(new[] { site }, ct))[0];
    }

    /// <summary>Archives the site (history is kept; audits and rank tracking stop).</summary>
    [HttpDelete("sites/{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        var site = await access.SiteAsync(id, ct, tracked: true);
        if (!site.IsArchived)
        {
            site.IsArchived = true;
            audit.Record("seo.site_archived", nameof(SeoSite), site.Id, after: Snapshot(site));
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    [HttpPost("sites/{id:guid}/restore")]
    public async Task<SiteDto> Restore(Guid id, CancellationToken ct)
    {
        var site = await access.SiteAsync(id, ct, tracked: true);
        if (site.IsArchived)
        {
            site.IsArchived = false;
            audit.Record("seo.site_restored", nameof(SeoSite), site.Id, after: Snapshot(site));
            await db.SaveChangesAsync(ct);
        }
        return (await ToDtosAsync(new[] { site }, ct))[0];
    }

    private void Apply(SeoSite site, SiteRequest r)
    {
        var errors = new Dictionary<string, string[]>();
        var normalized = SeoAccess.NormalizeDomain(r.Domain);
        if (normalized is null) errors["domain"] = new[] { "Enter a domain such as example.com (optionally with https://)." };
        if (!string.IsNullOrWhiteSpace(r.SitemapUrl) &&
            (!Uri.TryCreate(r.SitemapUrl.Trim(), UriKind.Absolute, out var sm) || SafeHttpFetcher.ValidateUrl(sm) is not null))
            errors["sitemapUrl"] = new[] { "The sitemap URL must be an absolute http(s) URL." };
        var competitors = new List<string>();
        foreach (var c in r.Competitors.Where(c => !string.IsNullOrWhiteSpace(c)))
        {
            var n = SeoAccess.NormalizeDomain(c);
            if (n is null) { errors["competitors"] = new[] { $"'{(c.Length > 60 ? c[..60] : c)}' is not a valid domain." }; break; }
            competitors.Add(RankMath.BareHost(n.Value.Domain));
        }
        var o = crawlerOptions.CurrentValue;
        if (r.MaxPages > o.AbsoluteMaxPages) errors["maxPages"] = new[] { $"At most {o.AbsoluteMaxPages} pages per audit." };
        if (errors.Count > 0) throw new DomainException("validation.failed", "Some fields are invalid.", DomainErrorKind.Validation, errors);

        site.Name = r.Name.Trim();
        site.Domain = normalized!.Value.Domain;
        site.Protocol = r.Protocol ?? normalized.Value.Protocol ?? "https";
        site.SitemapUrl = string.IsNullOrWhiteSpace(r.SitemapUrl) ? null : r.SitemapUrl.Trim();
        site.TargetCountry = r.TargetCountry.ToUpperInvariant();
        site.TargetLanguage = r.TargetLanguage;
        site.Competitors = competitors.Distinct().Where(c => c != RankMath.BareHost(site.Domain)).ToList();
        site.MaxPages = r.MaxPages ?? o.DefaultMaxPages;
        site.MaxDepth = r.MaxDepth ?? 10;
    }

    private void Stamp(SeoSite site, Guid? stamp)
    {
        if (stamp is null) return;
        if (stamp != site.ConcurrencyStamp)
            throw DomainException.Conflict("concurrency.conflict", "This site was changed by someone else. Reload and try again.");
        db.Entry(site).Property(s => s.ConcurrencyStamp).OriginalValue = stamp.Value;
    }

    private static object Snapshot(SeoSite s) => new
    {
        s.Name, s.Domain, s.Protocol, s.SitemapUrl, s.TargetCountry, s.TargetLanguage, s.Competitors, s.MaxPages, s.MaxDepth, s.IsArchived,
    };

    private async Task<List<SiteDto>> ToDtosAsync(IReadOnlyCollection<SeoSite> sites, CancellationToken ct)
    {
        var ids = sites.Select(s => s.Id).ToList();
        var clientIds = sites.Select(s => s.ClientAccountId).Distinct().ToList();
        var clients = await db.Set<ClientAccount>().AsNoTracking().Where(c => clientIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var audits = (await db.Set<SeoAudit>().AsNoTracking().Where(a => ids.Contains(a.SiteId))
                .Select(a => new { a.SiteId, a.Status, a.HealthScore, a.QueuedAt, a.FinishedAt }).ToListAsync(ct))
            .GroupBy(a => a.SiteId).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.QueuedAt).ToList());
        var keywordCounts = await db.Set<SeoKeyword>().AsNoTracking().Where(k => ids.Contains(k.SiteId))
            .GroupBy(k => k.SiteId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return sites.Select(s =>
        {
            var list = audits.GetValueOrDefault(s.Id);
            var latest = list?.FirstOrDefault();
            var completed = list?.FirstOrDefault(a => a.Status == SeoAuditStatus.Completed);
            return new SiteDto(s.Id, s.ClientAccountId, clients.GetValueOrDefault(s.ClientAccountId, string.Empty), s.Name, s.Domain, s.Protocol,
                s.BaseUrl, s.SitemapUrl, s.TargetCountry, s.TargetLanguage, s.Competitors, s.MaxPages, s.MaxDepth, s.IsArchived,
                completed?.HealthScore, completed?.FinishedAt ?? latest?.QueuedAt, latest?.Status, keywordCounts.GetValueOrDefault(s.Id),
                s.ConcurrencyStamp, s.CreatedAt, s.UpdatedAt);
        }).ToList();
    }
}

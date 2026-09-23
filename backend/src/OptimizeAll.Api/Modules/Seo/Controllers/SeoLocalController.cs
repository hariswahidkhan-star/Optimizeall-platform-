using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Seo;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Seo.Controllers;

public sealed class LocalProfileRequest
{
    [Required, MaxLength(200)]
    public string BusinessName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(40)]
    public string? Phone { get; set; }

    [MaxLength(500)]
    public string? Website { get; set; }

    public List<string> CompletedChecklist { get; set; } = new();
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class CitationRequest
{
    public CitationStatus Status { get; set; }

    [MaxLength(1000)]
    public string? ListingUrl { get; set; }

    [MaxLength(200)]
    public string? ListedName { get; set; }

    [MaxLength(500)]
    public string? ListedAddress { get; set; }

    [MaxLength(40)]
    public string? ListedPhone { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }
}

public sealed class ReviewRequest
{
    [Required, MaxLength(60)]
    public string Platform { get; set; } = string.Empty;

    [Range(1, 5)]
    public int Rating { get; set; }

    [MaxLength(150)]
    public string? AuthorName { get; set; }

    [MaxLength(4000)]
    public string? Text { get; set; }

    [Required]
    public DateTime? ReviewedAt { get; set; }

    public bool Responded { get; set; }

    [MaxLength(4000)]
    public string? ResponseText { get; set; }
}

public sealed record ChecklistItemDto(string Key, string Title, string Guidance, bool Done);

public sealed record CitationDto(
    Guid SourceId, string SourceKey, string Name, string Url, string Category, IReadOnlyList<string> Countries, CitationStatus Status,
    string? ListingUrl, string? ListedName, string? ListedAddress, string? ListedPhone, string? Notes, bool? NameMatches, bool? AddressMatches,
    bool? PhoneMatches, bool Consistent, DateTime? UpdatedAt);

public sealed record LocalProfileDto(string BusinessName, string? Address, string? Phone, string? Website, Guid ConcurrencyStamp);

public sealed record ReviewDto(
    Guid Id, string Platform, int Rating, string? AuthorName, string? Text, DateTime ReviewedAt, bool Responded, string? ResponseText);

public sealed record ReviewSummaryDto(int Count, double? AverageRating, double? ResponseRate, IReadOnlyDictionary<int, int> ByRating);

public sealed record LocalSeoDto(
    Guid SiteId, LocalProfileDto? Profile, IReadOnlyList<ChecklistItemDto> Checklist, int ChecklistDone, IReadOnlyList<CitationDto> Citations,
    int CitationsLive, int CitationsInconsistent, ReviewSummaryDto Reviews);

public sealed class BriefRequest
{
    [Required, MinLength(2), MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string TargetKeyword { get; set; } = string.Empty;

    [MaxLength(50)]
    public List<string> RelatedKeywords { get; set; } = new();

    [MaxLength(30)]
    public List<string> Questions { get; set; } = new();

    [MaxLength(60)]
    public List<string> Outline { get; set; } = new();

    [Range(100, 20000)]
    public int WordCountTarget { get; set; } = 1200;

    [MaxLength(10)]
    public List<string> CompetitorUrls { get; set; } = new();

    [MaxLength(8000)]
    public string? Notes { get; set; }

    public ContentBriefStatus Status { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record BriefDto(
    Guid Id, Guid SiteId, string Title, string TargetKeyword, IReadOnlyList<string> RelatedKeywords, IReadOnlyList<string> Questions,
    IReadOnlyList<string> Outline, int WordCountTarget, IReadOnlyList<string> CompetitorUrls, string? Notes, ContentBriefStatus Status,
    Guid ConcurrencyStamp, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record HandoffDto(BriefDto Brief, bool TaskCreated, string Message, string Markdown);

/// <summary>Local SEO (Business Profile checklist, NAP citations, reviews) and SEO content briefs.</summary>
[ApiController]
[HasPermission(Permissions.SeoManage)]
[Route("api/v1/agency/seo")]
public sealed class SeoLocalController(AppDbContext db, SeoAccess access, IAuditLogger audit, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("sites/{siteId:guid}/local")]
    public async Task<LocalSeoDto> Local(Guid siteId, CancellationToken ct)
    {
        await access.SiteAsync(siteId, ct);
        return await BuildAsync(siteId, ct);
    }

    [HttpPut("sites/{siteId:guid}/local/profile")]
    public async Task<LocalSeoDto> SaveProfile(Guid siteId, LocalProfileRequest request, CancellationToken ct)
    {
        var site = await access.SiteAsync(siteId, ct);
        var profile = await db.Set<SeoLocalProfile>().FirstOrDefaultAsync(p => p.SiteId == siteId, ct);
        if (profile is null)
        {
            profile = new SeoLocalProfile { SiteId = site.Id, ClientAccountId = site.ClientAccountId };
            db.Add(profile);
        }
        else if (request.ConcurrencyStamp is { } stamp)
        {
            if (stamp != profile.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "The profile was changed by someone else. Reload and try again.");
            db.Entry(profile).Property(p => p.ConcurrencyStamp).OriginalValue = stamp;
        }
        var known = LocalSeoCatalog.GbpChecklist.Select(i => i.Key).ToHashSet();
        var unknown = request.CompletedChecklist.FirstOrDefault(k => !known.Contains(k));
        if (unknown is not null) throw new DomainException("seo.checklist_unknown", $"Unknown checklist item '{unknown}'.");
        profile.BusinessName = request.BusinessName.Trim();
        profile.Address = request.Address?.Trim();
        profile.Phone = request.Phone?.Trim();
        profile.Website = request.Website?.Trim();
        profile.CompletedChecklist = request.CompletedChecklist.Distinct().ToList();
        await db.SaveChangesAsync(ct);
        return await BuildAsync(siteId, ct);
    }

    [HttpPut("sites/{siteId:guid}/local/citations/{sourceId:guid}")]
    public async Task<CitationDto> SaveCitation(Guid siteId, Guid sourceId, CitationRequest request, CancellationToken ct)
    {
        var site = await access.SiteAsync(siteId, ct);
        var source = await db.Set<SeoCitationSource>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == sourceId, ct) ?? throw DomainException.NotFound("Directory");
        if (!string.IsNullOrWhiteSpace(request.ListingUrl) && (!Uri.TryCreate(request.ListingUrl.Trim(), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")))
            throw new DomainException("validation.failed", "The listing URL must be an absolute http(s) URL.");
        var row = await db.Set<SeoCitation>().FirstOrDefaultAsync(c => c.SiteId == siteId && c.SourceId == sourceId, ct);
        if (row is null)
        {
            row = new SeoCitation { SiteId = site.Id, ClientAccountId = site.ClientAccountId, SourceId = sourceId };
            db.Add(row);
        }
        row.Status = request.Status;
        row.ListingUrl = request.ListingUrl?.Trim();
        row.ListedName = request.ListedName?.Trim();
        row.ListedAddress = request.ListedAddress?.Trim();
        row.ListedPhone = request.ListedPhone?.Trim();
        row.Notes = request.Notes?.Trim();
        await db.SaveChangesAsync(ct);
        var profile = await db.Set<SeoLocalProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.SiteId == siteId, ct);
        return ToCitation(source, row, profile);
    }

    [HttpGet("sites/{siteId:guid}/reviews")]
    public async Task<List<ReviewDto>> Reviews(Guid siteId, CancellationToken ct)
    {
        await access.SiteAsync(siteId, ct);
        return await db.Set<SeoReview>().AsNoTracking().Where(r => r.SiteId == siteId).OrderByDescending(r => r.ReviewedAt)
            .Select(r => new ReviewDto(r.Id, r.Platform, r.Rating, r.AuthorName, r.Text, r.ReviewedAt, r.Responded, r.ResponseText)).ToListAsync(ct);
    }

    [HttpPost("sites/{siteId:guid}/reviews")]
    public async Task<ReviewDto> AddReview(Guid siteId, ReviewRequest request, CancellationToken ct)
    {
        var site = await access.SiteAsync(siteId, ct);
        var row = new SeoReview { SiteId = site.Id, ClientAccountId = site.ClientAccountId };
        Apply(row, request);
        db.Add(row);
        await db.SaveChangesAsync(ct);
        return ToReview(row);
    }

    [HttpPut("reviews/{id:guid}")]
    public async Task<ReviewDto> UpdateReview(Guid id, ReviewRequest request, CancellationToken ct)
    {
        var row = await access.OwnedAsync<SeoReview>(id, r => r.ClientAccountId, "Review", ct);
        Apply(row, request);
        await db.SaveChangesAsync(ct);
        return ToReview(row);
    }

    [HttpDelete("reviews/{id:guid}")]
    public async Task<IActionResult> DeleteReview(Guid id, CancellationToken ct)
    {
        var row = await access.OwnedAsync<SeoReview>(id, r => r.ClientAccountId, "Review", ct);
        db.Remove(row);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ---------------------------------------------------------------- Content briefs

    [HttpGet("sites/{siteId:guid}/briefs")]
    public async Task<List<BriefDto>> Briefs(Guid siteId, CancellationToken ct)
    {
        await access.SiteAsync(siteId, ct);
        return (await db.Set<SeoContentBrief>().AsNoTracking().Where(b => b.SiteId == siteId).OrderByDescending(b => b.UpdatedAt).ToListAsync(ct))
            .Select(ToBrief).ToList();
    }

    [HttpPost("sites/{siteId:guid}/briefs")]
    public async Task<BriefDto> CreateBrief(Guid siteId, BriefRequest request, CancellationToken ct)
    {
        var site = await access.SiteAsync(siteId, ct);
        var row = new SeoContentBrief { SiteId = site.Id, ClientAccountId = site.ClientAccountId, CreatedByUserId = currentUser.Id };
        Apply(row, request);
        db.Add(row);
        audit.Record("seo.brief_created", nameof(SeoContentBrief), row.Id, after: new { row.Title, row.TargetKeyword });
        await db.SaveChangesAsync(ct);
        return ToBrief(row);
    }

    [HttpGet("briefs/{id:guid}")]
    public async Task<BriefDto> GetBrief(Guid id, CancellationToken ct) =>
        ToBrief(await access.OwnedAsync<SeoContentBrief>(id, b => b.ClientAccountId, "Brief", ct, tracked: false));

    [HttpPut("briefs/{id:guid}")]
    public async Task<BriefDto> UpdateBrief(Guid id, BriefRequest request, CancellationToken ct)
    {
        var row = await access.OwnedAsync<SeoContentBrief>(id, b => b.ClientAccountId, "Brief", ct);
        if (request.ConcurrencyStamp is { } stamp)
        {
            if (stamp != row.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "This brief was changed by someone else. Reload and try again.");
            db.Entry(row).Property(b => b.ConcurrencyStamp).OriginalValue = stamp;
        }
        Apply(row, request);
        await db.SaveChangesAsync(ct);
        return ToBrief(row);
    }

    [HttpDelete("briefs/{id:guid}")]
    public async Task<IActionResult> DeleteBrief(Guid id, CancellationToken ct)
    {
        var row = await access.OwnedAsync<SeoContentBrief>(id, b => b.ClientAccountId, "Brief", ct);
        db.Remove(row);
        audit.Record("seo.brief_deleted", nameof(SeoContentBrief), id, before: new { row.Title });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("briefs/{id:guid}/export.md")]
    public async Task<IActionResult> ExportBrief(Guid id, CancellationToken ct)
    {
        var row = await access.OwnedAsync<SeoContentBrief>(id, b => b.ClientAccountId, "Brief", ct, tracked: false);
        var site = await db.Set<SeoSite>().AsNoTracking().FirstAsync(s => s.Id == row.SiteId, ct);
        return File(Encoding.UTF8.GetBytes(Markdown(row, site)), "text/markdown; charset=utf-8", $"brief-{Slug(row.Title)}.md");
    }

    /// <summary>
    /// Hands the brief to the content team. The Projects module exposes no task API in this build, so the brief is marked
    /// "handed off" and its Markdown export is returned for attaching to a project task.
    /// </summary>
    [HttpPost("briefs/{id:guid}/handoff")]
    public async Task<HandoffDto> Handoff(Guid id, CancellationToken ct)
    {
        var row = await access.OwnedAsync<SeoContentBrief>(id, b => b.ClientAccountId, "Brief", ct);
        var site = await db.Set<SeoSite>().AsNoTracking().FirstAsync(s => s.Id == row.SiteId, ct);
        row.Status = ContentBriefStatus.HandedOff;
        audit.Record("seo.brief_handed_off", nameof(SeoContentBrief), row.Id, after: new { row.Title });
        await db.SaveChangesAsync(ct);
        return new HandoffDto(ToBrief(row), false,
            "Brief marked as handed off. Task creation is not available in this build — attach the exported brief to the project task.",
            Markdown(row, site));
    }

    public static string Markdown(SeoContentBrief b, SeoSite site)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {b.Title}").AppendLine();
        sb.AppendLine($"- **Site:** {site.Name} ({site.BaseUrl})");
        sb.AppendLine($"- **Target keyword:** {b.TargetKeyword}");
        sb.AppendLine($"- **Word count target:** {b.WordCountTarget}");
        sb.AppendLine($"- **Status:** {b.Status}").AppendLine();
        if (b.RelatedKeywords.Count > 0) { sb.AppendLine("## Related keywords"); foreach (var k in b.RelatedKeywords) sb.AppendLine($"- {k}"); sb.AppendLine(); }
        if (b.Questions.Count > 0) { sb.AppendLine("## Questions to answer"); foreach (var q in b.Questions) sb.AppendLine($"- {q}"); sb.AppendLine(); }
        if (b.Outline.Count > 0)
        {
            sb.AppendLine("## Outline");
            foreach (var line in b.Outline)
                sb.AppendLine(line.StartsWith('#') ? "#" + line : $"- {line}");
            sb.AppendLine();
        }
        if (b.CompetitorUrls.Count > 0) { sb.AppendLine("## Competitor pages to beat"); foreach (var u in b.CompetitorUrls) sb.AppendLine($"- {u}"); sb.AppendLine(); }
        if (!string.IsNullOrWhiteSpace(b.Notes)) sb.AppendLine("## Notes").AppendLine(b.Notes);
        return sb.ToString();
    }

    private async Task<LocalSeoDto> BuildAsync(Guid siteId, CancellationToken ct)
    {
        var profile = await db.Set<SeoLocalProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.SiteId == siteId, ct);
        var sources = await db.Set<SeoCitationSource>().AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        var citations = await db.Set<SeoCitation>().AsNoTracking().Where(c => c.SiteId == siteId).ToDictionaryAsync(c => c.SourceId, ct);
        var reviews = await db.Set<SeoReview>().AsNoTracking().Where(r => r.SiteId == siteId).Select(r => new { r.Rating, r.Responded }).ToListAsync(ct);
        var done = profile?.CompletedChecklist.ToHashSet() ?? new HashSet<string>();
        var list = sources.Select(s => ToCitation(s, citations.GetValueOrDefault(s.Id), profile)).ToList();
        return new LocalSeoDto(siteId,
            profile is null ? null : new LocalProfileDto(profile.BusinessName, profile.Address, profile.Phone, profile.Website, profile.ConcurrencyStamp),
            LocalSeoCatalog.GbpChecklist.Select(i => new ChecklistItemDto(i.Key, i.Title, i.Guidance, done.Contains(i.Key))).ToList(),
            LocalSeoCatalog.GbpChecklist.Count(i => done.Contains(i.Key)), list,
            list.Count(c => c.Status == CitationStatus.Live), list.Count(c => !c.Consistent),
            new ReviewSummaryDto(reviews.Count, reviews.Count == 0 ? null : Math.Round(reviews.Average(r => r.Rating), 2),
                reviews.Count == 0 ? null : Math.Round((double)reviews.Count(r => r.Responded) / reviews.Count, 4),
                Enumerable.Range(1, 5).ToDictionary(i => i, i => reviews.Count(r => r.Rating == i))));
    }

    private static CitationDto ToCitation(SeoCitationSource s, SeoCitation? c, SeoLocalProfile? p)
    {
        var nap = LocalSeoCatalog.Compare(p?.BusinessName, p?.Address, p?.Phone, c?.ListedName, c?.ListedAddress, c?.ListedPhone);
        return new CitationDto(s.Id, s.Key, s.Name, s.Url, s.Category, s.Countries, c?.Status ?? CitationStatus.NotStarted, c?.ListingUrl,
            c?.ListedName, c?.ListedAddress, c?.ListedPhone, c?.Notes, nap.NameMatches, nap.AddressMatches, nap.PhoneMatches, nap.Consistent, c?.UpdatedAt);
    }

    private static void Apply(SeoReview row, ReviewRequest r)
    {
        row.Platform = r.Platform.Trim();
        row.Rating = r.Rating;
        row.AuthorName = r.AuthorName?.Trim();
        row.Text = r.Text?.Trim();
        row.ReviewedAt = r.ReviewedAt!.Value.ToUniversalTime();
        row.Responded = r.Responded || !string.IsNullOrWhiteSpace(r.ResponseText);
        row.ResponseText = r.ResponseText?.Trim();
    }

    private static void Apply(SeoContentBrief row, BriefRequest r)
    {
        static List<string> Clean(IEnumerable<string> items, int max) =>
            items.Select(i => i.Trim()).Where(i => i.Length > 0).Select(i => i.Length > max ? i[..max] : i).Distinct().ToList();
        var urls = Clean(r.CompetitorUrls, 1000);
        if (urls.Any(u => !Uri.TryCreate(u, UriKind.Absolute, out var x) || x.Scheme is not ("http" or "https")))
            throw new DomainException("validation.failed", "Competitor URLs must be absolute http(s) URLs.");
        row.Title = r.Title.Trim();
        row.TargetKeyword = r.TargetKeyword.Trim();
        row.RelatedKeywords = Clean(r.RelatedKeywords, 200);
        row.Questions = Clean(r.Questions, 300);
        row.Outline = r.Outline.Select(i => i.TrimEnd()).Where(i => i.Trim().Length > 0).Select(i => i.Length > 300 ? i[..300] : i).ToList();
        row.WordCountTarget = r.WordCountTarget;
        row.CompetitorUrls = urls;
        row.Notes = r.Notes?.Trim();
        row.Status = r.Status;
    }

    private static ReviewDto ToReview(SeoReview r) => new(r.Id, r.Platform, r.Rating, r.AuthorName, r.Text, r.ReviewedAt, r.Responded, r.ResponseText);

    private static BriefDto ToBrief(SeoContentBrief b) => new(b.Id, b.SiteId, b.Title, b.TargetKeyword, b.RelatedKeywords, b.Questions, b.Outline,
        b.WordCountTarget, b.CompetitorUrls, b.Notes, b.Status, b.ConcurrencyStamp, b.CreatedAt, b.UpdatedAt);

    private static string Slug(string title)
    {
        var slug = new string(title.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-");
        return slug.Length == 0 ? "brief" : slug.Length > 60 ? slug[..60] : slug;
    }
}

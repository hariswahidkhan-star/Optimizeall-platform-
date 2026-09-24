using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Files;
using OptimizeAll.Api.Modules.Seo;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Files;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.LandingPages.Controllers;

public sealed class CreatePageRequest
{
    [Required]
    public Guid? ClientAccountId { get; set; }

    [Required, MinLength(2), MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? Slug { get; set; }

    [MaxLength(60)]
    public string? TemplateKey { get; set; }

    /// <summary>Form to place in the template's form block; when null and <see cref="CreateForm"/> is true, the template's form is created.</summary>
    public Guid? FormId { get; set; }

    public bool CreateForm { get; set; } = true;
}

public sealed class UpdatePageRequest
{
    [Required, MinLength(2), MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? MetaTitle { get; set; }

    [MaxLength(320)]
    public string? MetaDescription { get; set; }

    [MaxLength(1000)]
    public string? OgImageUrl { get; set; }

    public bool NoIndex { get; set; }
    public bool ExperimentEnabled { get; set; }

    /// <summary>Variants array: [{ key: "A", name, weight, blocks: [{ id, type, props }] }].</summary>
    public JsonElement Variants { get; set; }

    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record PageListItemDto(
    Guid Id, Guid ClientAccountId, string ClientName, string ClientSlug, string Name, string Slug, LandingPageStatus Status, bool HasUnpublishedChanges,
    bool ExperimentEnabled, int VariantCount, DateTime? PublishedAt, string PublicPath, DateTime UpdatedAt);

public sealed record PageDetailDto(
    Guid Id, Guid ClientAccountId, string ClientName, string ClientSlug, string Name, string Slug, LandingPageStatus Status, string? MetaTitle,
    string? MetaDescription, string? OgImageUrl, bool NoIndex, string? TemplateKey, JsonElement Variants, bool ExperimentEnabled,
    Guid ExperimentId, DateTime? ExperimentStartedAt, Guid? PublishedVersionId, int? PublishedVersion, DateTime? PublishedAt,
    bool HasUnpublishedChanges, string PublicPath, Guid ConcurrencyStamp, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record VersionDto(Guid Id, int Version, DateTime PublishedAt, Guid? PublishedByUserId, string ContentHash, bool IsCurrent);

public sealed record VersionDetailDto(VersionDto Version, PageSnapshot Snapshot);

public sealed record PageTemplateDto(
    string Key, string Name, string Category, string Description, string MetaTitle, string MetaDescription, string? FormTemplateKey, JsonElement Blocks);

public sealed record VariantStatsDto(
    string Key, string Name, int Weight, int Views, int UniqueVisitors, int Assigned, int Submissions, double? ConversionRate,
    double? AbsoluteLift, double? RelativeLift, double? PValue, bool Significant, string Note);

public sealed record DailyStatsDto(DateOnly Date, int Views, int Submissions);

public sealed record PageAnalyticsDto(
    Guid PageId, DateTime From, DateTime To, int Views, int UniqueVisitors, int Submissions, double? ConversionRate, bool ExperimentEnabled,
    Guid ExperimentId, DateTime? ExperimentStartedAt, IReadOnlyList<VariantStatsDto> Variants, IReadOnlyList<DailyStatsDto> Daily, string Method);

public sealed class PageListQuery : PageQuery
{
    public Guid? ClientId { get; set; }
    public LandingPageStatus? Status { get; set; }
}

/// <summary>Landing-page builder API (forms.manage): drafts, immutable publish snapshots, templates, A/B tests and analytics.</summary>
[ApiController]
[HasPermission(Permissions.FormsManage)]
[Route("api/v1/agency/pages")]
public sealed class LandingPagesController(
    AppDbContext db, SeoAccess access, IClientScope scope, LandingPageService pages, FormService forms, ImageUrlPolicy images,
    IDatabaseDialect dialect, IAuditLogger audit, ICurrentUser currentUser, IFileService files, TimeProvider clock) : ControllerBase
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    [HttpGet("client-options")]
    public Task<List<ClientOptionDto>> ClientOptions(CancellationToken ct) => access.ClientOptionsAsync(ct);

    [HttpGet("templates")]
    public async Task<List<PageTemplateDto>> Templates(CancellationToken ct)
    {
        var rows = await db.Set<LandingPageTemplate>().AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct);
        return rows.Select(t =>
        {
            using var doc = JsonDocument.Parse(t.BlocksJson);
            return new PageTemplateDto(t.Key, t.Name, t.Category, t.Description, t.MetaTitle, t.MetaDescription, t.FormTemplateKey, doc.RootElement.Clone());
        }).ToList();
    }

    /// <summary>Uploads a public image for pages (hero, logos, OG image). Returns the /api/v1/files/{id} URL.</summary>
    [HttpPost("images")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<StoredFileDto> UploadImage(IFormFile file, CancellationToken ct)
    {
        if (file is null) throw new DomainException("file.required", "Choose an image to upload.");
        var stored = await files.SaveImageAsync(file, FilePurpose.ContentImage, currentUser.Id, isPublic: true, ct);
        audit.Record("landing.image_uploaded", "StoredFile", stored.Id, after: new { stored.ContentType, stored.SizeBytes });
        await db.SaveChangesAsync(ct);
        return StoredFileDto.From(stored);
    }

    [HttpGet("landing-pages")]
    public async Task<PagedResult<PageListItemDto>> List([FromQuery] PageListQuery query, CancellationToken ct)
    {
        var q = await scope.ApplyAsync(db.Set<LandingPage>().AsNoTracking(), p => p.ClientAccountId, ct);
        if (query.ClientId is { } c) q = q.Where(p => p.ClientAccountId == c);
        if (query.Status is { } s) q = q.Where(p => p.Status == s);
        else q = q.Where(p => p.Status != LandingPageStatus.Archived);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search);
            q = q.Where(p => EF.Functions.Like(p.Name, like) || EF.Functions.Like(p.Slug, like));
        }
        var page = await q.OrderByDescending(p => p.UpdatedAt).ToPagedAsync(query, ct);
        var clients = await ClientsAsync(page.Items.Select(p => p.ClientAccountId), ct);
        var live = await pages.LiveSlugsAsync(page.Items, ct);
        return new PagedResult<PageListItemDto>(page.Items.Select(p =>
        {
            var client = clients.GetValueOrDefault(p.ClientAccountId);
            using var doc = JsonDocument.Parse(p.VariantsJson);
            return new PageListItemDto(p.Id, p.ClientAccountId, client.Name ?? string.Empty, client.Slug ?? string.Empty, p.Name, p.Slug, p.Status,
                p.HasUnpublishedChanges, p.ExperimentEnabled, doc.RootElement.GetArrayLength(), p.PublishedAt,
                PublicPath(client.Slug, live.GetValueOrDefault(p.Id, p.Slug)), p.UpdatedAt);
        }).ToList(), page.Total, page.Page, page.PageSize);
    }

    [HttpGet("landing-pages/{id:guid}")]
    public async Task<PageDetailDto> Get(Guid id, CancellationToken ct) => await ToDetailAsync(await LoadAsync(id, ct, tracked: false), ct);

    [HttpPost("landing-pages")]
    public async Task<ActionResult<PageDetailDto>> Create(CreatePageRequest request, CancellationToken ct)
    {
        var clientId = request.ClientAccountId!.Value;
        await access.EnsureAsync(clientId, "Client", ct);
        var slug = string.IsNullOrWhiteSpace(request.Slug) ? LandingPageService.Slugify(request.Name) : request.Slug.Trim().ToLowerInvariant();
        if (!LandingPageService.IsValidSlug(slug)) throw SlugError();
        if (await pages.SlugTakenAsync(clientId, slug, null, ct)) throw SlugTaken();

        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
        var page = new LandingPage { ClientAccountId = clientId, Name = request.Name.Trim(), Slug = slug, CreatedByUserId = currentUser.Id };
        JsonElement variants;
        if (request.TemplateKey is { } key)
        {
            // Hidden templates can't be picked any more (the picker lists active ones only).
            var template = await db.Set<LandingPageTemplate>().AsNoTracking().FirstOrDefaultAsync(t => t.Key == key && t.IsActive, ct)
                           ?? throw new DomainException("landing.template_not_found", "Unknown template.");
            var formId = request.FormId;
            if (formId is { } fid)
            {
                if (!await db.Set<Form>().AnyAsync(f => f.Id == fid && f.ClientAccountId == clientId && f.Status == FormStatus.Active, ct))
                    throw new DomainException("landing.form_invalid", "Choose an active form of this client.");
            }
            else if (request.CreateForm && template.FormTemplateKey is { } formTemplate)
            {
                var form = await forms.CreateFromTemplateAsync(clientId, formTemplate, $"{request.Name.Trim()} form", ct);
                formId = form.Id;
            }
            variants = LandingPageService.InstantiateTemplate(template.BlocksJson, formId, Now);
            page.TemplateKey = template.Key;
            page.MetaTitle = template.MetaTitle;
            page.MetaDescription = template.MetaDescription;
        }
        else
        {
            variants = LandingPageService.InstantiateTemplate(JsonSerializer.Serialize(new object[]
            {
                new { id = "hero", type = "hero", props = new { headline = request.Name.Trim(), subheadline = "Describe your offer in one sentence.", align = "center", theme = "brand" } },
            }), null, Now);
        }
        page.VariantsJson = LandingBlocks.Serialize(await pages.ParseVariantsAsync(clientId, variants, ct));
        db.Add(page);
        audit.Record("landing.page_created", nameof(LandingPage), page.Id, after: new { page.Name, page.Slug, page.TemplateKey, page.ClientAccountId });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = page.Id }, await ToDetailAsync(page, ct));
    }

    [HttpPut("landing-pages/{id:guid}")]
    public async Task<PageDetailDto> Update(Guid id, UpdatePageRequest request, CancellationToken ct)
    {
        var page = await LoadAsync(id, ct, tracked: true);
        if (page.Status == LandingPageStatus.Archived) throw DomainException.Conflict("landing.archived", "Restore the page before editing it.");
        if (request.ConcurrencyStamp is { } stamp)
        {
            if (stamp != page.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "This page was changed by someone else. Reload and try again.");
            db.Entry(page).Property(p => p.ConcurrencyStamp).OriginalValue = stamp;
        }
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (!LandingPageService.IsValidSlug(slug)) throw SlugError();
        if (slug != page.Slug && await pages.SlugTakenAsync(page.ClientAccountId, slug, id, ct)) throw SlugTaken();
        var metaErrors = LandingPageService.ValidateMeta(request.OgImageUrl, images);
        if (metaErrors.Count > 0)
            throw new DomainException("landing.invalid_content", "The page settings are invalid.", DomainErrorKind.Validation,
                new Dictionary<string, string[]> { ["ogImageUrl"] = metaErrors.ToArray() });
        var variants = await pages.ParseVariantsAsync(page.ClientAccountId, request.Variants, ct);
        if (request.ExperimentEnabled && variants.Count < 2)
            throw new DomainException("landing.experiment_needs_variants", "Add a second variant (B) before turning the A/B test on.");

        var before = new { page.Name, page.Slug, page.ExperimentEnabled, page.NoIndex };
        page.Name = request.Name.Trim();
        page.Slug = slug;
        page.MetaTitle = request.MetaTitle?.Trim();
        page.MetaDescription = request.MetaDescription?.Trim();
        page.OgImageUrl = string.IsNullOrWhiteSpace(request.OgImageUrl) ? null : request.OgImageUrl.Trim();
        page.NoIndex = request.NoIndex;
        if (request.ExperimentEnabled && !page.ExperimentEnabled) page.ExperimentStartedAt = Now;
        page.ExperimentEnabled = request.ExperimentEnabled;
        page.VariantsJson = LandingBlocks.Serialize(variants);
        page.HasUnpublishedChanges = true;
        audit.Record("landing.page_updated", nameof(LandingPage), page.Id, before, new { page.Name, page.Slug, page.ExperimentEnabled, page.NoIndex, Variants = variants.Count });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(page, ct);
    }

    /// <summary>Publishes the current draft as a new immutable version (versions are never modified).</summary>
    [HttpPost("landing-pages/{id:guid}/publish")]
    public async Task<PageDetailDto> Publish(Guid id, CancellationToken ct)
    {
        await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
        if (!await dialect.LockRowAsync(db, "landing_pages", id, ct)) throw DomainException.NotFound("Page");
        var page = await LoadAsync(id, ct, tracked: true);
        if (page.Status == LandingPageStatus.Archived) throw DomainException.Conflict("landing.archived", "Restore the page before publishing it.");
        // Re-validate against the current forms/images (a form may have been archived since the draft was saved).
        using (var draft = JsonDocument.Parse(page.VariantsJson))
            await pages.ParseVariantsAsync(page.ClientAccountId, draft.RootElement, ct);
        // The draft may carry a new address; another page's live address cannot be taken over.
        if (await pages.FindLiveAsync(page.ClientAccountId, page.Slug, ct, page.Id) is not null) throw SlugTaken();
        var next = (await db.Set<LandingPageVersion>().Where(v => v.PageId == id).MaxAsync(v => (int?)v.Version, ct) ?? 0) + 1;
        var snapshot = LandingPageService.BuildSnapshot(page);
        var version = new LandingPageVersion
        {
            PageId = page.Id, ClientAccountId = page.ClientAccountId, Version = next, SnapshotJson = snapshot,
            ContentHash = Normalization.Sha256Hex(snapshot), PublishedAt = Now, PublishedByUserId = currentUser.Id,
        };
        db.Add(version);
        page.PublishedVersionId = version.Id;
        page.PublishedAt = version.PublishedAt;
        page.Status = LandingPageStatus.Published;
        page.HasUnpublishedChanges = false;
        audit.Record("landing.page_published", nameof(LandingPage), page.Id, after: new { version.Version, version.ContentHash, page.Slug });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await ToDetailAsync(page, ct);
    }

    [HttpPost("landing-pages/{id:guid}/unpublish")]
    public async Task<PageDetailDto> Unpublish(Guid id, CancellationToken ct)
    {
        var page = await LoadAsync(id, ct, tracked: true);
        if (page.Status == LandingPageStatus.Published)
        {
            page.Status = LandingPageStatus.Draft;
            page.HasUnpublishedChanges = true;
            audit.Record("landing.page_unpublished", nameof(LandingPage), page.Id);
            await db.SaveChangesAsync(ct);
        }
        return await ToDetailAsync(page, ct);
    }

    /// <summary>Archives the page (taken offline; versions and submissions are kept).</summary>
    [HttpDelete("landing-pages/{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        var page = await LoadAsync(id, ct, tracked: true);
        if (page.Status != LandingPageStatus.Archived)
        {
            page.Status = LandingPageStatus.Archived;
            audit.Record("landing.page_archived", nameof(LandingPage), page.Id);
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    [HttpPost("landing-pages/{id:guid}/restore")]
    public async Task<PageDetailDto> Restore(Guid id, CancellationToken ct)
    {
        var page = await LoadAsync(id, ct, tracked: true);
        if (page.Status == LandingPageStatus.Archived)
        {
            page.Status = LandingPageStatus.Draft;
            page.HasUnpublishedChanges = true;
            audit.Record("landing.page_restored", nameof(LandingPage), page.Id);
            await db.SaveChangesAsync(ct);
        }
        return await ToDetailAsync(page, ct);
    }

    [HttpGet("landing-pages/{id:guid}/versions")]
    public async Task<List<VersionDto>> Versions(Guid id, CancellationToken ct)
    {
        var page = await LoadAsync(id, ct, tracked: false);
        return await db.Set<LandingPageVersion>().AsNoTracking().Where(v => v.PageId == id).OrderByDescending(v => v.Version)
            .Select(v => new VersionDto(v.Id, v.Version, v.PublishedAt, v.PublishedByUserId, v.ContentHash, v.Id == page.PublishedVersionId))
            .ToListAsync(ct);
    }

    [HttpGet("landing-pages/{id:guid}/versions/{versionId:guid}")]
    public async Task<VersionDetailDto> Version(Guid id, Guid versionId, CancellationToken ct)
    {
        var page = await LoadAsync(id, ct, tracked: false);
        var v = await db.Set<LandingPageVersion>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == versionId && x.PageId == id, ct)
                ?? throw DomainException.NotFound("Version");
        return new VersionDetailDto(new VersionDto(v.Id, v.Version, v.PublishedAt, v.PublishedByUserId, v.ContentHash, v.Id == page.PublishedVersionId),
            LandingPageService.ReadSnapshot(v));
    }

    /// <summary>Copies a version's content back into the draft (the version itself is unchanged).</summary>
    [HttpPost("landing-pages/{id:guid}/versions/{versionId:guid}/restore")]
    public async Task<PageDetailDto> RestoreVersion(Guid id, Guid versionId, CancellationToken ct)
    {
        var page = await LoadAsync(id, ct, tracked: true);
        var v = await db.Set<LandingPageVersion>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == versionId && x.PageId == id, ct)
                ?? throw DomainException.NotFound("Version");
        var snapshot = LandingPageService.ReadSnapshot(v);
        page.VariantsJson = snapshot.Variants.GetRawText();
        page.MetaTitle = snapshot.MetaTitle;
        page.MetaDescription = snapshot.MetaDescription;
        page.OgImageUrl = snapshot.OgImageUrl;
        page.NoIndex = snapshot.NoIndex;
        page.HasUnpublishedChanges = true;
        audit.Record("landing.version_restored", nameof(LandingPage), page.Id, after: new { v.Version });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(page, ct);
    }

    /// <summary>Starts a fresh experiment (new assignments and results); takes effect on the next publish.</summary>
    [HttpPost("landing-pages/{id:guid}/experiment/reset")]
    public async Task<PageDetailDto> ResetExperiment(Guid id, CancellationToken ct)
    {
        var page = await LoadAsync(id, ct, tracked: true);
        var previous = page.ExperimentId;
        page.ExperimentId = IdGenerator.NewId();
        page.ExperimentStartedAt = page.ExperimentEnabled ? Now : null;
        page.HasUnpublishedChanges = true;
        audit.Record("landing.experiment_reset", nameof(LandingPage), page.Id, before: new { ExperimentId = previous }, after: new { page.ExperimentId });
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(page, ct);
    }

    [HttpGet("landing-pages/{id:guid}/analytics")]
    public async Task<PageAnalyticsDto> Analytics(Guid id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        var page = await LoadAsync(id, ct, tracked: false);
        var end = to?.ToUniversalTime() ?? Now;
        var start = from?.ToUniversalTime() ?? end.AddDays(-30);
        if (start > end) throw new DomainException("range.invalid", "'from' must be before or equal to 'to'.");

        JsonElement variantsJson;
        var version = page.PublishedVersionId is { } vid ? await db.Set<LandingPageVersion>().AsNoTracking().FirstOrDefaultAsync(v => v.Id == vid, ct) : null;
        PageSnapshot? snapshot = version is null ? null : LandingPageService.ReadSnapshot(version);
        using var draft = JsonDocument.Parse(page.VariantsJson);
        variantsJson = snapshot?.Variants ?? draft.RootElement;
        var experimentId = snapshot?.ExperimentId ?? page.ExperimentId;
        var experimentOn = snapshot?.ExperimentEnabled ?? false;

        var views = await db.Set<LandingPageView>().AsNoTracking().Where(v => v.PageId == id && v.ViewedAt >= start && v.ViewedAt <= end)
            .Select(v => new { v.VariantKey, v.VisitorHash, v.ExperimentId, v.ViewedAt }).ToListAsync(ct);
        var submissions = await db.Set<FormSubmission>().AsNoTracking().Where(s => s.LandingPageId == id && s.SubmittedAt >= start && s.SubmittedAt <= end)
            .Select(s => new { s.VariantKey, s.ExperimentId, s.SubmittedAt }).ToListAsync(ct);
        var assigned = await db.Set<LandingPageAssignment>().AsNoTracking().Where(a => a.PageId == id && a.ExperimentId == experimentId)
            .GroupBy(a => a.VariantKey).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var inExperiment = experimentOn ? views.Where(v => v.ExperimentId == experimentId).ToList() : views;
        var subsInExperiment = experimentOn ? submissions.Where(s => s.ExperimentId == experimentId).ToList() : submissions;
        var variantRows = LandingPageService.Variants(variantsJson);
        var stats = variantRows.Select(v =>
        {
            var vViews = inExperiment.Where(x => x.VariantKey == v.Key).ToList();
            var unique = vViews.Select(x => x.VisitorHash).Distinct().Count();
            var subs = subsInExperiment.Count(s => (s.VariantKey ?? "A") == v.Key);
            return (v.Key, v.Name, v.Weight, Views: vViews.Count, Unique: unique, Assigned: assigned.GetValueOrDefault(v.Key), Subs: subs);
        }).ToList();
        var control = stats.FirstOrDefault(s => s.Key == "A");
        var variantDtos = stats.Select(s =>
        {
            var rate = ExperimentMath.Rate(s.Subs, s.Unique);
            if (s.Key == "A" || control.Key is null)
                return new VariantStatsDto(s.Key, s.Name, s.Weight, s.Views, s.Unique, s.Assigned, s.Subs, rate, null, null, null, false, "Control");
            var controlRate = ExperimentMath.Rate(control.Subs, control.Unique);
            var test = ExperimentMath.TwoProportionZTest(control.Subs, control.Unique, Math.Min(s.Subs, s.Unique), s.Unique);
            var enough = s.Unique >= ExperimentMath.MinAssignmentsForConclusion && control.Unique >= ExperimentMath.MinAssignmentsForConclusion;
            var significant = enough && test.PValue is < ExperimentMath.SignificanceLevel;
            var note = !enough ? "Not enough data for a reliable conclusion"
                : significant ? (rate > controlRate ? "Significantly better than control" : "Significantly worse than control")
                : "No significant difference yet";
            return new VariantStatsDto(s.Key, s.Name, s.Weight, s.Views, s.Unique, s.Assigned, s.Subs, rate,
                rate is null || controlRate is null ? null : Math.Round(rate.Value - controlRate.Value, 4),
                rate is null || controlRate is null or 0 ? null : Math.Round((rate.Value - controlRate.Value) / controlRate.Value, 4),
                test.PValue is null ? null : Math.Round(test.PValue.Value, 4), significant, note);
        }).ToList();

        var totalUnique = views.Select(v => v.VisitorHash).Distinct().Count();
        var daily = views.Select(v => DateOnly.FromDateTime(v.ViewedAt)).Concat(submissions.Select(s => DateOnly.FromDateTime(s.SubmittedAt))).Distinct().OrderBy(d => d)
            .Select(d => new DailyStatsDto(d, views.Count(v => DateOnly.FromDateTime(v.ViewedAt) == d), submissions.Count(s => DateOnly.FromDateTime(s.SubmittedAt) == d)))
            .ToList();
        return new PageAnalyticsDto(id, start, end, views.Count, totalUnique, submissions.Count, ExperimentMath.Rate(submissions.Count, totalUnique),
            experimentOn, experimentId, page.ExperimentStartedAt, variantDtos, daily,
            "Conversion = form submissions ÷ unique visitors (bots and staff previews excluded). Variants are compared with control (A) using a two-sided two-proportion z-test (α = 0.05, at least 100 visitors per variant).");
    }

    private async Task<LandingPage> LoadAsync(Guid id, CancellationToken ct, bool tracked)
    {
        var q = db.Set<LandingPage>().Where(p => p.Id == id);
        if (!tracked) q = q.AsNoTracking();
        var page = await q.FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("Page");
        await access.EnsureAsync(page.ClientAccountId, "Page", ct);
        return page;
    }

    private async Task<Dictionary<Guid, (string? Name, string? Slug)>> ClientsAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return (await db.Set<ClientAccount>().AsNoTracking().Where(c => list.Contains(c.Id)).Select(c => new { c.Id, c.Name, c.Slug }).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => ((string?)c.Name, (string?)c.Slug));
    }

    private async Task<PageDetailDto> ToDetailAsync(LandingPage p, CancellationToken ct)
    {
        var client = (await ClientsAsync(new[] { p.ClientAccountId }, ct)).GetValueOrDefault(p.ClientAccountId);
        int? version = p.PublishedVersionId is { } vid
            ? await db.Set<LandingPageVersion>().AsNoTracking().Where(v => v.Id == vid).Select(v => (int?)v.Version).FirstOrDefaultAsync(ct)
            : null;
        var liveSlug = (await pages.LiveSlugsAsync(new[] { p }, ct)).GetValueOrDefault(p.Id, p.Slug);
        using var doc = JsonDocument.Parse(p.VariantsJson);
        return new PageDetailDto(p.Id, p.ClientAccountId, client.Name ?? string.Empty, client.Slug ?? string.Empty, p.Name, p.Slug, p.Status, p.MetaTitle,
            p.MetaDescription, p.OgImageUrl, p.NoIndex, p.TemplateKey, doc.RootElement.Clone(), p.ExperimentEnabled, p.ExperimentId, p.ExperimentStartedAt,
            p.PublishedVersionId, version, p.PublishedAt, p.HasUnpublishedChanges, PublicPath(client.Slug, liveSlug), p.ConcurrencyStamp, p.CreatedAt, p.UpdatedAt);
    }

    public static string PublicPath(string? clientSlug, string slug) => $"/lp/{clientSlug}/{slug}";

    private static DomainException SlugTaken() =>
        DomainException.Conflict("landing.slug_taken", "This client already has a page with that URL slug.");

    private static DomainException SlugError() => new("landing.invalid_slug",
        "Slugs use lower-case letters, digits and dashes (max 80), e.g. spring-offer.", DomainErrorKind.Validation,
        new Dictionary<string, string[]> { ["slug"] = new[] { "Use lower-case letters, digits and dashes." } });
}

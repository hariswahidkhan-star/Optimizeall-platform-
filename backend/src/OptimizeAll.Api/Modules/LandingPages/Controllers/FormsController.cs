using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Seo;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.LandingPages.Controllers;

public sealed class CreateFormRequest
{
    [Required]
    public Guid? ClientAccountId { get; set; }

    [Required, MinLength(2), MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Start from a form template (contact, quote, newsletter, webinar-registration); otherwise a blank contact form.</summary>
    [MaxLength(60)]
    public string? TemplateKey { get; set; }
}

public sealed class UpdateFormRequest
{
    [Required, MinLength(2), MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    public FormStatus Status { get; set; } = FormStatus.Active;

    /// <summary>{ steps: [{ id, title, description, fields: [{ key, type, label, required, options, validation, showIf, urlParam }] }] }</summary>
    public JsonElement Schema { get; set; }

    [Required, MaxLength(60)]
    public string SubmitLabel { get; set; } = "Submit";

    [Required, MaxLength(1000)]
    public string SuccessMessage { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? RedirectUrl { get; set; }

    public List<Guid> NotifyUserIds { get; set; } = new();
    public bool AutoresponderEnabled { get; set; }

    [MaxLength(200)]
    public string? AutoresponderSubject { get; set; }

    [MaxLength(5000)]
    public string? AutoresponderBody { get; set; }

    public List<string> AllowedOrigins { get; set; } = new();

    [MaxLength(2000)]
    public string? ConsentText { get; set; }

    public CaptchaProvider Captcha { get; set; }

    [Range(0, 60)]
    public int MinFillSeconds { get; set; } = 3;

    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record FormListItemDto(
    Guid Id, Guid ClientAccountId, string ClientName, string Name, FormStatus Status, int FieldCount, int StepCount, int Submissions,
    DateTime? LastSubmissionAt, DateTime UpdatedAt);

public sealed record FormDetailDto(
    Guid Id, Guid ClientAccountId, string ClientName, string Name, FormStatus Status, JsonElement Schema, string SubmitLabel, string SuccessMessage,
    string? RedirectUrl, IReadOnlyList<Guid> NotifyUserIds, bool AutoresponderEnabled, string? AutoresponderSubject, string? AutoresponderBody,
    IReadOnlyList<string> AllowedOrigins, string? ConsentText, int ConsentVersion, CaptchaProvider Captcha, int MinFillSeconds, string? TemplateKey,
    Guid ConcurrencyStamp, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record FormTemplateDto(string Key, string Name, string Description, JsonElement Schema, string SubmitLabel);

public sealed record StaffOptionDto(Guid Id, string DisplayName, string Email);

public sealed record SubmissionFileDto(Guid Id, string FieldKey, string FileName, string ContentType, long SizeBytes);

public sealed record SubmissionDto(
    Guid Id, DateTime SubmittedAt, string? Name, string? Email, string? Phone, IReadOnlyDictionary<string, string> Values, string? UtmSource,
    string? UtmMedium, string? UtmCampaign, string? UtmTerm, string? UtmContent, string? Referrer, string? EmbedOrigin, Guid? LandingPageId,
    string? LandingPageName, string? VariantKey, bool ConsentGiven, int? ConsentVersion, string? ConsentText, bool EventPublished,
    IReadOnlyList<SubmissionFileDto> Files, FormSubmissionStatus Status = FormSubmissionStatus.New, string? Note = null, DateTime? StatusChangedAt = null);

public sealed record EmbedDto(string FormUrl, string IframeSnippet, IReadOnlyList<string> AllowedOrigins, string FrameAncestors, string Guidance);

public sealed class FormListQuery : PageQuery
{
    public Guid? ClientId { get; set; }
    public FormStatus? Status { get; set; }
}

public sealed class SubmissionQuery : PageQuery
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? LandingPageId { get; set; }

    /// <summary>Follow-up status filter; by default everything except spam is listed.</summary>
    public FormSubmissionStatus? Status { get; set; }
}

/// <summary>Form builder, submissions (with CSV export and file downloads) and embed codes (forms.manage).</summary>
[ApiController]
[HasPermission(Permissions.FormsManage)]
[Route("api/v1/agency/pages")]
public sealed class FormsController(
    AppDbContext db, SeoAccess access, IClientScope scope, FormService forms, FormFileStore fileStore, IAuditLogger audit,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet("form-templates")]
    public async Task<List<FormTemplateDto>> Templates(CancellationToken ct)
    {
        var rows = await db.Set<FormTemplate>().AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct);
        return rows.Select(t =>
        {
            using var doc = JsonDocument.Parse(t.SchemaJson);
            return new FormTemplateDto(t.Key, t.Name, t.Description, doc.RootElement.Clone(), t.SubmitLabel);
        }).ToList();
    }

    [HttpGet("staff-options")]
    public async Task<List<StaffOptionDto>> StaffOptions(CancellationToken ct) =>
        await (await forms.StaffCandidatesQueryAsync(ct)).OrderBy(u => u.DisplayName).Take(200)
            .Select(u => new StaffOptionDto(u.Id, u.DisplayName, u.Email)).ToListAsync(ct);

    [HttpGet("forms")]
    public async Task<PagedResult<FormListItemDto>> List([FromQuery] FormListQuery query, CancellationToken ct)
    {
        var q = await scope.ApplyAsync(db.Set<Form>().AsNoTracking(), f => f.ClientAccountId, ct);
        if (query.ClientId is { } c) q = q.Where(f => f.ClientAccountId == c);
        if (query.Status is { } s) q = q.Where(f => f.Status == s);
        else q = q.Where(f => f.Status != FormStatus.Archived);
        if (!string.IsNullOrWhiteSpace(query.Search)) q = q.Where(f => EF.Functions.Like(f.Name, PagingExtensions.LikePattern(query.Search)));
        var page = await q.OrderByDescending(f => f.UpdatedAt).ToPagedAsync(query, ct);
        var ids = page.Items.Select(f => f.Id).ToList();
        var stats = await db.Set<FormSubmission>().AsNoTracking().Where(s => ids.Contains(s.FormId)).GroupBy(s => s.FormId)
            .Select(g => new { g.Key, Count = g.Count(), Last = g.Max(s => s.SubmittedAt) }).ToDictionaryAsync(x => x.Key, ct);
        var clients = await ClientNamesAsync(page.Items.Select(f => f.ClientAccountId), ct);
        return new PagedResult<FormListItemDto>(page.Items.Select(f =>
        {
            var schema = FormSchemas.Deserialize(f.SchemaJson);
            var stat = stats.GetValueOrDefault(f.Id);
            return new FormListItemDto(f.Id, f.ClientAccountId, clients.GetValueOrDefault(f.ClientAccountId, string.Empty), f.Name, f.Status,
                schema.AllFields.Count(x => x.Type != "hidden"), schema.Steps.Count, stat?.Count ?? 0, stat?.Last, f.UpdatedAt);
        }).ToList(), page.Total, page.Page, page.PageSize);
    }

    [HttpGet("forms/{id:guid}")]
    public async Task<FormDetailDto> Get(Guid id, CancellationToken ct) => await ToDetailAsync(await LoadAsync(id, ct, tracked: false), ct);

    [HttpPost("forms")]
    public async Task<ActionResult<FormDetailDto>> Create(CreateFormRequest request, CancellationToken ct)
    {
        var clientId = request.ClientAccountId!.Value;
        await access.EnsureAsync(clientId, "Client", ct);
        var form = await forms.CreateFromTemplateAsync(clientId, request.TemplateKey ?? "contact", request.Name.Trim(), ct);
        audit.Record("forms.form_created", nameof(Form), form.Id, after: new { form.Name, form.TemplateKey, form.ClientAccountId });
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = form.Id }, await ToDetailAsync(form, ct));
    }

    [HttpPut("forms/{id:guid}")]
    public async Task<FormDetailDto> Update(Guid id, UpdateFormRequest request, CancellationToken ct)
    {
        var form = await LoadAsync(id, ct, tracked: true);
        if (request.ConcurrencyStamp is { } stamp)
        {
            if (stamp != form.ConcurrencyStamp) throw DomainException.Conflict("concurrency.conflict", "This form was changed by someone else. Reload and try again.");
            db.Entry(form).Property(f => f.ConcurrencyStamp).OriginalValue = stamp;
        }
        var before = Snapshot(form);
        await forms.ApplyAsync(form, new FormInput
        {
            Name = request.Name, Status = request.Status, Schema = request.Schema, SubmitLabel = request.SubmitLabel, SuccessMessage = request.SuccessMessage,
            RedirectUrl = request.RedirectUrl, NotifyUserIds = request.NotifyUserIds, AutoresponderEnabled = request.AutoresponderEnabled,
            AutoresponderSubject = request.AutoresponderSubject, AutoresponderBody = request.AutoresponderBody, AllowedOrigins = request.AllowedOrigins,
            ConsentText = request.ConsentText, Captcha = request.Captcha, MinFillSeconds = request.MinFillSeconds,
        }, ct);
        audit.Record("forms.form_updated", nameof(Form), form.Id, before, Snapshot(form));
        await db.SaveChangesAsync(ct);
        return await ToDetailAsync(form, ct);
    }

    /// <summary>Archives the form (it stops accepting submissions; submissions are kept).</summary>
    [HttpDelete("forms/{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        var form = await LoadAsync(id, ct, tracked: true);
        if (form.Status != FormStatus.Archived)
        {
            form.Status = FormStatus.Archived;
            audit.Record("forms.form_archived", nameof(Form), form.Id);
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }

    [HttpGet("forms/{id:guid}/submissions")]
    public async Task<PagedResult<SubmissionDto>> Submissions(Guid id, [FromQuery] SubmissionQuery query, CancellationToken ct)
    {
        var form = await LoadAsync(id, ct, tracked: false);
        var q = Filter(id, query);
        var page = await q.OrderByDescending(s => s.SubmittedAt).ToPagedAsync(query, ct);
        return new PagedResult<SubmissionDto>(await ToDtosAsync(form, page.Items, ct), page.Total, page.Page, page.PageSize);
    }

    [HttpGet("forms/{id:guid}/submissions/{submissionId:guid}")]
    public async Task<SubmissionDto> Submission(Guid id, Guid submissionId, CancellationToken ct)
    {
        var form = await LoadAsync(id, ct, tracked: false);
        var row = await db.Set<FormSubmission>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == submissionId && s.FormId == id, ct)
                  ?? throw DomainException.NotFound("Submission");
        return (await ToDtosAsync(form, new[] { row }, ct))[0];
    }

    [HttpGet("forms/{id:guid}/submissions/export.csv")]
    public async Task<IActionResult> Export(Guid id, [FromQuery] SubmissionQuery query, CancellationToken ct)
    {
        var form = await LoadAsync(id, ct, tracked: false);
        var rows = await Filter(id, query).OrderByDescending(s => s.SubmittedAt).Take(50_000).ToListAsync(ct);
        var dtos = await ToDtosAsync(form, rows, ct);
        var fieldKeys = FormSchemas.Deserialize(form.SchemaJson).AllFields.Select(f => f.Key).ToList();
        audit.Record("forms.submissions_exported", nameof(Form), form.Id, after: new { Rows = rows.Count });
        await db.SaveChangesAsync(ct);
        var header = new[] { "submitted_at", "name", "email", "phone" }.Concat(fieldKeys)
            .Concat(new[] { "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "referrer", "landing_page", "variant", "consent_given", "consent_version",
                "status", "note" });
        var lines = dtos.Select(d => new object?[] { d.SubmittedAt, d.Name, d.Email, d.Phone }
            .Concat(fieldKeys.Select(k => (object?)d.Values.GetValueOrDefault(k)))
            .Concat(new object?[] { d.UtmSource, d.UtmMedium, d.UtmCampaign, d.UtmTerm, d.UtmContent, d.Referrer, d.LandingPageName, d.VariantKey,
                d.ConsentGiven ? "yes" : "no", d.ConsentVersion, d.Status.ToString(), d.Note }));
        return Csv.File($"{LandingPageService.Slugify(form.Name)}-submissions.csv", header, lines);
    }

    [HttpGet("forms/{id:guid}/submissions/{submissionId:guid}/files/{fileId:guid}")]
    public async Task<IActionResult> DownloadFile(Guid id, Guid submissionId, Guid fileId, CancellationToken ct)
    {
        await LoadAsync(id, ct, tracked: false);
        var file = await db.Set<FormSubmissionFile>().AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId && f.SubmissionId == submissionId && f.FormId == id, ct) ?? throw DomainException.NotFound("File");
        var stream = fileStore.OpenRead(file.StorageKey) ?? throw DomainException.NotFound("File");
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'";
        Response.Headers.CacheControl = "private, no-store";
        return File(stream, file.ContentType, file.FileName);
    }

    /// <summary>Embed code: an iframe of /f/{id} plus a postMessage listener that resizes it (only from the app's origin).</summary>
    [HttpGet("forms/{id:guid}/embed")]
    public async Task<EmbedDto> Embed(Guid id, CancellationToken ct)
    {
        var form = await LoadAsync(id, ct, tracked: false);
        var app = FormService.CanonicalOrigin(configuration["Email:AppBaseUrl"]) ?? "http://localhost:5173";
        var url = $"{app}/f/{form.Id}";
        var elementId = $"oa-form-{form.Id:N}";
        var title = System.Net.WebUtility.HtmlEncode(form.Name);
        var snippet =
            $"<iframe id=\"{elementId}\" src=\"{url}\" title=\"{title}\" loading=\"lazy\" style=\"width:100%;border:0;min-height:420px\"></iframe>\n" +
            "<script>\n" +
            "  window.addEventListener('message', function (e) {\n" +
            $"    if (e.origin !== '{app}' || !e.data || e.data.type !== 'oa-form:height' || e.data.formId !== '{form.Id}') return;\n" +
            $"    document.getElementById('{elementId}').style.height = Math.min(Math.max(e.data.height, 200), 5000) + 'px';\n" +
            "  });\n" +
            "</script>";
        var ancestors = form.AllowedOrigins.Count == 0 ? "'none'" : string.Join(' ', form.AllowedOrigins);
        return new EmbedDto(url, snippet, form.AllowedOrigins, $"frame-ancestors {ancestors}",
            form.AllowedOrigins.Count == 0
                ? "Add the websites that will embed this form to Allowed origins; embedding is refused everywhere else."
                : "Serve /f/* with this Content-Security-Policy frame-ancestors value (see docs/SEO_CRO.md); the form also refuses to load or submit from other origins.");
    }

    private IQueryable<FormSubmission> Filter(Guid formId, SubmissionQuery query)
    {
        var q = db.Set<FormSubmission>().AsNoTracking().Where(s => s.FormId == formId);
        if (query.From is { } from) { var f = from.ToUniversalTime(); q = q.Where(s => s.SubmittedAt >= f); }
        if (query.To is { } to) { var t = to.ToUniversalTime(); q = q.Where(s => s.SubmittedAt <= t); }
        if (query.LandingPageId is { } p) q = q.Where(s => s.LandingPageId == p);
        if (query.Status is { } st) q = q.Where(s => s.Status == st);
        else q = q.Where(s => s.Status != FormSubmissionStatus.Spam);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var like = PagingExtensions.LikePattern(query.Search);
            q = q.Where(s => EF.Functions.Like(s.Email!, like) || EF.Functions.Like(s.Name!, like) || EF.Functions.Like(s.Phone!, like));
        }
        return q;
    }

    private async Task<Form> LoadAsync(Guid id, CancellationToken ct, bool tracked)
    {
        var q = db.Set<Form>().Where(f => f.Id == id);
        if (!tracked) q = q.AsNoTracking();
        var form = await q.FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("Form");
        await access.EnsureAsync(form.ClientAccountId, "Form", ct);
        return form;
    }

    private async Task<Dictionary<Guid, string>> ClientNamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Set<ClientAccount>().AsNoTracking().Where(c => list.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
    }

    private async Task<FormDetailDto> ToDetailAsync(Form f, CancellationToken ct)
    {
        var client = (await ClientNamesAsync(new[] { f.ClientAccountId }, ct)).GetValueOrDefault(f.ClientAccountId, string.Empty);
        using var schema = JsonDocument.Parse(f.SchemaJson);
        return new FormDetailDto(f.Id, f.ClientAccountId, client, f.Name, f.Status, schema.RootElement.Clone(), f.SubmitLabel, f.SuccessMessage, f.RedirectUrl,
            f.NotifyUserIds, f.AutoresponderEnabled, f.AutoresponderSubject, f.AutoresponderBody, f.AllowedOrigins, f.ConsentText, f.ConsentVersion,
            f.Captcha, f.MinFillSeconds, f.TemplateKey, f.ConcurrencyStamp, f.CreatedAt, f.UpdatedAt);
    }

    private async Task<List<SubmissionDto>> ToDtosAsync(Form form, IReadOnlyCollection<FormSubmission> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var files = (await db.Set<FormSubmissionFile>().AsNoTracking().Where(f => ids.Contains(f.SubmissionId)).ToListAsync(ct))
            .GroupBy(f => f.SubmissionId).ToDictionary(g => g.Key, g => g.ToList());
        var pageIds = rows.Where(r => r.LandingPageId != null).Select(r => r.LandingPageId!.Value).Distinct().ToList();
        var pages = await db.Set<LandingPage>().AsNoTracking().Where(p => pageIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var consents = await db.Set<FormConsentVersion>().AsNoTracking().Where(c => c.FormId == form.Id).ToDictionaryAsync(c => c.Version, c => c.Text, ct);
        return rows.Select(s => new SubmissionDto(s.Id, s.SubmittedAt, s.Name, s.Email, s.Phone,
            JsonSerializer.Deserialize<Dictionary<string, string>>(s.DataJson) ?? new Dictionary<string, string>(),
            s.UtmSource, s.UtmMedium, s.UtmCampaign, s.UtmTerm, s.UtmContent, s.Referrer, s.EmbedOrigin, s.LandingPageId,
            s.LandingPageId is { } p ? pages.GetValueOrDefault(p) : null, s.VariantKey, s.ConsentGiven, s.ConsentVersion,
            s.ConsentVersion is { } v ? consents.GetValueOrDefault(v) : null, s.EventPublishedAt is not null,
            files.GetValueOrDefault(s.Id)?.Select(f => new SubmissionFileDto(f.Id, f.FieldKey, f.FileName, f.ContentType, f.SizeBytes)).ToList()
            ?? new List<SubmissionFileDto>(), s.Status, s.Note, s.StatusChangedAt)).ToList();
    }

    private static object Snapshot(Form f) => new
    {
        f.Name, f.Status, Fields = FormSchemas.Deserialize(f.SchemaJson).AllFields.Count(), f.NotifyUserIds, f.AutoresponderEnabled, f.AllowedOrigins,
        f.ConsentVersion, f.Captcha, f.MinFillSeconds,
    };
}

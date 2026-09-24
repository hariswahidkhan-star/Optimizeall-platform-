using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.LandingPages.Templates;
using OptimizeAll.Api.Modules.Seo;
using OptimizeAll.Api.Modules.SocialMedia;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.LandingPages.Controllers;

public sealed record PageTemplateAdminDto(
    string Key, string Name, string Category, string Description, string MetaTitle, string MetaDescription, string? FormTemplateKey, int BlockCount,
    int SortOrder, bool IsActive, bool IsCustom, bool IsCustomized, int PagesUsing, Guid ConcurrencyStamp);

public sealed class PageTemplateRequest
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(60)] public string Category { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string Description { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string MetaTitle { get; set; } = string.Empty;
    [Required, MaxLength(320)] public string MetaDescription { get; set; } = string.Empty;
    [MaxLength(60)] public string? FormTemplateKey { get; set; }
    [Range(0, 100_000)] public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class SaveAsTemplateRequest
{
    [Required, MinLength(2), MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(60)] public string? Category { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
}

public sealed record FormTemplateAdminDto(
    string Key, string Name, string Description, JsonElement Schema, string SubmitLabel, string SuccessMessage, string? ConsentText,
    string? AutoresponderSubject, string? AutoresponderBody, int SortOrder, bool IsActive, bool IsCustom, bool IsCustomized, int FormsUsing,
    Guid ConcurrencyStamp);

public sealed class FormTemplateRequest
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string Description { get; set; } = string.Empty;
    public JsonElement Schema { get; set; }
    [Required, MaxLength(60)] public string SubmitLabel { get; set; } = "Submit";
    [Required, MaxLength(1000)] public string SuccessMessage { get; set; } = string.Empty;
    [MaxLength(2000)] public string? ConsentText { get; set; }
    [MaxLength(200)] public string? AutoresponderSubject { get; set; }
    [MaxLength(5000)] public string? AutoresponderBody { get; set; }
    [Range(0, 100_000)] public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class SubmissionUpdateRequest
{
    public FormSubmissionStatus? Status { get; set; }
    [MaxLength(2000)] public string? Note { get; set; }
}

public sealed class SubmissionBulkRequest
{
    [Required, MinLength(1), MaxLength(500)] public List<Guid> Ids { get; set; } = new();

    /// <summary>New status for every selected submission; or set <see cref="Delete"/>.</summary>
    public FormSubmissionStatus? Status { get; set; }
    public bool Delete { get; set; }
}

public sealed record SubmissionBulkResult(int Updated, int Deleted);

public sealed record SubmissionStatusDto(Guid Id, FormSubmissionStatus Status, string? Note, DateTime? StatusChangedAt);

/// <summary>
/// Duplicate/restore of pages and forms, follow-up of submissions (status, note, delete, bulk) and the agency's page and
/// form template library (reads: forms.manage; changes to the library: settings.manage as well).
/// </summary>
[ApiController]
[HasPermission(Permissions.FormsManage)]
[Route("api/v1/agency/pages")]
public sealed class LandingManageController(
    AppDbContext db, SeoAccess access, FormFileStore fileStore, ICurrentUser currentUser, IAuditLogger audit, TimeProvider clock) : ControllerBase
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ------------------------------------------------------------------ pages & forms

    /// <summary>Copies a page's draft (all variants) into a new draft page with a free slug. Published versions are not copied.</summary>
    [HttpPost("landing-pages/{id:guid}/duplicate")]
    public async Task<object> DuplicatePage(Guid id, CancellationToken ct)
    {
        var source = await LoadPageAsync(id, ct);
        var slug = await FreeSlugAsync(source.ClientAccountId, source.Slug, ct);
        var copy = new LandingPage
        {
            ClientAccountId = source.ClientAccountId, Name = Cap(source.Name + " (copy)", 150), Slug = slug, Status = LandingPageStatus.Draft,
            MetaTitle = source.MetaTitle, MetaDescription = source.MetaDescription, OgImageUrl = source.OgImageUrl, NoIndex = source.NoIndex,
            TemplateKey = source.TemplateKey, VariantsJson = source.VariantsJson, HasUnpublishedChanges = true, CreatedByUserId = currentUser.Id,
        };
        db.Add(copy);
        audit.Record("landing.page_duplicated", nameof(LandingPage), copy.Id, after: new { From = id, copy.Name, copy.Slug });
        await db.SaveChangesAsync(ct);
        return new { id = copy.Id, copy.Name, copy.Slug };
    }

    [HttpPost("forms/{id:guid}/duplicate")]
    public async Task<object> DuplicateForm(Guid id, CancellationToken ct)
    {
        var source = await LoadFormAsync(id, ct);
        var copy = new Form
        {
            ClientAccountId = source.ClientAccountId, Name = Cap(source.Name + " (copy)", 150), Status = FormStatus.Draft, SchemaJson = source.SchemaJson,
            SubmitLabel = source.SubmitLabel, SuccessMessage = source.SuccessMessage, RedirectUrl = source.RedirectUrl, NotifyUserIds = source.NotifyUserIds.ToList(),
            AutoresponderEnabled = source.AutoresponderEnabled, AutoresponderSubject = source.AutoresponderSubject, AutoresponderBody = source.AutoresponderBody,
            AllowedOrigins = source.AllowedOrigins.ToList(), Captcha = source.Captcha, MinFillSeconds = source.MinFillSeconds, TemplateKey = source.TemplateKey,
        };
        db.Add(copy);
        if (!string.IsNullOrWhiteSpace(source.ConsentText))
        {
            copy.ConsentText = source.ConsentText;
            copy.ConsentVersion = 1;
            db.Add(new FormConsentVersion { FormId = copy.Id, Version = 1, Text = source.ConsentText, CreatedAt = Now });
        }
        audit.Record("forms.form_duplicated", nameof(Form), copy.Id, after: new { From = id, copy.Name });
        await db.SaveChangesAsync(ct);
        return new { id = copy.Id, copy.Name, copy.Status };
    }

    /// <summary>Brings an archived form back as a draft (activate it in the builder to accept submissions again).</summary>
    [HttpPost("forms/{id:guid}/restore")]
    public async Task<object> RestoreForm(Guid id, CancellationToken ct)
    {
        var form = await LoadFormAsync(id, ct);
        if (form.Status != FormStatus.Archived) throw DomainException.Conflict("forms.not_archived", "Only archived forms can be restored.");
        form.Status = FormStatus.Draft;
        audit.Record("forms.form_restored", nameof(Form), form.Id);
        await db.SaveChangesAsync(ct);
        return new { id = form.Id, form.Name, form.Status };
    }

    // ------------------------------------------------------------------ submissions

    [HttpPatch("forms/{id:guid}/submissions/{submissionId:guid}")]
    public async Task<SubmissionStatusDto> UpdateSubmission(Guid id, Guid submissionId, SubmissionUpdateRequest request, CancellationToken ct)
    {
        await LoadFormAsync(id, ct);
        var row = await db.Set<FormSubmission>().FirstOrDefaultAsync(s => s.Id == submissionId && s.FormId == id, ct) ?? throw DomainException.NotFound("Submission");
        var before = new { row.Status };
        if (request.Status is { } status && status != row.Status)
        {
            row.Status = status;
            row.StatusChangedAt = Now;
            row.StatusChangedByUserId = currentUser.Id;
        }
        if (request.Note is not null) row.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        audit.Record("forms.submission_updated", nameof(FormSubmission), row.Id, before, new { row.Status, HasNote = row.Note is not null });
        await db.SaveChangesAsync(ct);
        return new SubmissionStatusDto(row.Id, row.Status, row.Note, row.StatusChangedAt);
    }

    /// <summary>Permanently deletes a submission and its uploaded files (e.g. spam or an erasure request).</summary>
    [HttpDelete("forms/{id:guid}/submissions/{submissionId:guid}")]
    public async Task<IActionResult> DeleteSubmission(Guid id, Guid submissionId, CancellationToken ct)
    {
        await LoadFormAsync(id, ct);
        if (!await db.Set<FormSubmission>().AnyAsync(s => s.Id == submissionId && s.FormId == id, ct)) throw DomainException.NotFound("Submission");
        await DeleteSubmissionsAsync(id, new[] { submissionId }, ct);
        return NoContent();
    }

    [HttpPost("forms/{id:guid}/submissions/bulk")]
    public async Task<SubmissionBulkResult> BulkSubmissions(Guid id, SubmissionBulkRequest request, CancellationToken ct)
    {
        await LoadFormAsync(id, ct);
        if (request.Delete == request.Status.HasValue)
            throw new DomainException("validation.failed", "Choose either a new status or delete.");
        var ids = request.Ids.Distinct().ToList();
        if (request.Delete) return new SubmissionBulkResult(0, await DeleteSubmissionsAsync(id, ids, ct));
        var status = request.Status!.Value;
        var now = Now;
        var updated = await db.Set<FormSubmission>().Where(s => s.FormId == id && ids.Contains(s.Id) && s.Status != status)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, status).SetProperty(s => s.StatusChangedAt, now)
                .SetProperty(s => s.StatusChangedByUserId, currentUser.Id), ct);
        audit.Record("forms.submissions_bulk_status", nameof(Form), id, after: new { status, updated });
        await db.SaveChangesAsync(ct);
        return new SubmissionBulkResult(updated, 0);
    }

    private async Task<int> DeleteSubmissionsAsync(Guid formId, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var files = await db.Set<FormSubmissionFile>().AsNoTracking().Where(f => f.FormId == formId && ids.Contains(f.SubmissionId)).ToListAsync(ct);
        await db.Set<FormSubmissionFile>().Where(f => f.FormId == formId && ids.Contains(f.SubmissionId)).ExecuteDeleteAsync(ct);
        await db.Set<FormEmailOutbox>().Where(o => ids.Contains(o.SubmissionId)).ExecuteDeleteAsync(ct);
        var deleted = await db.Set<FormSubmission>().Where(s => s.FormId == formId && ids.Contains(s.Id)).ExecuteDeleteAsync(ct);
        foreach (var f in files) fileStore.Delete(f.StorageKey);
        audit.Record("forms.submissions_deleted", nameof(Form), formId, before: new { deleted, files = files.Count });
        await db.SaveChangesAsync(ct);
        return deleted;
    }

    // ------------------------------------------------------------------ page template library

    [HttpGet("admin/templates")]
    public async Task<List<PageTemplateAdminDto>> PageTemplates(CancellationToken ct)
    {
        var rows = await db.Set<LandingPageTemplate>().AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct);
        var usage = await db.Set<LandingPage>().AsNoTracking().Where(p => p.TemplateKey != null).GroupBy(p => p.TemplateKey!)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return rows.Select(t => ToDto(t, usage.GetValueOrDefault(t.Key))).ToList();
    }

    [HttpPut("admin/templates/{key}")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<PageTemplateAdminDto> UpdatePageTemplate(string key, PageTemplateRequest request, CancellationToken ct)
    {
        var t = await db.Set<LandingPageTemplate>().FirstOrDefaultAsync(x => x.Key == key, ct) ?? throw DomainException.NotFound("Template");
        StampGuard.Expect(db, t, request.ConcurrencyStamp, "template");
        if (request.FormTemplateKey is { Length: > 0 } fk && !await db.Set<FormTemplate>().AnyAsync(f => f.Key == fk, ct))
            throw Invalid("formTemplateKey", "Choose an existing form template.");
        var before = new { t.Name, t.IsActive };
        t.Name = request.Name.Trim();
        t.Category = request.Category.Trim();
        t.Description = request.Description.Trim();
        t.MetaTitle = request.MetaTitle.Trim();
        t.MetaDescription = request.MetaDescription.Trim();
        t.FormTemplateKey = string.IsNullOrWhiteSpace(request.FormTemplateKey) ? null : request.FormTemplateKey.Trim();
        t.SortOrder = request.SortOrder;
        t.IsActive = request.IsActive;
        if (!t.IsCustom) t.IsCustomized = true;
        t.UpdatedAt = Now;
        audit.Record("landing.template_updated", nameof(LandingPageTemplate), key, before, new { t.Name, t.IsActive });
        await db.SaveChangesAsync(ct);
        return ToDto(t, await db.Set<LandingPage>().CountAsync(p => p.TemplateKey == key, ct));
    }

    /// <summary>Saves variant A of a page (its draft blocks) as a reusable template; its form block becomes a placeholder.</summary>
    [HttpPost("landing-pages/{id:guid}/save-as-template")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<PageTemplateAdminDto> SavePageAsTemplate(Guid id, SaveAsTemplateRequest request, CancellationToken ct)
    {
        var page = await LoadPageAsync(id, ct);
        var variants = JsonNode.Parse(page.VariantsJson)!.AsArray();
        var blocks = variants.FirstOrDefault()?["blocks"]?.AsArray() ?? throw DomainException.Conflict("landing.empty_page", "The page has no content to save.");
        string? formTemplateKey = null;
        foreach (var block in blocks)
        {
            if (block?["props"]?["formId"] is { } formNode && Guid.TryParse(formNode.GetValue<string>(), out var formId))
            {
                formTemplateKey ??= await db.Set<Form>().Where(f => f.Id == formId).Select(f => f.TemplateKey).FirstOrDefaultAsync(ct);
                block["props"]!["formId"] = TemplateCatalog.FormPlaceholder;
            }
        }
        if (formTemplateKey is not null && !await db.Set<FormTemplate>().AnyAsync(f => f.Key == formTemplateKey, ct)) formTemplateKey = null;
        var maxOrder = await db.Set<LandingPageTemplate>().MaxAsync(x => (int?)x.SortOrder, ct) ?? 0;
        var t = new LandingPageTemplate
        {
            Key = "custom-" + Guid.NewGuid().ToString("N")[..12], Name = request.Name.Trim(), Category = Cap(request.Category?.Trim() is { Length: > 0 } c ? c : "Custom", 60),
            Description = Cap(request.Description?.Trim() is { Length: > 0 } d ? d : $"Saved from the page {page.Name}.", 500),
            MetaTitle = Cap(page.MetaTitle ?? page.Name, 150), MetaDescription = Cap(page.MetaDescription ?? string.Empty, 320), BlocksJson = blocks.ToJsonString(),
            FormTemplateKey = formTemplateKey, SortOrder = maxOrder + 10, IsCustom = true, UpdatedAt = Now,
        };
        db.Add(t);
        audit.Record("landing.template_created", nameof(LandingPageTemplate), t.Key, after: new { t.Name, FromPage = id });
        await db.SaveChangesAsync(ct);
        return ToDto(t, 0);
    }

    /// <summary>Deletes an agency template; built-in templates are hidden instead.</summary>
    [HttpDelete("admin/templates/{key}")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> DeletePageTemplate(string key, CancellationToken ct)
    {
        var t = await db.Set<LandingPageTemplate>().FirstOrDefaultAsync(x => x.Key == key, ct) ?? throw DomainException.NotFound("Template");
        if (t.IsCustom) db.Remove(t);
        else t.IsActive = false;
        audit.Record(t.IsCustom ? "landing.template_deleted" : "landing.template_hidden", nameof(LandingPageTemplate), key, before: new { t.Name });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("admin/templates/{key}/reset")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<PageTemplateAdminDto> ResetPageTemplate(string key, CancellationToken ct)
    {
        var t = await db.Set<LandingPageTemplate>().FirstOrDefaultAsync(x => x.Key == key, ct) ?? throw DomainException.NotFound("Template");
        var d = TemplateCatalog.Pages.FirstOrDefault(p => p.Key == key)
                ?? throw DomainException.Conflict("landing.template_custom", "Agency templates have no built-in version to reset to.");
        t.Name = d.Name;
        t.Category = d.Category;
        t.Description = d.Description;
        t.MetaTitle = d.MetaTitle;
        t.MetaDescription = d.MetaDescription;
        t.BlocksJson = d.BlocksJson;
        t.FormTemplateKey = d.FormTemplateKey;
        t.SortOrder = d.SortOrder;
        t.IsActive = true;
        t.IsCustomized = false;
        t.UpdatedAt = Now;
        audit.Record("landing.template_reset", nameof(LandingPageTemplate), key);
        await db.SaveChangesAsync(ct);
        return ToDto(t, await db.Set<LandingPage>().CountAsync(p => p.TemplateKey == key, ct));
    }

    // ------------------------------------------------------------------ form template library

    [HttpGet("admin/form-templates")]
    public async Task<List<FormTemplateAdminDto>> FormTemplates(CancellationToken ct)
    {
        var rows = await db.Set<FormTemplate>().AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(ct);
        var usage = await db.Set<Form>().AsNoTracking().Where(f => f.TemplateKey != null).GroupBy(f => f.TemplateKey!)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return rows.Select(t => ToDto(t, usage.GetValueOrDefault(t.Key))).ToList();
    }

    [HttpPut("admin/form-templates/{key}")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<FormTemplateAdminDto> UpdateFormTemplate(string key, FormTemplateRequest request, CancellationToken ct)
    {
        var t = await db.Set<FormTemplate>().FirstOrDefaultAsync(x => x.Key == key, ct) ?? throw DomainException.NotFound("Template");
        StampGuard.Expect(db, t, request.ConcurrencyStamp, "template");
        var schema = ParseSchema(request.Schema);
        var before = new { t.Name, t.IsActive };
        t.Name = request.Name.Trim();
        t.Description = request.Description.Trim();
        t.SchemaJson = FormSchemas.Serialize(schema);
        t.SubmitLabel = request.SubmitLabel.Trim();
        t.SuccessMessage = request.SuccessMessage.Trim();
        t.ConsentText = string.IsNullOrWhiteSpace(request.ConsentText) ? null : request.ConsentText.Trim();
        t.AutoresponderSubject = string.IsNullOrWhiteSpace(request.AutoresponderSubject) ? null : request.AutoresponderSubject.Trim();
        t.AutoresponderBody = string.IsNullOrWhiteSpace(request.AutoresponderBody) ? null : request.AutoresponderBody.Trim();
        t.SortOrder = request.SortOrder;
        t.IsActive = request.IsActive;
        if (!t.IsCustom) t.IsCustomized = true;
        t.UpdatedAt = Now;
        audit.Record("forms.template_updated", nameof(FormTemplate), key, before, new { t.Name, t.IsActive });
        await db.SaveChangesAsync(ct);
        return ToDto(t, await db.Set<Form>().CountAsync(f => f.TemplateKey == key, ct));
    }

    [HttpPost("forms/{id:guid}/save-as-template")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<FormTemplateAdminDto> SaveFormAsTemplate(Guid id, SaveAsTemplateRequest request, CancellationToken ct)
    {
        var form = await LoadFormAsync(id, ct);
        var maxOrder = await db.Set<FormTemplate>().MaxAsync(x => (int?)x.SortOrder, ct) ?? 0;
        var t = new FormTemplate
        {
            Key = "custom-" + Guid.NewGuid().ToString("N")[..12], Name = request.Name.Trim(),
            Description = Cap(request.Description?.Trim() is { Length: > 0 } d ? d : $"Saved from the form {form.Name}.", 500), SchemaJson = form.SchemaJson,
            SubmitLabel = form.SubmitLabel, SuccessMessage = form.SuccessMessage, ConsentText = form.ConsentText, AutoresponderSubject = form.AutoresponderSubject,
            AutoresponderBody = form.AutoresponderBody, SortOrder = maxOrder + 10, IsCustom = true, UpdatedAt = Now,
        };
        db.Add(t);
        audit.Record("forms.template_created", nameof(FormTemplate), t.Key, after: new { t.Name, FromForm = id });
        await db.SaveChangesAsync(ct);
        return ToDto(t, 0);
    }

    [HttpDelete("admin/form-templates/{key}")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<IActionResult> DeleteFormTemplate(string key, CancellationToken ct)
    {
        var t = await db.Set<FormTemplate>().FirstOrDefaultAsync(x => x.Key == key, ct) ?? throw DomainException.NotFound("Template");
        if (t.IsCustom && await db.Set<LandingPageTemplate>().AnyAsync(p => p.FormTemplateKey == key, ct))
            throw DomainException.Conflict("forms.template_in_use", "A page template uses this form template; change that page template first.");
        if (t.IsCustom) db.Remove(t);
        else t.IsActive = false;
        audit.Record(t.IsCustom ? "forms.template_deleted" : "forms.template_hidden", nameof(FormTemplate), key, before: new { t.Name });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("admin/form-templates/{key}/reset")]
    [HasPermission(Permissions.SettingsManage)]
    public async Task<FormTemplateAdminDto> ResetFormTemplate(string key, CancellationToken ct)
    {
        var t = await db.Set<FormTemplate>().FirstOrDefaultAsync(x => x.Key == key, ct) ?? throw DomainException.NotFound("Template");
        var d = TemplateCatalog.Forms.FirstOrDefault(f => f.Key == key)
                ?? throw DomainException.Conflict("forms.template_custom", "Agency templates have no built-in version to reset to.");
        t.Name = d.Name;
        t.Description = d.Description;
        t.SchemaJson = d.SchemaJson;
        t.SubmitLabel = d.SubmitLabel;
        t.SuccessMessage = d.SuccessMessage;
        t.ConsentText = d.ConsentText;
        t.AutoresponderSubject = d.AutoresponderSubject;
        t.AutoresponderBody = d.AutoresponderBody;
        t.SortOrder = d.SortOrder;
        t.IsActive = true;
        t.IsCustomized = false;
        t.UpdatedAt = Now;
        audit.Record("forms.template_reset", nameof(FormTemplate), key);
        await db.SaveChangesAsync(ct);
        return ToDto(t, await db.Set<Form>().CountAsync(f => f.TemplateKey == key, ct));
    }

    // ------------------------------------------------------------------ helpers

    private async Task<LandingPage> LoadPageAsync(Guid id, CancellationToken ct)
    {
        var page = await db.Set<LandingPage>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound("Page");
        await access.EnsureAsync(page.ClientAccountId, "Page", ct);
        return page;
    }

    private async Task<Form> LoadFormAsync(Guid id, CancellationToken ct)
    {
        var form = await db.Set<Form>().FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw DomainException.NotFound("Form");
        await access.EnsureAsync(form.ClientAccountId, "Form", ct);
        return form;
    }

    private async Task<string> FreeSlugAsync(Guid clientId, string slug, CancellationToken ct)
    {
        var baseSlug = slug.Length > 90 ? slug[..90].TrimEnd('-') : slug;
        for (var i = 2; i < 200; i++)
        {
            var candidate = $"{baseSlug}-copy{(i == 2 ? string.Empty : "-" + (i - 1))}";
            if (!await db.Set<LandingPage>().AnyAsync(p => p.ClientAccountId == clientId && p.Slug == candidate, ct)) return candidate;
        }
        return $"{baseSlug}-{Guid.NewGuid().ToString("N")[..6]}";
    }

    private static FormSchema ParseSchema(JsonElement json)
    {
        FormSchema? schema;
        try
        {
            schema = json.ValueKind == JsonValueKind.Object ? json.Deserialize<FormSchema>(FormSchemas.Json) : null;
        }
        catch (JsonException)
        {
            schema = null;
        }
        if (schema is null) throw Invalid("schema", "The form schema is required.");
        var errors = FormSchemas.ValidateSchema(schema);
        if (errors.Count > 0)
            throw new DomainException("forms.invalid", "The form template is invalid.", DomainErrorKind.Validation,
                errors.ToDictionary(e => "schema." + e.Key, e => e.Value));
        return schema;
    }

    private static string Cap(string value, int max) => value.Length <= max ? value : value[..max];

    private static DomainException Invalid(string field, string message) =>
        new("validation.failed", message, errors: new Dictionary<string, string[]> { [field] = new[] { message } });

    private static PageTemplateAdminDto ToDto(LandingPageTemplate t, int pages)
    {
        using var doc = JsonDocument.Parse(t.BlocksJson);
        return new PageTemplateAdminDto(t.Key, t.Name, t.Category, t.Description, t.MetaTitle, t.MetaDescription, t.FormTemplateKey,
            doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement.GetArrayLength() : 0, t.SortOrder, t.IsActive, t.IsCustom, t.IsCustomized, pages,
            t.ConcurrencyStamp);
    }

    private static FormTemplateAdminDto ToDto(FormTemplate t, int forms)
    {
        using var doc = JsonDocument.Parse(t.SchemaJson);
        return new FormTemplateAdminDto(t.Key, t.Name, t.Description, doc.RootElement.Clone(), t.SubmitLabel, t.SuccessMessage, t.ConsentText,
            t.AutoresponderSubject, t.AutoresponderBody, t.SortOrder, t.IsActive, t.IsCustom, t.IsCustomized, forms, t.ConcurrencyStamp);
    }
}

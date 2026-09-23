using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.EmailMarketing.Audiences;
using OptimizeAll.Api.Modules.EmailMarketing.Delivery;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Templates;

public sealed record TemplateListItem(Guid Id, Guid? ClientAccountId, string Name, string Category, string Subject, bool IsGlobal, bool IsArchived, DateTime UpdatedAt);

public sealed record TemplateDto(Guid Id, Guid? ClientAccountId, string Name, string Category, string Subject, string? PreviewText, JsonElement Design,
    bool IsGlobal, bool IsArchived, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class TemplateRequest
{
    public Guid? ClientAccountId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [MaxLength(40)] public string? Category { get; set; }
    [Required, MaxLength(200)] public string Subject { get; set; } = string.Empty;
    [MaxLength(200)] public string? PreviewText { get; set; }
    [Required] public JsonElement Design { get; set; }
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed class RenderRequest
{
    public Guid? ClientAccountId { get; set; }
    [MaxLength(200)] public string? Subject { get; set; }
    [MaxLength(200)] public string? PreviewText { get; set; }
    public JsonElement Design { get; set; }
}

public sealed record RenderResult(string Subject, string Html, string Text, int SizeBytes, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

public sealed class TestSendRequest
{
    public Guid? ClientAccountId { get; set; }
    [Required, MaxLength(254)] public string To { get; set; } = string.Empty;
    public Guid? SenderProfileId { get; set; }
}

public sealed record TestSendResult(bool Sent, string? ProviderMessageId, string? Error);

/// <summary>Content validation shared by templates, campaigns and automation emails.</summary>
public static class ContentValidation
{
    /// <summary>Hard errors: invalid structure, unknown merge tags, malformed tags.</summary>
    public static List<string> Errors(EmailDesign design, string? subject, string? previewText)
    {
        var errors = DesignRules.Validate(design);
        var unknown = DesignRules.MergeTagNames(design).Concat(MergeTags.Names(subject)).Concat(MergeTags.Names(previewText))
            .Where(n => !MergeTags.IsAllowed(n)).Distinct().ToList();
        if (unknown.Count > 0) errors.Add($"Unknown merge tags: {string.Join(", ", unknown.Select(u => "{{" + u + "}}"))}. Allowed: {string.Join(", ", MergeTags.Whitelist.Select(w => "{{" + w + "}}"))}, {{{{custom.<field>}}}}.");
        var texts = DesignRules.AllBlocks(design.Blocks).SelectMany(b => new[] { b.Html, b.Text, b.Title, b.Subtitle, b.Href, b.Alt }).Append(subject).Append(previewText);
        if (texts.Any(MergeTags.HasMalformedTags)) errors.Add("A merge tag is not closed or is malformed (use {{first_name|fallback}}).");
        return errors;
    }

    /// <summary>Stores rich text already sanitized, so what is saved is what is sent.</summary>
    public static void Sanitize(EmailDesign design)
    {
        foreach (var block in DesignRules.AllBlocks(design.Blocks))
            if (block.Html is not null) block.Html = HtmlSanitizer.Sanitize(block.Html);
    }

    public static EmailDesign Parse(JsonElement json)
    {
        try { return EmailDesign.Parse(json.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : json.GetRawText()); }
        catch (FormatException ex) { throw new DomainException("email.design_invalid", ex.Message); }
    }
}

public sealed class TemplateService(
    AppDbContext db,
    EmailAccess access,
    IAuditLogger audit,
    ICurrentUser currentUser,
    MessageComposer composer,
    EmailSettingsStore settings,
    EmailProviderResolver providers,
    IOptions<EmailOptions> emailOptions)
{
    public async Task<IReadOnlyList<TemplateListItem>> ListAsync(Guid? clientId, bool includeGlobal, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(clientId, ct);
        var key = Workspace.Key(clientId);
        var q = db.Set<EmailTemplate>().AsNoTracking().Where(t => !t.IsArchived && (t.ScopeKey == key || (includeGlobal && t.ScopeKey == Workspace.AgencyKey)));
        return await q.OrderBy(t => t.ScopeKey == key ? 0 : 1).ThenBy(t => t.Category).ThenBy(t => t.Name)
            .Select(t => new TemplateListItem(t.Id, t.ClientAccountId, t.Name, t.Category, t.Subject, t.ClientAccountId == null, t.IsArchived, t.UpdatedAt))
            .ToListAsync(ct);
    }

    /// <summary>Loads a template visible to staff: its own workspace, or an agency (global) template.</summary>
    public Task<EmailTemplate> LoadAsync(Guid id, CancellationToken ct) =>
        access.LoadAsync(db.Set<EmailTemplate>().Where(t => t.Id == id), t => t.ClientAccountId, "Template", ct);

    public async Task<TemplateDto> GetAsync(Guid id, CancellationToken ct) => ToDto(await LoadAsync(id, ct));

    public async Task<TemplateDto> CreateAsync(TemplateRequest r, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(r.ClientAccountId, ct);
        var design = Validated(r);
        var template = new EmailTemplate
        {
            ClientAccountId = r.ClientAccountId, ScopeKey = Workspace.Key(r.ClientAccountId), Name = r.Name.Trim(),
            Category = Text.Clean(r.Category, 40) ?? "other", Subject = r.Subject.Trim(), PreviewText = Text.Clean(r.PreviewText, 200), DesignJson = design.ToJson(),
        };
        db.Set<EmailTemplate>().Add(template);
        audit.Record("email.template.created", nameof(EmailTemplate), template.Id, after: new { template.Name, template.Category, template.ScopeKey });
        await db.SaveChangesAsync(ct);
        return ToDto(template);
    }

    public async Task<TemplateDto> UpdateAsync(Guid id, TemplateRequest r, CancellationToken ct)
    {
        var template = await LoadAsync(id, ct);
        AudienceService.ExpectStamp(template.ConcurrencyStamp, r.ConcurrencyStamp);
        var design = Validated(r);
        var before = new { template.Name, template.Subject, template.Category };
        template.Name = r.Name.Trim();
        template.Category = Text.Clean(r.Category, 40) ?? template.Category;
        template.Subject = r.Subject.Trim();
        template.PreviewText = Text.Clean(r.PreviewText, 200);
        template.DesignJson = design.ToJson();
        audit.Record("email.template.updated", nameof(EmailTemplate), template.Id, before, new { template.Name, template.Subject, template.Category });
        await db.SaveChangesAsync(ct);
        return ToDto(template);
    }

    /// <summary>Copies a template (e.g. an agency starter template) into a workspace.</summary>
    public async Task<TemplateDto> DuplicateAsync(Guid id, Guid? targetClientId, CancellationToken ct)
    {
        var source = await LoadAsync(id, ct);
        await access.EnsureStaffWorkspaceAsync(targetClientId, ct);
        var copy = new EmailTemplate
        {
            ClientAccountId = targetClientId, ScopeKey = Workspace.Key(targetClientId), Name = Text.Truncate(source.Name + " (copy)", 150), Category = source.Category,
            Subject = source.Subject, PreviewText = source.PreviewText, DesignJson = source.DesignJson,
        };
        db.Set<EmailTemplate>().Add(copy);
        audit.Record("email.template.duplicated", nameof(EmailTemplate), copy.Id, after: new { from = source.Id, copy.ScopeKey });
        await db.SaveChangesAsync(ct);
        return ToDto(copy);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        var template = await LoadAsync(id, ct);
        template.IsArchived = true;
        audit.Record("email.template.archived", nameof(EmailTemplate), template.Id, before: new { template.Name });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Renders unsaved content with sample data (desktop/mobile preview in the editor).</summary>
    public async Task<RenderResult> RenderAsync(RenderRequest r, CancellationToken ct)
    {
        await access.EnsureStaffWorkspaceAsync(r.ClientAccountId, ct);
        var design = ContentValidation.Parse(r.Design);
        var errors = ContentValidation.Errors(design, r.Subject, r.PreviewText);
        ContentValidation.Sanitize(design);
        var ws = await settings.GetAsync(r.ClientAccountId, ct);
        var composed = composer.Compose(design, r.Subject ?? string.Empty, r.PreviewText, composer.SampleValues(ws), TokenSource.Subscriber, Guid.Empty, null);
        var warnings = new List<string>();
        if (!DesignRules.HasFooter(design)) warnings.Add("Add the footer block: every marketing email needs the physical address and an unsubscribe link, and sending is blocked without it.");
        if (string.IsNullOrWhiteSpace(ws.PhysicalAddress)) warnings.Add("The workspace has no physical postal address yet (Email settings).");
        warnings.AddRange(ImageAltWarnings(design));
        var size = ContentChecks.SizeBytes(composed.Html);
        if (size > ContentChecks.GmailClipBytes) warnings.Add($"The email is {size / 1024} KB; Gmail clips messages over 102 KB.");
        var spam = ContentChecks.FindSpamPhrases(r.Subject, composed.Text);
        if (spam.Count > 0) warnings.Add($"Phrases that often trigger spam filters: {string.Join(", ", spam)}.");
        warnings.AddRange(ContentChecks.SubjectWarnings(r.Subject));
        return new RenderResult(composed.Subject, composed.Html, composed.Text, size, errors, warnings);
    }

    public static IEnumerable<string> ImageAltWarnings(EmailDesign design)
    {
        var missing = DesignRules.AllBlocks(design.Blocks).Count(b => b.Type == "image" && string.IsNullOrWhiteSpace(b.Alt));
        if (missing > 0) yield return $"{missing} image(s) have no alt text (shown when images are blocked, read by screen readers).";
    }

    public async Task<TestSendResult> TestSendTemplateAsync(Guid id, TestSendRequest r, CancellationToken ct)
    {
        var template = await LoadAsync(id, ct);
        var clientId = r.ClientAccountId ?? template.ClientAccountId;
        await access.EnsureStaffWorkspaceAsync(clientId, ct);
        return await TestSendAsync(clientId, EmailDesign.Parse(template.DesignJson), template.Subject, template.PreviewText, r, ct);
    }

    /// <summary>
    /// Sends a test to a verified staff address only (never to arbitrary recipients: tests bypass consent, so they are
    /// limited to agency staff with a verified email). Subject is prefixed with [Test].
    /// </summary>
    public async Task<TestSendResult> TestSendAsync(Guid? clientId, EmailDesign design, string subject, string? preview, TestSendRequest r, CancellationToken ct)
    {
        var normalized = Normalization.Email(r.To);
        var staff = await db.Set<User>().AsNoTracking()
            .Where(u => u.NormalizedEmail == normalized && u.EmailVerifiedAt != null && u.Status == UserStatus.Active)
            .Select(u => new { u.Email, u.DisplayName, Roles = u.Roles.Select(x => x.Role).ToList() }).FirstOrDefaultAsync(ct);
        if (staff is null || !staff.Roles.Any(role => role is not (Role.Participant or Role.Client)))
            throw new DomainException("email.test_recipient_not_allowed", "Test emails can only be sent to a verified staff email address.");

        var errors = ContentValidation.Errors(design, subject, preview);
        if (errors.Count > 0) throw EmailProblem.Invalid("email.design_invalid", "Fix the content errors before sending a test.", errors);
        ContentValidation.Sanitize(design);
        var ws = await settings.GetAsync(clientId, ct);
        var sender = await ResolveSenderAsync(clientId, r.SenderProfileId, ct);
        var composed = composer.Compose(design, subject, preview, composer.SampleValues(ws), TokenSource.Subscriber, Guid.Empty, null);
        var provider = await providers.ForWorkspaceAsync(clientId, ct);
        var result = await provider.SendAsync(new OutboundEmail(clientId, staff.Email, staff.DisplayName,
            sender?.FromEmail ?? emailOptions.Value.FromAddress, sender?.FromName ?? emailOptions.Value.FromName, sender?.ReplyTo,
            "[Test] " + composed.Subject, composed.Html, composed.Text, new Dictionary<string, string>(),
            new Dictionary<string, string> { ["oa_kind"] = "test" }), ct);
        audit.Record("email.test_sent", "EmailTest", Workspace.Key(clientId), after: new { to = staff.Email, subject, outcome = result.Outcome.ToString(), by = currentUser.IdOrNull });
        await db.SaveChangesAsync(ct);
        return new TestSendResult(result.Outcome == ProviderOutcome.Accepted, result.MessageId, result.Outcome == ProviderOutcome.Accepted ? null : result.Error);
    }

    private async Task<SenderProfile?> ResolveSenderAsync(Guid? clientId, Guid? senderId, CancellationToken ct)
    {
        var key = Workspace.Key(clientId);
        var q = db.Set<SenderProfile>().AsNoTracking().Where(p => p.ScopeKey == key && p.VerifiedAt != null);
        return senderId is { } id ? await q.FirstOrDefaultAsync(p => p.Id == id, ct) : await q.OrderByDescending(p => p.IsDefault).FirstOrDefaultAsync(ct);
    }

    private static EmailDesign Validated(TemplateRequest r)
    {
        var design = ContentValidation.Parse(r.Design);
        var errors = ContentValidation.Errors(design, r.Subject, r.PreviewText);
        if (errors.Count > 0) throw EmailProblem.Invalid("email.design_invalid", "The email content is invalid.", errors, "design");
        ContentValidation.Sanitize(design);
        return design;
    }

    private static TemplateDto ToDto(EmailTemplate t) => new(t.Id, t.ClientAccountId, t.Name, t.Category, t.Subject, t.PreviewText,
        JsonDocument.Parse(t.DesignJson).RootElement.Clone(), t.ClientAccountId == null, t.IsArchived, t.UpdatedAt, t.ConcurrencyStamp);
}

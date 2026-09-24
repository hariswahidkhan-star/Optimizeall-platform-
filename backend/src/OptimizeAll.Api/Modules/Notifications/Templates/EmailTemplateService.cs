using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Content.Copy;
using OptimizeAll.Api.Modules.Website.Settings;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Content;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Notifications.Templates;

// ---------- DTOs ----------

public sealed record EmailVariableDto(string Name, string Description, string Sample, bool Required);

public sealed record EmailTemplateSummaryDto(string Key, string Group, string Name, string Description, string Subject, bool IsCustomized, DateTime? UpdatedAt);

public sealed record EmailTemplateDto(
    string Key, string Group, string Name, string Description, string Subject, string Body, string? ActionLabel,
    string DefaultSubject, string DefaultBody, string? DefaultActionLabel, bool HasActionLabel, bool HasHtml,
    IReadOnlyList<EmailVariableDto> Variables, bool IsCustomized, DateTime? UpdatedAt, Guid? ConcurrencyStamp);

public sealed record EmailPreviewDto(string Subject, string Text, string? Html);

public class EmailTemplateInput
{
    [Required, MinLength(1), MaxLength(300)]
    public string Subject { get; set; } = string.Empty;

    [Required, MinLength(1), MaxLength(20000)]
    public string Body { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? ActionLabel { get; set; }
}

public sealed class UpdateEmailTemplateRequest : EmailTemplateInput
{
    /// <summary>The stamp of the override being replaced; <c>null</c> when the template currently uses its default.</summary>
    public Guid? ConcurrencyStamp { get; set; }
}

public sealed record RenderedEmail(string Subject, string Text, string? Html);

/// <summary>Effective (overridden or default) template texts.</summary>
public sealed record EmailTemplateTexts(string Subject, string Body, string? ActionLabel);

/// <summary>
/// Renders <c>{{variable}}</c> templates. Text output substitutes values verbatim; HTML output encodes every literal and
/// value, turns blank lines into paragraphs and line breaks into &lt;br&gt;, and replaces the <c>{{action}}</c>
/// placeholder with a button.
/// </summary>
public static partial class EmailTemplateRenderer
{
    public static string RenderText(string template, IReadOnlyDictionary<string, string> values) =>
        VariableRegex().Replace(template, m => values.GetValueOrDefault(m.Groups[1].Value) ?? string.Empty);

    public static string RenderHtmlFragment(string template, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string>? rawHtml = null)
    {
        var enc = HtmlEncoder.Default;
        var sb = new StringBuilder();
        var last = 0;
        foreach (Match m in VariableRegex().Matches(template))
        {
            sb.Append(EncodeText(template[last..m.Index]));
            var name = m.Groups[1].Value;
            if (rawHtml is not null && rawHtml.TryGetValue(name, out var html)) sb.Append(html);
            else sb.Append(EncodeText(values.GetValueOrDefault(name) ?? string.Empty));
            last = m.Index + m.Length;
        }
        sb.Append(EncodeText(template[last..]));
        var paragraphs = sb.ToString().Split("\n\n", StringSplitOptions.None)
            .Select(p => p.Trim('\n'))
            .Where(p => p.Trim().Length > 0)
            .Select(p => $"<p>{p.Replace("\n", "<br>")}</p>");
        return string.Concat(paragraphs);

        string EncodeText(string s) => enc.Encode(s).Replace("&#xA;", "\n");
    }

    public static IReadOnlyList<string> Variables(string template) =>
        VariableRegex().Matches(template).Select(m => m.Groups[1].Value).Distinct().ToList();

    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9]*)\s*\}\}")]
    private static partial Regex VariableRegex();
}

/// <summary>Lists, edits, previews and applies the editable email templates (<see cref="EmailTemplateCatalog"/>).</summary>
public sealed class EmailTemplateService(AppDbContext db, IAuditLogger audit, ICurrentUser user, IDatabaseDialect dialect)
{
    private Dictionary<string, EmailTemplateOverride>? _overrides;
    private string? _siteName;

    public async Task<IReadOnlyList<EmailTemplateSummaryDto>> ListAsync(CancellationToken ct)
    {
        var overrides = await OverridesAsync(ct);
        return EmailTemplateCatalog.All.Select(d =>
        {
            overrides.TryGetValue(d.Key, out var o);
            return new EmailTemplateSummaryDto(d.Key, d.Group, d.Name, d.Description, o?.Subject ?? d.Subject, o is not null, o?.UpdatedAt);
        }).ToList();
    }

    public async Task<EmailTemplateDto> GetAsync(string key, CancellationToken ct)
    {
        var def = Definition(key);
        var o = await db.Set<EmailTemplateOverride>().AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, ct);
        return ToDto(def, o);
    }

    public async Task<EmailTemplateDto> UpdateAsync(string key, UpdateEmailTemplateRequest request, CancellationToken ct)
    {
        var def = Definition(key);
        var texts = Validate(def, request);
        var row = await db.Set<EmailTemplateOverride>().FirstOrDefaultAsync(x => x.Key == key, ct);
        if (row is not null) ConcurrencyGuard.Apply(db, row, request.ConcurrencyStamp ?? Guid.Empty);
        else if (request.ConcurrencyStamp is not null) throw Changed();

        var isDefault = texts.Subject == def.Subject && texts.Body == def.Body && texts.ActionLabel == def.ActionLabel;
        if (isDefault)
        {
            if (row is not null)
            {
                audit.Record("content.email_template_reset", nameof(EmailTemplateOverride), key, new { row.Subject, row.Body, row.ActionLabel });
                db.Remove(row);
            }
        }
        else if (row is null)
        {
            row = new EmailTemplateOverride
            {
                Key = key, Subject = texts.Subject, Body = texts.Body, ActionLabel = texts.ActionLabel, UpdatedByUserId = user.IdOrNull,
            };
            db.Add(row);
            audit.Record("content.email_template_updated", nameof(EmailTemplateOverride), key,
                new { def.Subject, def.Body, def.ActionLabel }, new { texts.Subject, texts.Body, texts.ActionLabel });
        }
        else
        {
            audit.Record("content.email_template_updated", nameof(EmailTemplateOverride), key,
                new { row.Subject, row.Body, row.ActionLabel }, new { texts.Subject, texts.Body, texts.ActionLabel });
            row.Subject = texts.Subject;
            row.Body = texts.Body;
            row.ActionLabel = texts.ActionLabel;
            row.UpdatedByUserId = user.IdOrNull;
        }
        await SaveAsync(ct);
        _overrides = null;
        return await GetAsync(key, ct);
    }

    public async Task ResetAsync(string key, Guid? stamp, CancellationToken ct)
    {
        Definition(key);
        var row = await db.Set<EmailTemplateOverride>().FirstOrDefaultAsync(x => x.Key == key, ct);
        if (row is null) return;
        ConcurrencyGuard.Apply(db, row, stamp ?? Guid.Empty);
        audit.Record("content.email_template_reset", nameof(EmailTemplateOverride), key, new { row.Subject, row.Body, row.ActionLabel });
        db.Remove(row);
        await SaveAsync(ct);
        _overrides = null;
    }

    /// <summary>Renders unsaved texts with the sample values (a notification template is shown inside the current layout).</summary>
    public async Task<EmailPreviewDto> PreviewAsync(string key, EmailTemplateInput input, CancellationToken ct)
    {
        var def = Definition(key);
        var texts = Validate(def, input);
        var samples = def.Variables.ToDictionary(v => v.Name, v => v.Sample);
        if (key == EmailTemplateCatalog.LayoutKey)
        {
            var r = RenderNotification(texts, new EmailTemplateTexts("{{title}}", "{{body}}", null), samples["displayName"],
                "Your submission was approved", "Your post for Summer Launch was approved. The reward has been added to your earnings.",
                "https://app.example.com/app/submissions/123", "https://app.example.com" + AppLinks.NotificationPreferences, await SiteNameAsync(ct));
            return new EmailPreviewDto(r.Subject, r.Text, r.Html);
        }
        if (key.StartsWith(EmailTemplateCatalog.NotificationPrefix, StringComparison.Ordinal))
        {
            var layout = await TextsAsync(EmailTemplateCatalog.LayoutKey, ct);
            var r = RenderNotification(layout, texts, samples["displayName"], samples["title"], samples["body"], samples["link"],
                "https://app.example.com" + AppLinks.NotificationPreferences, await SiteNameAsync(ct));
            return new EmailPreviewDto(r.Subject, r.Text, r.Html);
        }
        var plain = Render(texts, samples);
        return new EmailPreviewDto(plain.Subject, plain.Text, null);
    }

    // ---------------------------------------------------------------- Use by senders

    /// <summary>The effective texts of a template: the saved override, or the shipped default.</summary>
    public async Task<EmailTemplateTexts> TextsAsync(string key, CancellationToken ct)
    {
        var def = Definition(key);
        var overrides = await OverridesAsync(ct);
        return overrides.TryGetValue(key, out var o)
            ? new EmailTemplateTexts(o.Subject, o.Body, def.ActionLabel is null ? null : o.ActionLabel ?? def.ActionLabel)
            : new EmailTemplateTexts(def.Subject, def.Body, def.ActionLabel);
    }

    /// <summary>Renders a website (plain text) template with the given values; <c>siteName</c> is filled in automatically.</summary>
    public async Task<RenderedEmail> RenderAsync(string key, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        var texts = await TextsAsync(key, ct);
        var all = new Dictionary<string, string>(values) { ["siteName"] = await SiteNameAsync(ct) };
        return Render(texts, all);
    }

    /// <summary>Builds a notification email from the layout and the notification type's template.</summary>
    public async Task<EmailMessage> ComposeNotificationAsync(Notification notification, User user, string appBaseUrl, CancellationToken ct)
    {
        var layout = await TextsAsync(EmailTemplateCatalog.LayoutKey, ct);
        var typeKey = EmailTemplateCatalog.NotificationKey(notification.Type);
        var own = EmailTemplateCatalog.Find(typeKey) is null
            ? new EmailTemplateTexts("{{title}}", "{{body}}", null)
            : await TextsAsync(typeKey, ct);
        var link = EmailLinks.Build(appBaseUrl, notification.LinkUrl);
        var preferences = EmailLinks.Build(appBaseUrl, AppLinks.NotificationPreferences)!;
        var r = RenderNotification(layout, own, user.DisplayName, notification.Title, notification.Body, link, preferences, await SiteNameAsync(ct));
        return new EmailMessage(user.Email, user.DisplayName, r.Subject, r.Text, r.Html);
    }

    /// <summary>
    /// Pure rendering of a notification email. The notification-settings link is always appended (it is not part of any
    /// template, so an edit can never remove it).
    /// </summary>
    public static RenderedEmail RenderNotification(
        EmailTemplateTexts layout, EmailTemplateTexts own, string displayName, string title, string body, string? link,
        string preferencesUrl, string siteName)
    {
        var enc = HtmlEncoder.Default;
        var values = new Dictionary<string, string>
        {
            ["title"] = title, ["body"] = body, ["link"] = link ?? string.Empty, ["displayName"] = displayName, ["siteName"] = siteName,
        };
        var subject = OneLine(EmailTemplateRenderer.RenderText(own.Subject, values));
        var content = EmailTemplateRenderer.RenderText(own.Body, values);
        var actionLabel = layout.ActionLabel ?? "Open";

        var layoutValues = new Dictionary<string, string>(values)
        {
            ["subject"] = subject,
            ["content"] = content,
            ["action"] = link is null ? string.Empty : $"{actionLabel}: {link}\n\n",
        };
        var finalSubject = OneLine(EmailTemplateRenderer.RenderText(layout.Subject, layoutValues));
        if (finalSubject.Length == 0) finalSubject = subject;
        var text = EmailTemplateRenderer.RenderText(layout.Body, layoutValues) + $"\nManage your notification settings: {preferencesUrl}\n";

        var button = link is null ? string.Empty
            : $"<a href=\"{enc.Encode(link)}\" style=\"background:#2563eb;color:#ffffff;padding:10px 16px;border-radius:6px;text-decoration:none\">{enc.Encode(actionLabel)}</a>\n\n";
        var fragment = EmailTemplateRenderer.RenderHtmlFragment(layout.Body, layoutValues, new Dictionary<string, string> { ["action"] = button });
        var html = "<!DOCTYPE html><html><body style=\"font-family:Arial,Helvetica,sans-serif;color:#1f2937;line-height:1.5\">" +
                   $"<h2 style=\"font-size:18px\">{enc.Encode(finalSubject)}</h2>" +
                   fragment +
                   $"<p style=\"font-size:12px;color:#6b7280\">{enc.Encode(siteName)} · <a href=\"{enc.Encode(preferencesUrl)}\">Notification settings</a></p>" +
                   "</body></html>";
        return new RenderedEmail(finalSubject, text, html);
    }

    private static RenderedEmail Render(EmailTemplateTexts texts, IReadOnlyDictionary<string, string> values) =>
        new(OneLine(EmailTemplateRenderer.RenderText(texts.Subject, values)), EmailTemplateRenderer.RenderText(texts.Body, values), null);

    private static string OneLine(string value) => string.Join(' ', value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    // ---------------------------------------------------------------- Helpers

    private static EmailTemplateDefinition Definition(string key) =>
        EmailTemplateCatalog.Find(key) ?? throw new DomainException("email_template.not_found", "Email template was not found.", DomainErrorKind.NotFound);

    private static EmailTemplateTexts Validate(EmailTemplateDefinition def, EmailTemplateInput input)
    {
        var e = new FieldErrors();
        var subject = PlainText.Clean(input.Subject);
        var body = PlainText.Clean(input.Body);
        var label = def.ActionLabel is null ? null : PlainText.Clean(input.ActionLabel);
        if (subject.Length == 0) e.Add("subject", "Enter the subject.");
        if (subject.Contains('\n')) e.Add("subject", "Use a single line.");
        if (body.Length == 0) e.Add("body", "Enter the email text.");
        if (def.ActionLabel is not null && string.IsNullOrEmpty(label)) e.Add("actionLabel", "Enter the button label.");

        var allowed = def.Variables.Select(v => v.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var (field, text) in new[] { ("subject", subject), ("body", body) })
        {
            var unknown = EmailTemplateRenderer.Variables(text).Where(v => !allowed.Contains(v)).ToList();
            if (unknown.Count > 0)
                e.Add(field, $"Unknown variable {{{{{unknown[0]}}}}}. Available: {string.Join(", ", def.Variables.Select(v => "{{" + v.Name + "}}"))}.");
        }
        // Required variables (the confirmation / unsubscribe links, the layout's content) must be in the email text itself:
        // a subject line is flattened to one line and never carries the link the recipient needs.
        var used = EmailTemplateRenderer.Variables(body);
        foreach (var required in def.Variables.Where(v => v.Required && !used.Contains(v.Name)))
            e.Add("body", $"Keep {{{{{required.Name}}}}} in the email ({required.Description.TrimEnd('.').ToLowerInvariant()}).");
        e.ThrowIfAny("email_template.invalid", "The template is invalid.");
        return new EmailTemplateTexts(subject, body, string.IsNullOrEmpty(label) ? null : label);
    }

    private async Task<Dictionary<string, EmailTemplateOverride>> OverridesAsync(CancellationToken ct) =>
        _overrides ??= await db.Set<EmailTemplateOverride>().AsNoTracking().ToDictionaryAsync(x => x.Key, ct);

    private async Task<string> SiteNameAsync(CancellationToken ct)
    {
        if (_siteName is not null) return _siteName;
        var doc = await db.Set<Domain.Website.SiteSettingsDocument>().AsNoTracking()
            .FirstOrDefaultAsync(d => d.Key == Domain.Website.SiteSettingsDocument.DefaultKey, ct);
        return _siteName = SiteSettingsService.Parse(doc?.Json).SiteName;
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            throw Changed();
        }
    }

    private static DomainException Changed() =>
        DomainException.Conflict("concurrency.conflict", "This template was changed by someone else. Reload and try again.");

    private static EmailTemplateDto ToDto(EmailTemplateDefinition d, EmailTemplateOverride? o) => new(
        d.Key, d.Group, d.Name, d.Description, o?.Subject ?? d.Subject, o?.Body ?? d.Body,
        d.ActionLabel is null ? null : o?.ActionLabel ?? d.ActionLabel,
        d.Subject, d.Body, d.ActionLabel, d.ActionLabel is not null, d.HasHtml,
        d.Variables.Select(v => new EmailVariableDto(v.Name, v.Description, v.Sample, v.Required)).ToList(),
        o is not null, o?.UpdatedAt, o?.ConcurrencyStamp);
}

/// <summary>Link rules shared by email senders.</summary>
public static class EmailLinks
{
    /// <summary>App-relative links are prefixed with the app base URL; absolute https links are kept; anything else is dropped.</summary>
    public static string? Build(string appBaseUrl, string? linkUrl)
    {
        if (string.IsNullOrWhiteSpace(linkUrl)) return null;
        if (linkUrl.StartsWith('/') && !linkUrl.StartsWith("//", StringComparison.Ordinal))
            return appBaseUrl.TrimEnd('/') + linkUrl;
        return Uri.TryCreate(linkUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? linkUrl : null;
    }
}

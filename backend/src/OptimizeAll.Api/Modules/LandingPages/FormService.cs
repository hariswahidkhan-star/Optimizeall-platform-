using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.LandingPages;

public sealed class FormInput
{
    public string Name { get; set; } = string.Empty;
    public FormStatus Status { get; set; } = FormStatus.Active;
    public JsonElement Schema { get; set; }
    public string SubmitLabel { get; set; } = "Submit";
    public string SuccessMessage { get; set; } = string.Empty;
    public string? RedirectUrl { get; set; }
    public List<Guid> NotifyUserIds { get; set; } = new();
    public bool AutoresponderEnabled { get; set; }
    public string? AutoresponderSubject { get; set; }
    public string? AutoresponderBody { get; set; }
    public List<string> AllowedOrigins { get; set; } = new();
    public string? ConsentText { get; set; }
    public CaptchaProvider Captcha { get; set; }
    public int MinFillSeconds { get; set; } = 3;
}

/// <summary>Form definitions: validation, consent versioning and creation from templates.</summary>
public sealed class FormService(AppDbContext db, TimeProvider clock)
{
    public const int MaxMinFillSeconds = 60;

    public async Task<Form> CreateFromTemplateAsync(Guid clientId, string templateKey, string name, CancellationToken ct)
    {
        var template = await db.Set<FormTemplate>().AsNoTracking().FirstOrDefaultAsync(t => t.Key == templateKey, ct)
                       ?? throw new DomainException("forms.template_not_found", "Unknown form template.");
        var form = new Form
        {
            ClientAccountId = clientId, Name = name.Length > 150 ? name[..150] : name, SchemaJson = template.SchemaJson,
            SubmitLabel = template.SubmitLabel, SuccessMessage = template.SuccessMessage, TemplateKey = template.Key,
            AutoresponderSubject = template.AutoresponderSubject, AutoresponderBody = template.AutoresponderBody,
            AutoresponderEnabled = template.AutoresponderSubject is not null,
        };
        db.Add(form);
        if (!string.IsNullOrWhiteSpace(template.ConsentText)) AddConsentVersion(form, template.ConsentText);
        await db.SaveChangesAsync(ct);
        return form;
    }

    /// <summary>Validates <paramref name="input"/> and applies it to <paramref name="form"/> (new consent text → new consent version).</summary>
    public async Task ApplyAsync(Form form, FormInput input, CancellationToken ct)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var l)) errors[key] = l = new List<string>();
            l.Add(message);
        }

        FormSchema? schema = null;
        try
        {
            schema = input.Schema.ValueKind == JsonValueKind.Object ? input.Schema.Deserialize<FormSchema>(FormSchemas.Json) : null;
            if (schema is null) Add("schema", "The form schema is required.");
        }
        catch (JsonException ex)
        {
            Add("schema", "The form schema is not valid: " + (ex.Message.Length > 200 ? ex.Message[..200] : ex.Message));
        }
        if (schema is not null)
            foreach (var (key, messages) in FormSchemas.ValidateSchema(schema))
                foreach (var m in messages) Add($"schema.{key}", m);

        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 150) Add("name", "A name (max 150 characters) is required.");
        if (string.IsNullOrWhiteSpace(input.SubmitLabel) || input.SubmitLabel.Trim().Length > 60) Add("submitLabel", "A button label (max 60 characters) is required.");
        if (string.IsNullOrWhiteSpace(input.SuccessMessage) || input.SuccessMessage.Length > 1000) Add("successMessage", "A success message (max 1000 characters) is required.");
        if (!string.IsNullOrWhiteSpace(input.RedirectUrl) &&
            (!Uri.TryCreate(input.RedirectUrl.Trim(), UriKind.Absolute, out var redirect) || redirect.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(redirect.UserInfo)))
            Add("redirectUrl", "The redirect must be an absolute https URL.");
        if (input.MinFillSeconds is < 0 or > MaxMinFillSeconds) Add("minFillSeconds", $"Between 0 and {MaxMinFillSeconds} seconds.");

        var origins = new List<string>();
        foreach (var raw in input.AllowedOrigins.Where(o => !string.IsNullOrWhiteSpace(o)))
        {
            var origin = NormalizeOrigin(raw);
            if (origin is null) Add("allowedOrigins", $"'{(raw.Length > 60 ? raw[..60] : raw)}' is not an origin such as https://www.example.com.");
            else origins.Add(origin);
        }
        if (origins.Count > 20) Add("allowedOrigins", "At most 20 origins.");

        if (input.AutoresponderEnabled)
        {
            if (string.IsNullOrWhiteSpace(input.AutoresponderSubject) || input.AutoresponderSubject.Length > 200) Add("autoresponderSubject", "A subject (max 200 characters) is required.");
            if (string.IsNullOrWhiteSpace(input.AutoresponderBody) || input.AutoresponderBody.Length > 5000) Add("autoresponderBody", "A message (max 5000 characters) is required.");
            if (schema is not null && !schema.AllFields.Any(f => f.Type == "email")) Add("autoresponderEnabled", "Add an email field so the autoresponder has an address to reply to.");
        }
        if (schema is not null && schema.AllFields.Any(f => f.Type == "consent") && string.IsNullOrWhiteSpace(input.ConsentText))
            Add("consentText", "Consent fields need the consent wording.");
        if (input.ConsentText is { Length: > 2000 }) Add("consentText", "At most 2000 characters.");

        var notify = input.NotifyUserIds.Distinct().ToList();
        if (notify.Count > 20) Add("notifyUserIds", "At most 20 people can be notified.");
        else if (notify.Count > 0)
        {
            var staff = await StaffCandidatesQuery().Where(u => notify.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
            if (staff.Count != notify.Count) Add("notifyUserIds", "Notifications can only go to active staff who manage forms.");
        }

        if (errors.Count > 0)
            throw new DomainException("forms.invalid", "The form is invalid.", DomainErrorKind.Validation, errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));

        form.Name = input.Name.Trim();
        form.Status = input.Status;
        form.SchemaJson = FormSchemas.Serialize(schema!);
        form.SubmitLabel = input.SubmitLabel.Trim();
        form.SuccessMessage = input.SuccessMessage.Trim();
        form.RedirectUrl = string.IsNullOrWhiteSpace(input.RedirectUrl) ? null : input.RedirectUrl.Trim();
        form.NotifyUserIds = notify;
        form.AutoresponderEnabled = input.AutoresponderEnabled;
        form.AutoresponderSubject = input.AutoresponderSubject?.Trim();
        form.AutoresponderBody = input.AutoresponderBody?.Trim();
        form.AllowedOrigins = origins.Distinct().ToList();
        form.Captcha = input.Captcha;
        form.MinFillSeconds = input.MinFillSeconds;
        var consent = string.IsNullOrWhiteSpace(input.ConsentText) ? null : input.ConsentText.Trim();
        if (consent != form.ConsentText)
        {
            if (consent is null) form.ConsentText = null;
            else AddConsentVersion(form, consent);
        }
    }

    private void AddConsentVersion(Form form, string text)
    {
        form.ConsentVersion += 1;
        form.ConsentText = text;
        db.Add(new FormConsentVersion { FormId = form.Id, Version = form.ConsentVersion, Text = text, CreatedAt = clock.GetUtcNow().UtcDateTime });
    }

    /// <summary>Active staff whose roles grant forms.manage (notification recipients).</summary>
    public IQueryable<User> StaffCandidatesQuery()
    {
        var roles = Enum.GetValues<Role>().Where(r => RolePermissions.For(r).Contains(Permissions.FormsManage)).ToList();
        return db.Set<User>().AsNoTracking().Where(u => u.Status == UserStatus.Active && u.Roles.Any(r => roles.Contains(r.Role)));
    }

    /// <summary>Scheme + host (+ non-default port) of any http(s) URL, lower-cased (e.g. the web app's own origin).</summary>
    public static string? CanonicalOrigin(string? raw) =>
        Uri.TryCreate(raw?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0
            ? uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant()
            : null;

    /// <summary>"https://Example.com/" → "https://example.com"; http only for localhost. Null when not an origin.</summary>
    public static string? NormalizeOrigin(string raw)
    {
        if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri)) return null;
        if (!string.IsNullOrEmpty(uri.UserInfo) || (uri.AbsolutePath != "/" && uri.AbsolutePath.Length > 0) || uri.Query.Length > 0) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)) return null;
        return uri.IsDefaultPort ? $"{uri.Scheme}://{uri.IdnHost.ToLowerInvariant()}" : $"{uri.Scheme}://{uri.IdnHost.ToLowerInvariant()}:{uri.Port}";
    }
}

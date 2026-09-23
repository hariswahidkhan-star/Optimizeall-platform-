using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.LandingPages;

public sealed class SubmissionPayload
{
    /// <summary>Field key → value (string, number, boolean or array of strings for multiselect).</summary>
    public Dictionary<string, JsonElement> Values { get; set; } = new();

    /// <summary>Honeypot: must be empty (the field is hidden from people).</summary>
    public string? Hp { get; set; }

    /// <summary>Render token from the form definition (minimum fill time).</summary>
    public string? Token { get; set; }

    public string? CaptchaToken { get; set; }
    public Guid? LandingPageId { get; set; }
    public string? VariantKey { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmTerm { get; set; }
    public string? UtmContent { get; set; }
    public string? Referrer { get; set; }
}

public sealed record SubmissionRequestContext(string? IpAddress, string? Origin, string? EmbedOrigin, string AppOrigin);

public sealed record SubmissionOutcome(Guid? SubmissionId, string Message, string? RedirectUrl);

/// <summary>Links used in staff notifications (mirror these in AppLinks when the lead wires the routes).</summary>
public static class FormLinks
{
    public static string Submissions(Guid formId) => $"/agency/pages/forms/{formId}/submissions";
}

/// <summary>
/// Public form submission pipeline: origin allow-list, honeypot, signed minimum-fill-time token, per-IP rate limit,
/// optional CAPTCHA, schema validation with conditional logic, file checks, landing-page attribution, then one write
/// transaction (submission, files, staff notifications, autoresponder outbox). After commit the submission is claimed
/// with a conditional update and <see cref="FormSubmitted"/> is published exactly once.
/// </summary>
public sealed class FormSubmissionService(
    AppDbContext db, IDatabaseDialect dialect, IPrivacyHasher hasher, FormRenderTokens tokens, CaptchaVerifier captcha,
    FormFileStore fileStore, INotificationService notifications, IEventPublisher events, TimeProvider clock, ILogger<FormSubmissionService> logger)
{
    public const int MaxPerFormPerIp = 5;
    public const int MaxPerIp = 20;
    public static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(10);

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Form> LoadActiveFormAsync(Guid formId, CancellationToken ct) =>
        await db.Set<Form>().AsNoTracking().FirstOrDefaultAsync(f => f.Id == formId && f.Status == FormStatus.Active, ct)
        ?? throw DomainException.NotFound("Form");

    /// <summary>
    /// Embedding rules: a declared embed origin (X-Embed-Origin, sent by the /f/{id} page) must be on the form's allow-list;
    /// a browser Origin other than the web app must also be on it.
    /// </summary>
    public static void CheckOrigins(Form form, string? origin, string? embedOrigin, string appOrigin)
    {
        if (!string.IsNullOrWhiteSpace(embedOrigin))
        {
            var normalized = FormService.CanonicalOrigin(embedOrigin);
            if (normalized is null || (normalized != appOrigin && !form.AllowedOrigins.Contains(normalized)))
                throw DomainException.Forbidden("forms.origin_not_allowed", "This form may not be embedded on this website.");
        }
        if (!string.IsNullOrWhiteSpace(origin))
        {
            // "null" (sandboxed/opaque origins) is treated like any other unknown origin.
            var normalized = FormService.CanonicalOrigin(origin);
            if (normalized is null || (normalized != appOrigin && !form.AllowedOrigins.Contains(normalized)))
                throw DomainException.Forbidden("forms.origin_not_allowed", "Submissions from this website are not allowed.");
        }
    }

    public async Task<SubmissionOutcome> SubmitAsync(Guid formId, SubmissionPayload payload, IReadOnlyList<UploadedFile> files,
        SubmissionRequestContext context, CancellationToken ct)
    {
        var form = await LoadActiveFormAsync(formId, ct);
        CheckOrigins(form, context.Origin, context.EmbedOrigin, context.AppOrigin);
        var accepted = new SubmissionOutcome(null, form.SuccessMessage, form.RedirectUrl);

        // Honeypot: pretend success so bots learn nothing; nothing is stored or published.
        if (!string.IsNullOrEmpty(payload.Hp))
        {
            logger.LogInformation("Form {FormId}: honeypot submission discarded", formId);
            return accepted;
        }

        var elapsed = tokens.ElapsedSeconds(payload.Token, formId)
                      ?? throw new DomainException("forms.token_invalid", "This form has expired. Reload the page and try again.");
        if (elapsed < form.MinFillSeconds)
            throw new DomainException("forms.too_fast", "That was quick! Please take a moment to review your answers and submit again.");

        var ipHash = hasher.Hash(context.IpAddress);
        if (ipHash is not null)
        {
            var since = Now - RateWindow;
            var recent = db.Set<FormSubmission>().AsNoTracking().Where(s => s.IpHash == ipHash && s.SubmittedAt >= since);
            if (await recent.CountAsync(s => s.FormId == formId, ct) >= MaxPerFormPerIp || await recent.CountAsync(ct) >= MaxPerIp)
                throw new DomainException("forms.rate_limited", "Too many submissions from your network. Please try again in a few minutes.", DomainErrorKind.TooManyRequests);
        }

        if (await captcha.VerifyAsync(form.Captcha, form.ClientAccountId, payload.CaptchaToken, context.IpAddress, ct) == CaptchaVerifier.Result.Failed)
            throw new DomainException("forms.captcha_failed", "Please complete the verification challenge.");

        var schema = FormSchemas.Deserialize(form.SchemaJson);
        var raw = payload.Values.ToDictionary(v => v.Key, v => (IReadOnlyList<string>)ToStrings(v.Value));
        var result = FormSchemas.ValidateSubmission(schema, raw, files);
        if (!result.IsValid)
            throw new DomainException("forms.invalid_submission", "Please correct the highlighted fields.", DomainErrorKind.Validation, result.Errors);

        var (pageId, variantKey, experimentId) = await AttributionAsync(form, payload, ct);
        var values = result.Values;
        var submission = new FormSubmission
        {
            FormId = form.Id, ClientAccountId = form.ClientAccountId, LandingPageId = pageId, VariantKey = variantKey, ExperimentId = experimentId,
            DataJson = JsonSerializer.Serialize(values),
            Email = Clip(schema.AllFields.Where(f => f.Type == "email").Select(f => values.GetValueOrDefault(f.Key)).FirstOrDefault(v => v is not null), 254),
            Name = Clip(DisplayName(values), 200),
            Phone = Clip(schema.AllFields.Where(f => f.Type == "phone").Select(f => values.GetValueOrDefault(f.Key)).FirstOrDefault(v => v is not null), 40),
            UtmSource = Clip(payload.UtmSource ?? values.GetValueOrDefault("utm_source"), 100),
            UtmMedium = Clip(payload.UtmMedium ?? values.GetValueOrDefault("utm_medium"), 100),
            UtmCampaign = Clip(payload.UtmCampaign ?? values.GetValueOrDefault("utm_campaign"), 150),
            UtmTerm = Clip(payload.UtmTerm ?? values.GetValueOrDefault("utm_term"), 150),
            UtmContent = Clip(payload.UtmContent ?? values.GetValueOrDefault("utm_content"), 150),
            Referrer = Uri.TryCreate(payload.Referrer, UriKind.Absolute, out var r) && r.Scheme is "http" or "https" ? Clip(r.AbsoluteUri, 1000) : null,
            EmbedOrigin = FormService.CanonicalOrigin(context.EmbedOrigin),
            IpHash = ipHash,
            ConsentGiven = result.ConsentGiven,
            ConsentVersion = result.ConsentGiven && form.ConsentVersion > 0 ? form.ConsentVersion : null,
            SubmittedAt = Now,
        };

        var written = new List<string>();
        try
        {
            await using var tx = await dialect.BeginWriteTransactionAsync(db, ct);
            db.Add(submission);
            foreach (var file in result.Files)
            {
                var key = fileStore.NewKey(Now, file.Extension);
                // Images lose EXIF/GPS/XMP metadata before storage (same stripper as other uploads).
                var bytes = file.ContentType.StartsWith("image/", StringComparison.Ordinal)
                    ? OptimizeAll.Api.Modules.Files.ImageMetadataStripper.Strip(file.Content) : file.Content;
                await fileStore.WriteAsync(key, bytes, ct);
                written.Add(key);
                db.Add(new FormSubmissionFile
                {
                    SubmissionId = submission.Id, FormId = form.Id, FieldKey = file.FieldKey, FileName = file.FileName, ContentType = file.ContentType,
                    SizeBytes = bytes.LongLength, StorageKey = key,
                });
            }
            await StageFollowUpsAsync(form, submission, values, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            foreach (var key in written) fileStore.Delete(key);
            throw;
        }
        db.ChangeTracker.Clear();

        await PublishOnceAsync(submission.Id, ct);
        return accepted with { SubmissionId = submission.Id };
    }

    /// <summary>Claims the submission (EventPublishedAt null → now) and publishes FormSubmitted only if this call won the claim.</summary>
    public async Task<bool> PublishOnceAsync(Guid submissionId, CancellationToken ct)
    {
        var now = Now;
        var claimed = await db.Set<FormSubmission>().Where(s => s.Id == submissionId && s.EventPublishedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EventPublishedAt, now), ct);
        if (claimed == 0) return false;
        var s = await db.Set<FormSubmission>().AsNoTracking().FirstAsync(x => x.Id == submissionId, ct);
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(s.DataJson) ?? new Dictionary<string, string>();
        await events.PublishAsync(new FormSubmitted(s.Id, s.FormId, s.ClientAccountId, s.Email, s.Name, s.Phone, fields,
            s.UtmSource, s.UtmMedium, s.UtmCampaign, s.SubmittedAt), CancellationToken.None);
        return true;
    }

    private async Task StageFollowUpsAsync(Form form, FormSubmission submission, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        if (form.NotifyUserIds.Count > 0)
        {
            var summary = string.Join("\n", values.Where(v => !v.Key.StartsWith("utm_", StringComparison.Ordinal)).Take(12)
                .Select(v => $"{v.Key}: {(v.Value.Length > 200 ? v.Value[..200] + "…" : v.Value)}"));
            foreach (var userId in form.NotifyUserIds)
            {
                await notifications.StageAsync(new NotificationRequest(userId, "forms.submission",
                    $"New submission: {form.Name}",
                    $"{submission.Name ?? submission.Email ?? "Someone"} submitted “{form.Name}”.\n\n{summary}",
                    FormLinks.Submissions(form.Id), new[] { NotificationChannel.Email }), ct);
            }
        }

        if (form.AutoresponderEnabled && submission.Email is { } to && form.AutoresponderSubject is { } subject && form.AutoresponderBody is { } body)
        {
            db.Add(new FormEmailOutbox
            {
                Key = $"autoresponder:{submission.Id:N}", SubmissionId = submission.Id, ToAddress = to, ToName = submission.Name,
                Subject = Clip(Merge(subject, values, form), 200)!, Body = Clip(Merge(body, values, form), 10000)!,
                Status = OutboxEmailStatus.Pending, NextAttemptAt = Now, CreatedAt = Now,
            });
        }
    }

    private async Task<(Guid? PageId, string? VariantKey, Guid? ExperimentId)> AttributionAsync(Form form, SubmissionPayload payload, CancellationToken ct)
    {
        if (payload.LandingPageId is not { } pageId) return (null, null, null);
        var page = await db.Set<LandingPage>().AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pageId && p.ClientAccountId == form.ClientAccountId && p.Status == LandingPageStatus.Published, ct);
        if (page?.PublishedVersionId is not { } versionId) return (null, null, null);
        var version = await db.Set<LandingPageVersion>().AsNoTracking().FirstAsync(v => v.Id == versionId, ct);
        var snapshot = LandingPageService.ReadSnapshot(version);
        if (!LandingPageService.FormIdsIn(snapshot.Variants).Contains(form.Id)) return (null, null, null);
        var keys = LandingPageService.Variants(snapshot.Variants).Select(v => v.Key).ToList();
        var key = payload.VariantKey is { } k && keys.Contains(k) ? k : "A";
        return (page.Id, key, snapshot.ExperimentEnabled ? snapshot.ExperimentId : null);
    }

    private static List<string> ToStrings(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => new List<string> { value.GetString()! },
        JsonValueKind.Number => new List<string> { value.GetRawText() },
        JsonValueKind.True => new List<string> { "true" },
        JsonValueKind.False => new List<string>(),
        JsonValueKind.Array => value.EnumerateArray().Where(e => e.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText()).Take(100).ToList(),
        _ => new List<string>(),
    };

    private static string? DisplayName(IReadOnlyDictionary<string, string> v)
    {
        foreach (var key in new[] { "name", "full_name", "fullName", "fullname" })
            if (v.TryGetValue(key, out var n)) return n;
        var first = v.GetValueOrDefault("first_name") ?? v.GetValueOrDefault("firstName");
        var last = v.GetValueOrDefault("last_name") ?? v.GetValueOrDefault("lastName");
        var joined = string.Join(' ', new[] { first, last }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return joined.Length > 0 ? joined : null;
    }

    /// <summary>Replaces {{field_key}} and {{form}} placeholders with submitted values (plain text).</summary>
    public static string Merge(string template, IReadOnlyDictionary<string, string> values, Form form)
    {
        var result = template.Replace("{{form}}", form.Name, StringComparison.Ordinal);
        foreach (var (key, value) in values) result = result.Replace("{{" + key + "}}", value, StringComparison.Ordinal);
        // Unfilled placeholders (optional fields) collapse: "Hi {{first_name}}," → "Hi there,".
        result = System.Text.RegularExpressions.Regex.Replace(result, @"\{\{name\}\}|\{\{first_name\}\}", "there");
        return System.Text.RegularExpressions.Regex.Replace(result, @"\{\{[A-Za-z0-9_-]+\}\}", string.Empty);
    }

    private static string? Clip(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}

/// <summary>Publishes FormSubmitted for submissions whose publish was never claimed (e.g. the process stopped after commit).</summary>
public sealed class FormEventRetryJob(AppDbContext db, FormSubmissionService service, TimeProvider clock) : Common.Jobs.IJob
{
    public string Name => "forms.event-retry";

    public async Task<string> ExecuteAsync(CancellationToken ct)
    {
        var before = clock.GetUtcNow().UtcDateTime.AddMinutes(-2);
        var ids = await db.Set<FormSubmission>().AsNoTracking().Where(s => s.EventPublishedAt == null && s.SubmittedAt < before)
            .OrderBy(s => s.SubmittedAt).Select(s => s.Id).Take(100).ToListAsync(ct);
        var published = 0;
        foreach (var id in ids)
            if (await service.PublishOnceAsync(id, ct)) published++;
        return $"{published} event(s) published";
    }
}

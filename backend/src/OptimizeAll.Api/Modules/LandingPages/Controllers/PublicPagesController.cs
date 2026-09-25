using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Hosting;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.LandingPages;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.LandingPages.Controllers;

public sealed record CaptchaDto(string Provider, string SiteKey);

public sealed record PublicFormDto(
    Guid Id, string Name, JsonElement Schema, string SubmitLabel, string SuccessMessage, string? RedirectUrl, string? ConsentText,
    int ConsentVersion, CaptchaDto? Captcha, string Token);

public sealed record PublicLandingPageDto(
    Guid PageId, string ClientName, string ClientSlug, string Slug, string Title, string? MetaDescription, string? OgImageUrl, bool NoIndex,
    Guid VersionId, int Version, string VariantKey, Guid? ExperimentId, JsonElement Blocks, IReadOnlyList<PublicFormDto> Forms);

public sealed record SubmissionResultDto(bool Ok, string Message, string? RedirectUrl);

/// <summary>
/// Anonymous endpoints behind the public landing pages (/lp/{client}/{slug}) and the embeddable form (/f/{formId}).
/// Rate limited (Public policy) and scoped to published pages / active forms only.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/v1/public")]
public sealed class PublicPagesController(
    AppDbContext db, IDatabaseDialect dialect, IPrivacyHasher hasher, FormRenderTokens tokens, CaptchaVerifier captcha,
    FormSubmissionService submissions, LandingPageService pages, IPublicOrigin publicOrigin, TimeProvider clock) : ControllerBase
{
    public const string VisitorHeader = "X-Visitor-Id";
    public const string EmbedOriginHeader = "X-Embed-Origin";
    private const long MaxMultipartBytes = 12 * 1024 * 1024;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private string AppOrigin => FormService.CanonicalOrigin(publicOrigin.Current) ?? string.Empty;

    /// <summary>The published version of a landing page, with the visitor's (sticky) A/B variant and its forms. Counts a view.</summary>
    [HttpGet("lp/{clientSlug}/{pageSlug}")]
    public async Task<PublicLandingPageDto> Page(string clientSlug, string pageSlug, [FromQuery(Name = "utm_source")] string? utmSource,
        [FromQuery] string? referrer, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var client = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Slug == clientSlug).Select(c => new { c.Id, c.Name, c.Slug })
            .FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("Page");
        // Matched on the published snapshot's slug: a rename saved in the draft is not live until it is published.
        var live = await pages.FindLiveAsync(client.Id, pageSlug, ct) ?? throw DomainException.NotFound("Page");
        var (page, version, snapshot) = (live.Page, live.Version, live.Snapshot);
        var variants = LandingPageService.Variants(snapshot.Variants);

        var userAgent = Request.Headers.UserAgent.ToString();
        var isBot = TrackingUrl.IsSuspectedBot(userAgent);
        var visitorId = Request.Headers[VisitorHeader].ToString();
        if (visitorId.Length is 0 or > 200) visitorId = string.Empty;
        var visitorHash = hasher.Hash(visitorId.Length > 0 ? $"v:{visitorId}" : $"ip:{HttpContext.Connection.RemoteIpAddress}|{userAgent}|{Now:yyyy-MM-dd}")!;

        var variantKey = "A";
        Guid? experimentId = null;
        if (snapshot.ExperimentEnabled && variants.Count > 1 && !isBot && visitorId.Length > 0)
        {
            variantKey = await AssignAsync(page.Id, snapshot.ExperimentId, VariantAssigner.VisitorSubject(visitorHash),
                variants.Select(v => new WeightedVariant(v.Key, v.Weight)).ToList(), ct);
            experimentId = snapshot.ExperimentId;
        }
        var chosen = variants.FirstOrDefault(v => v.Key == variantKey);
        if (chosen.Key is null) chosen = variants[0];

        if (!isBot) await CountViewAsync(page, version.Id, chosen.Key, experimentId, visitorHash, utmSource, referrer, ct);

        var formIds = chosen.Blocks.EnumerateArray().Where(b => b.GetProperty("type").GetString() == "form")
            .Select(b => b.GetProperty("props").GetProperty("formId").GetGuid()).Distinct().ToList();
        var forms = await db.Set<Form>().AsNoTracking()
            .Where(f => formIds.Contains(f.Id) && f.ClientAccountId == client.Id && f.Status == FormStatus.Active).ToListAsync(ct);
        var formDtos = new List<PublicFormDto>();
        foreach (var form in forms) formDtos.Add(await ToPublicFormAsync(form, ct));

        return new PublicLandingPageDto(page.Id, client.Name, client.Slug, page.Slug, snapshot.MetaTitle ?? snapshot.Name, snapshot.MetaDescription,
            snapshot.OgImageUrl, snapshot.NoIndex, version.Id, version.Version, chosen.Key, experimentId, chosen.Blocks, formDtos);
    }

    /// <summary>Form definition for the embeddable /f/{formId} page. X-Embed-Origin (the embedding site) must be allowed.</summary>
    [HttpGet("forms/{formId:guid}")]
    public async Task<PublicFormDto> Form(Guid formId, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var form = await submissions.LoadActiveFormAsync(formId, ct);
        FormSubmissionService.CheckOrigins(form, null, Request.Headers[EmbedOriginHeader].ToString(), AppOrigin);
        return await ToPublicFormAsync(form, ct);
    }

    /// <summary>
    /// Submits a form. JSON body (<see cref="SubmissionPayload"/>), or multipart/form-data with a "payload" JSON part and one
    /// file part per file field (part name = field key).
    /// </summary>
    [HttpPost("forms/{formId:guid}/submissions")]
    [RequestSizeLimit(MaxMultipartBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxMultipartBytes)]
    public async Task<ActionResult<SubmissionResultDto>> Submit(Guid formId, CancellationToken ct)
    {
        SubmissionPayload? payload;
        var files = new List<UploadedFile>();
        try
        {
            if (Request.HasFormContentType)
            {
                var formData = await Request.ReadFormAsync(ct);
                payload = JsonSerializer.Deserialize<SubmissionPayload>(formData["payload"].ToString(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                foreach (var file in formData.Files.Take(FormSchemas.MaxFilesPerSubmission + 1))
                {
                    if (file.Length > FormSchemas.AbsoluteFileMaxMb * 1024L * 1024L)
                        throw new DomainException("forms.invalid_submission", "Please correct the highlighted fields.", DomainErrorKind.Validation,
                            new Dictionary<string, string[]> { [file.Name] = new[] { $"Files must be at most {FormSchemas.AbsoluteFileMaxMb} MB." } });
                    using var buffer = new MemoryStream();
                    await file.CopyToAsync(buffer, ct);
                    files.Add(new UploadedFile(file.Name, file.FileName, buffer.ToArray()));
                }
            }
            else
            {
                payload = await JsonSerializer.DeserializeAsync<SubmissionPayload>(Request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web), ct);
            }
        }
        catch (JsonException)
        {
            throw new DomainException("forms.payload_invalid", "The submission could not be read.");
        }
        if (payload is null) throw new DomainException("forms.payload_invalid", "The submission could not be read.");

        var outcome = await submissions.SubmitAsync(formId, payload, files,
            new SubmissionRequestContext(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.Origin.ToString(),
                Request.Headers[EmbedOriginHeader].ToString(), AppOrigin), ct);
        return StatusCode(StatusCodes.Status201Created, new SubmissionResultDto(true, outcome.Message, outcome.RedirectUrl));
    }

    private async Task<PublicFormDto> ToPublicFormAsync(Form form, CancellationToken ct)
    {
        var siteKey = await captcha.SiteKeyAsync(form.Captcha, form.ClientAccountId, ct);
        using var schema = JsonDocument.Parse(form.SchemaJson);
        return new PublicFormDto(form.Id, form.Name, schema.RootElement.Clone(), form.SubmitLabel, form.SuccessMessage, form.RedirectUrl,
            form.ConsentText, form.ConsentVersion,
            siteKey is null ? null : new CaptchaDto(form.Captcha == CaptchaProvider.HCaptcha ? "hcaptcha" : "turnstile", siteKey),
            tokens.Issue(form.Id, form.ConsentVersion));
    }

    /// <summary>Sticky assignment per (experiment, visitor); a concurrent duplicate insert reads the winner back.</summary>
    private async Task<string> AssignAsync(Guid pageId, Guid experimentId, string subject, IReadOnlyCollection<WeightedVariant> variants, CancellationToken ct)
    {
        var existing = await db.Set<LandingPageAssignment>().AsNoTracking()
            .Where(a => a.ExperimentId == experimentId && a.SubjectKey == subject).Select(a => a.VariantKey).FirstOrDefaultAsync(ct);
        if (existing is not null && variants.Any(v => v.Key == existing)) return existing;
        var key = VariantAssigner.Assign(experimentId, subject, variants);
        var row = new LandingPageAssignment { PageId = pageId, ExperimentId = experimentId, SubjectKey = subject, VariantKey = key, AssignedAt = Now };
        db.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
            return key;
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return await db.Set<LandingPageAssignment>().AsNoTracking()
                .Where(a => a.ExperimentId == experimentId && a.SubjectKey == subject).Select(a => a.VariantKey).FirstAsync(ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task CountViewAsync(LandingPage page, Guid versionId, string variantKey, Guid? experimentId, string visitorHash,
        string? utmSource, string? referrer, CancellationToken ct)
    {
        var dayStart = Now.Date;
        var seenToday = await db.Set<LandingPageView>().AsNoTracking()
            .AnyAsync(v => v.PageId == page.Id && v.VisitorHash == visitorHash && v.ViewedAt >= dayStart, ct);
        db.Add(new LandingPageView
        {
            PageId = page.Id, ClientAccountId = page.ClientAccountId, VersionId = versionId, VariantKey = variantKey, ExperimentId = experimentId,
            VisitorHash = visitorHash, ViewedAt = Now, IsUnique = !seenToday,
            ReferrerHost = Uri.TryCreate(referrer, UriKind.Absolute, out var r) && r.Host.Length is > 0 and <= 253 ? r.Host : null,
            UtmSource = utmSource is { Length: > 100 } ? utmSource[..100] : utmSource,
        });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
}

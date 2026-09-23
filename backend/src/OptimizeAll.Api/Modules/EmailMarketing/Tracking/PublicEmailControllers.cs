using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.EmailMarketing.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;

namespace OptimizeAll.Api.Modules.EmailMarketing.Tracking;

/// <summary>
/// Tracking routes served outside /api (nginx must proxy <c>location /e/</c> to the API):
/// <c>GET /e/o/{token}.gif</c> open pixel, <c>GET /e/c/{token}</c> click redirect, <c>GET|POST /e/u/{token}</c> unsubscribe
/// (POST = RFC 8058 one-click). Tokens are HMAC-signed; clicks only redirect to links stored for that message.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Tracking)]
public sealed class EmailTrackingController(EngagementService engagement, EmailMarketingUrls urls, ILogger<EmailTrackingController> logger) : ControllerBase
{
    private static readonly byte[] Pixel = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    /// <summary>Open pixel. Always answers the pixel (an invalid token is simply not recorded) so validity is not revealed.</summary>
    [HttpGet("e/o/{token}.gif")]
    public async Task<IActionResult> Open(string token, CancellationToken ct)
    {
        NoStore();
        try
        {
            await engagement.RecordOpenAsync(token, Request.Headers.UserAgent.ToString(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Open tracking failed");
        }
        return File(Pixel, "image/gif");
    }

    /// <summary>Click redirect to the stored link (302). Forged tokens or foreign links answer 404, never a redirect.</summary>
    [HttpGet("e/c/{token}")]
    public async Task<IActionResult> Click(string token, CancellationToken ct)
    {
        NoStore();
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var destination = await engagement.RecordClickAsync(token, Request.Headers.UserAgent.ToString(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return destination is null ? NotFound() : Redirect(destination);
    }

    /// <summary>Opening the unsubscribe link shows the confirmation page (link scanners must not unsubscribe people).</summary>
    [HttpGet("e/u/{token}")]
    public IActionResult UnsubscribePage(string token)
    {
        NoStore();
        if (token.Length > 120 || token.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))) return NotFound();
        return Redirect(urls.UnsubscribePage(token));
    }

    /// <summary>RFC 8058 one-click unsubscribe (<c>List-Unsubscribe-Post: List-Unsubscribe=One-Click</c>). No cookies, no redirects.</summary>
    [HttpPost("e/u/{token}")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> OneClick(string token, CancellationToken ct)
    {
        NoStore();
        var ok = await engagement.UnsubscribeAsync(token, "one-click", HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return ok ? Content("You have been unsubscribed.", "text/plain") : NotFound();
    }

    private void NoStore()
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";
        Response.Headers.Pragma = "no-cache";
    }
}

/// <summary>Public pages' API: preference center, unsubscribe page, hosted signup form and double opt-in confirmation.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/v1/public/email")]
public sealed class PublicEmailController(EngagementService engagement, SignalsService signals, EmailMarketingUrls urls) : ControllerBase
{
    public const string SignatureHeader = "X-OA-Signature";
    private const int MaxSignedBytes = 32 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [HttpGet("preferences/{token}")]
    public async Task<ActionResult<PreferencesDto>> Preferences(string token, CancellationToken ct) =>
        await engagement.PreferencesAsync(token, ct) is { } prefs ? prefs : NotFoundProblem();

    [HttpPut("preferences/{token}")]
    public async Task<ActionResult<PreferencesDto>> UpdatePreferences(string token, PreferencesUpdate update, CancellationToken ct) =>
        await engagement.UpdatePreferencesAsync(token, update, HttpContext.Connection.RemoteIpAddress?.ToString(), ct) is { } prefs ? prefs : NotFoundProblem();

    /// <summary>The unsubscribe confirmation page's button.</summary>
    [HttpPost("unsubscribe/{token}")]
    public async Task<IActionResult> Unsubscribe(string token, CancellationToken ct) =>
        await engagement.UnsubscribeAsync(token, "unsubscribe-page", HttpContext.Connection.RemoteIpAddress?.ToString(), ct) ? NoContent() : NotFoundProblem();

    [HttpGet("forms/{key}")]
    public async Task<ActionResult<SignupForm>> Form(string key, CancellationToken ct) =>
        await engagement.SignupFormAsync(key, ct) is { } form ? form : NotFoundProblem();

    /// <summary>Hosted signup form submit. Answers 202 whether or not the address was known (no enumeration).</summary>
    [HttpPost("forms/{key}")]
    public async Task<IActionResult> Signup(string key, SignupRequest request, CancellationToken ct) =>
        await engagement.SignupAsync(key, request, HttpContext.Connection.RemoteIpAddress?.ToString(), ct)
            ? Accepted(new { message = "Thanks! If this is a new subscription, check your inbox to confirm it." })
            : NotFoundProblem();

    [HttpPost("confirm/{token}")]
    public async Task<IActionResult> Confirm(string token, CancellationToken ct)
    {
        var result = await engagement.ConfirmAsync(token, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return result is { } r
            ? Ok(new { list = r.ListName, workspace = r.Workspace })
            : EmailProblem.Result(HttpContext, 400, "email.confirmation_invalid", "This confirmation link is invalid or has expired. Sign up again to get a new one.");
    }

    /// <summary>
    /// Revenue attribution (server-to-server), HMAC-SHA256 signed like the tracking postback: header
    /// <c>X-OA-Signature: sha256=&lt;hex of HMAC(Tracking:PostbackSecret, raw body)&gt;</c>. Idempotent per external reference.
    /// </summary>
    [HttpPost("conversions")]
    public async Task<IActionResult> Conversion(CancellationToken ct)
    {
        var (raw, error) = await ReadSignedAsync(ct);
        if (error is not null) return error;
        var body = Deserialize<ConversionRequest>(raw!);
        var validation = Validate(body);
        if (validation is not null) return validation;
        return Ok(await signals.RecordConversionAsync(body!, ct));
    }

    /// <summary>Custom events for journeys (e.g. cart_abandoned), signed like <see cref="Conversion"/>.</summary>
    [HttpPost("events")]
    public async Task<IActionResult> Event(CancellationToken ct)
    {
        var (raw, error) = await ReadSignedAsync(ct);
        if (error is not null) return error;
        var body = Deserialize<CustomEventRequest>(raw!);
        var validation = Validate(body);
        if (validation is not null) return validation;
        var result = await signals.RecordEventAsync(body!, ct);
        return result.Accepted ? Ok(result) : Accepted(result);
    }

    private async Task<(byte[]? Raw, IActionResult? Error)> ReadSignedAsync(CancellationToken ct)
    {
        var secret = urls.SigningSecret;
        if (secret is null)
            return (null, EmailProblem.Result(HttpContext, 503, "email.signing_not_configured", "Signed email APIs are not configured on this server (Tracking:PostbackSecret)."));
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxSignedBytes) return (null, EmailProblem.Result(HttpContext, 413, "email.payload_too_large", "The body is too large."));
        }
        var raw = buffer.ToArray();
        if (!IsValidSignature(Request.Headers[SignatureHeader].ToString(), raw, secret))
            return (null, EmailProblem.Result(HttpContext, 401, "email.invalid_signature", "The signature is missing or invalid."));
        return (raw, null);
    }

    /// <summary>Constant-time check of "sha256=&lt;hex&gt;" against HMAC-SHA256(secret, body).</summary>
    public static bool IsValidSignature(string? header, byte[] body, string secret)
    {
        if (string.IsNullOrWhiteSpace(header) || !header.Trim().StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)) return false;
        byte[] provided;
        try { provided = Convert.FromHexString(header.Trim()["sha256=".Length..]); }
        catch (FormatException) { return false; }
        return CryptographicOperations.FixedTimeEquals(provided, HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
    }

    private static T? Deserialize<T>(byte[] raw) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(raw, Json); }
        catch (JsonException) { throw new DomainException("email.payload_invalid", "The body is not valid JSON."); }
    }

    private IActionResult? Validate(object? body)
    {
        if (body is null) return EmailProblem.Result(HttpContext, 400, "email.payload_invalid", "The body is empty.");
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        if (System.ComponentModel.DataAnnotations.Validator.TryValidateObject(body, new(body), results, true)) return null;
        throw new DomainException("email.payload_invalid", "The body is invalid.", DomainErrorKind.Validation,
            results.GroupBy(r => r.MemberNames.FirstOrDefault() ?? "body").ToDictionary(g => JsonNamingPolicy.CamelCase.ConvertName(g.Key), g => g.Select(r => r.ErrorMessage ?? "Invalid.").ToArray()));
    }

    private ObjectResult NotFoundProblem() =>
        EmailProblem.Result(HttpContext, 404, "email.link_invalid", "This link is invalid or no longer active.");
}

/// <summary>Provider webhooks per workspace ("agency" or a client id): ESP delivery/bounce/complaint events and Twilio SMS.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Webhooks)]
public sealed class EmailWebhooksController(WebhookService webhooks, EmailMarketingUrls urls) : ControllerBase
{
    private const int MaxBytes = 2 * 1024 * 1024;

    /// <summary>SendGrid signed event webhook (ECDSA; requires the verification key in the "sendgrid" integration settings).</summary>
    [HttpPost("api/v1/public/email/webhooks/sendgrid/{workspace}")]
    public async Task<IActionResult> SendGrid(string workspace, CancellationToken ct)
    {
        if (!Workspace.TryParse(workspace, out var clientId)) return NotFound();
        var raw = await ReadAsync(ct);
        if (raw is null) return EmailProblem.Result(HttpContext, 413, "email.payload_too_large", "The body is too large.");
        var verdict = await webhooks.VerifySendGridAsync(clientId, Request.Headers["X-Twilio-Email-Event-Webhook-Signature"].ToString(),
            Request.Headers["X-Twilio-Email-Event-Webhook-Timestamp"].ToString(), raw, ct);
        if (verdict != WebhookVerdict.Ok) return VerdictResult(verdict, "SendGrid");
        IReadOnlyList<ProviderEvent> events;
        try { events = WebhookService.ParseSendGrid(raw); }
        catch (JsonException) { return EmailProblem.Result(HttpContext, 400, "email.payload_invalid", "The body is not valid JSON."); }
        var applied = await webhooks.ApplyAsync(clientId, events, ct);
        return Ok(new { received = events.Count, applied });
    }

    /// <summary>Mailgun webhook (HMAC signature with the webhook signing key in the "mailgun" integration).</summary>
    [HttpPost("api/v1/public/email/webhooks/mailgun/{workspace}")]
    public async Task<IActionResult> Mailgun(string workspace, CancellationToken ct)
    {
        if (!Workspace.TryParse(workspace, out var clientId)) return NotFound();
        var raw = await ReadAsync(ct);
        if (raw is null) return EmailProblem.Result(HttpContext, 413, "email.payload_too_large", "The body is too large.");
        (WebhookVerdict Verdict, ProviderEvent? Event) parsed;
        try { parsed = await webhooks.VerifyAndParseMailgunAsync(clientId, raw, ct); }
        catch (JsonException) { return EmailProblem.Result(HttpContext, 400, "email.payload_invalid", "The body is not valid JSON."); }
        if (parsed.Verdict != WebhookVerdict.Ok) return VerdictResult(parsed.Verdict, "Mailgun");
        var applied = parsed.Event is null ? 0 : await webhooks.ApplyAsync(clientId, new[] { parsed.Event }, ct);
        return Ok(new { applied });
    }

    /// <summary>Twilio inbound SMS (STOP/START keywords). Answers empty TwiML.</summary>
    [HttpPost("api/v1/public/sms/webhooks/twilio/{workspace}/inbound")]
    public async Task<IActionResult> TwilioInbound(string workspace, CancellationToken ct)
    {
        if (!Workspace.TryParse(workspace, out var clientId) || !Request.HasFormContentType) return NotFound();
        var form = await Request.ReadFormAsync(ct);
        var pairs = form.Select(f => new KeyValuePair<string, string>(f.Key, f.Value.ToString())).ToList();
        var (verdict, _) = await webhooks.VerifyTwilioAsync(clientId, CallbackUrl(), pairs, Request.Headers["X-Twilio-Signature"].ToString(), ct);
        if (verdict != WebhookVerdict.Ok) return VerdictResult(verdict, "Twilio");
        await webhooks.HandleInboundSmsAsync(clientId, form["From"].ToString(), form["Body"].ToString(), ct);
        return Content("<?xml version=\"1.0\" encoding=\"UTF-8\"?><Response></Response>", "text/xml");
    }

    /// <summary>Twilio message status callback.</summary>
    [HttpPost("api/v1/public/sms/webhooks/twilio/{workspace}/status")]
    public async Task<IActionResult> TwilioStatus(string workspace, CancellationToken ct)
    {
        if (!Workspace.TryParse(workspace, out var clientId) || !Request.HasFormContentType) return NotFound();
        var form = await Request.ReadFormAsync(ct);
        var pairs = form.Select(f => new KeyValuePair<string, string>(f.Key, f.Value.ToString())).ToList();
        var (verdict, _) = await webhooks.VerifyTwilioAsync(clientId, CallbackUrl(), pairs, Request.Headers["X-Twilio-Signature"].ToString(), ct);
        if (verdict != WebhookVerdict.Ok) return VerdictResult(verdict, "Twilio");
        await webhooks.HandleSmsStatusAsync(clientId, form["MessageSid"].ToString(), form["MessageStatus"].ToString(), form["ErrorCode"].ToString(),
            form["To"].ToString(), ct);
        return NoContent();
    }

    /// <summary>The public URL Twilio signed (EmailMarketing:PublicBaseUrl + path + query), independent of proxies.</summary>
    private string CallbackUrl() => urls.PublicBaseUrl + Request.Path + Request.QueryString;

    private async Task<byte[]?> ReadAsync(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBytes) return null;
        }
        return buffer.ToArray();
    }

    private ObjectResult VerdictResult(WebhookVerdict verdict, string provider) => verdict == WebhookVerdict.NotConfigured
        ? EmailProblem.Result(HttpContext, 503, "email.webhook_not_configured", $"{provider} webhook verification is not configured for this workspace.")
        : EmailProblem.Result(HttpContext, 401, "email.invalid_signature", $"The {provider} signature is missing or invalid.");
}

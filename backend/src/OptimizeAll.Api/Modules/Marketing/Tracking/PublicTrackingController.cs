using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Marketing.Shared;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Marketing.Tracking;

public sealed class ConversionPostback
{
    public string? Code { get; set; }
    public string? ExternalReference { get; set; }
    public decimal? Value { get; set; }
    public string? Currency { get; set; }
    public DateTime? OccurredAt { get; set; }
}

public sealed record ConversionResult(Guid Id, bool Duplicate);

/// <summary>Anonymous tracking endpoints: the short-link redirect and the advertiser conversion postback.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
public sealed class PublicTrackingController(
    AppDbContext db, IPrivacyHasher hasher, MarketingUrls urls, TimeProvider clock) : ControllerBase
{
    public const string SignatureHeader = "X-OA-Signature";
    private const int MaxPostbackBytes = 16 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 302 to the campaign destination with the link's UTM parameters. The destination always comes from stored
    /// configuration (never from the request), so the endpoint cannot be used as an open redirect.
    /// </summary>
    [HttpGet("t/{code}")]
    public async Task<IActionResult> Follow(string code, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer-when-downgrade";

        var row = await (
            from l in db.Set<TrackingLink>().AsNoTracking()
            join c in db.Set<Campaign>() on l.CampaignId equals c.Id
            where l.Code == code
            select new { Link = l, c.TrackingDestinationUrl }).FirstOrDefaultAsync(ct);
        if (row is null || !TrackingUrl.IsValidDestination(TrackingService.EffectiveDestination(row.Link, row.TrackingDestinationUrl)))
            return NotFound();

        var now = clock.GetUtcNow().UtcDateTime;
        var userAgent = Request.Headers.UserAgent.ToString();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var visitorHash = hasher.Hash($"{ip}|{userAgent}|{now:yyyy-MM-dd}");
        var seen = await db.Set<TrackingClick>().AnyAsync(k => k.TrackingLinkId == row.Link.Id && k.VisitorHash == visitorHash, ct);

        db.Set<TrackingClick>().Add(new TrackingClick
        {
            TrackingLinkId = row.Link.Id,
            ClickedAt = now,
            VisitorHash = visitorHash,
            Referrer = ReferrerHost(Request.Headers.Referer.ToString()),
            IsUnique = !seen,
            IsSuspectedBot = TrackingUrl.IsSuspectedBot(userAgent),
        });
        await db.SaveChangesAsync(ct);

        return Redirect(TrackingService.BuildTarget(row.Link, row.TrackingDestinationUrl));
    }

    /// <summary>
    /// Server-to-server conversion postback signed with HMAC-SHA256 over the raw body
    /// (header X-OA-Signature: sha256=&lt;hex&gt;). Idempotent per (link, externalReference).
    /// </summary>
    [HttpPost("api/v1/public/conversions")]
    public async Task<IActionResult> Conversion(CancellationToken ct)
    {
        var secret = urls.PostbackSecret;
        if (secret is null)
            return ProblemResult(StatusCodes.Status503ServiceUnavailable, "tracking.postback_not_configured",
                "Conversion postbacks are not configured on this server.");

        Request.EnableBuffering();
        byte[] raw;
        using (var buffer = new MemoryStream())
        {
            var chunk = new byte[4096];
            int read;
            while ((read = await Request.Body.ReadAsync(chunk, ct)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxPostbackBytes)
                    return ProblemResult(StatusCodes.Status413PayloadTooLarge, "tracking.postback_too_large", "The postback body is too large.");
            }
            raw = buffer.ToArray();
        }
        Request.Body.Position = 0;

        if (!IsValidSignature(Request.Headers[SignatureHeader].ToString(), raw, secret))
            return ProblemResult(StatusCodes.Status401Unauthorized, "tracking.invalid_signature", "The postback signature is missing or invalid.");

        ConversionPostback? body;
        try
        {
            body = JsonSerializer.Deserialize<ConversionPostback>(raw, Json);
        }
        catch (JsonException)
        {
            throw new DomainException("tracking.postback_invalid", "The postback body is not valid JSON.");
        }
        var now = clock.GetUtcNow().UtcDateTime;
        Validate(body, now);

        var code = body!.Code!.Trim();
        var linkId = await db.Set<TrackingLink>().Where(l => l.Code == code).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct)
                     ?? throw new DomainException("tracking.link_not_found", "Unknown tracking code.", DomainErrorKind.NotFound);
        var reference = body.ExternalReference!.Trim();

        var existing = await db.Set<TrackingConversion>().AsNoTracking()
            .Where(c => c.TrackingLinkId == linkId && c.ExternalReference == reference).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (existing is { } existingId) return Ok(new ConversionResult(existingId, true));

        var conversion = new TrackingConversion
        {
            TrackingLinkId = linkId,
            ExternalReference = reference,
            Value = body.Value is { } v && body.Currency is { } cur ? Money.Round(v, Money.Normalize(cur)) : body.Value,
            Currency = body.Currency is null ? null : Money.Normalize(body.Currency),
            OccurredAt = body.OccurredAt!.Value.ToUniversalTime(),
            ReceivedAt = now,
            VerifiedAt = now,
            Source = "postback",
        };
        db.Set<TrackingConversion>().Add(conversion);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            var winner = await db.Set<TrackingConversion>().AsNoTracking()
                .Where(c => c.TrackingLinkId == linkId && c.ExternalReference == reference).Select(c => c.Id).FirstAsync(ct);
            return Ok(new ConversionResult(winner, true));
        }
        return Ok(new ConversionResult(conversion.Id, false));
    }

    /// <summary>Constant-time comparison of "sha256=&lt;hex&gt;" against HMAC-SHA256(secret, body).</summary>
    public static bool IsValidSignature(string? header, byte[] body, string secret)
    {
        if (string.IsNullOrWhiteSpace(header)) return false;
        var value = header.Trim();
        if (!value.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)) return false;
        byte[] provided;
        try { provided = Convert.FromHexString(value["sha256=".Length..]); }
        catch (FormatException) { return false; }
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        return CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    private static void Validate(ConversionPostback? body, DateTime now)
    {
        var errors = new Dictionary<string, string[]>();
        if (body is null) throw new DomainException("tracking.postback_invalid", "The postback body is empty.");
        if (string.IsNullOrWhiteSpace(body.Code) || body.Code.Length > 32) errors["code"] = new[] { "A tracking code is required." };
        if (string.IsNullOrWhiteSpace(body.ExternalReference) || body.ExternalReference.Trim().Length > 150)
            errors["externalReference"] = new[] { "An external reference (max 150 characters) is required." };
        if (body.OccurredAt is null) errors["occurredAt"] = new[] { "occurredAt is required." };
        else if (body.OccurredAt.Value.ToUniversalTime() > now.AddDays(1)) errors["occurredAt"] = new[] { "occurredAt is in the future." };
        if (body.Value is < 0) errors["value"] = new[] { "value must not be negative." };
        if (body.Currency is not null && !Money.IsSupported(body.Currency.Trim())) errors["currency"] = new[] { "Unsupported currency." };
        if (body.Value is not null && body.Currency is null) errors["currency"] = new[] { "currency is required with value." };
        if (errors.Count > 0)
            throw new DomainException("tracking.postback_invalid", "The postback is invalid.", DomainErrorKind.Validation, errors);
    }

    private static string? ReferrerHost(string? referrer) =>
        Uri.TryCreate(referrer, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? (uri.Host.Length > 500 ? uri.Host[..500] : uri.Host)
            : null;

    private ObjectResult ProblemResult(int status, string code, string title)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Type = $"https://docs.optimizeall.app/errors/{code}" };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}

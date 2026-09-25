using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Hosting;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.EmailMarketing;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.EmailMarketing.Shared;

/// <summary>
/// Configuration (section "EmailMarketing"). Read on every use so hosts and tests can change it at runtime.
///   EmailMarketing:PublicBaseUrl  origin that serves the tracking routes /e/... (default: the web app origin, see
///                                 IPublicOrigin: site URL → Email:AppBaseUrl → request → remembered; since nginx
///                                 proxies /e/ to the API on the web origin)
///   EmailMarketing:TokenSecret    HMAC key for open/click/unsubscribe tokens (default: derived from Security:HashSalt)
///   Tracking:PostbackSecret       HMAC secret for the signed conversion/event APIs (shared with the tracking postback)
/// </summary>
public sealed class EmailMarketingUrls(IConfiguration configuration, IPublicOrigin publicOrigin)
{
    public string AppBaseUrl => publicOrigin.Current;

    public string PublicBaseUrl =>
        (configuration["EmailMarketing:PublicBaseUrl"] is { Length: > 0 } url ? url : AppBaseUrl).TrimEnd('/');

    public string? SigningSecret => configuration["Tracking:PostbackSecret"] is { Length: > 0 } s ? s : null;

    public string OpenPixel(string token) => $"{PublicBaseUrl}/e/o/{token}.gif";
    public string Click(string token) => $"{PublicBaseUrl}/e/c/{token}";
    /// <summary>RFC 8058 one-click endpoint (List-Unsubscribe); a GET shows the confirmation page.</summary>
    public string Unsubscribe(string token) => $"{PublicBaseUrl}/e/u/{token}";
    public string UnsubscribePage(string token) => $"{AppBaseUrl}/email/unsubscribe/{token}";
    public string Preferences(string token) => $"{AppBaseUrl}/email/preferences/{token}";
    public string ConfirmSubscription(string token) => $"{AppBaseUrl}/email/confirm/{token}";
    public string SignupForm(string key) => $"{AppBaseUrl}/email/subscribe/{key}";
}

public enum TokenPurpose : byte
{
    Open = (byte)'O',
    Click = (byte)'C',
    Unsubscribe = (byte)'U',
    Preferences = (byte)'P',
    Confirm = (byte)'D',
}

/// <summary>What a token's message id refers to.</summary>
public enum TokenSource : byte
{
    /// <summary><c>MessageId</c> is a subscriber id (no message; e.g. signup confirmation, preference links in DOI mail).</summary>
    Subscriber = 0,
    CampaignRecipient = 1,
    AutomationStepRun = 2,
    /// <summary><c>MessageId</c> is a list membership (double opt-in).</summary>
    Membership = 3,
}

public readonly record struct TrackingToken(TokenPurpose Purpose, TokenSource Source, Guid MessageId, Guid Extra);

/// <summary>
/// Compact HMAC-signed tokens for the anonymous tracking and preference endpoints:
/// base64url(purpose | source | messageId | extra) + "." + base64url(HMAC-SHA256[0..16]).
/// The purpose is signed, so an open token cannot be replayed as an unsubscribe token, and nothing about the
/// destination is in the token: a click token names a stored link id.
/// </summary>
public sealed class TrackingTokens(IConfiguration configuration)
{
    private const int PayloadLength = 34;
    private const int MacLength = 16;

    private byte[] Key
    {
        get
        {
            var configured = configuration["EmailMarketing:TokenSecret"];
            if (!string.IsNullOrEmpty(configured)) return Encoding.UTF8.GetBytes(configured);
            var salt = configuration["Security:HashSalt"];
            if (string.IsNullOrEmpty(salt)) throw new InvalidOperationException("Security:HashSalt (or EmailMarketing:TokenSecret) is required.");
            return HMACSHA256.HashData(Encoding.UTF8.GetBytes(salt), "optimizeall/email-tracking/v1"u8);
        }
    }

    public string Create(TokenPurpose purpose, TokenSource source, Guid messageId, Guid extra = default)
    {
        Span<byte> payload = stackalloc byte[PayloadLength];
        payload[0] = (byte)purpose;
        payload[1] = (byte)source;
        messageId.TryWriteBytes(payload[2..18]);
        extra.TryWriteBytes(payload[18..34]);
        var mac = HMACSHA256.HashData(Key, payload)[..MacLength];
        return Base64Url(payload) + "." + Base64Url(mac);
    }

    /// <summary>Verifies the signature (constant time) and the expected purpose. Returns null for anything forged or malformed.</summary>
    public TrackingToken? Read(string? token, TokenPurpose expected)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 120) return null;
        var dot = token.IndexOf('.');
        if (dot <= 0) return null;
        var payload = FromBase64Url(token[..dot]);
        var mac = FromBase64Url(token[(dot + 1)..]);
        if (payload is not { Length: PayloadLength } || mac is not { Length: MacLength }) return null;
        var expectedMac = HMACSHA256.HashData(Key, payload)[..MacLength];
        if (!CryptographicOperations.FixedTimeEquals(mac, expectedMac)) return null;
        if (payload[0] != (byte)expected) return null;
        if (!Enum.IsDefined(typeof(TokenSource), payload[1])) return null;
        return new TrackingToken((TokenPurpose)payload[0], (TokenSource)payload[1], new Guid(payload.AsSpan(2, 16)), new Guid(payload.AsSpan(18, 16)));
    }

    /// <summary>Encodes an expiry (unix seconds) in the extra guid of a confirmation token.</summary>
    public static Guid ExpiryGuid(DateTime expiresAtUtc)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(new DateTimeOffset(expiresAtUtc).ToUnixTimeSeconds()).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    public static DateTime ExpiryFrom(Guid extra) =>
        DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToInt64(extra.ToByteArray(), 0)).UtcDateTime;

    private static string Base64Url(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? FromBase64Url(string value)
    {
        if (value.Length > 100 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) return null;
        var s = value.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", 0 => string.Empty, _ => "!" };
        try { return Convert.FromBase64String(s); }
        catch (FormatException) { return null; }
    }
}

/// <summary>
/// Tenant checks for the email module. Staff endpoints require agency staff (<c>clients.view</c>) and an existing client
/// (the agency's own workspace is <c>null</c>); other tenants' rows answer 404.
/// </summary>
public sealed class EmailAccess(IClientScope scope, AppDbContext db)
{
    public async Task EnsureStaffWorkspaceAsync(Guid? clientAccountId, CancellationToken ct)
    {
        if (!scope.IsStaff) throw DomainException.NotFound("Workspace");
        if (clientAccountId is { } id) await scope.EnsureAccessAsync(id, ClientMemberRole.Viewer, ct);
    }

    /// <summary>Loads a workspace-owned row for staff; 404 when missing or not accessible.</summary>
    public async Task<T> LoadAsync<T>(IQueryable<T> query, Func<T, Guid?> clientOf, string what, CancellationToken ct) where T : class
    {
        if (!scope.IsStaff) throw DomainException.NotFound(what);
        var row = await query.FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound(what);
        if (clientOf(row) is { } clientId) await scope.EnsureAccessAsync(clientId, ClientMemberRole.Viewer, ct);
        return row;
    }

    public async Task<IReadOnlyList<Guid>> ClientMemberIdsAsync(CancellationToken ct) => await scope.MemberClientIdsAsync(ct);

    public Task EnsureClientMemberAsync(Guid clientId, ClientMemberRole role, CancellationToken ct) => scope.EnsureAccessAsync(clientId, role, ct);

    public async Task<string> WorkspaceNameAsync(Guid? clientAccountId, CancellationToken ct) =>
        clientAccountId is null
            ? "Optimize All (agency)"
            : await db.Set<ClientAccount>().Where(c => c.Id == clientAccountId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? "Client";
}

public static class EmailProblem
{
    public static ObjectResult Result(HttpContext http, int status, string code, string title)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Type = $"https://docs.optimizeall.app/errors/{code}" };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = http.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }

    public static DomainException Invalid(string code, string message, IEnumerable<string> errors, string field = "errors") =>
        new(code, message, DomainErrorKind.Validation, new Dictionary<string, string[]> { [field] = errors.ToArray() });
}

public static class Text
{
    public static string? Clean(string? value, int max)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return null;
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    public static string Truncate(string? value, int max) =>
        value is null ? string.Empty : value.Length <= max ? value : value[..max];

    public static string UrlHash(string url) => Normalization.Sha256Hex(url);
}

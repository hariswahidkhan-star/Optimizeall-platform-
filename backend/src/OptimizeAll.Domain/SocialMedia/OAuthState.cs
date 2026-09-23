using System.Security.Cryptography;
using System.Text;

namespace OptimizeAll.Domain.SocialMedia;

/// <summary>Payload of an OAuth "state" parameter: which profile/ad account is being connected, by whom, until when.</summary>
public sealed record OAuthStatePayload(string Purpose, Guid TargetId, Guid UserId, DateTime ExpiresAt, string Nonce);

/// <summary>
/// Signed OAuth state: <c>base64url(purpose|targetId|userId|expiresUnix|nonce)</c> + "." + <c>base64url(HMAC-SHA256)</c>.
/// Verification rejects tampering (constant-time comparison), expiry, malformed values and a different signed-in user,
/// so a callback can only complete the flow its own user started. The PKCE verifier is derived from the same key and
/// nonce, so nothing has to be stored between start and callback.
/// </summary>
public static class OAuthState
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public static string Sign(OAuthStatePayload payload, byte[] key)
    {
        if (payload.Purpose.Contains('|') || payload.Nonce.Contains('|')) throw new ArgumentException("Invalid state payload.");
        var body = string.Join('|', payload.Purpose, payload.TargetId.ToString("N"), payload.UserId.ToString("N"),
            new DateTimeOffset(DateTime.SpecifyKind(payload.ExpiresAt, DateTimeKind.Utc)).ToUnixTimeSeconds(), payload.Nonce);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        return Base64Url(bodyBytes) + "." + Base64Url(HMACSHA256.HashData(key, bodyBytes));
    }

    public static string NewNonce() => Base64Url(RandomNumberGenerator.GetBytes(18));

    /// <summary>Returns the payload, or null (with a reason) when the state is invalid for this user at this time.</summary>
    public static OAuthStatePayload? Verify(string? state, byte[] key, Guid expectedUserId, DateTime now, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(state) || state.Length > 1000)
        {
            reason = "missing";
            return null;
        }
        var dot = state.IndexOf('.');
        if (dot <= 0 || dot == state.Length - 1)
        {
            reason = "malformed";
            return null;
        }
        var bodyBytes = FromBase64Url(state[..dot]);
        var signature = FromBase64Url(state[(dot + 1)..]);
        if (bodyBytes is null || signature is null)
        {
            reason = "malformed";
            return null;
        }
        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(key, bodyBytes), signature))
        {
            reason = "signature";
            return null;
        }
        var parts = Encoding.UTF8.GetString(bodyBytes).Split('|');
        if (parts.Length != 5 || !Guid.TryParseExact(parts[1], "N", out var target) || !Guid.TryParseExact(parts[2], "N", out var user)
            || !long.TryParse(parts[3], out var expiresUnix))
        {
            reason = "malformed";
            return null;
        }
        var expires = DateTimeOffset.FromUnixTimeSeconds(expiresUnix).UtcDateTime;
        if (expires <= now)
        {
            reason = "expired";
            return null;
        }
        if (user != expectedUserId)
        {
            reason = "user";
            return null;
        }
        return new OAuthStatePayload(parts[0], target, user, expires, parts[4]);
    }

    /// <summary>PKCE code verifier (43+ chars) derived from the state key and nonce.</summary>
    public static string PkceVerifier(byte[] key, string nonce) =>
        Base64Url(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("pkce|" + nonce)));

    public static string PkceChallenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[]? FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        try
        {
            return Convert.FromBase64String(s);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace OptimizeAll.Api.Common.Security;

public sealed class SecurityOptions
{
    public const string Section = "Security";

    /// <summary>Secret salt for hashing IP addresses/device ids (fraud signals, click dedup) so raw values are never stored.</summary>
    public string HashSalt { get; set; } = string.Empty;

    /// <summary>Allowed browser origins for CORS (the web app). Same-origin deployments can leave this empty.</summary>
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();

    /// <summary>Send the refresh cookie with the Secure flag. Only disable for plain-http local development.</summary>
    public bool SecureCookies { get; set; } = true;
}

public interface IPrivacyHasher
{
    /// <summary>Keyed SHA-256 (HMAC) hex digest; null/empty input returns null.</summary>
    string? Hash(string? value);
}

public sealed class PrivacyHasher(IOptions<SecurityOptions> options) : IPrivacyHasher
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(
        string.IsNullOrEmpty(options.Value.HashSalt) ? throw new InvalidOperationException("Security:HashSalt is required.") : options.Value.HashSalt);

    public string? Hash(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value.Trim()))).ToLowerInvariant();
}

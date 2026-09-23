using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;

namespace OptimizeAll.Api.Common.Security;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "optimizeall";
    public string Audience { get; set; } = "optimizeall-web";

    /// <summary>HMAC-SHA256 key; at least 32 bytes. Must come from secret configuration in non-development environments.</summary>
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;

    public SymmetricSecurityKey GetKey()
    {
        var bytes = Encoding.UTF8.GetBytes(SigningKey);
        if (bytes.Length < 32)
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 bytes.");
        return new SymmetricSecurityKey(bytes);
    }
}

public sealed record IssuedAccessToken(string Token, DateTime ExpiresAt);

public interface ITokenService
{
    IssuedAccessToken CreateAccessToken(User user);

    /// <summary>Creates an opaque random token and returns (raw token, SHA-256 hash). Only the hash is persisted.</summary>
    (string Raw, string Hash) CreateOpaqueToken();

    string Hash(string rawToken);
}

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public IssuedAccessToken CreateAccessToken(User user)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(AppClaims.UserId, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, IdGenerator.NewId().ToString()),
            new(AppClaims.SecurityVersion, user.SecurityVersion.ToString()),
            new(AppClaims.EmailVerified, user.IsEmailVerified ? "1" : "0"),
        };
        claims.AddRange(user.Roles.Select(r => new Claim(AppClaims.Role, r.Role.ToString())));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: new SigningCredentials(_options.GetKey(), SecurityAlgorithms.HmacSha256));

        return new IssuedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public (string Raw, string Hash) CreateOpaqueToken()
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (raw, Hash(raw));
    }

    public string Hash(string rawToken) => Normalization.Sha256Hex(rawToken);
}

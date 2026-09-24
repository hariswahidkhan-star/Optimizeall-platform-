using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;

namespace OptimizeAll.Api.Modules.Auth.Google;

/// <summary>What one Google sign-in attempt needs to remember between <c>start</c> and <c>callback</c>.</summary>
public sealed record GoogleFlow(string StateId, string Nonce, string CodeVerifier, string Mode, Guid? UserId, string? ReturnTo, DateTime ExpiresAt);

/// <summary>The verified Google identity of a new user, carried from <c>callback</c> to <c>complete</c> (terms step).</summary>
public sealed record GoogleSignUpTicket(string Subject, string Email, string? Name, DateTime ExpiresAt);

internal sealed record GoogleStatePayload(string StateId, DateTime ExpiresAt);

/// <summary>
/// Signs and encrypts (ASP.NET Core Data Protection) the OAuth <c>state</c> parameter, the flow cookie (nonce + PKCE
/// verifier, never sent to Google) and the sign-up ticket. Each uses its own purpose string, so one can never be
/// replayed as another; expiry is embedded and checked against the app clock by the caller.
/// </summary>
public sealed class GoogleFlowProtector(IDataProtectionProvider provider)
{
    public const string FlowModeSignIn = "signin";
    public const string FlowModeLink = "link";

    private readonly IDataProtector _state = provider.CreateProtector("OptimizeAll.Auth.Google.State.v1");
    private readonly IDataProtector _flow = provider.CreateProtector("OptimizeAll.Auth.Google.Flow.v1");
    private readonly IDataProtector _ticket = provider.CreateProtector("OptimizeAll.Auth.Google.SignUpTicket.v1");

    public static string RandomToken() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public string ProtectState(string stateId, DateTime expiresAt) => Protect(_state, new GoogleStatePayload(stateId, expiresAt));
    public string ProtectFlow(GoogleFlow flow) => Protect(_flow, flow);
    public string ProtectTicket(GoogleSignUpTicket ticket) => Protect(_ticket, ticket);

    internal GoogleStatePayload? UnprotectState(string? value) => Unprotect<GoogleStatePayload>(_state, value);
    public GoogleFlow? UnprotectFlow(string? value) => Unprotect<GoogleFlow>(_flow, value);
    public GoogleSignUpTicket? UnprotectTicket(string? value) => Unprotect<GoogleSignUpTicket>(_ticket, value);

    private static string Protect<T>(IDataProtector protector, T value) =>
        Base64UrlEncoder.Encode(protector.Protect(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static T? Unprotect<T>(IDataProtector protector, string? value) where T : class
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(protector.Unprotect(Base64UrlEncoder.DecodeBytes(value)));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Accounts;

public interface IProfileService
{
    Task<ProfileDto> GetProfileAsync(Guid userId, CancellationToken ct);
    Task<ProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct);
    Task<PayoutProfileDto> GetPayoutProfileAsync(Guid userId, CancellationToken ct);
    Task<PayoutProfileDto> UpdatePayoutProfileAsync(Guid userId, UpdatePayoutProfileRequest request, CancellationToken ct);
}

/// <summary>
/// Decrypts a participant's payout destination for finance/payment code. The raw value must never be logged,
/// audited or returned by participant-facing endpoints; callers are responsible for authorizing the access.
/// </summary>
public interface IPayoutDestinationReader
{
    /// <summary>Returns the decrypted destination (normalized IBAN/account number, PayPal email or E.164 number), or null when no payout profile exists.</summary>
    Task<string?> RevealAsync(Guid userId, CancellationToken ct = default);
}

public static class PayoutDestinationProtection
{
    public const string Purpose = "OptimizeAll.PayoutProfile.Destination.v1";
}

public sealed class PayoutDestinationReader(AppDbContext db, IDataProtectionProvider protection) : IPayoutDestinationReader
{
    public async Task<string?> RevealAsync(Guid userId, CancellationToken ct = default)
    {
        var encrypted = await db.Set<PayoutProfile>().AsNoTracking()
            .Where(p => p.UserId == userId).Select(p => p.EncryptedDestination).FirstOrDefaultAsync(ct);
        return encrypted is null ? null : protection.CreateProtector(PayoutDestinationProtection.Purpose).Unprotect(encrypted);
    }
}

public sealed class ProfileService(
    AppDbContext db,
    IAuditLogger audit,
    IDataProtectionProvider protection) : IProfileService
{
    public async Task<ProfileDto> GetProfileAsync(Guid userId, CancellationToken ct) =>
        ToDto(await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw DomainException.NotFound("User"));

    public async Task<ProfileDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await db.Set<User>().FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw DomainException.NotFound("User");

        var errors = new Dictionary<string, string[]>();
        var timeZone = request.TimeZone.Trim();
        if (!FieldRules.IsTimeZone(timeZone))
            errors["timeZone"] = new[] { "Use an IANA time zone such as Asia/Karachi or Europe/London." };
        var language = request.LanguageCode.Trim();
        if (!FieldRules.IsLanguageCode(language))
            errors["languageCode"] = new[] { "Use a language code such as en, ar or pt-BR." };
        var (interests, interestError) = FieldRules.NormalizeInterests(request.Interests);
        if (interestError is not null) errors["interests"] = new[] { interestError };

        string? whatsApp = string.IsNullOrWhiteSpace(request.WhatsAppNumber)
            ? null
            : request.WhatsAppNumber.Replace(" ", string.Empty).Replace("-", string.Empty);
        if (whatsApp is not null && !FieldRules.IsE164(whatsApp))
            errors["whatsAppNumber"] = new[] { "Use international format, e.g. +923001234567." };
        else if (request.WhatsAppOptIn && whatsApp is null)
            errors["whatsAppNumber"] = new[] { "Add your WhatsApp number to turn on WhatsApp notifications." };

        if (errors.Count > 0)
            throw new DomainException("profile.invalid", "Some profile fields are invalid.", DomainErrorKind.Validation, errors);

        var before = Snapshot(user);
        user.DisplayName = request.DisplayName.Trim();
        user.CountryCode = request.CountryCode.Trim().ToUpperInvariant();
        user.LanguageCode = NormalizeLanguage(language);
        user.TimeZone = timeZone;
        user.Interests = interests;
        user.MarketingEmailOptIn = request.MarketingEmailOptIn;
        // The number is only kept while the participant has opted in to WhatsApp messages (data minimisation).
        user.WhatsAppOptIn = request.WhatsAppOptIn;
        user.WhatsAppNumber = request.WhatsAppOptIn ? whatsApp : null;

        var after = Snapshot(user);
        var changed = before.Where(kv => !Equals(kv.Value, after[kv.Key])).Select(kv => kv.Key).ToArray();
        if (changed.Length > 0)
            audit.Record("account.profile_updated", nameof(User), user.Id, after: new { changedFields = changed });
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    public async Task<PayoutProfileDto> GetPayoutProfileAsync(Guid userId, CancellationToken ct)
    {
        var profile = await db.Set<PayoutProfile>().AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        return ToDto(profile);
    }

    public async Task<PayoutProfileDto> UpdatePayoutProfileAsync(Guid userId, UpdatePayoutProfileRequest request, CancellationToken ct)
    {
        var method = request.Method!.Value;
        if (!Enum.IsDefined(method))
            throw FieldRules.FieldError("payout_profile.invalid_method", "method", "Choose a payout method.");

        var (normalized, hint, error) = FieldRules.NormalizePayoutDestination(method, request.Destination);
        if (error is not null)
            throw FieldRules.FieldError("payout_profile.invalid_destination", "destination", error);

        var currency = Money.Normalize(request.PreferredCurrency);
        if (!Money.IsSupported(currency))
            throw FieldRules.FieldError("payout_profile.unsupported_currency", "preferredCurrency",
                $"Supported currencies: {string.Join(", ", Money.SupportedCurrencies.OrderBy(c => c))}.");

        var profile = await db.Set<PayoutProfile>().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var before = profile is null ? null : AuditView(profile);
        if (profile is null)
        {
            profile = new PayoutProfile { UserId = userId };
            db.Set<PayoutProfile>().Add(profile);
        }

        profile.Method = method;
        profile.AccountHolderName = request.AccountHolderName.Trim();
        profile.EncryptedDestination = protection.CreateProtector(PayoutDestinationProtection.Purpose).Protect(normalized!);
        profile.MaskedDestination = hint!;
        profile.PreferredCurrency = currency;
        profile.CountryCode = string.IsNullOrWhiteSpace(request.CountryCode) ? null : request.CountryCode.Trim().ToUpperInvariant();

        // Only the masked hint is audited: the raw destination never leaves this method in clear text.
        audit.Record("account.payout_profile_updated", nameof(PayoutProfile), profile.Id, before, AuditView(profile));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (Common.Errors.ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("payout_profile.concurrent_update", "Your payout details were just updated in another session. Reload and try again.");
        }
        return ToDto(profile);
    }

    /// <summary>"PT-br" → "pt-BR", "zh-hant" → "zh-Hant".</summary>
    private static string NormalizeLanguage(string language)
    {
        var parts = language.Split('-', 2);
        var primary = parts[0].ToLowerInvariant();
        if (parts.Length == 1) return primary;
        var sub = parts[1];
        sub = sub.Length == 2 ? sub.ToUpperInvariant() : char.ToUpperInvariant(sub[0]) + sub[1..].ToLowerInvariant();
        return primary + "-" + sub;
    }

    private static object AuditView(PayoutProfile p) => new
    {
        p.Method, destinationHint = p.MaskedDestination, p.PreferredCurrency, p.CountryCode,
        accountHolderInitial = p.AccountHolderName.Length > 0 ? p.AccountHolderName[..1] : string.Empty,
    };

    private static Dictionary<string, object?> Snapshot(User u) => new()
    {
        ["displayName"] = u.DisplayName,
        ["countryCode"] = u.CountryCode,
        ["languageCode"] = u.LanguageCode,
        ["timeZone"] = u.TimeZone,
        ["interests"] = string.Join(',', u.Interests),
        ["marketingEmailOptIn"] = u.MarketingEmailOptIn,
        ["whatsAppNumber"] = u.WhatsAppNumber,
        ["whatsAppOptIn"] = u.WhatsAppOptIn,
    };

    public static ProfileDto ToDto(User u) => new(
        u.Id, u.Email, u.IsEmailVerified, u.DisplayName, u.CountryCode, u.LanguageCode, u.TimeZone, u.Interests,
        u.MarketingEmailOptIn, u.WhatsAppNumber, u.WhatsAppOptIn, u.Tier, u.ReferralCode, u.CreatedAt);

    private static PayoutProfileDto ToDto(PayoutProfile? p) => p is null
        ? new PayoutProfileDto(false, null, null, null, null, null, null)
        : new PayoutProfileDto(true, p.Method, p.AccountHolderName, p.MaskedDestination, p.PreferredCurrency, p.CountryCode, p.UpdatedAt);
}

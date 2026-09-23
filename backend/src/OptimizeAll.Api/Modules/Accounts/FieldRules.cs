using System.Numerics;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Accounts;

/// <summary>
/// Pure validation / formatting rules shared by the Accounts, Social, Content, Notifications, Support and Admin
/// modules. Everything here is side-effect free and unit-tested.
/// </summary>
public static partial class FieldRules
{
    public const string Bullet = "•";

    /// <summary>E.164 phone number: '+', country code not starting with 0, 8–15 digits in total.</summary>
    public static bool IsE164(string? value) => value is not null && E164Regex().IsMatch(value);

    /// <summary>ISO 3166-1 alpha-2 shape check (two ASCII letters).</summary>
    public static bool IsCountryCode(string? value) => value is not null && CountryRegex().IsMatch(value);

    /// <summary>BCP 47 primary language with optional region/script, e.g. "en", "ar", "pt-BR".</summary>
    public static bool IsLanguageCode(string? value) => value is not null && LanguageRegex().IsMatch(value);

    public static bool IsTimeZone(string? value) =>
        !string.IsNullOrWhiteSpace(value) && TimeZoneInfo.TryFindSystemTimeZoneById(value.Trim(), out _);

    /// <summary>Lower-case slug: "verify-email", "faq-2".</summary>
    public static bool IsSlug(string? value) => value is not null && SlugRegex().IsMatch(value);

    public static bool IsEmail(string? value) => value is not null && value.Length <= 254 && EmailRegex().IsMatch(value);

    /// <summary>Normalizes a bank account number / IBAN: removes spaces and dashes, upper-cases.</summary>
    public static string NormalizeAccountNumber(string value) =>
        new string(value.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();

    /// <summary>
    /// Bank destination: 8–34 alphanumerics. Values shaped like an IBAN (two letters, two check digits, at least 15
    /// characters) must also pass the ISO 13616 mod-97 checksum.
    /// </summary>
    public static bool IsBankAccount(string normalized)
    {
        if (!BankRegex().IsMatch(normalized)) return false;
        return !LooksLikeIban(normalized) || IsValidIban(normalized);
    }

    public static bool LooksLikeIban(string normalized) => normalized.Length >= 15 && IbanShapeRegex().IsMatch(normalized);

    public static bool IsValidIban(string normalized)
    {
        if (!LooksLikeIban(normalized)) return false;
        var rearranged = normalized[4..] + normalized[..4];
        var digits = new System.Text.StringBuilder(rearranged.Length * 2);
        foreach (var c in rearranged)
        {
            if (char.IsDigit(c)) digits.Append(c);
            else if (c is >= 'A' and <= 'Z') digits.Append(c - 'A' + 10);
            else return false;
        }
        return BigInteger.Parse(digits.ToString()) % 97 == 1;
    }

    /// <summary>Masks the tail of a value: "••••1234". Values of four characters or fewer are fully masked.</summary>
    public static string MaskTail(string value, int visible = 4)
    {
        var bullets = string.Concat(Enumerable.Repeat(Bullet, 4));
        return value.Length <= visible ? bullets : bullets + value[^visible..];
    }

    /// <summary>Masks an email: "jane.doe@gmail.com" → "j•••@gmail.com".</summary>
    public static string MaskEmail(string email)
    {
        var at = email.LastIndexOf('@');
        if (at <= 0) return MaskTail(email);
        return email[0] + string.Concat(Enumerable.Repeat(Bullet, 3)) + email[at..];
    }

    /// <summary>Validates and normalizes a raw payout destination for a method. Returns (normalized, masked hint) or errors.</summary>
    public static (string? Normalized, string? Hint, string? Error) NormalizePayoutDestination(PayoutMethod method, string raw)
    {
        var value = raw.Trim();
        switch (method)
        {
            case PayoutMethod.PayPal:
                if (!IsEmail(value)) return (null, null, "Enter the email address of your PayPal account.");
                var email = value.ToLowerInvariant();
                return (email, MaskEmail(email), null);
            case PayoutMethod.BankTransfer:
                var account = NormalizeAccountNumber(value);
                if (!BankRegex().IsMatch(account))
                    return (null, null, "Enter your IBAN or account number (8–34 letters and digits).");
                if (LooksLikeIban(account) && !IsValidIban(account))
                    return (null, null, "This IBAN is not valid. Check it for typos.");
                return (account, MaskTail(account), null);
            case PayoutMethod.MobileWallet:
                var phone = value.Replace(" ", string.Empty).Replace("-", string.Empty);
                if (!IsE164(phone)) return (null, null, "Enter the wallet's phone number in international format, e.g. +923001234567.");
                return (phone, MaskTail(phone), null);
            case PayoutMethod.Other:
                if (value.Length is < 3 or > 200) return (null, null, "Enter 3–200 characters describing where to pay you.");
                return (value, MaskTail(value), null);
            default:
                return (null, null, "Unknown payout method.");
        }
    }

    /// <summary>Normalizes interest tags (trim, lower-case, distinct) and validates the limits.</summary>
    public static (List<string> Tags, string? Error) NormalizeInterests(IEnumerable<string?>? interests)
    {
        var tags = (interests ?? Array.Empty<string?>())
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => i!.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (tags.Count > 20) return (tags, "Choose at most 20 interests.");
        if (tags.Any(t => t.Length > 40)) return (tags, "Each interest can be at most 40 characters.");
        return (tags, null);
    }

    /// <summary>
    /// Application URL rule for admin-managed content: absolute https URL, or an app-relative path starting with a
    /// single '/'. Rejects protocol-relative ("//host") and backslash tricks.
    /// </summary>
    public static bool IsSafeContentUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim();
        if (v.StartsWith('/'))
            return !v.StartsWith("//", StringComparison.Ordinal) && !v.Contains('\\') && !v.Any(char.IsWhiteSpace);
        return Uri.TryCreate(v, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host);
    }

    /// <summary>Path prefix of files served by the Files module (<c>GET /api/v1/files/{id}</c>).</summary>
    public const string UploadedFilePrefix = "/api/v1/files/";

    /// <summary>
    /// Image URL rule (hero, asset and banner images), matching the web app's CSP <c>img-src</c>: an uploaded file
    /// (<c>/api/v1/files/{guid}</c>) or an absolute https URL on the default port whose host is in
    /// <paramref name="allowedHosts"/> (exact, case-insensitive; configured as <c>Content:AllowedImageHosts</c>).
    /// </summary>
    public static bool IsAllowedImageUrl(string? value, IReadOnlyCollection<string> allowedHosts)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var v = value.Trim();
        if (v.StartsWith(UploadedFilePrefix, StringComparison.Ordinal))
            return Guid.TryParseExact(v[UploadedFilePrefix.Length..], "D", out _);
        return Uri.TryCreate(v, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
               string.IsNullOrEmpty(uri.UserInfo) && uri.IsDefaultPort && !string.IsNullOrEmpty(uri.Host) &&
               allowedHosts.Any(h => string.Equals(h.Trim(), uri.IdnHost, StringComparison.OrdinalIgnoreCase));
    }

    public static DomainException FieldError(string code, string field, string message) =>
        new(code, message, DomainErrorKind.Validation, new Dictionary<string, string[]> { [field] = new[] { message } });

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164Regex();

    [GeneratedRegex("^[A-Za-z]{2}$")]
    private static partial Regex CountryRegex();

    [GeneratedRegex("^[a-zA-Z]{2,3}(-[A-Za-z0-9]{2,8})?$")]
    private static partial Regex LanguageRegex();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex("^[A-Z0-9]{8,34}$")]
    private static partial Regex BankRegex();

    [GeneratedRegex("^[A-Z]{2}[0-9]{2}[A-Z0-9]+$")]
    private static partial Regex IbanShapeRegex();
}

/// <summary>Optimistic concurrency helper for staff edits of <see cref="IConcurrencyStamped"/> rows.</summary>
public static class ConcurrencyGuard
{
    /// <summary>
    /// Rejects a stale stamp immediately (409) and also pins the original value so a concurrent writer between
    /// load and save is detected by EF (DbUpdateConcurrencyException → 409).
    /// </summary>
    public static void Apply<T>(AppDbContext db, T entity, Guid expectedStamp) where T : class, IConcurrencyStamped
    {
        if (entity.ConcurrencyStamp != expectedStamp)
            throw DomainException.Conflict("concurrency.conflict", "This record was changed by someone else. Reload and try again.");
        db.Entry(entity).Property(x => x.ConcurrencyStamp).OriginalValue = expectedStamp;
    }

    /// <summary>Marks the row modified so its UpdatedAt/ConcurrencyStamp advance even when only children changed.</summary>
    public static void Touch<T>(AppDbContext db, T entity) where T : class, IAuditedEntity =>
        db.Entry(entity).Property(x => x.UpdatedAt).IsModified = true;
}

public static class UserQueries
{
    /// <summary>Loads a user with roles or throws 404.</summary>
    public static async Task<User> LoadUserAsync(this AppDbContext db, Guid id, CancellationToken ct) =>
        await db.Set<User>().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id, ct)
        ?? throw DomainException.NotFound("User");
}

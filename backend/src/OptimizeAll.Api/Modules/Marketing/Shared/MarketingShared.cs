using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Errors;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Modules.Marketing.Shared;

/// <summary>
/// Public URLs and secrets used by the growth features. Read from configuration on every use (not cached) so
/// hosts and tests can change them at runtime:
///   Email:AppBaseUrl          web app base URL (referral/invitation links)
///   Tracking:PublicBaseUrl    base URL of the short tracking links (default: Email:AppBaseUrl)
///   Tracking:PostbackSecret   HMAC-SHA256 secret advertisers use to sign conversion postbacks
/// </summary>
public sealed class MarketingUrls(IConfiguration configuration)
{
    public string AppBaseUrl => (configuration["Email:AppBaseUrl"] is { Length: > 0 } url ? url : "http://localhost:5173").TrimEnd('/');

    public string TrackingBaseUrl =>
        (configuration["Tracking:PublicBaseUrl"] is { Length: > 0 } url ? url : AppBaseUrl).TrimEnd('/');

    public string? PostbackSecret => configuration["Tracking:PostbackSecret"] is { Length: > 0 } s ? s : null;

    public string ReferralLink(string code) => $"{AppBaseUrl}/register?ref={Uri.EscapeDataString(code)}";

    public string InvitationLink(string code) => $"{AppBaseUrl}/join/{code}";

    public string TrackingShortUrl(string code) => $"{TrackingBaseUrl}/t/{code}";
}

public static class CodeGenerator
{
    private const string UrlSafe = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>Cryptographically random URL-safe code.</summary>
    public static string New(int length = 8) => RandomNumberGenerator.GetString(UrlSafe, length);
}

public static class DbErrors
{
    public static bool IsUniqueViolation(DbUpdateException ex) => ProblemExceptionHandler.IsUniqueViolation(ex);
}

/// <summary>Validated UTC [From, To] range used by list/analytics endpoints.</summary>
public readonly record struct DateRange(DateTime From, DateTime To)
{
    public const int MaxDays = 366;

    /// <summary>Defaults to the last <paramref name="defaultDays"/> days ending now. Rejects from &gt; to and ranges over 366 days.</summary>
    public static DateRange Resolve(DateTime? from, DateTime? to, DateTime nowUtc, int defaultDays = 30)
    {
        var end = to.HasValue ? AsUtc(to.Value) : nowUtc;
        var start = from.HasValue ? AsUtc(from.Value) : end.AddDays(-defaultDays);
        if (start > end)
            throw new DomainException("range.invalid", "'from' must be before or equal to 'to'.");
        if ((end - start).TotalDays > MaxDays)
            throw new DomainException("range.too_long", $"The date range may span at most {MaxDays} days.");
        return new DateRange(start, end);
    }

    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

/// <summary>Amount in one currency.</summary>
public sealed record MoneyAmount(string Currency, decimal Amount);

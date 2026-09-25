using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Hosting;
using OptimizeAll.Api.Common.Errors;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Modules.Marketing.Shared;

/// <summary>
/// Public URLs and secrets used by the growth features. Read from configuration on every use (not cached) so
/// hosts and tests can change them at runtime:
///   (web app origin)          referral/invitation links: <see cref="IPublicOrigin"/> (site URL → Email:AppBaseUrl → request)
///   Tracking:PublicBaseUrl    base URL of the short tracking links (default: the web app origin)
///   Tracking:PostbackSecret   HMAC-SHA256 secret advertisers use to sign conversion postbacks
/// </summary>
public sealed class MarketingUrls(IConfiguration configuration, IPublicOrigin publicOrigin)
{
    public string AppBaseUrl => publicOrigin.Current;

    public string TrackingBaseUrl =>
        (configuration["Tracking:PublicBaseUrl"] is { Length: > 0 } url ? url : AppBaseUrl).TrimEnd('/');

    public string? PostbackSecret => configuration["Tracking:PostbackSecret"] is { Length: > 0 } s ? s : null;

    public string ReferralLink(string code) => $"{AppBaseUrl}{AppLinks.Register}?ref={Uri.EscapeDataString(code)}";

    public string InvitationLink(string code) => AppBaseUrl + AppLinks.Invitation(code);

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

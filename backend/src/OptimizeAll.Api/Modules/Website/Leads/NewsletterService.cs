using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Accounts;
using OptimizeAll.Api.Modules.Notifications.Templates;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Website;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Website.Leads;

/// <summary>
/// Newsletter signup with double opt-in. Subscribing stores the consent (version, timestamp, IP hash, source, UTM) as
/// Pending and emails a single-use confirmation link (48 h); confirming publishes <see cref="NewsletterSubscribed"/>. Every
/// email carries a stable unsubscribe link. Responses never reveal whether an address is already subscribed.
/// </summary>
public sealed class NewsletterService(
    AppDbContext db, IDatabaseDialect dialect, FormGuard guard, IPrivacyHasher hasher, ICurrentUser user, IEmailSender email,
    IOptions<EmailOptions> emailOptions, IOptions<SecurityOptions> security, IEventPublisher events, IAuditLogger audit, TimeProvider clock,
    ILogger<NewsletterService> logger, EmailTemplateService templates, IOptions<ExportOptions> exports)
{
    public static readonly TimeSpan ConfirmLifetime = TimeSpan.FromHours(48);

    public const string SubscribeMessage = "Almost there! Check your inbox and click the link to confirm your subscription.";

    public async Task<NewsletterResultDto> SubscribeAsync(NewsletterSubscribeInput input, CancellationToken ct)
    {
        if (guard.Check(input, ConsentTexts.NewsletterVersion) == FormCheck.Spam) return new NewsletterResultDto("pending", SubscribeMessage);
        var address = input.Email.Trim();
        if (!FieldRules.IsEmail(address)) throw FieldRules.FieldError("website.invalid", "email", "Enter a valid email address.");
        var normalized = Normalization.Email(address);
        var now = clock.GetUtcNow().UtcDateTime;

        var subscriber = await db.Set<NewsletterSubscriber>().FirstOrDefaultAsync(s => s.NormalizedEmail == normalized, ct);
        if (subscriber?.Status == NewsletterStatus.Confirmed) return new NewsletterResultDto("pending", SubscribeMessage);

        var isNew = subscriber is null;
        subscriber ??= new NewsletterSubscriber { Email = address, NormalizedEmail = normalized };
        var token = NewToken();
        subscriber.Status = NewsletterStatus.Pending;
        subscriber.ConfirmTokenHash = Sha256(token);
        subscriber.ConfirmTokenExpiresAt = now.Add(ConfirmLifetime);
        subscriber.ConsentVersion = input.ConsentVersion;
        subscriber.ConsentAt = now;
        subscriber.IpHash = hasher.Hash(user.IpAddress);
        subscriber.Source = WebsiteRules.Clean(input.Source) is { } src ? (src.Length > 60 ? src[..60] : src) : "website";
        subscriber.UtmSource = WebsiteRules.Clean(input.Utm?.Source);
        subscriber.UtmMedium = WebsiteRules.Clean(input.Utm?.Medium);
        subscriber.UtmCampaign = WebsiteRules.Clean(input.Utm?.Campaign);
        subscriber.UnsubscribedAt = null;
        if (isNew)
        {
            subscriber.UnsubscribeTokenHash = Sha256(UnsubscribeToken(subscriber.Id));
            db.Set<NewsletterSubscriber>().Add(subscriber);
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (dialect.IsUniqueViolation(ex))
        {
            // A concurrent signup for the same address won; its confirmation email is on the way.
            return new NewsletterResultDto("pending", SubscribeMessage);
        }

        var baseUrl = emailOptions.Value.AppBaseUrl.TrimEnd('/');
        var mail = await templates.RenderAsync(EmailTemplateCatalog.NewsletterConfirm, new Dictionary<string, string>
        {
            ["confirmUrl"] = $"{baseUrl}/newsletter/confirm?token={Uri.EscapeDataString(token)}",
            ["unsubscribeUrl"] = UnsubscribeUrl(subscriber.Id),
            ["hours"] = ConfirmLifetime.TotalHours.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
        }, ct);
        await SendAsync(subscriber.Email, mail.Subject, mail.Text, ct);
        return new NewsletterResultDto("pending", SubscribeMessage);
    }

    public async Task<NewsletterResultDto> ConfirmAsync(NewsletterTokenInput input, CancellationToken ct)
    {
        var hash = Sha256(input.Token.Trim());
        var now = clock.GetUtcNow().UtcDateTime;
        var subscriber = await db.Set<NewsletterSubscriber>().FirstOrDefaultAsync(s => s.ConfirmTokenHash == hash, ct);
        if (subscriber is null || subscriber.ConfirmTokenExpiresAt < now || subscriber.Status != NewsletterStatus.Pending)
            throw new DomainException("website.newsletter_invalid_token", "This confirmation link is invalid or has expired. Please sign up again.");

        // Conditional update: a double click confirms (and publishes) exactly once.
        var changed = await db.Set<NewsletterSubscriber>()
            .Where(s => s.Id == subscriber.Id && s.Status == NewsletterStatus.Pending && s.ConfirmTokenHash == hash)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, NewsletterStatus.Confirmed)
                .SetProperty(x => x.ConfirmedAt, now)
                .SetProperty(x => x.ConfirmTokenHash, (string?)null)
                .SetProperty(x => x.ConfirmTokenExpiresAt, (DateTime?)null)
                .SetProperty(x => x.UpdatedAt, now), ct);
        if (changed == 1)
            await events.PublishAsync(new NewsletterSubscribed(subscriber.Id, subscriber.Email, now), ct);
        return new NewsletterResultDto("confirmed", "You're subscribed. Thanks for joining!");
    }

    public async Task<NewsletterResultDto> UnsubscribeAsync(NewsletterTokenInput input, CancellationToken ct)
    {
        var hash = Sha256(input.Token.Trim());
        var subscriber = await db.Set<NewsletterSubscriber>().FirstOrDefaultAsync(s => s.UnsubscribeTokenHash == hash, ct)
            ?? throw new DomainException("website.newsletter_invalid_token", "This unsubscribe link is invalid.");
        if (subscriber.Status != NewsletterStatus.Unsubscribed)
        {
            subscriber.Status = NewsletterStatus.Unsubscribed;
            subscriber.UnsubscribedAt = clock.GetUtcNow().UtcDateTime;
            subscriber.ConfirmTokenHash = null;
            subscriber.ConfirmTokenExpiresAt = null;
            await db.SaveChangesAsync(ct);
        }
        return new NewsletterResultDto("unsubscribed", "You've been unsubscribed and won't receive the newsletter any more.");
    }

    /// <summary>
    /// Stable, unguessable unsubscribe token for a subscriber (HMAC of the id with the server secret), so every newsletter
    /// can carry a working link without storing the raw token. Email senders call <see cref="UnsubscribeUrl"/>.
    /// </summary>
    public string UnsubscribeToken(Guid subscriberId)
    {
        var key = Encoding.UTF8.GetBytes("newsletter-unsubscribe:" + security.Value.HashSalt);
        return Base64Url(HMACSHA256.HashData(key, subscriberId.ToByteArray()));
    }

    public string UnsubscribeUrl(Guid subscriberId) =>
        $"{emailOptions.Value.AppBaseUrl.TrimEnd('/')}/newsletter/unsubscribe?token={UnsubscribeToken(subscriberId)}";

    // ---------------------------------------------------------------- Staff (site.manage)

    public async Task<PagedResult<SubscriberDto>> ListAsync(SubscriberQuery query, CancellationToken ct) =>
        CmsStore.Map(await Filter(query).OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id).ToPagedAsync(query, ct), ToDto);

    /// <summary>The list's filters (status, email search), shared with the CSV export so it holds what the list shows.</summary>
    private IQueryable<NewsletterSubscriber> Filter(SubscriberQuery query)
    {
        var q = db.Set<NewsletterSubscriber>().AsNoTracking();
        if (query.Status is { } status) q = q.Where(s => s.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search)) q = q.Where(s => EF.Functions.Like(s.Email, PagingExtensions.LikePattern(query.Search), "\\"));
        return q;
    }

    /// <summary>Unsubscribes an address on the subscriber's behalf (e.g. they asked by reply). Idempotent.</summary>
    public async Task<SubscriberDto> UnsubscribeByStaffAsync(Guid id, CancellationToken ct)
    {
        var s = await db.Set<NewsletterSubscriber>().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new DomainException("website.not_found", "Subscriber was not found.", DomainErrorKind.NotFound);
        if (s.Status != NewsletterStatus.Unsubscribed)
        {
            var before = s.Status;
            s.Status = NewsletterStatus.Unsubscribed;
            s.UnsubscribedAt = clock.GetUtcNow().UtcDateTime;
            s.ConfirmTokenHash = null;
            s.ConfirmTokenExpiresAt = null;
            audit.Record("website.subscriber_unsubscribed", nameof(NewsletterSubscriber), s.Id, new { Status = before }, new { s.Status });
            await db.SaveChangesAsync(ct);
        }
        return ToDto(s);
    }

    /// <summary>Erases a subscriber entirely (data-erasure request). The audit entry keeps only the id.</summary>
    public async Task DeleteSubscriberAsync(Guid id, CancellationToken ct)
    {
        var s = await db.Set<NewsletterSubscriber>().FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new DomainException("website.not_found", "Subscriber was not found.", DomainErrorKind.NotFound);
        audit.Record("website.subscriber_deleted", nameof(NewsletterSubscriber), s.Id, new { s.Status, s.Source });
        db.Remove(s);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NewsletterSubscriber>> ExportAsync(SubscriberQuery query, CancellationToken ct)
    {
        var q = Filter(query);
        await ExportLimit.EnsureAsync(q, exports.Value.NewsletterSubscribers, ct);
        var rows = await q.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
        audit.Record("website.subscribers_exported", nameof(NewsletterSubscriber), "bulk", after: new { query.Status, query.Search, count = rows.Count });
        await db.SaveChangesAsync(ct);
        return rows;
    }

    public static SubscriberDto ToDto(NewsletterSubscriber s) => new(
        s.Id, s.Email, s.Status, s.Source, s.ConsentVersion, s.ConsentAt, s.ConfirmedAt, s.UnsubscribedAt, s.UtmSource, s.CreatedAt);

    private async Task SendAsync(string to, string subject, string text, CancellationToken ct)
    {
        try
        {
            var result = await email.SendAsync(new EmailMessage(to, to, subject, text), ct);
            if (!result.Success) logger.LogWarning("Newsletter email failed: {Error}", result.Error);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Newsletter email failed");
        }
    }

    private static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

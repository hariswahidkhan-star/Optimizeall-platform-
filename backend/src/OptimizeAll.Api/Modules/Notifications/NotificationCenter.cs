using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OptimizeAll.Api.Common.Audit;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Notifications;

// ---------- DTOs ----------

public sealed record NotificationDto(Guid Id, string Type, string Title, string Body, string? LinkUrl, DateTime CreatedAt, DateTime? ReadAt, bool IsRead);

public sealed class NotificationListQuery
{
    public bool UnreadOnly { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}

public sealed record UnreadCountDto(int Count);

public sealed record ReadAllResponse(int Updated);

public sealed record ChannelAvailabilityDto(NotificationChannel Channel, bool Available, string? Reason);

public sealed record PreferenceCellDto(NotificationChannel Channel, bool Enabled, bool Locked, bool Available);

public sealed record PreferenceRowDto(string Type, string Label, string Description, bool Essential, bool Marketing, IReadOnlyList<PreferenceCellDto> Channels);

public sealed record NotificationPreferencesDto(IReadOnlyList<ChannelAvailabilityDto> Channels, IReadOnlyList<PreferenceRowDto> Types);

public sealed class PreferenceChange
{
    [Required, MaxLength(60)]
    public string Type { get; set; } = string.Empty;

    [Required]
    public NotificationChannel? Channel { get; set; }

    public bool Enabled { get; set; }
}

public sealed class UpdatePreferencesRequest
{
    [Required, MinLength(1), MaxLength(200)]
    public List<PreferenceChange> Preferences { get; set; } = new();
}

public sealed class DeliveryQuery : PageQuery
{
    public DeliveryStatus? Status { get; set; }
    public NotificationChannel? Channel { get; set; }
    public Guid? UserId { get; set; }
}

public sealed record DeliveryDto(
    Guid Id, Guid NotificationId, Guid UserId, string? UserEmail, string? Type, string? Title, NotificationChannel Channel,
    DeliveryStatus Status, int Attempts, DateTime NextAttemptAt, DateTime? LockedUntil, string? LastError,
    string? ProviderMessageId, DateTime CreatedAt, DateTime? SentAt);

// ---------- Catalog ----------

/// <summary>Human-readable labels for every notification type (the preference matrix rows).</summary>
public static class NotificationCatalog
{
    private static readonly Dictionary<string, (string Label, string Description)> Labels = new()
    {
        [NotificationTypes.AccountEmailVerification] = ("Email verification", "Links to confirm your email address."),
        [NotificationTypes.AccountPasswordReset] = ("Password reset", "Links to reset your password."),
        [NotificationTypes.AccountStatusChanged] = ("Account status", "When your account is suspended or reactivated."),
        [NotificationTypes.SocialAccountVerified] = ("Social profile verification", "When a reviewer verifies or rejects one of your social profiles."),
        [NotificationTypes.SubmissionReceived] = ("Submission received", "Confirmation that we received your post submission."),
        [NotificationTypes.SubmissionDecision] = ("Submission decisions", "When a submission is approved, rejected or needs correction."),
        [NotificationTypes.SubmissionReversed] = ("Reversed approvals", "When an approved submission is reversed."),
        [NotificationTypes.AppealResolved] = ("Appeal outcomes", "When an appeal you filed is resolved."),
        [NotificationTypes.EarningApproved] = ("Earnings approved", "When a reward or bonus becomes payable."),
        [NotificationTypes.PayoutScheduled] = ("Payout scheduled", "When your earnings are included in an upcoming payout."),
        [NotificationTypes.PayoutPaid] = ("Payout paid", "When a payout has been sent to you."),
        [NotificationTypes.PayoutHold] = ("Payout holds", "When a payout is put on hold or released."),
        [NotificationTypes.CampaignAlert] = ("New campaigns", "New campaigns you're eligible for (marketing)."),
        [NotificationTypes.OnboardingReminder] = ("Getting-started reminders", "Reminders to finish setting up your account."),
        [NotificationTypes.Reactivation] = ("We miss you", "Occasional messages when you haven't been active (marketing)."),
        [NotificationTypes.Achievement] = ("Achievements", "Badges and milestones you unlock."),
        [NotificationTypes.ReferralQualified] = ("Referrals", "When someone you referred qualifies."),
        [NotificationTypes.SupportReply] = ("Support replies", "When our team replies to your support ticket."),
        [NotificationTypes.ReviewLiveCheckDue] = ("Live checks due (staff)", "Submissions waiting for a live-post check."),
        [NotificationTypes.BatchPrepared] = ("Payout batch prepared (staff)", "A payout batch is ready for finance review."),
    };

    /// <summary>Every constant declared in <see cref="NotificationTypes"/>, in declaration order.</summary>
    public static readonly IReadOnlyList<string> AllTypes = typeof(NotificationTypes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();

    /// <summary>
    /// Staff-only kinds and the permission a user needs to receive them. Users without it never get these
    /// notifications, so the preference matrix leaves them out.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> StaffTypes = new Dictionary<string, string>
    {
        [NotificationTypes.ReviewLiveCheckDue] = Permissions.SubmissionsReview,
        [NotificationTypes.BatchPrepared] = Permissions.PayoutsView,
    };

    /// <summary>The kinds shown in the preference matrix of a user with <paramref name="permissions"/>.</summary>
    public static IEnumerable<string> TypesFor(IReadOnlySet<string> permissions) =>
        AllTypes.Where(t => !StaffTypes.TryGetValue(t, out var needed) || permissions.Contains(needed));

    public static (string Label, string Description) Describe(string type) =>
        Labels.TryGetValue(type, out var d) ? d : (type, string.Empty);

    public static readonly NotificationChannel[] ConfigurableChannels = { NotificationChannel.Email, NotificationChannel.WhatsApp };
}

// ---------- Service ----------

public sealed class NotificationCenterService(
    AppDbContext db, IOptions<WhatsAppOptions> whatsApp, IAuditLogger audit, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<PagedResult<NotificationDto>> ListAsync(Guid userId, NotificationListQuery query, CancellationToken ct)
    {
        var q = db.Set<Notification>().AsNoTracking().Where(n => n.UserId == userId);
        if (query.UnreadOnly) q = q.Where(n => n.ReadAt == null);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(n => new NotificationDto(n.Id, n.Type, n.Title, n.Body, n.LinkUrl, n.CreatedAt, n.ReadAt, n.ReadAt != null))
            .ToListAsync(ct);
        return new PagedResult<NotificationDto>(items, total, query.Page, query.PageSize);
    }

    public async Task<UnreadCountDto> UnreadCountAsync(Guid userId, CancellationToken ct) =>
        new(await db.Set<Notification>().CountAsync(n => n.UserId == userId && n.ReadAt == null, ct));

    public async Task MarkReadAsync(Guid userId, Guid id, CancellationToken ct)
    {
        var now = Now;
        var updated = await db.Set<Notification>().Where(n => n.Id == id && n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        if (updated == 0 && !await db.Set<Notification>().AnyAsync(n => n.Id == id && n.UserId == userId, ct))
            throw DomainException.NotFound("Notification");
    }

    public async Task<ReadAllResponse> MarkAllReadAsync(Guid userId, CancellationToken ct)
    {
        var now = Now;
        var updated = await db.Set<Notification>().Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
        return new ReadAllResponse(updated);
    }

    // ----- Preferences -----

    /// <summary>The caller's preference matrix; staff-only kinds are listed only with the matching permission.</summary>
    public async Task<NotificationPreferencesDto> GetPreferencesAsync(Guid userId, IReadOnlySet<string> permissions, CancellationToken ct)
    {
        var user = await db.Set<User>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw DomainException.NotFound("User");
        var rows = await db.Set<NotificationPreference>().AsNoTracking().Where(p => p.UserId == userId).ToListAsync(ct);
        var disabled = rows.Where(r => !r.Enabled).Select(r => (r.Type, r.Channel)).ToHashSet();

        var whatsAppReason = !whatsApp.Value.IsConfigured
            ? "WhatsApp notifications are not available yet."
            : !user.WhatsAppOptIn || string.IsNullOrWhiteSpace(user.WhatsAppNumber)
                ? "Add your WhatsApp number and opt in on your profile to receive WhatsApp messages."
                : null;
        var emailReason = string.IsNullOrWhiteSpace(user.Email) ? "No email address on file." : null;

        var channels = new List<ChannelAvailabilityDto>
        {
            new(NotificationChannel.InApp, true, null),
            new(NotificationChannel.Email, emailReason is null, emailReason),
            new(NotificationChannel.WhatsApp, whatsAppReason is null, whatsAppReason),
        };

        var types = NotificationCatalog.TypesFor(permissions).Select(type =>
        {
            var essential = NotificationTypes.Essential.Contains(type);
            var (label, description) = NotificationCatalog.Describe(type);
            var cells = new List<PreferenceCellDto> { new(NotificationChannel.InApp, true, true, true) };
            cells.Add(new PreferenceCellDto(NotificationChannel.Email, essential || !disabled.Contains((type, NotificationChannel.Email)),
                essential, emailReason is null));
            cells.Add(new PreferenceCellDto(NotificationChannel.WhatsApp, essential || !disabled.Contains((type, NotificationChannel.WhatsApp)),
                essential, whatsAppReason is null));
            return new PreferenceRowDto(type, label, description, essential, NotificationTypes.Marketing.Contains(type), cells);
        }).ToList();

        return new NotificationPreferencesDto(channels, types);
    }

    public async Task<NotificationPreferencesDto> UpdatePreferencesAsync(Guid userId, IReadOnlySet<string> permissions,
        UpdatePreferencesRequest request, CancellationToken ct)
    {
        var known = NotificationCatalog.TypesFor(permissions).ToHashSet();
        var errors = new List<string>();
        foreach (var change in request.Preferences)
        {
            if (!known.Contains(change.Type)) errors.Add($"Unknown notification type '{change.Type}'.");
            else if (change.Channel is not (NotificationChannel.Email or NotificationChannel.WhatsApp))
                errors.Add("In-app notifications are always on; only Email and WhatsApp can be changed.");
            else if (NotificationTypes.Essential.Contains(change.Type))
                errors.Add($"'{NotificationCatalog.Describe(change.Type).Label}' messages are essential and can't be turned off.");
        }
        if (errors.Count > 0)
            throw new DomainException("notifications.preference_locked", "Some preferences can't be changed.", DomainErrorKind.Validation,
                new Dictionary<string, string[]> { ["preferences"] = errors.Distinct().ToArray() });

        var existing = await db.Set<NotificationPreference>().Where(p => p.UserId == userId).ToListAsync(ct);
        var now = Now;
        // Last change wins when the same cell appears twice in one request.
        foreach (var change in request.Preferences.GroupBy(c => (c.Type, c.Channel!.Value)).Select(g => g.Last()))
        {
            var row = existing.FirstOrDefault(p => p.Type == change.Type && p.Channel == change.Channel!.Value);
            if (row is null)
            {
                row = new NotificationPreference { UserId = userId, Type = change.Type, Channel = change.Channel!.Value };
                db.Set<NotificationPreference>().Add(row);
                existing.Add(row);
            }
            row.Enabled = change.Enabled;
            row.UpdatedAt = now;
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (Common.Errors.ProblemExceptionHandler.IsUniqueViolation(ex))
        {
            throw DomainException.Conflict("notifications.concurrent_update", "Your preferences were changed in another session. Reload and try again.");
        }
        return await GetPreferencesAsync(userId, permissions, ct);
    }

    // ----- Admin: outbox -----

    public Task<PagedResult<DeliveryDto>> ListDeliveriesAsync(DeliveryQuery query, CancellationToken ct) =>
        QueryDeliveriesAsync(query, null, ct);

    private async Task<PagedResult<DeliveryDto>> QueryDeliveriesAsync(DeliveryQuery query, Guid? id, CancellationToken ct)
    {
        var q = from d in db.Set<NotificationDelivery>().AsNoTracking()
                join n in db.Set<Notification>().AsNoTracking() on d.NotificationId equals n.Id into ns
                from n in ns.DefaultIfEmpty()
                join u in db.Set<User>().AsNoTracking() on d.UserId equals u.Id into us
                from u in us.DefaultIfEmpty()
                select new { d, n, u };
        if (id is { } deliveryId) q = q.Where(x => x.d.Id == deliveryId);
        if (query.Status is { } status) q = q.Where(x => x.d.Status == status);
        if (query.Channel is { } channel) q = q.Where(x => x.d.Channel == channel);
        if (query.UserId is { } userId) q = q.Where(x => x.d.UserId == userId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search);
            q = q.Where(x => (x.u != null && EF.Functions.Like(x.u.Email, p)) || (x.n != null && EF.Functions.Like(x.n.Type, p)));
        }
        q = query.Desc ? q.OrderByDescending(x => x.d.CreatedAt).ThenByDescending(x => x.d.Id) : q.OrderBy(x => x.d.CreatedAt).ThenBy(x => x.d.Id);

        var page = await q.Select(x => new DeliveryDto(x.d.Id, x.d.NotificationId, x.d.UserId, x.u == null ? null : x.u.Email,
                x.n == null ? null : x.n.Type, x.n == null ? null : x.n.Title, x.d.Channel, x.d.Status, x.d.Attempts, x.d.NextAttemptAt,
                x.d.LockedUntil, x.d.LastError, x.d.ProviderMessageId, x.d.CreatedAt, x.d.SentAt))
            .ToPagedAsync(query, ct);
        return page;
    }

    /// <summary>Returns a permanently failed delivery to the queue (fresh attempt budget). Only Failed deliveries can be retried.</summary>
    public async Task<DeliveryDto> RetryDeliveryAsync(Guid id, CancellationToken ct)
    {
        var now = Now;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var before = await db.Set<NotificationDelivery>().AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct)
                     ?? throw DomainException.NotFound("NotificationDelivery");
        var updated = await db.Set<NotificationDelivery>().Where(d => d.Id == id && d.Status == DeliveryStatus.Failed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, DeliveryStatus.Pending)
                .SetProperty(d => d.NextAttemptAt, now)
                .SetProperty(d => d.Attempts, 0)
                .SetProperty(d => d.LockedUntil, (DateTime?)null), ct);
        if (updated == 0)
            throw DomainException.Conflict("notifications.not_failed", "Only failed deliveries can be retried.");

        audit.Record("notification.delivery_retried", nameof(NotificationDelivery), id,
            new { before.Status, before.Attempts, before.LastError }, new { Status = DeliveryStatus.Pending, Attempts = 0 });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await QueryDeliveriesAsync(new DeliveryQuery { PageSize = 1 }, id, ct)).Items.Single();
    }
}

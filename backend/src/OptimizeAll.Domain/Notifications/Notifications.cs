using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Notifications;

public enum NotificationChannel
{
    InApp,
    Email,
    WhatsApp,
}

/// <summary>Stable notification kinds; participants can mute non-essential kinds per channel.</summary>
public static class NotificationTypes
{
    public const string AccountEmailVerification = "account.email_verification";
    public const string AccountPasswordReset = "account.password_reset";
    public const string AccountStatusChanged = "account.status_changed";
    public const string SocialAccountVerified = "social.verification";
    public const string SubmissionReceived = "submission.received";
    public const string SubmissionDecision = "submission.decision";
    public const string SubmissionReversed = "submission.reversed";
    public const string AppealResolved = "appeal.resolved";
    public const string EarningApproved = "earning.approved";
    public const string PayoutScheduled = "payout.scheduled";
    public const string PayoutPaid = "payout.paid";
    public const string PayoutHold = "payout.hold";
    public const string CampaignAlert = "campaign.alert";
    public const string OnboardingReminder = "retention.onboarding_reminder";
    public const string Reactivation = "retention.reactivation";
    public const string Achievement = "achievement.awarded";
    public const string ReferralQualified = "referral.qualified";
    public const string SupportReply = "support.reply";
    public const string ReviewLiveCheckDue = "review.live_check_due";
    public const string BatchPrepared = "payout.batch_prepared";

    /// <summary>Transactional kinds that cannot be muted.</summary>
    public static readonly IReadOnlySet<string> Essential = new HashSet<string>
    {
        AccountEmailVerification, AccountPasswordReset, AccountStatusChanged, PayoutPaid,
    };

    /// <summary>Marketing kinds that require marketing consent for email/WhatsApp.</summary>
    public static readonly IReadOnlySet<string> Marketing = new HashSet<string>
    {
        CampaignAlert, Reactivation,
    };
}

/// <summary>An in-app notification (the notification center). External channel sends are tracked in <see cref="NotificationDelivery"/>.</summary>
public class Notification : Entity
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? LinkUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public enum DeliveryStatus
{
    Pending,
    Sending,
    Sent,
    Failed,
    /// <summary>Not sent by design: channel not configured, user opted out, or no address.</summary>
    Skipped,
}

/// <summary>Outbox row for an external channel send, processed by the dispatch job with retry/backoff.</summary>
public class NotificationDelivery : Entity
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public NotificationChannel Channel { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string? LastError { get; set; }
    public string? ProviderMessageId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
}

public class NotificationPreference
{
    public Guid UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public NotificationChannel Channel { get; set; }
    public bool Enabled { get; set; }
    public DateTime UpdatedAt { get; set; }
}

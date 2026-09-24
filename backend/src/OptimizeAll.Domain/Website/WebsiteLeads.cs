using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Website;

public enum InquiryType
{
    Contact,
    Audit,
    Quote,
    Consultation,
}

public enum InquiryStatus
{
    New,
    InProgress,
    Qualified,
    Converted,
    Closed,
    Spam,
}

/// <summary>A lead captured by a public website form. Published as <c>WebsiteInquiryReceived</c> after commit.</summary>
public class WebsiteInquiry : AuditedEntity, IConcurrencyStamped
{
    public InquiryType Type { get; set; }
    public InquiryStatus Status { get; set; } = InquiryStatus.New;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Company { get; set; }
    public string? Website { get; set; }
    public string? Message { get; set; }
    public List<string> ServiceSlugs { get; set; } = new();
    public List<Guid> PackageIds { get; set; } = new();
    public string? BudgetRange { get; set; }
    public string? Timeline { get; set; }

    /// <summary>Type-specific extra answers (audit goals, quote details) as a JSON object of strings.</summary>
    public string PayloadJson { get; set; } = "{}";
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public string? UtmTerm { get; set; }
    public string? UtmContent { get; set; }
    public string? Referrer { get; set; }
    public string? LandingPath { get; set; }
    public string ConsentVersion { get; set; } = string.Empty;
    public DateTime ConsentAt { get; set; }
    public string? IpHash { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public string? StaffNotes { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>One weekly availability window in the consultation time zone ("Monday 09:00–12:00").</summary>
public sealed record AvailabilityWindow(DayOfWeek Day, string Start, string End);

/// <summary>Consultation booking rules (one row).</summary>
public class ConsultationSettings : AuditedEntity, IConcurrencyStamped
{
    public const string DefaultKey = "default";

    public string Key { get; set; } = DefaultKey;

    /// <summary>IANA time zone the weekly windows are expressed in.</summary>
    public string TimeZone { get; set; } = "UTC";
    public int SlotMinutes { get; set; } = 30;

    /// <summary>Bookings must be made at least this many hours ahead.</summary>
    public int MinNoticeHours { get; set; } = 12;

    /// <summary>How far ahead visitors can book.</summary>
    public int MaxDaysAhead { get; set; } = 30;
    public List<AvailabilityWindow> WeeklyAvailability { get; set; } = new();
    public bool IsEnabled { get; set; } = true;
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

/// <summary>A day (in the consultation time zone) with no bookable slots.</summary>
public class ConsultationBlackout : Entity
{
    public DateOnly Date { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum BookingStatus
{
    Confirmed,
    Cancelled,
    Completed,
    NoShow,
}

/// <summary>
/// A booked consultation. <see cref="SlotKey"/> is the UTC slot start while the booking holds the slot and null once
/// cancelled; its unique index makes double booking impossible even under concurrent requests.
/// </summary>
public class ConsultationBooking : AuditedEntity, IConcurrencyStamped
{
    public DateTime SlotStart { get; set; }
    public DateTime SlotEnd { get; set; }
    public string? SlotKey { get; set; }
    public BookingStatus Status { get; set; } = BookingStatus.Confirmed;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Company { get; set; }
    public string? Website { get; set; }
    public string? Notes { get; set; }
    public List<string> ServiceSlugs { get; set; } = new();

    /// <summary>The visitor's IANA time zone (for their confirmation email).</summary>
    public string VisitorTimeZone { get; set; } = "UTC";
    public Guid? InquiryId { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public static string KeyFor(DateTime slotStartUtc) => slotStartUtc.ToString("yyyy-MM-dd'T'HH:mm'Z'", System.Globalization.CultureInfo.InvariantCulture);
}

public enum NewsletterStatus
{
    Pending,
    Confirmed,
    Unsubscribed,
}

/// <summary>A newsletter subscriber (double opt-in). Tokens are stored only as hashes.</summary>
public class NewsletterSubscriber : AuditedEntity
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public NewsletterStatus Status { get; set; } = NewsletterStatus.Pending;
    public string? ConfirmTokenHash { get; set; }
    public DateTime? ConfirmTokenExpiresAt { get; set; }
    public string UnsubscribeTokenHash { get; set; } = string.Empty;
    public string ConsentVersion { get; set; } = string.Empty;
    public DateTime ConsentAt { get; set; }
    public string? IpHash { get; set; }
    public string? Source { get; set; }
    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? UnsubscribedAt { get; set; }
}

/// <summary>
/// A public form token that was spent on a successful submission (inquiry, consultation booking, job application).
/// Only the SHA-256 of the token's random id is stored; the unique hash makes each token single-use. Rows are deleted
/// once the token would have expired anyway.
/// </summary>
public class UsedFormToken : Entity
{
    public string TokenHash { get; set; } = string.Empty;
    public DateTime UsedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

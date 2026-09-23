using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Website.Catalog;
using OptimizeAll.Api.Modules.Website.Shared;
using OptimizeAll.Domain.Website;

namespace OptimizeAll.Api.Modules.Website.Leads;

public abstract class ContactDetailsInput : PublicFormInput
{
    [Required, MinLength(2), MaxLength(120)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(254)] public string Email { get; set; } = string.Empty;
    [MaxLength(32)] public string? Phone { get; set; }
    [MaxLength(150)] public string? Company { get; set; }
    [MaxLength(500)] public string? Website { get; set; }
}

public sealed class ContactInquiryInput : ContactDetailsInput
{
    [Required, MinLength(10), MaxLength(5000)] public string Message { get; set; } = string.Empty;
    public List<string>? ServiceSlugs { get; set; }
}

public sealed class AuditInquiryInput : ContactDetailsInput
{
    /// <summary>What the visitor wants to achieve (more leads, lower CPA, rankings…).</summary>
    [Required, MinLength(5), MaxLength(2000)] public string Goals { get; set; } = string.Empty;
    [Required, MaxLength(40)] public string BudgetRange { get; set; } = string.Empty;
    public List<string>? ServiceSlugs { get; set; }
    [MaxLength(500)] public string? Competitors { get; set; }
    [MaxLength(5000)] public string? Message { get; set; }
}

public sealed class QuoteInquiryInput : ContactDetailsInput
{
    public List<string>? ServiceSlugs { get; set; }
    public List<Guid>? PackageIds { get; set; }
    [Required, MaxLength(40)] public string BudgetRange { get; set; } = string.Empty;
    [Required, MaxLength(40)] public string Timeline { get; set; } = string.Empty;
    [Required, MinLength(10), MaxLength(5000)] public string Message { get; set; } = string.Empty;
}

public sealed record InquiryAcceptedDto(string Reference, string Message);

// ---------- Staff inbox ----------

public sealed record InquirySummaryDto(
    Guid Id, string Reference, InquiryType Type, InquiryStatus Status, string Name, string Email, string? Company, IReadOnlyList<string> ServiceSlugs,
    string? BudgetRange, string? UtmSource, string? UtmCampaign, Guid? AssignedToUserId, DateTime CreatedAt);

public sealed record InquiryDto(
    Guid Id, string Reference, InquiryType Type, InquiryStatus Status, string Name, string Email, string? Phone, string? Company, string? Website,
    string? Message, IReadOnlyList<string> ServiceSlugs, IReadOnlyList<Guid> PackageIds, string? BudgetRange, string? Timeline,
    IReadOnlyDictionary<string, string> Details, string? UtmSource, string? UtmMedium, string? UtmCampaign, string? UtmTerm, string? UtmContent,
    string? Referrer, string? LandingPath, string ConsentVersion, DateTime ConsentAt, Guid? AssignedToUserId, string? StaffNotes,
    Guid? BookingId, DateTime CreatedAt, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed class InquiryQuery : PageQuery
{
    public InquiryType? Type { get; set; }
    public InquiryStatus? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    [MaxLength(100)] public string? Service { get; set; }
    [MaxLength(150)] public string? UtmSource { get; set; }
}

public sealed class UpdateInquiryInput : StampedInput
{
    [Required] public InquiryStatus? Status { get; set; }
    public Guid? AssignedToUserId { get; set; }
    [MaxLength(4000)] public string? StaffNotes { get; set; }
}

public sealed record CountByDto(string Key, int Count);

public sealed record WebsiteOverviewDto(
    int InquiriesLast30Days, int InquiriesPrevious30Days, int NewInquiries, int UpcomingConsultations, int ConfirmedSubscribers,
    int PendingSubscribers, int NewApplications, int PublishedPosts, int PostsInReview, IReadOnlyList<CountByDto> InquiriesByType,
    IReadOnlyList<CountByDto> InquiriesBySource, IReadOnlyList<CountByDto> InquiriesByDay);

// ---------- Consultations ----------

public sealed record SlotsDto(string TimeZone, int SlotMinutes, bool Enabled, IReadOnlyList<DateTime> Slots);

public sealed class BookConsultationInput : ContactDetailsInput
{
    /// <summary>Slot start (UTC) as returned by <c>GET /public/consultations/slots</c>.</summary>
    [Required] public DateTime? SlotStart { get; set; }

    /// <summary>The visitor's IANA time zone, used for their confirmation email.</summary>
    [Required, MaxLength(64)] public string VisitorTimeZone { get; set; } = "UTC";
    [MaxLength(2000)] public string? Notes { get; set; }
    public List<string>? ServiceSlugs { get; set; }
}

public sealed record BookingConfirmationDto(string Reference, DateTime SlotStart, DateTime SlotEnd, string VisitorTimeZone, string Message);

public sealed record ConsultationSettingsDto(
    string TimeZone, int SlotMinutes, int MinNoticeHours, int MaxDaysAhead, IReadOnlyList<AvailabilityWindow> WeeklyAvailability, bool IsEnabled,
    IReadOnlyList<BlackoutDto> Blackouts, Guid ConcurrencyStamp);

public sealed record BlackoutDto(Guid Id, DateOnly Date, string? Reason);

public sealed class AvailabilityWindowInput
{
    [Required] public DayOfWeek? Day { get; set; }
    [Required, MaxLength(5)] public string Start { get; set; } = string.Empty;
    [Required, MaxLength(5)] public string End { get; set; } = string.Empty;
}

public sealed class ConsultationSettingsInput : StampedInput
{
    [Required, MaxLength(64)] public string TimeZone { get; set; } = "UTC";
    [Range(15, 240)] public int SlotMinutes { get; set; } = 30;
    [Range(0, 336)] public int MinNoticeHours { get; set; } = 12;
    [Range(1, 180)] public int MaxDaysAhead { get; set; } = 30;
    public List<AvailabilityWindowInput>? WeeklyAvailability { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class BlackoutInput
{
    [Required] public DateOnly? Date { get; set; }
    [MaxLength(200)] public string? Reason { get; set; }
}

public sealed record BookingDto(
    Guid Id, string Reference, DateTime SlotStart, DateTime SlotEnd, BookingStatus Status, string Name, string Email, string? Phone,
    string? Company, string? Website, string? Notes, IReadOnlyList<string> ServiceSlugs, string VisitorTimeZone, Guid? InquiryId,
    string? CancellationReason, DateTime? CancelledAt, DateTime CreatedAt, Guid ConcurrencyStamp);

public sealed class BookingQuery : PageQuery
{
    public BookingStatus? Status { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

public sealed class CancelBookingInput : StampedInput
{
    [Required, MinLength(3), MaxLength(500)] public string Reason { get; set; } = string.Empty;

    /// <summary>Email the visitor about the cancellation (default true).</summary>
    public bool NotifyVisitor { get; set; } = true;
}

public sealed class RescheduleBookingInput : StampedInput
{
    [Required] public DateTime? SlotStart { get; set; }
    public bool NotifyVisitor { get; set; } = true;
}

public sealed class BookingStatusInput : StampedInput
{
    [Required] public BookingStatus? Status { get; set; }
}

// ---------- Newsletter ----------

public sealed class NewsletterSubscribeInput : PublicFormInput
{
    [Required, MaxLength(254)] public string Email { get; set; } = string.Empty;

    /// <summary>Where the signup happened (footer, blog, home…).</summary>
    [MaxLength(60)] public string? Source { get; set; }
}

public sealed class NewsletterTokenInput
{
    [Required, MaxLength(200)] public string Token { get; set; } = string.Empty;
}

public sealed record NewsletterResultDto(string Status, string Message);

public sealed record SubscriberDto(
    Guid Id, string Email, NewsletterStatus Status, string? Source, string ConsentVersion, DateTime ConsentAt, DateTime? ConfirmedAt,
    DateTime? UnsubscribedAt, string? UtmSource, DateTime CreatedAt);

public sealed class SubscriberQuery : PageQuery
{
    public NewsletterStatus? Status { get; set; }
}

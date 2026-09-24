using System.ComponentModel.DataAnnotations;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Support;

namespace OptimizeAll.Api.Modules.Support;

// ---------- Participant ----------

public sealed class CreateTicketRequest
{
    [Required, MinLength(3), MaxLength(200)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public TicketCategory? Category { get; set; }

    [Required, MinLength(10), MaxLength(5000)]
    public string Body { get; set; } = string.Empty;

    public Guid? SubmissionId { get; set; }
    public Guid? PayoutItemId { get; set; }
}

public sealed class TicketMessageRequest
{
    [Required, MinLength(1), MaxLength(5000)]
    public string Body { get; set; } = string.Empty;
}

public sealed class MyTicketQuery : PageQuery
{
    public TicketStatus? Status { get; set; }
}

public sealed record TicketSummaryDto(
    Guid Id, string Reference, string Subject, TicketCategory Category, TicketStatus Status, TicketPriority Priority,
    DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>A message as the participant sees it. Staff authors are shown as the support team.</summary>
public sealed record ParticipantMessageDto(Guid Id, string Body, bool FromStaff, string AuthorName, DateTime CreatedAt);

public sealed record ParticipantTicketDto(
    Guid Id, string Reference, string Subject, TicketCategory Category, TicketStatus Status, TicketPriority Priority,
    Guid? SubmissionId, Guid? PayoutItemId, DateTime CreatedAt, DateTime UpdatedAt, DateTime? ResolvedAt,
    bool CanReply, IReadOnlyList<ParticipantMessageDto> Messages);

// ---------- Staff ----------

public sealed class StaffTicketQuery : PageQuery
{
    public TicketStatus? Status { get; set; }
    public TicketPriority? Priority { get; set; }
    public TicketCategory? Category { get; set; }

    /// <summary>A user id, "me" or "unassigned".</summary>
    [MaxLength(40)]
    public string? AssignedTo { get; set; }
}

public sealed record TicketPersonDto(Guid Id, string DisplayName, string Email);

public sealed record StaffTicketSummaryDto(
    Guid Id, string Reference, string Subject, TicketCategory Category, TicketStatus Status, TicketPriority Priority,
    TicketPersonDto Requester, TicketPersonDto? AssignedTo, DateTime CreatedAt, DateTime UpdatedAt, Guid ConcurrencyStamp);

public sealed record StaffMessageDto(Guid Id, string Body, bool IsInternalNote, bool FromStaff, Guid AuthorUserId, string AuthorName, DateTime CreatedAt);

public sealed record RequesterSummaryDto(
    Guid Id, string Email, string DisplayName, string CountryCode, UserStatus Status, ParticipantTier Tier, DateTime CreatedAt,
    int OpenTicketCount, int TotalTicketCount);

public sealed record StaffTicketDto(
    Guid Id, string Reference, string Subject, TicketCategory Category, TicketStatus Status, TicketPriority Priority,
    Guid? SubmissionId, Guid? PayoutItemId, DateTime CreatedAt, DateTime UpdatedAt, DateTime? ResolvedAt,
    RequesterSummaryDto Requester, TicketPersonDto? AssignedTo, IReadOnlyList<StaffMessageDto> Messages, Guid ConcurrencyStamp);

public sealed class StaffMessageRequest
{
    [Required, MinLength(1), MaxLength(5000)]
    public string Body { get; set; } = string.Empty;

    public bool IsInternalNote { get; set; }
}

public sealed class UpdateTicketRequest
{
    [Required]
    public TicketStatus? Status { get; set; }

    [Required]
    public TicketPriority? Priority { get; set; }

    public Guid? AssignedToUserId { get; set; }

    /// <summary>Optional: re-file the ticket under another category.</summary>
    public TicketCategory? Category { get; set; }

    [Required]
    public Guid? ConcurrencyStamp { get; set; }
}

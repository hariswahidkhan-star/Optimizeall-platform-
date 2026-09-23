using OptimizeAll.Domain.Common;

namespace OptimizeAll.Domain.Support;

public enum TicketStatus
{
    Open,
    AwaitingParticipant,
    AwaitingStaff,
    Resolved,
    Closed,
}

public enum TicketPriority
{
    Low,
    Normal,
    High,
    Urgent,
}

public enum TicketCategory
{
    General,
    Account,
    SocialProfile,
    Submission,
    Payout,
    Technical,
    Dispute,
}

public class SupportTicket : AuditedEntity, IConcurrencyStamped
{
    public string Reference { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public TicketCategory Category { get; set; }
    public TicketPriority Priority { get; set; } = TicketPriority.Normal;
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public Guid? AssignedToUserId { get; set; }
    public Guid? SubmissionId { get; set; }
    public Guid? PayoutItemId { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public List<SupportMessage> Messages { get; set; } = new();
}

public class SupportMessage : Entity
{
    public Guid TicketId { get; set; }
    public Guid AuthorUserId { get; set; }
    public string Body { get; set; } = string.Empty;

    /// <summary>Staff-only notes are never returned to the participant.</summary>
    public bool IsInternalNote { get; set; }
    public DateTime CreatedAt { get; set; }
}

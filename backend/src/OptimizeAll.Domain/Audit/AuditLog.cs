namespace OptimizeAll.Domain.Audit;

/// <summary>
/// Append-only audit record of a privileged or financial action. Rows can never be updated or deleted
/// (enforced in AppDbContext). Snapshots are JSON with sensitive fields excluded.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ActorUserId { get; set; }

    /// <summary>"system" for background jobs, otherwise the actor's primary role at the time.</summary>
    public string ActorType { get; set; } = "user";

    /// <summary>Dotted action name, e.g. "campaign.reward_rules_changed", "payout.batch_finalized".</summary>
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Reason { get; set; }
    public string? IpAddress { get; set; }
    public string? CorrelationId { get; set; }
}

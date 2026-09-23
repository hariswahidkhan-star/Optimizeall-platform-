namespace OptimizeAll.Domain.Common;

/// <summary>Base type for aggregate/entity rows keyed by a time-ordered GUID.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = IdGenerator.NewId();
}

/// <summary>Rows with creation/update timestamps maintained by the persistence layer (UTC).</summary>
public interface IAuditedEntity
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Optimistic concurrency. The persistence layer regenerates <see cref="ConcurrencyStamp"/> on every update
/// and uses it as a concurrency token, so two actors editing the same row cannot silently overwrite each other.
/// </summary>
public interface IConcurrencyStamped
{
    Guid ConcurrencyStamp { get; set; }
}

public abstract class AuditedEntity : Entity, IAuditedEntity
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

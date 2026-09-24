using System.Text.Json;
using System.Text.Json.Serialization;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Audit;

public interface IAuditLogger
{
    /// <summary>
    /// Stages an audit record in the current DbContext. It is written by the caller's SaveChanges, i.e. in the
    /// same transaction as the change it describes. Pass snapshots as anonymous objects without secrets.
    /// </summary>
    void Record(string action, string entityType, object entityId, object? before = null, object? after = null, string? reason = null);

    /// <summary>Same as <see cref="Record"/> for system (background job) actions.</summary>
    void RecordSystem(string action, string entityType, object entityId, object? after = null, string? reason = null);
}

/// <remarks>
/// During an impersonation session the actor is the impersonated user and <see cref="AuditLog.ImpersonatorUserId"/> the
/// staff member acting as them ("Admin X as User Y"); <see cref="AuditLog.ActorType"/> is then "impersonation".
/// </remarks>
public sealed class AuditLogger(AppDbContext db, ICurrentUser currentUser, IImpersonationContext impersonation, TimeProvider clock) : IAuditLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        MaxDepth = 8,
    };

    public void Record(string action, string entityType, object entityId, object? before = null, object? after = null, string? reason = null)
    {
        var roles = currentUser.Roles;
        var impersonatorId = currentUser.IsAuthenticated ? impersonation.ImpersonatorId : null;
        db.Set<AuditLog>().Add(new AuditLog
        {
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            ActorUserId = currentUser.IdOrNull,
            ImpersonatorUserId = impersonatorId,
            ActorType = !currentUser.IsAuthenticated ? "anonymous"
                : impersonatorId is not null ? "impersonation"
                : roles.Contains(OptimizeAll.Domain.Identity.Role.Admin) ? "Admin"
                : roles.Count > 0 ? roles.First().ToString() : "user",
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString() ?? string.Empty,
            BeforeJson = Serialize(before),
            AfterJson = Serialize(after),
            Reason = reason,
            IpAddress = currentUser.IpAddress,
            CorrelationId = currentUser.CorrelationId,
        });
    }

    public void RecordSystem(string action, string entityType, object entityId, object? after = null, string? reason = null)
    {
        db.Set<AuditLog>().Add(new AuditLog
        {
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            ActorType = "system",
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString() ?? string.Empty,
            AfterJson = Serialize(after),
            Reason = reason,
        });
    }

    private static string? Serialize(object? value) => value is null ? null : JsonSerializer.Serialize(value, JsonOptions);
}

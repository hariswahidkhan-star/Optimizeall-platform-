using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.Api.Modules.SocialMedia;

/// <summary>
/// Optimistic concurrency for marketing edits: when the caller sends the stamp it loaded, a mismatch answers 409
/// (<c>concurrency.conflict</c>) with a friendly message, and the stamp is used as the original value so a race between
/// load and save is also detected by EF (DbUpdateConcurrencyException → 409).
/// </summary>
public static class StampGuard
{
    public static void Expect<T>(DbContext db, T entity, Guid? stamp, string what) where T : class, IConcurrencyStamped
    {
        if (stamp is not { } expected) return;
        if (expected != entity.ConcurrencyStamp)
            throw DomainException.Conflict("concurrency.conflict", $"This {what} was changed by someone else. Reload and try again.");
        db.Entry(entity).Property(e => e.ConcurrencyStamp).OriginalValue = expected;
    }
}

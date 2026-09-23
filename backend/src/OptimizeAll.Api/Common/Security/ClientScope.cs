using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Common;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Common.Security;

/// <summary>
/// Tenant scoping for client-owned data. Staff with <c>clients.view</c> may access every client; users with the
/// Client role may only access organizations they are a member of. Every endpoint that reads or writes client-owned
/// data must go through this service (return 404, never 403, for another tenant's records).
/// </summary>
public interface IClientScope
{
    /// <summary>True when the caller is agency staff with access to all clients.</summary>
    bool IsStaff { get; }

    /// <summary>Client ids the caller is a member of (empty for staff; use <see cref="IsStaff"/>).</summary>
    Task<IReadOnlyList<Guid>> MemberClientIdsAsync(CancellationToken ct = default);

    /// <summary>Filters a query of client-owned rows to what the caller may see.</summary>
    Task<IQueryable<T>> ApplyAsync<T>(IQueryable<T> query, System.Linq.Expressions.Expression<Func<T, Guid>> clientId, CancellationToken ct = default);

    /// <summary>Throws 404 unless the caller may access the client; for client users also enforces the required member duty (Owner satisfies all).</summary>
    Task EnsureAccessAsync(Guid clientAccountId, ClientMemberRole minimumClientRole = ClientMemberRole.Viewer, CancellationToken ct = default);

    /// <summary>The caller's member role in the client (null for staff or non-members).</summary>
    Task<ClientMemberRole?> MemberRoleAsync(Guid clientAccountId, CancellationToken ct = default);
}

public sealed class ClientScope(AppDbContext db, ICurrentUser currentUser) : IClientScope
{
    private IReadOnlyList<Guid>? _memberIds;

    public bool IsStaff => currentUser.HasPermission(Permissions.ClientsView);

    public async Task<IReadOnlyList<Guid>> MemberClientIdsAsync(CancellationToken ct = default)
    {
        if (_memberIds is not null) return _memberIds;
        var userId = currentUser.IdOrNull;
        _memberIds = userId is null
            ? Array.Empty<Guid>()
            : await db.Set<ClientMember>().AsNoTracking().Where(m => m.UserId == userId)
                .Select(m => m.ClientAccountId).ToListAsync(ct);
        return _memberIds;
    }

    public async Task<IQueryable<T>> ApplyAsync<T>(IQueryable<T> query, System.Linq.Expressions.Expression<Func<T, Guid>> clientId, CancellationToken ct = default)
    {
        if (IsStaff) return query;
        var ids = await MemberClientIdsAsync(ct);
        var parameter = clientId.Parameters[0];
        var contains = System.Linq.Expressions.Expression.Call(
            typeof(Enumerable), nameof(Enumerable.Contains), new[] { typeof(Guid) },
            System.Linq.Expressions.Expression.Constant(ids.ToList()), clientId.Body);
        return query.Where(System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(contains, parameter));
    }

    public async Task EnsureAccessAsync(Guid clientAccountId, ClientMemberRole minimumClientRole = ClientMemberRole.Viewer, CancellationToken ct = default)
    {
        if (IsStaff)
        {
            if (!await db.Set<ClientAccount>().AnyAsync(c => c.Id == clientAccountId, ct)) throw DomainException.NotFound("Client");
            return;
        }
        var role = await MemberRoleAsync(clientAccountId, ct);
        if (role is null) throw DomainException.NotFound("Client");
        // Roles are duties, not a ladder: Owner may do everything, Viewer-level access is open to every member,
        // otherwise the member must hold exactly the required duty (Approver or Billing).
        if (role != ClientMemberRole.Owner && minimumClientRole != ClientMemberRole.Viewer && role != minimumClientRole)
            throw DomainException.Forbidden("client.insufficient_role", $"This action requires the {minimumClientRole} role in your organization.");
    }

    public async Task<ClientMemberRole?> MemberRoleAsync(Guid clientAccountId, CancellationToken ct = default)
    {
        var userId = currentUser.IdOrNull;
        if (userId is null) return null;
        return await db.Set<ClientMember>().AsNoTracking()
            .Where(m => m.ClientAccountId == clientAccountId && m.UserId == userId)
            .Select(m => (ClientMemberRole?)m.Role).FirstOrDefaultAsync(ct);
    }
}

using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Projects;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Clients;

/// <summary>
/// Extension point for client health: other modules contribute reasons (e.g. Billing: "Invoice INV-1042 is 21 days
/// overdue"). Register with <c>services.AddScoped&lt;IClientHealthSignalProvider, MyProvider&gt;()</c>. Providers must be
/// read-only and fast; a failing provider is skipped (logged), never breaking the health board.
/// </summary>
public interface IClientHealthSignalProvider
{
    /// <summary>Adds reasons for any of <paramref name="clientIds"/> to <paramref name="reasons"/> (keyed by client id).</summary>
    Task ContributeAsync(IReadOnlyCollection<Guid> clientIds, DateTime nowUtc, IDictionary<Guid, List<HealthReason>> reasons, CancellationToken ct);
}

/// <summary>Records the last paid invoice on the client (Billing publishes InvoicePaid). Idempotent.</summary>
public sealed class InvoicePaidHealthHandler(AppDbContext db) : IEventHandler<InvoicePaid>
{
    public async Task HandleAsync(InvoicePaid e, CancellationToken ct) =>
        await db.Set<ClientAccount>()
            .Where(c => c.Id == e.ClientAccountId && (c.LastInvoicePaidAt == null || c.LastInvoicePaidAt < e.OccurredAt))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastInvoicePaidAt, e.OccurredAt), ct);
}

public sealed class ClientHealthService(
    AppDbContext db,
    IClientScope scope,
    IEnumerable<IClientHealthSignalProvider> providers,
    TimeProvider clock,
    ILogger<ClientHealthService> logger)
{
    public async Task<ClientHealthDto> ForClientAsync(Guid clientId, CancellationToken ct)
    {
        await scope.EnsureAccessAsync(clientId, ct: ct);
        return (await ComputeAsync(new[] { clientId }, ct)).Single();
    }

    /// <summary>Health board: every Onboarding/Active/Paused client (optionally only the given account manager's).</summary>
    public async Task<IReadOnlyList<ClientHealthDto>> BoardAsync(Guid? accountManagerId, CancellationToken ct)
    {
        var q = db.Set<ClientAccount>().AsNoTracking().Where(c => c.Status != ClientAccountStatus.Churned);
        if (accountManagerId is { } am) q = q.Where(c => c.AccountManagerUserId == am);
        var ids = await q.Select(c => c.Id).ToListAsync(ct);
        return (await ComputeAsync(ids, ct)).OrderByDescending(h => h.Level).ThenBy(h => h.Score).ThenBy(h => h.ClientName).ToList();
    }

    public async Task<List<ClientHealthDto>> ComputeAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new List<ClientHealthDto>();
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var clients = await db.Set<ClientAccount>().AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Status, c.AccountManagerUserId, c.CreatedAt }).ToListAsync(ct);

        var overdue = await db.Set<ProjectTask>().AsNoTracking()
            .Where(t => ids.Contains(t.ClientAccountId) && t.Status != ProjectTaskStatus.Done && t.DueDate != null && t.DueDate < today)
            .GroupBy(t => t.ClientAccountId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var pending = await db.Set<Deliverable>().AsNoTracking()
            .Where(d => ids.Contains(d.ClientAccountId) && d.Status == DeliverableStatus.ClientReview)
            .GroupBy(d => d.ClientAccountId).Select(g => new { g.Key, Count = g.Count(), Oldest = g.Min(d => d.SentToClientAt) })
            .ToDictionaryAsync(x => x.Key, ct);

        // Last activity: newest message in a client-visible thread, task change, deliverable change or time entry.
        // Internal (staff-only) threads are the team talking among themselves, not an exchange with the client, so a
        // busy internal thread must not mask a client that has gone quiet.
        var lastMessage = await db.Set<MessageThread>().AsNoTracking().Where(t => ids.Contains(t.ClientAccountId) && !t.IsInternal && t.MessageCount > 0)
            .GroupBy(t => t.ClientAccountId).Select(g => new { g.Key, At = g.Max(t => t.LastMessageAt) }).ToDictionaryAsync(x => x.Key, x => x.At, ct);
        var lastTask = await db.Set<ProjectTask>().AsNoTracking().Where(t => ids.Contains(t.ClientAccountId))
            .GroupBy(t => t.ClientAccountId).Select(g => new { g.Key, At = g.Max(t => t.UpdatedAt) }).ToDictionaryAsync(x => x.Key, x => x.At, ct);
        var lastDeliverable = await db.Set<Deliverable>().AsNoTracking().Where(t => ids.Contains(t.ClientAccountId))
            .GroupBy(t => t.ClientAccountId).Select(g => new { g.Key, At = g.Max(t => t.UpdatedAt) }).ToDictionaryAsync(x => x.Key, x => x.At, ct);
        var lastTime = await db.Set<TimeEntry>().AsNoTracking().Where(t => ids.Contains(t.ClientAccountId))
            .GroupBy(t => t.ClientAccountId).Select(g => new { g.Key, At = g.Max(t => t.UpdatedAt) }).ToDictionaryAsync(x => x.Key, x => x.At, ct);

        var since = now.AddDays(-90);
        var feedback = await db.Set<ClientFeedback>().AsNoTracking().Where(f => ids.Contains(f.ClientAccountId) && f.CreatedAt >= since.AddDays(-275))
            .Select(f => new { f.ClientAccountId, f.Kind, f.Score, f.CreatedAt }).ToListAsync(ct);

        var external = new Dictionary<Guid, List<HealthReason>>();
        foreach (var provider in providers)
        {
            try
            {
                await provider.ContributeAsync(ids, now, external, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Client health provider {Provider} failed; skipped", provider.GetType().Name);
            }
        }

        var managers = clients.Where(c => c.AccountManagerUserId != null).Select(c => c.AccountManagerUserId!.Value).Distinct().ToList();
        var people = await db.Set<User>().AsNoTracking().Where(u => managers.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new PersonDto(u.Id, u.DisplayName, u.Email), ct);

        var result = new List<ClientHealthDto>();
        foreach (var c in clients)
        {
            DateTime? last = new[]
            {
                lastMessage.TryGetValue(c.Id, out var m) ? m : (DateTime?)null,
                lastTask.TryGetValue(c.Id, out var t) ? t : null,
                lastDeliverable.TryGetValue(c.Id, out var d) ? d : null,
                lastTime.TryGetValue(c.Id, out var te) ? te : null,
            }.Max();
            var csat = feedback.Where(f => f.ClientAccountId == c.Id && f.Kind == ClientFeedbackKind.Csat && f.CreatedAt >= since).ToList();
            var latestNps = feedback.Where(f => f.ClientAccountId == c.Id && f.Kind == ClientFeedbackKind.Nps)
                .OrderByDescending(f => f.CreatedAt).Select(f => (int?)f.Score).FirstOrDefault();
            var p = pending.GetValueOrDefault(c.Id);
            var signals = new HealthSignals(
                overdue.GetValueOrDefault(c.Id), p?.Count ?? 0,
                p?.Oldest is { } oldest ? (now - oldest).TotalDays : 0,
                last is { } l ? (now - l).TotalDays : (now - c.CreatedAt).TotalDays < 7 ? 0 : null,
                csat.Count == 0 ? null : csat.Average(x => x.Score), csat.Count, latestNps,
                external.TryGetValue(c.Id, out var ext) ? ext : new List<HealthReason>());
            var health = ClientHealthCalculator.Compute(signals);
            result.Add(new ClientHealthDto(c.Id, c.Name, c.Status, health.Score, health.Level, health.Reasons,
                signals.OverdueTasks, signals.PendingApprovals,
                c.AccountManagerUserId is { } am && people.TryGetValue(am, out var person) ? person : null));
        }
        return result;
    }
}

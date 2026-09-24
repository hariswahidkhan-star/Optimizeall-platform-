using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Events;
using OptimizeAll.Api.Common.Notifications;
using OptimizeAll.Api.Modules.Billing;
using OptimizeAll.Domain.Agency;
using OptimizeAll.Domain.Billing;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Events;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.PaymentsHub;

/// <summary>
/// Payments hub: a unified tracking view and manual actions over incoming (Billing) and outgoing (Payouts) money.
/// It owns only claims, proofs and per-client reminder schedules; every money write is delegated to the Billing and
/// Payouts services.
/// </summary>
public static class PaymentsHubModule
{
    public static IServiceCollection AddPaymentsHubModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<PaymentsHubQueries>();
        services.AddScoped<PaymentsHubActions>();
        services.AddScoped<PaymentClaimService>();
        services.AddScoped<PaymentProofService>();
        services.AddScoped<PaymentReminderService>();
        services.AddScoped<IEventHandler<InvoicePaid>, InvoicePaidNotificationHandler>();
        return services;
    }
}

/// <summary>
/// Tells the client's account manager (in-app) that an invoice was paid in full. Runs after commit on
/// <see cref="InvoicePaid"/>; one notification per (invoice, settlement time), so a redelivered event does not repeat it.
/// </summary>
public sealed class InvoicePaidNotificationHandler(AppDbContext db, INotificationService notifications) : IEventHandler<InvoicePaid>
{
    public async Task HandleAsync(InvoicePaid e, CancellationToken ct)
    {
        var client = await db.Set<ClientAccount>().AsNoTracking().Where(c => c.Id == e.ClientAccountId)
            .Select(c => new { c.Name, c.AccountManagerUserId }).FirstOrDefaultAsync(ct);
        if (client?.AccountManagerUserId is not { } manager) return;
        if (!await db.Set<User>().AnyAsync(u => u.Id == manager && u.Status == UserStatus.Active, ct)) return;
        var number = await db.Set<Invoice>().Where(i => i.Id == e.InvoiceId).Select(i => i.Number).FirstOrDefaultAsync(ct);
        var link = BillingLinks.AgencyInvoice(e.InvoiceId);
        var since = e.OccurredAt.AddMinutes(-1);
        if (await db.Set<Domain.Notifications.Notification>().AnyAsync(n => n.UserId == manager && n.Type == BillingNotificationTypes.InvoicePaid &&
                                                                          n.LinkUrl == link && n.CreatedAt >= since, ct))
            return;
        await notifications.StageAsync(new NotificationRequest(manager, BillingNotificationTypes.InvoicePaid,
            $"Invoice {number} was paid",
            $"{client.Name} paid invoice {number} in full ({e.Amount.ToString("N" + Money.MinorUnitDigits(e.Currency), System.Globalization.CultureInfo.InvariantCulture)} {e.Currency}).",
            link), ct);
        await db.SaveChangesAsync(ct);
    }
}

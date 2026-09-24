using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Payouts;

/// <summary>Regressions found by the finance money journey (frontend/e2e/j-finance).</summary>
public sealed class FinanceJourneyRegressionTests : FreshDatabaseTest
{
    /// <summary>
    /// Switches the settlement currency (no unpaid earnings yet) and moves the clock to five minutes before the current
    /// period's cutoff; returns that period. Clients must be created after this (access tokens live 15 minutes).
    /// </summary>
    private async Task<PayoutPeriod> SettleInAsync(string currency)
    {
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        (await finance.PutAsJsonAsync("/api/v1/finance/payout-schedule", new
        {
            frequency = "Weekly", anchorCutoffDate = "2026-01-04", cutoffLocalTime = "23:59:59", timeZone = "UTC",
            paymentDelayDays = 2, minimumPayoutAmount = 1, settlementCurrency = currency, earningHoldDays = 0,
            autoPrepareBatches = false, effectiveFrom = Api.Now(), reason = "Settle payouts in " + currency, confirm = true,
        })).EnsureSuccessStatusCode();
        var period = PayoutPeriodCalculator.PeriodContaining(await Api.ScheduleAsync(), Api.Now());
        if (period.CutoffUtc.AddMinutes(-5) > Api.Now()) Api.SetNow(period.CutoffUtc.AddMinutes(-5));
        return period;
    }

    [Fact]
    public async Task Payout_notifications_show_amounts_in_the_currency_minor_units()
    {
        var period = await SettleInAsync("KWD");
        var (_, finance1) = await Api.CreateClientAsync(Role.Finance);
        var (_, finance2) = await Api.CreateClientAsync(Role.Finance);
        var participant = await Api.ParticipantAsync();
        await Api.EarnAsync(participant.Id, 12.345m, "KWD");

        Api.SetNow(period.CutoffUtc.AddMinutes(2));
        var batchId = (await finance1.PrepareAsync()).GetProperty("batch").Id();
        await finance2.FinalizeAsync(batchId);
        var itemId = Assert.Single(await Api.ItemsAsync(batchId)).Id;
        (await finance2.PostAsJsonAsync($"/api/v1/finance/payout-batches/{batchId}/items/{itemId}/record-payment",
            new { paymentReference = "KNET-0001", paidAt = Api.Now() })).EnsureSuccessStatusCode();

        var notes = await Api.WithDbAsync(db => db.Set<Notification>().AsNoTracking()
            .Where(n => n.UserId == participant.Id).Select(n => new { n.Type, n.Body }).ToListAsync());
        // KWD has three decimals: 12.345, never "12.35" (a different amount).
        Assert.Contains("12.345 KWD", notes.Single(n => n.Type == NotificationTypes.PayoutScheduled).Body);
        Assert.Contains("12.345 KWD", notes.Single(n => n.Type == NotificationTypes.PayoutPaid).Body);
        var prepared = await Api.WithDbAsync(db => db.Set<Notification>().AsNoTracking()
            .Where(n => n.Type == NotificationTypes.BatchPrepared).Select(n => n.Body).FirstAsync());
        Assert.Contains("12.345 KWD", prepared);
    }

    [Fact]
    public async Task Payout_notifications_show_zero_decimal_currencies_without_decimals()
    {
        var period = await SettleInAsync("JPY");
        var (_, finance1) = await Api.CreateClientAsync(Role.Finance);
        var (_, finance2) = await Api.CreateClientAsync(Role.Finance);
        var participant = await Api.ParticipantAsync();
        await Api.EarnAsync(participant.Id, 1500m, "JPY");

        Api.SetNow(period.CutoffUtc.AddMinutes(2));
        var batchId = (await finance1.PrepareAsync()).GetProperty("batch").Id();
        await finance2.FinalizeAsync(batchId);
        var body = await Api.WithDbAsync(db => db.Set<Notification>().AsNoTracking()
            .Where(n => n.UserId == participant.Id && n.Type == NotificationTypes.PayoutScheduled).Select(n => n.Body).SingleAsync());
        Assert.Contains("1,500 JPY", body);
        Assert.DoesNotContain("1500.00", body);
    }

    [Fact]
    public async Task The_next_cycle_estimate_leaves_out_test_accounts_which_are_never_paid()
    {
        await SettleInAsync("USD");
        var (_, admin) = await Api.CreateClientAsync(Role.Admin);
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        var created = await (await admin.PostAsJsonAsync("/api/v1/admin/test-users", new { roles = new[] { "Participant" } })).ReadJsonAsync();
        var real = await Api.ParticipantAsync();
        await Api.EarnAsync(created.Id(), 30m);
        await Api.EarnAsync(real.Id, 12.5m);

        var summary = await finance.GetJsonAsync("/api/v1/admin/payments/summary");
        var estimate = summary.GetProperty("outgoing").GetProperty("nextCycle").GetProperty("estimatedAvailable");
        var usd = estimate.EnumerateArray().Single(e => e.Str("currency") == "USD");
        Assert.Equal(12.5m, usd.Dec("amount"));
    }
}

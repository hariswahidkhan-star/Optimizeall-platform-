using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Payouts;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.IntegrationTests.Infrastructure;
using FK = OptimizeAll.IntegrationTests.Payouts.FinanceKit;
using FreshDatabaseTest = OptimizeAll.IntegrationTests.Payouts.FreshDatabaseTest;

namespace OptimizeAll.IntegrationTests.PaymentsHub;

/// <summary>Payments hub, outgoing money: every write goes through the Payouts flow (four-eyes, holds, conditional updates).</summary>
public sealed class PaymentsHubPayoutTests : FreshDatabaseTest
{
    private const string Hub = PaymentsHubKit.Hub;

    /// <summary>A finalized batch (prepared by one finance user, finalized by another) with one item per amount.</summary>
    private async Task<(Guid BatchId, List<PayoutItem> Items, List<TestUser> Users, HttpClient Preparer)> FinalizedAsync(params decimal[] amounts)
    {
        var period = await FK.AlignToFreshPeriodAsync(Api);
        var users = new List<TestUser>();
        foreach (var amount in amounts)
        {
            var u = await FK.ParticipantAsync(Api);
            users.Add(u);
            await FK.EarnAsync(Api, u.Id, amount);
        }
        FK.SetNow(Api, period.CutoffUtc.AddMinutes(1));
        var (_, preparer) = await Api.CreateClientAsync(Role.Finance);
        var batchId = FK.Id((await FK.PrepareAsync(preparer)).GetProperty("batch"));
        var (_, finalizer) = await Api.CreateClientAsync(Role.Finance);
        await FK.FinalizeAsync(finalizer, batchId);
        var items = await FK.ItemsAsync(Api, batchId);
        Assert.All(items, i => Assert.Equal(PayoutItemStatus.AwaitingPayment, i.Status));
        return (batchId, items, users, preparer);
    }

    [Fact]
    public async Task Marking_a_payout_paid_goes_through_the_payouts_flow_and_a_retry_is_a_replay()
    {
        var (batchId, items, users, preparer) = await FinalizedAsync(40m);
        var item = items.Single();
        var url = $"{Hub}/payouts/{item.Id}/mark-paid";
        var body = new { paymentReference = "WISE-TX-1001", paidAt = FK.Now(Api), note = "Paid from the USD account" };

        var paid = await (await preparer.PostAsJsonAsync(url, body)).ReadJsonAsync();
        Assert.False(paid.GetProperty("replayed").GetBoolean());
        Assert.Equal("Paid", paid.GetProperty("record").GetProperty("status").GetString());
        Assert.Equal("Completed", paid.GetProperty("batchStatus").GetString());
        var retry = await (await preparer.PostAsJsonAsync(url, body)).ReadJsonAsync();
        Assert.True(retry.GetProperty("replayed").GetBoolean());
        // A different payment for an already paid item is a real conflict.
        var (_, other) = await Api.CreateClientAsync(Role.Admin);
        await (await other.PostAsJsonAsync(url, body with { paymentReference = "WISE-TX-9999" })).ShouldFailAsync(409, "payout.already_recorded");

        var stored = Assert.Single(await FK.ItemsAsync(Api, batchId));
        Assert.Equal("WISE-TX-1001", stored.PaymentReference);
        Assert.All(await Api.WithDbAsync(db => db.Set<EarningEntry>().Where(e => e.UserId == users[0].Id).ToListAsync()),
            e => Assert.Equal(EarningStatus.Paid, e.Status));
        Assert.Equal(1, await Api.WithDbAsync(db => db.Set<PayoutItemEarning>().CountAsync(p => p.PayoutItemId == item.Id)));

        // The participant sees status, masked reference and date.
        var participant = await Api.LoginAsync(users[0]);
        var mine = await (await participant.GetAsync($"/api/v1/me/payouts/{item.Id}")).ReadJsonAsync();
        var payout = mine.GetProperty("payout");
        Assert.Equal("Paid", payout.GetProperty("status").GetString());
        Assert.EndsWith("1001", payout.GetProperty("paymentReference").GetString());
        Assert.NotEqual(JsonValueKind.Null, payout.GetProperty("paidAt").ValueKind);
    }

    [Fact]
    public async Task Segregation_and_holds_of_the_payouts_flow_apply_to_hub_actions()
    {
        // System-prepared batch: the finalizer may not record its payments (existing four-eyes rule).
        var period = await FK.AlignToFreshPeriodAsync(Api);
        var user = await FK.ParticipantAsync(Api);
        await FK.EarnAsync(Api, user.Id, 25m);
        FK.SetNow(Api, period.CutoffUtc.AddMinutes(1));
        await Api.RunJobAsync<PayoutPreparationJob>();
        var batch = Assert.Single(await FK.BatchesAsync(Api));
        var (_, finalizer) = await Api.CreateClientAsync(Role.Finance);
        await FK.FinalizeAsync(finalizer, batch.Id);
        var item = Assert.Single(await FK.ItemsAsync(Api, batch.Id));
        var url = $"{Hub}/payouts/{item.Id}/mark-paid";
        await (await finalizer.PostAsJsonAsync(url, new { paymentReference = "SELF-001", paidAt = FK.Now(Api) })).ShouldFailAsync(403, "payout.self_record");
        var batchPaid = await (await finalizer.PostAsJsonAsync($"{Hub}/payout-batches/{batch.Id}/mark-paid",
            new { paymentReference = "BULK-SELF", paidAt = FK.Now(Api), confirm = true })).ReadJsonAsync();
        Assert.Equal(1, batchPaid.GetProperty("invalid").GetInt32());

        // A participant on hold needs an override reason.
        var (_, holder) = await Api.CreateClientAsync(Role.Finance);
        (await holder.PostAsJsonAsync("/api/v1/finance/holds", new { userId = user.Id, reason = "Fraud check pending" })).EnsureSuccessStatusCode();
        var (_, recorder) = await Api.CreateClientAsync(Role.Finance);
        await (await recorder.PostAsJsonAsync(url, new { paymentReference = "HOLD-001", paidAt = FK.Now(Api) })).ShouldFailAsync(409, "payout.user_on_hold");
        var ok = await recorder.PostAsJsonAsync(url, new { paymentReference = "HOLD-001", paidAt = FK.Now(Api), overrideReason = "Transfer left before the hold was placed" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        // Billing-only staff can't touch payouts and don't see them.
        var (_, manager) = await Api.CreateClientAsync(Role.AccountManager);
        await (await manager.PostAsJsonAsync($"{Hub}/payouts/{item.Id}/mark-failed", new { kind = "Failed", reason = "Account closed" })).ShouldFailAsync(403);
        var managerList = await (await manager.GetAsync($"{Hub}?direction=Outgoing")).ReadJsonAsync();
        Assert.Equal(0, managerList.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Marking_a_payout_failed_or_returned_requeues_its_earnings_for_the_next_batch()
    {
        var (batchId, items, users, preparer) = await FinalizedAsync(30m, 50m);
        var returned = items.Single(i => i.Amount == 30m);
        var url = $"{Hub}/payouts/{returned.Id}/mark-failed";
        var body = new { kind = "Returned", reason = "IBAN closed (R03)" };
        var failed = await (await preparer.PostAsJsonAsync(url, body)).ReadJsonAsync();
        Assert.True(failed.GetProperty("requeued").GetBoolean());
        Assert.Equal("Failed", failed.GetProperty("record").GetProperty("status").GetString());
        Assert.True((await (await preparer.PostAsJsonAsync(url, body)).ReadJsonAsync()).GetProperty("replayed").GetBoolean());
        var stored = (await FK.ItemsAsync(Api, batchId)).Single(i => i.Id == returned.Id);
        Assert.Equal("Returned by the bank: IBAN closed (R03)", stored.FailureReason);
        var earnings = await Api.WithDbAsync(db => db.Set<EarningEntry>().Where(e => e.UserId == users[0].Id).ToListAsync());
        Assert.All(earnings, e =>
        {
            Assert.Equal(EarningStatus.Approved, e.Status);
            Assert.Null(e.PayoutItemId);
        });

        // A failed item can't then be marked paid; the other item is paid with the batch action.
        await (await preparer.PostAsJsonAsync($"{Hub}/payouts/{returned.Id}/mark-paid", new { paymentReference = "LATE-001", paidAt = FK.Now(Api) }))
            .ShouldFailAsync(409, "payout.item_not_awaiting_payment");
        await (await preparer.PostAsJsonAsync($"{Hub}/payout-batches/{batchId}/mark-paid", new { paymentReference = "BULK-77", paidAt = FK.Now(Api) }))
            .ShouldFailAsync(400, "request.confirm_required");
        var bulk = await (await preparer.PostAsJsonAsync($"{Hub}/payout-batches/{batchId}/mark-paid",
            new { paymentReference = "BULK-77", paidAt = FK.Now(Api), confirm = true })).ReadJsonAsync();
        Assert.Equal(1, bulk.GetProperty("recorded").GetInt32());
        Assert.Equal("Completed", bulk.GetProperty("batchStatus").GetString());

        // The hub lists both outgoing records with their statuses; the summary shows what was paid out.
        var list = await (await preparer.GetAsync($"{Hub}?direction=Outgoing&batchId={batchId}")).ReadJsonAsync();
        var statuses = list.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("status").GetString()).OrderBy(s => s).ToList();
        Assert.Equal(new[] { "Failed", "Paid" }, statuses);
        var summary = await (await preparer.GetAsync($"{Hub}/summary")).ReadJsonAsync();
        var paidOut = summary.GetProperty("outgoing").GetProperty("paidOutThisMonth").EnumerateArray().Single();
        Assert.Equal(50m, paidOut.GetProperty("amount").GetDecimal());
        // The re-queued 30 USD is expected in the next cycle.
        var next = summary.GetProperty("outgoing").GetProperty("nextCycle").GetProperty("estimatedAvailable").EnumerateArray().Single();
        Assert.Equal(30m, next.GetProperty("amount").GetDecimal());
    }
}

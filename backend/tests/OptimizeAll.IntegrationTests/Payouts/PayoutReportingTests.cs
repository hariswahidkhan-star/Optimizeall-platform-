using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Domain.Support;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Payouts;

public sealed class ParticipantSummaryTests : FreshDatabaseTest
{
    [Fact]
    public async Task Summary_buckets_and_payout_history_follow_the_earning_lifecycle()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var me = await Api.ParticipantAsync();

        // Paid: 20 (paid in this period's batch below). Reversed: a 4 earning cancelled before payment.
        var toPay = await Api.EarnAsync(me.Id, 20m);
        var cancelled = await Api.EarnAsync(me.Id, 4m);
        await Api.ReverseAsync(cancelled.Id);
        Api.SetNow(period.CutoffUtc.AddMinutes(1));
        var (financeUser, finance) = await Api.CreateClientAsync(Role.Finance);
        var batchId = (await finance.PrepareAsync()).GetProperty("batch").Id();
        var (_, finance2) = await Api.CreateClientAsync(Role.Finance);
        await finance2.FinalizeAsync(batchId);
        var item = Assert.Single(await Api.ItemsAsync(batchId));

        // Scheduled: in the finalized batch, not yet paid.
        var client = await Api.LoginAsync(me);
        var scheduledSummary = await client.GetJsonAsync("/api/v1/me/earnings/summary");
        Assert.Equal(20m, scheduledSummary.Dec("scheduled"));
        Assert.Equal(0m, scheduledSummary.Dec("paid"));
        var history = await client.GetJsonAsync("/api/v1/me/payouts");
        Assert.Equal("AwaitingPayment", history.GetProperty("items")[0].Str("status"));

        await finance2.PostJsonAsync($"/api/v1/finance/payout-batches/{batchId}/items/{item.Id}/record-payment",
            new { paymentReference = "WISE-TRANSFER-123456", paidAt = Api.Now() });

        // Approved + on hold: a fresh 15 earning (available in 3 days). Pending: a 6 bonus awaiting approval + an open
        // submission estimated at 9 USD + one at 5 EUR without an FX rate (reported separately).
        await Api.EarnAsync(me.Id, 15m);
        await Api.EarnAsync(me.Id, 6m, type: EarningType.QualityBonus, requiresApproval: true);
        await Api.SubmissionAsync(me.Id, SubmissionStatus.Pending, 9m);
        await Api.SubmissionAsync(me.Id, SubmissionStatus.UnderReview, 5m, "EUR");
        await Api.SubmissionAsync(me.Id, SubmissionStatus.Rejected, 50m);

        client = await Api.LoginAsync(me);
        var summary = await client.GetJsonAsync("/api/v1/me/earnings/summary");
        Assert.Equal("USD", summary.Str("currency"));
        Assert.Equal(15m, summary.Dec("pending"));
        Assert.Equal(15m, summary.Dec("approved"));
        Assert.Equal(15m, summary.Dec("onHold"));
        Assert.Equal(0m, summary.Dec("scheduled"));
        Assert.Equal(20m, summary.Dec("paid"));
        Assert.Equal(4m, summary.Dec("reversed"));
        Assert.Equal(15m, summary.Dec("availableForNextPayout"));
        Assert.Equal(35m, summary.Dec("lifetimeEarned"));
        Assert.False(summary.GetProperty("activeHold").GetBoolean());
        var next = summary.GetProperty("nextPayout");
        Assert.True(next.GetProperty("meetsMinimum").GetBoolean());
        Assert.Equal(15m, next.Dec("estimatedAmount"));
        Assert.Equal(10m, next.Dec("minimumPayoutAmount"));
        var pendingByCurrency = summary.GetProperty("pendingByCurrency").EnumerateArray().ToDictionary(p => p.Str("currency"));
        Assert.True(pendingByCurrency["USD"].GetProperty("converted").GetBoolean());
        Assert.False(pendingByCurrency["EUR"].GetProperty("converted").GetBoolean());
        Assert.Equal(5m, pendingByCurrency["EUR"].Dec("amount"));

        // Payout history: masked reference and included earnings.
        var payouts = await client.GetJsonAsync("/api/v1/me/payouts");
        var payout = Assert.Single(payouts.GetProperty("items").EnumerateArray());
        Assert.Equal("Paid", payout.Str("status"));
        Assert.Equal("••••3456", payout.Str("paymentReference"));
        Assert.Equal(20m, payout.Dec("amount"));
        Assert.Equal($"PB-{period.PeriodKey}", payout.Str("batchReference"));
        var detail = await client.GetJsonAsync($"/api/v1/me/payouts/{item.Id}");
        var included = Assert.Single(detail.GetProperty("earnings").EnumerateArray());
        Assert.Equal(toPay.Id, included.Id());
        Assert.Equal("Paid", included.Str("status"));

        // Another participant cannot see it.
        var (_, stranger) = await Api.CreateClientAsync(Role.Participant);
        await (await stranger.GetAsync($"/api/v1/me/payouts/{item.Id}")).ShouldFailAsync(404);

        // A payout hold is reported with a neutral message; the estimate drops to zero.
        finance = await Api.LoginAsync(financeUser);
        await finance.PostJsonAsync("/api/v1/finance/holds", new { userId = me.Id, reason = "Chargeback investigation #42" });
        var held = await client.GetJsonAsync("/api/v1/me/earnings/summary");
        Assert.True(held.GetProperty("activeHold").GetBoolean());
        Assert.DoesNotContain("Chargeback", held.Str("holdMessage"));
        Assert.Equal(0m, held.GetProperty("nextPayout").Dec("estimatedAmount"));
        var balance = await finance.GetJsonAsync($"/api/v1/finance/users/{me.Id}/balance");
        Assert.Equal(summary.Dec("paid"), balance.Dec("paid"));

        var holds = await finance.GetJsonAsync($"/api/v1/finance/holds?active=true&userId={me.Id}");
        var holdId = holds.GetProperty("items")[0].Id();
        var released = await finance.PostJsonAsync($"/api/v1/finance/holds/{holdId}/release", new { note = "Investigation closed" });
        Assert.False(released.GetProperty("isActive").GetBoolean());
        await (await finance.PostAsJsonAsync($"/api/v1/finance/holds/{holdId}/release", new { })).ShouldFailAsync(409, "payout.hold_not_active");

        var earnings = await client.GetJsonAsync("/api/v1/me/earnings?status=Paid");
        Assert.Equal(1, earnings.GetProperty("total").GetInt32());
        Assert.Equal("USD", earnings.GetProperty("items")[0].Str("settlementCurrency"));
    }
}

public sealed class ScheduleSettingsTests : FreshDatabaseTest
{
    private object Schedule(string currency, string timeZone = "UTC", DateTime? effectiveFrom = null) => new
    {
        frequency = "Monthly", anchorCutoffDate = "2026-01-31", cutoffLocalTime = "18:00", timeZone,
        paymentDelayDays = 3, minimumPayoutAmount = 25, settlementCurrency = currency, earningHoldDays = 7,
        autoPrepareBatches = true, effectiveFrom = effectiveFrom ?? Api.Now(), reason = "Move to monthly payouts", confirm = true,
    };

    [Fact]
    public async Task Schedule_versions_and_settlement_currency_changes_are_guarded()
    {
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        var initial = await finance.GetJsonAsync("/api/v1/finance/payout-schedule");
        Assert.Equal("Biweekly", initial.GetProperty("current").Str("frequency"));
        Assert.False(initial.GetProperty("current").GetProperty("isDefault").GetBoolean());
        Assert.Equal("Initial schedule", initial.GetProperty("history")[0].Str("changeReason"));
        Assert.Equal(6, initial.GetProperty("upcoming").GetArrayLength());

        // No unpaid earnings yet: the currency can change.
        var eur = await (await finance.PutAsJsonAsync("/api/v1/finance/payout-schedule", Schedule("EUR"))).ReadJsonAsync();
        Assert.Equal("EUR", eur.GetProperty("current").Str("settlementCurrency"));
        Assert.Equal("Monthly", eur.GetProperty("current").Str("frequency"));
        Assert.Equal("18:00:00", eur.GetProperty("current").Str("cutoffLocalTime"));
        Assert.Equal(2, eur.GetProperty("history").GetArrayLength());

        var user = await Api.ParticipantAsync();
        await Api.EarnAsync(user.Id, 10m, "EUR");
        await (await finance.PutAsJsonAsync("/api/v1/finance/payout-schedule", Schedule("USD")))
            .ShouldFailAsync(409, "payout.settlement_currency_in_use");
        // Other changes in the same currency are fine; future versions are listed as scheduled changes.
        var future = await (await finance.PutAsJsonAsync("/api/v1/finance/payout-schedule", Schedule("EUR", "Europe/Berlin", Api.Now().AddDays(10)))).ReadJsonAsync();
        Assert.Equal("UTC", future.GetProperty("current").Str("timeZone"));
        Assert.Equal("Europe/Berlin", future.GetProperty("scheduledChanges")[0].Str("timeZone"));

        await (await finance.PutAsJsonAsync("/api/v1/finance/payout-schedule", Schedule("EUR", "Nowhere/Land"))).ShouldFailAsync(400, "payout.invalid_time_zone");
        await (await finance.PutAsJsonAsync("/api/v1/finance/payout-schedule", Schedule("EUR", effectiveFrom: Api.Now().AddDays(-1))))
            .ShouldFailAsync(400, "payout.effective_in_past");
        var (_, reviewer) = await Api.CreateClientAsync(Role.Reviewer);
        await (await reviewer.PutAsJsonAsync("/api/v1/finance/payout-schedule", Schedule("EUR"))).ShouldFailAsync(403);
        Assert.Equal(2, await Api.WithDbAsync(db => db.Set<AuditLog>().CountAsync(a => a.Action == "payout.schedule_changed")));
    }
}

public sealed class ReconciliationAndExportTests : FreshDatabaseTest
{
    [Fact]
    public async Task Reconciliation_is_balanced_and_detects_an_injected_discrepancy()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var a = await Api.ParticipantAsync();
        var b = await Api.ParticipantAsync();
        await Api.EarnAsync(a.Id, 10m);
        await Api.EarnAsync(a.Id, 15.5m);
        await Api.EarnAsync(b.Id, 40m);
        Api.SetNow(period.CutoffUtc.AddMinutes(1));
        var (_, f1) = await Api.CreateClientAsync(Role.Finance);
        var batchId = (await f1.PrepareAsync()).GetProperty("batch").Id();
        var (_, f2) = await Api.CreateClientAsync(Role.Finance);
        await f2.FinalizeAsync(batchId);
        var items = await Api.ItemsAsync(batchId);
        var itemA = items.Single(i => i.UserId == a.Id);
        await f2.PostJsonAsync($"/api/v1/finance/payout-batches/{batchId}/items/{itemA.Id}/record-payment",
            new { paymentReference = "REF-SHARED-1", paidAt = Api.Now() });

        var report = await f2.GetJsonAsync($"/api/v1/finance/payout-batches/{batchId}/reconciliation");
        Assert.True(report.GetProperty("isBalanced").GetBoolean(), report.ToString());
        Assert.Equal(65.5m, report.Dec("expected"));
        Assert.Equal(25.5m, report.Dec("recordedPaid"));
        Assert.Equal(40m, report.Dec("awaiting"));
        Assert.Empty(report.GetProperty("discrepancies").EnumerateArray());

        // Corrupt the database behind the application's back.
        var itemB = items.Single(i => i.UserId == b.Id);
        await Api.WithDbAsync(db => db.Set<PayoutItem>().Where(i => i.Id == itemB.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Amount, 41m)));
        var broken = await f2.GetJsonAsync($"/api/v1/finance/payout-batches/{batchId}/reconciliation");
        Assert.False(broken.GetProperty("isBalanced").GetBoolean());
        var types = broken.GetProperty("discrepancies").EnumerateArray().Select(d => d.Str("type")).ToList();
        Assert.Contains("item_amount_mismatch", types);
        Assert.Contains("batch_total_mismatch", types);
        Assert.Contains(broken.GetProperty("discrepancies").EnumerateArray(), d => d.Str("type") == "item_amount_mismatch" && d.Id("itemId") == itemB.Id);

        var csv = await (await f2.GetAsync($"/api/v1/finance/payout-batches/{batchId}/reconciliation.csv")).Content.ReadAsStringAsync();
        Assert.Contains("DISCREPANCY", csv);
        Assert.Contains("item_amount_mismatch", csv);

        // Same payment reference used on a second item → warning (not an error).
        await Api.WithDbAsync(db => db.Set<PayoutItem>().Where(i => i.Id == itemB.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Amount, 40m)));
        await f2.PostJsonAsync($"/api/v1/finance/payout-batches/{batchId}/items/{itemB.Id}/record-payment",
            new { paymentReference = "REF-SHARED-1", paidAt = Api.Now() });
        var warned = await f2.GetJsonAsync($"/api/v1/finance/payout-batches/{batchId}/reconciliation");
        Assert.True(warned.GetProperty("isBalanced").GetBoolean());
        Assert.Equal("Completed", warned.Str("status"));
        Assert.All(warned.GetProperty("discrepancies").EnumerateArray(), d =>
        {
            Assert.Equal("duplicate_payment_reference", d.Str("type"));
            Assert.Equal("warning", d.Str("severity"));
        });
    }

    [Fact]
    public async Task Exports_review_warnings_and_payment_instructions()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var a = await Api.ParticipantAsync(destination: "GB29NWBK60161331926819");
        var riskySubmission = await Api.SubmissionAsync(a.Id, SubmissionStatus.Approved, 33m, riskScore: 80);
        var earning = await Api.EarnAsync(a.Id, 33m, submissionId: riskySubmission);
        await Api.WithDbAsync(async db =>
        {
            db.Add(new Appeal { SubmissionId = riskySubmission, UserId = a.Id, DecisionAppealed = SubmissionStatus.Rejected, Reason = "Please re-check" });
            db.Add(new SupportTicket { Reference = "T-" + Guid.NewGuid().ToString("N")[..8], UserId = a.Id, Subject = "Wrong amount", Category = TicketCategory.Dispute });
            await db.SaveChangesAsync();
        });
        Api.SetNow(period.CutoffUtc.AddMinutes(1));
        var (_, f1) = await Api.CreateClientAsync(Role.Finance);
        var batchId = (await f1.PrepareAsync()).GetProperty("batch").Id();

        var detail = await f1.BatchAsync(batchId);
        var warnings = detail.GetProperty("warnings");
        Assert.Single(warnings.GetProperty("openAppeals").EnumerateArray());
        Assert.Single(warnings.GetProperty("openDisputes").EnumerateArray());
        Assert.Contains(warnings.GetProperty("highRiskSubmissions").EnumerateArray(), w => w.Str("detail").Contains("80"));
        Assert.Equal(50, warnings.GetProperty("highRiskThreshold").GetInt32());
        var totals = detail.GetProperty("totalsByStatus").EnumerateArray().Single();
        Assert.Equal("Pending", totals.Str("status"));

        var itemId = detail.ItemFor(a.Id).Id("itemId");
        var itemDetail = await f1.GetJsonAsync($"/api/v1/finance/payout-batches/{batchId}/items/{itemId}");
        Assert.Equal(33m, itemDetail.Dec("earningsTotal"));
        Assert.Equal(earning.Id, Assert.Single(itemDetail.GetProperty("earnings").EnumerateArray()).Id());

        var export = await (await f1.GetAsync($"/api/v1/finance/payout-batches/{batchId}/export.csv")).Content.ReadAsStringAsync();
        Assert.Contains("Batch reference,Period,Item ID,Participant name,Email,Country,Amount,Currency", export);
        Assert.Contains(a.Email, export);
        Assert.Contains("••••6819", export);
        Assert.DoesNotContain("GB29NWBK60161331926819", export);

        // Payment instructions: only for finalized batches, require confirm, contain the decrypted destination, audited.
        await (await f1.GetAsync($"/api/v1/finance/payout-batches/{batchId}/payment-instructions.csv?confirm=true"))
            .ShouldFailAsync(409, "payout.batch_not_finalized");
        var (_, f2) = await Api.CreateClientAsync(Role.Finance);
        await f2.FinalizeAsync(batchId);
        await (await f2.GetAsync($"/api/v1/finance/payout-batches/{batchId}/payment-instructions.csv"))
            .ShouldFailAsync(400, "request.confirm_required");
        var instructions = await f2.GetAsync($"/api/v1/finance/payout-batches/{batchId}/payment-instructions.csv?confirm=true");
        instructions.EnsureSuccessStatusCode();
        var text = await instructions.Content.ReadAsStringAsync();
        Assert.Contains("Destination (confidential)", text);
        Assert.Contains("GB29NWBK60161331926819", text);
        var audit = await Api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking().SingleAsync(x => x.Action == "payout.payment_instructions_exported"));
        Assert.Equal(batchId.ToString(), audit.EntityId);
        Assert.DoesNotContain("GB29NWBK", audit.AfterJson);

        var (_, manager) = await Api.CreateClientAsync(Role.CampaignManager);
        await (await manager.GetAsync($"/api/v1/finance/payout-batches/{batchId}/payment-instructions.csv?confirm=true")).ShouldFailAsync(403);

        var list = await f2.GetJsonAsync("/api/v1/finance/payout-batches?status=Finalized");
        var summary = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(1, summary.GetProperty("itemCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("paidCount").GetInt32());
        Assert.NotNull(summary.GetProperty("finalizedBy").GetProperty("email").GetString());
    }
}

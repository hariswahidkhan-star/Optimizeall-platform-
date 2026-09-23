using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Payouts;

public sealed class BatchCutoffTests : FreshDatabaseTest
{
    [Fact]
    public async Task Biweekly_cutoff_includes_earnings_available_before_it_and_respects_the_hold_period()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var schedule = await Api.ScheduleAsync();
        Assert.Equal(PayoutFrequency.Biweekly, schedule.Frequency);
        Assert.Equal(3, schedule.EarningHoldDays);
        var user = await Api.ParticipantAsync();

        // Approved now → available in 3 days, before the cutoff (14 days away).
        var early = await Api.EarnAsync(user.Id, 12m);
        // Approved 12 days into the period → available after the cutoff (hold period) → next period.
        Api.SetNow(period.CutoffUtc.AddDays(-2));
        var late = await Api.EarnAsync(user.Id, 30m);
        Assert.True(late.AvailableAt > period.CutoffUtc);

        Api.SetNow(period.CutoffUtc.AddMinutes(5));
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        var prepared = await finance.PrepareAsync();
        Assert.True(prepared.GetProperty("created").GetBoolean());
        var batch = prepared.GetProperty("batch");
        Assert.Equal(period.PeriodKey, batch.Str("periodKey"));
        Assert.Equal($"PB-{period.PeriodKey}", batch.Str("reference"));
        Assert.Equal(period.CutoffUtc, batch.GetProperty("cutoffAt").GetDateTime().ToUniversalTime());
        Assert.Equal(1, batch.GetProperty("itemCount").GetInt32());
        Assert.Equal(12m, batch.Dec("totalAmount"));

        Assert.Equal(EarningStatus.Scheduled, (await Api.EarningAsync(early.Id)).Status);
        Assert.Equal(EarningStatus.Approved, (await Api.EarningAsync(late.Id)).Status);
        Assert.Null((await Api.EarningAsync(late.Id)).PayoutItemId);
    }

    [Fact]
    public async Task Karachi_schedule_cuts_off_at_local_time_boundary()
    {
        await Api.AlignToFreshPeriodAsync();
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        var update = await finance.PutAsJsonAsync("/api/v1/finance/payout-schedule", new
        {
            frequency = "Weekly", anchorCutoffDate = "2026-01-04", cutoffLocalTime = "23:59:59", timeZone = "Asia/Karachi",
            paymentDelayDays = 2, minimumPayoutAmount = 0, settlementCurrency = "USD", earningHoldDays = 0,
            autoPrepareBatches = true, effectiveFrom = Api.Now(), reason = "Pay Pakistani participants weekly", confirm = true,
        });
        var response = await update.ReadJsonAsync();
        Assert.Equal("Asia/Karachi", response.GetProperty("current").Str("timeZone"));
        var currentPeriod = response.GetProperty("currentPeriod");
        var cutoff = currentPeriod.GetProperty("cutoffAt").GetDateTime().ToUniversalTime();
        // 23:59:59 PKT (UTC+5) is 18:59:59 UTC on the cutoff local date.
        Assert.Equal(new TimeSpan(18, 59, 59), cutoff.TimeOfDay);
        Assert.Equal(DateOnly.Parse(currentPeriod.Str("cutoffLocalDate")), DateOnly.FromDateTime(cutoff));
        Assert.Equal(6, response.GetProperty("upcoming").GetArrayLength());

        var inside = await Api.ParticipantAsync();
        var outside = await Api.ParticipantAsync();
        Api.SetNow(cutoff);                         // exactly at the cutoff instant → belongs to this period
        var onBoundary = await Api.EarnAsync(inside.Id, 7m);
        Assert.Equal(cutoff, onBoundary.AvailableAt);
        Api.SetNow(cutoff.AddSeconds(1));           // 00:00:00 PKT → next period
        var afterBoundary = await Api.EarnAsync(outside.Id, 8m);

        Api.SetNow(cutoff.AddMinutes(10));
        var (_, finance2) = await Api.CreateClientAsync(Role.Finance);
        var prepared = await finance2.PrepareAsync();
        var batchId = prepared.GetProperty("batch").Id();
        Assert.Equal(currentPeriod.Str("periodKey"), prepared.GetProperty("batch").Str("periodKey"));
        var items = await Api.ItemsAsync(batchId);
        Assert.Single(items);
        Assert.Equal(inside.Id, items[0].UserId);
        Assert.Equal(EarningStatus.Approved, (await Api.EarningAsync(afterBoundary.Id)).Status);
    }
}

public sealed class BatchSelectionTests : FreshDatabaseTest
{
    [Fact]
    public async Task Only_approved_unpaid_eligible_earnings_are_included_with_netting_minimum_and_exclusions()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var (financeUser, finance) = await Api.CreateClientAsync(Role.Finance);

        var paid = await Api.ParticipantAsync();
        var approved = await Api.EarnAsync(paid.Id, 25m);
        var pendingApproval = await Api.EarnAsync(paid.Id, 5m, type: EarningType.QualityBonus, requiresApproval: true);
        var declined = await Api.EarnAsync(paid.Id, 6m, type: EarningType.QualityBonus, requiresApproval: true);
        var reversed = await Api.EarnAsync(paid.Id, 7m);
        await Api.ReverseAsync(reversed.Id);
        await Api.WithDbAsync(async db =>
        {
            var d = await db.Set<EarningEntry>().FirstAsync(e => e.Id == declined.Id);
            d.Status = EarningStatus.Declined;
            d.Reason = "Does not meet the quality bar";
            // Rows already Scheduled or Paid (in other batches) must never be picked up again.
            db.Set<EarningEntry>().AddRange(
                RawEntry(paid.Id, 100m, EarningStatus.Paid, Api.Now()),
                RawEntry(paid.Id, 200m, EarningStatus.Scheduled, Api.Now()));
            await db.SaveChangesAsync();
        });

        var clawback = await Api.ParticipantAsync();
        await Api.EarnAsync(clawback.Id, 30m);
        await finance.PostJsonAsync("/api/v1/finance/adjustments", new
        {
            requestId = Guid.NewGuid(), userId = clawback.Id, amount = -8m, currency = "USD",
            reason = "Duplicate reward recovered after dispute", confirm = true,
        });

        var held = await Api.ParticipantAsync();
        await Api.EarnAsync(held.Id, 50m);
        (await finance.PostAsJsonAsync("/api/v1/finance/holds", new { userId = held.Id, reason = "Fraud review" })).EnsureSuccessStatusCode();

        var suspended = await Api.ParticipantAsync();
        await Api.EarnAsync(suspended.Id, 40m);
        await Api.SetUserStatusAsync(suspended.Id, UserStatus.Suspended);

        var small = await Api.ParticipantAsync();
        var smallFirst = await Api.EarnAsync(small.Id, 5m);

        var noProfile = await Api.ParticipantAsync(withProfile: false);
        var noProfileEarning = await Api.EarnAsync(noProfile.Id, 15m);

        Api.SetNow(period.CutoffUtc.AddMinutes(1));
        finance = await Api.LoginAsync(financeUser);
        var prepared = await finance.PrepareAsync();
        var batchId = prepared.GetProperty("batch").Id();
        var detail = await finance.BatchAsync(batchId);

        // Paid participant: only the approved 25 (not pending/declined/reversed/scheduled/paid rows).
        var paidItem = detail.ItemFor(paid.Id);
        Assert.Equal(25m, paidItem.Dec("amount"));
        Assert.Equal(1, paidItem.GetProperty("earningCount").GetInt32());
        Assert.Equal("Pending", paidItem.Str("status"));
        Assert.Equal("••••6702", paidItem.Str("destinationHint"));
        Assert.Equal(EarningStatus.Scheduled, (await Api.EarningAsync(approved.Id)).Status);
        Assert.Equal(EarningStatus.PendingApproval, (await Api.EarningAsync(pendingApproval.Id)).Status);

        // Clawback netting: 30 − 8.
        Assert.Equal(22m, detail.ItemFor(clawback.Id).Dec("amount"));
        Assert.Equal(2, detail.ItemFor(clawback.Id).GetProperty("earningCount").GetInt32());

        // Missing payout profile → Held item, visible to finance.
        var heldItem = detail.ItemFor(noProfile.Id);
        Assert.Equal("Held", heldItem.Str("status"));
        Assert.Equal("No payout details on file", heldItem.Str("holdReason"));
        Assert.Single(detail.GetProperty("warnings").GetProperty("missingPayoutDetails").EnumerateArray());

        // Held, suspended and below-minimum participants are excluded and reported.
        var exclusions = detail.GetProperty("warnings").GetProperty("exclusions").EnumerateArray()
            .ToDictionary(e => e.GetProperty("user").Id(), e => e.Str("reason"));
        Assert.Equal("PayoutHold", exclusions[held.Id]);
        Assert.Equal("AccountInactive", exclusions[suspended.Id]);
        Assert.Equal("BelowMinimum", exclusions[small.Id]);
        Assert.Equal(3, detail.GetProperty("items").GetProperty("total").GetInt32());

        // Totals: payable items only (25 + 22), held item excluded.
        Assert.Equal(47m, detail.GetProperty("batch").Dec("totalAmount"));
        Assert.Equal(2, detail.GetProperty("batch").GetProperty("itemCount").GetInt32());

        // Finalize (another finance user): the held item's earnings are released for a later batch.
        var (_, finance2) = await Api.CreateClientAsync(Role.Finance);
        await finance2.FinalizeAsync(batchId);
        var released = await Api.EarningAsync(noProfileEarning.Id);
        Assert.Equal(EarningStatus.Approved, released.Status);
        Assert.Null(released.PayoutItemId);

        // Next period: the below-minimum participant crosses the threshold and is paid both earnings.
        var nextPeriodStart = Api.Now();
        var smallSecond = await Api.EarnAsync(small.Id, 6m);
        var next = PayoutPeriodCalculator.PeriodContaining(await Api.ScheduleAsync(), nextPeriodStart);
        Api.SetNow(next.CutoffUtc.AddMinutes(1));
        finance = await Api.LoginAsync(financeUser);
        var second = await finance.PrepareAsync();
        Assert.Equal(next.PeriodKey, second.GetProperty("batch").Str("periodKey"));
        var secondItems = await Api.ItemsAsync(second.GetProperty("batch").Id());
        var smallItem = secondItems.Single(i => i.UserId == small.Id);
        Assert.Equal(11m, smallItem.Amount);
        Assert.Equal(2, smallItem.EarningCount);
        Assert.Equal(smallItem.Id, (await Api.EarningAsync(smallFirst.Id)).PayoutItemId);
        Assert.Equal(smallItem.Id, (await Api.EarningAsync(smallSecond.Id)).PayoutItemId);
        // The participant still without payout details is held again (not silently dropped).
        Assert.Equal(PayoutItemStatus.Held, secondItems.Single(i => i.UserId == noProfile.Id).Status);
    }

    [Fact]
    public async Task Holding_and_unholding_items_in_a_draft_updates_totals_and_regenerate_reselects()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var a = await Api.ParticipantAsync();
        var b = await Api.ParticipantAsync();
        await Api.EarnAsync(a.Id, 20m);
        await Api.EarnAsync(b.Id, 30m);
        Api.SetNow(period.CutoffUtc.AddMinutes(1));
        var (financeUser, finance) = await Api.CreateClientAsync(Role.Finance);
        var batchId = (await finance.PrepareAsync()).GetProperty("batch").Id();
        var before = await finance.BatchAsync(batchId);
        var itemA = before.ItemFor(a.Id).Id("itemId");

        var held = await finance.PostJsonAsync($"/api/v1/finance/payout-batches/{batchId}/items/{itemA}/hold", new { reason = "Checking a dispute" });
        Assert.Equal("Held", held.Str("status"));
        var afterHold = await finance.BatchAsync(batchId);
        Assert.Equal(30m, afterHold.GetProperty("batch").Dec("totalAmount"));
        Assert.NotEqual(before.Str("concurrencyStamp"), afterHold.Str("concurrencyStamp"));

        // Finalizing with the stamp seen before the hold is rejected (stale review).
        var (_, finance2) = await Api.CreateClientAsync(Role.Finance);
        await (await finance2.PostAsJsonAsync($"/api/v1/finance/payout-batches/{batchId}/finalize",
            new { confirm = true, concurrencyStamp = before.Str("concurrencyStamp") })).ShouldFailAsync(409, "concurrency.conflict");

        var unheld = await finance.PostJsonAsync($"/api/v1/finance/payout-batches/{batchId}/items/{itemA}/unhold", new { note = "Resolved" });
        Assert.Equal("Pending", unheld.Str("status"));
        Assert.Equal(50m, (await finance.BatchAsync(batchId)).GetProperty("batch").Dec("totalAmount"));

        // A hold placed on the participant holds their draft item automatically; regenerate then excludes them.
        var hold = await finance.PostJsonAsync("/api/v1/finance/holds", new { userId = b.Id, reason = "KYC documents requested" });
        Assert.Single(hold.GetProperty("heldDraftItemIds").EnumerateArray());
        await (await finance.PostAsJsonAsync("/api/v1/finance/holds", new { userId = b.Id, reason = "Second hold" })).ShouldFailAsync(409, "payout.hold_exists");

        var regenerated = await finance.PostJsonAsync($"/api/v1/finance/payout-batches/{batchId}/regenerate", new { reason = "Participant placed on hold" });
        Assert.Equal(1, regenerated.GetProperty("itemCount").GetInt32());
        Assert.Equal(20m, regenerated.Dec("totalAmount"));
        var detail = await finance.BatchAsync(batchId);
        Assert.Equal("PayoutHold", detail.GetProperty("warnings").GetProperty("exclusions")[0].Str("reason"));
        Assert.Single(await Api.ItemsAsync(batchId));
        var bEarnings = await Api.WithDbAsync(db => db.Set<EarningEntry>().AsNoTracking().Where(e => e.UserId == b.Id).ToListAsync());
        Assert.All(bEarnings, e => { Assert.Equal(EarningStatus.Approved, e.Status); Assert.Null(e.PayoutItemId); });

        // Regenerating made financeUser the preparer: four-eyes applies to them.
        await (await finance.PostAsJsonAsync($"/api/v1/finance/payout-batches/{batchId}/finalize",
            new { confirm = true, concurrencyStamp = detail.Str("concurrencyStamp") })).ShouldFailAsync(403, "payout.self_finalize");
        Assert.Equal(financeUser.Id, detail.GetProperty("batch").GetProperty("preparedBy").Id());
    }

    [Fact]
    public async Task Prepare_without_eligible_earnings_or_for_an_open_period_is_rejected()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        await (await finance.PostAsJsonAsync("/api/v1/finance/payout-batches/prepare", new { })).ShouldFailAsync(409, "payout.no_eligible_earnings");
        await (await finance.PostAsJsonAsync("/api/v1/finance/payout-batches/prepare", new { periodKey = period.PeriodKey }))
            .ShouldFailAsync(400, "payout.period_not_completed");
        await (await finance.PostAsJsonAsync("/api/v1/finance/payout-batches/prepare", new { periodKey = "2026-01-05" }))
            .ShouldFailAsync(400, "payout.invalid_period");
        Assert.Empty(await Api.BatchesAsync());
    }

    private static EarningEntry RawEntry(Guid userId, decimal amount, EarningStatus status, DateTime now) => new()
    {
        UserId = userId, Type = EarningType.PostReward, Status = status, Amount = amount, Currency = "USD", ExchangeRate = 1m,
        SettlementAmount = amount, SettlementCurrency = "USD", IdempotencyKey = "raw:" + Guid.NewGuid().ToString("N"),
        Description = "Raw test entry", CreatedAt = now.AddDays(-30), AvailableAt = now.AddDays(-20), ApprovedAt = now.AddDays(-30),
        PaidAt = status == EarningStatus.Paid ? now.AddDays(-10) : null,
    };
}

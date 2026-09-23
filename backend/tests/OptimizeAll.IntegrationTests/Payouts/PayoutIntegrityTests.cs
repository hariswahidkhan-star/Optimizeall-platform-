using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Modules.Payouts;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Payouts;

/// <summary>Regression tests for the payout financial-integrity findings (C1, C2, H2, L1, M1, M2, M3).</summary>
public sealed class PayoutIntegrityTests : FreshDatabaseTest
{
    private const string Batches = "/api/v1/finance/payout-batches";

    /// <summary>Prepares a draft (by a dedicated preparer) for participants earning the given amounts.</summary>
    private async Task<(Guid BatchId, PayoutPeriod Period, List<TestUser> Users, List<EarningEntry> Earnings)> DraftAsync(params decimal[] amounts)
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var users = new List<TestUser>();
        var earnings = new List<EarningEntry>();
        foreach (var amount in amounts)
        {
            var u = await Api.ParticipantAsync();
            users.Add(u);
            earnings.Add(await Api.EarnAsync(u.Id, amount));
        }
        Api.SetNow(period.CutoffUtc.AddMinutes(1));
        var (_, preparer) = await Api.CreateClientAsync(Role.Finance);
        var batchId = (await preparer.PrepareAsync()).GetProperty("batch").Id();
        return (batchId, period, users, earnings);
    }

    private static Task<HttpResponseMessage> CancelAsync(HttpClient finance, Guid batchId) =>
        finance.PostAsJsonAsync($"{Batches}/{batchId}/cancel", new { reason = "Bank file rejected", confirm = true });

    private Task SetAttemptStatusAsync(Guid itemId, PaymentAttemptStatus status) =>
        Api.WithDbAsync(db => db.Set<PaymentAttempt>().Where(a => a.PayoutItemId == itemId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status)));

    // ---------------------------------------------------------------- C1

    [Fact]
    public async Task C1_a_hold_created_while_a_batch_is_being_prepared_holds_the_new_draft_item_and_the_user_is_not_paid()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var target = await Api.ParticipantAsync();
        var other = await Api.ParticipantAsync();
        var targetEarning = await Api.EarnAsync(target.Id, 20m);
        await Api.EarnAsync(other.Id, 30m);
        Api.SetNow(period.CutoffUtc.AddMinutes(1));
        var (_, preparer) = await Api.CreateClientAsync(Role.Finance);
        var (_, holder) = await Api.CreateClientAsync(Role.Finance);
        var (_, finalizer) = await Api.CreateClientAsync(Role.Finance);

        // Deterministic interleaving: preparation has already read "no active hold" for the target when the hold is placed.
        var hooks = Api.Services.GetRequiredService<PayoutTestHooks>();
        Task<HttpResponseMessage>? holdRequest = null;
        var holdCompletedDuringPrepare = false;
        hooks.AfterPrepareSelection = async _ =>
        {
            hooks.AfterPrepareSelection = null;
            holdRequest = holder.PostAsJsonAsync("/api/v1/finance/holds", new { userId = target.Id, reason = "Fraud review opened" });
            holdCompletedDuringPrepare = await Task.WhenAny(holdRequest, Task.Delay(TimeSpan.FromSeconds(3))) == holdRequest;
        };
        var batchId = (await preparer.PrepareAsync()).GetProperty("batch").Id();
        Assert.NotNull(holdRequest);
        var hold = await (await holdRequest!).ReadJsonAsync();

        // The hold waited for the preparation (same named lock), then saw and held the new draft item.
        Assert.False(holdCompletedDuringPrepare);
        var targetItem = (await Api.ItemsAsync(batchId)).Single(i => i.UserId == target.Id);
        Assert.Equal(targetItem.Id, Assert.Single(hold.GetProperty("heldDraftItemIds").EnumerateArray()).GetGuid());
        Assert.Equal(PayoutItemStatus.Held, targetItem.Status);

        await finalizer.FinalizeAsync(batchId);
        var items = await Api.ItemsAsync(batchId);
        Assert.Equal(PayoutItemStatus.Held, items.Single(i => i.UserId == target.Id).Status);
        Assert.Equal(PayoutItemStatus.AwaitingPayment, items.Single(i => i.UserId == other.Id).Status);
        var released = await Api.EarningAsync(targetEarning.Id);
        Assert.Equal(EarningStatus.Approved, released.Status);
        Assert.Null(released.PayoutItemId);
        Assert.False(await Api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == target.Id && n.Type == NotificationTypes.PayoutScheduled)));
    }

    [Fact]
    public async Task C1_finalize_holds_pending_items_of_participants_put_on_hold_or_suspended_after_preparation()
    {
        var (batchId, _, users, earnings) = await DraftAsync(20m, 30m, 40m);
        var (onHold, suspended, payable) = (users[0], users[1], users[2]);

        // A hold written behind the API's back (e.g. a hold that raced the preparation) and a suspension.
        await Api.WithDbAsync(async db =>
        {
            db.Add(new PayoutHold { UserId = onHold.Id, Reason = "Chargeback investigation", CreatedAt = Api.Now(), CreatedByUserId = payable.Id });
            await db.SaveChangesAsync();
        });
        await Api.SetUserStatusAsync(suspended.Id, UserStatus.Suspended);
        Assert.All(await Api.ItemsAsync(batchId), i => Assert.Equal(PayoutItemStatus.Pending, i.Status));

        var (_, finalizer) = await Api.CreateClientAsync(Role.Finance);
        var result = await finalizer.FinalizeAsync(batchId);
        Assert.Single(result.GetProperty("dispatch").EnumerateArray());
        Assert.Equal(1, result.GetProperty("batch").GetProperty("itemCount").GetInt32());
        Assert.Equal(40m, result.GetProperty("batch").Dec("totalAmount"));

        var items = await Api.ItemsAsync(batchId);
        var heldItem = items.Single(i => i.UserId == onHold.Id);
        Assert.Equal(PayoutItemStatus.Held, heldItem.Status);
        Assert.Equal("Payout hold: Chargeback investigation", heldItem.HoldReason);
        var suspendedItem = items.Single(i => i.UserId == suspended.Id);
        Assert.Equal(PayoutItemStatus.Held, suspendedItem.Status);
        Assert.Equal("Account not active (Suspended)", suspendedItem.HoldReason);
        Assert.Equal(PayoutItemStatus.AwaitingPayment, items.Single(i => i.UserId == payable.Id).Status);
        foreach (var e in earnings.Take(2))
        {
            var stored = await Api.EarningAsync(e.Id);
            Assert.Equal(EarningStatus.Approved, stored.Status);
            Assert.Null(stored.PayoutItemId);
        }
        Assert.Equal(2, await Api.WithDbAsync(db => db.Set<AuditLog>().CountAsync(a => a.Action == "payout.item_held")));
    }

    // ---------------------------------------------------------------- C2

    [Fact]
    public async Task C2_finalize_is_refused_to_the_beneficiary_or_the_creator_or_approver_of_an_earning_in_the_batch()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var beneficiary = await Api.CreateUserAsync(new[] { Role.Finance });
        var creator = await Api.CreateUserAsync(new[] { Role.Finance });
        var (approver, approverClient) = await Api.CreateClientAsync(Role.Finance);
        var (_, bonusAuthor) = await Api.CreateClientAsync(Role.Finance);

        // A finance user who is also paid in the batch.
        await Api.AddPayoutProfileAsync(beneficiary.Id, "PK36SCBL0000001123456799");
        await Api.EarnAsync(beneficiary.Id, 25m);
        // An earning recorded (and thereby approved) by a finance user.
        var p1 = await Api.ParticipantAsync();
        await Api.EarnAsync(p1.Id, 20m, createdBy: creator.Id);
        // A credit approved through the pending-earnings queue.
        var p2 = await Api.ParticipantAsync();
        var credit = await (await bonusAuthor.PostAsJsonAsync("/api/v1/finance/adjustments", new
        {
            requestId = Guid.NewGuid(), userId = p2.Id, amount = 30m, currency = "USD", reason = "Goodwill credit after dispute", confirm = true,
        })).ReadJsonAsync();
        var creditRow = credit.GetProperty("earning");
        Assert.Equal("PendingApproval", creditRow.Str("status"));
        await approverClient.PostJsonAsync($"/api/v1/finance/pending-earnings/{creditRow.Id()}/approve",
            new { concurrencyStamp = creditRow.Str("concurrencyStamp") });

        Api.SetNow(period.CutoffUtc.AddMinutes(1));
        var (_, preparer) = await Api.CreateClientAsync(Role.Finance);
        var batchId = (await preparer.PrepareAsync()).GetProperty("batch").Id();
        Assert.Equal(3, (await Api.ItemsAsync(batchId)).Count);
        var stamp = (await preparer.BatchAsync(batchId)).Str("concurrencyStamp");
        object Body() => new { confirm = true, reason = "Reviewed", concurrencyStamp = stamp };

        foreach (var conflicted in new[] { beneficiary, creator, approver })
        {
            var client = await Api.LoginAsync(conflicted); // fresh token after the clock moved
            await (await client.PostAsJsonAsync($"{Batches}/{batchId}/finalize", Body())).ShouldFailAsync(403, "payout.conflict_of_interest");
        }
        Assert.Equal(PayoutBatchStatus.Draft, (await Api.BatchesAsync()).Single().Status);

        var (_, independent) = await Api.CreateClientAsync(Role.Finance);
        (await independent.PostAsJsonAsync($"{Batches}/{batchId}/finalize", Body())).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task C2_payments_of_a_system_prepared_batch_must_be_recorded_by_someone_other_than_the_finalizer()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var user = await Api.ParticipantAsync();
        await Api.EarnAsync(user.Id, 25m);
        Api.SetNow(period.CutoffUtc.AddMinutes(1));
        await Api.RunJobAsync<PayoutPreparationJob>();
        var batch = Assert.Single(await Api.BatchesAsync());
        Assert.Null(batch.PreparedByUserId);

        var (_, finalizer) = await Api.CreateClientAsync(Role.Finance);
        var (_, recorder) = await Api.CreateClientAsync(Role.Finance);
        await finalizer.FinalizeAsync(batch.Id);
        var item = Assert.Single(await Api.ItemsAsync(batch.Id));
        var url = $"{Batches}/{batch.Id}/items/{item.Id}/record-payment";

        await (await finalizer.PostAsJsonAsync(url, new { paymentReference = "WIRE-SELF-1", paidAt = Api.Now() }))
            .ShouldFailAsync(403, "payout.self_record");
        var bulk = await finalizer.PostJsonAsync($"{Batches}/{batch.Id}/record-payments", new object[]
        {
            new { itemId = item.Id, paymentReference = "WIRE-SELF-2", paidAt = Api.Now() },
        });
        Assert.Equal("invalid", bulk[0].Str("status"));
        Assert.Equal(PayoutItemStatus.AwaitingPayment, Assert.Single(await Api.ItemsAsync(batch.Id)).Status);

        var paid = await recorder.PostJsonAsync(url, new { paymentReference = "WIRE-OTHER-1", paidAt = Api.Now() });
        Assert.Equal("Paid", paid.GetProperty("item").Str("status"));
    }

    // ---------------------------------------------------------------- H2 + L1

    [Fact]
    public async Task H2_L1_a_finalized_batch_cannot_be_cancelled_once_money_may_be_in_flight()
    {
        var (batchId, _, _, _) = await DraftAsync(20m, 30m);
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        await finance.FinalizeAsync(batchId);
        var items = await Api.ItemsAsync(batchId);
        var first = items[0];

        // L1: a provider submission without an outcome blocks cancel and a manual mark-failed.
        await SetAttemptStatusAsync(first.Id, PaymentAttemptStatus.Submitted);
        await (await CancelAsync(finance, batchId)).ShouldFailAsync(409, "payout.attempt_in_flight");
        await (await finance.PostAsJsonAsync($"{Batches}/{batchId}/items/{first.Id}/mark-failed", new { reason = "Bank rejected the transfer" }))
            .ShouldFailAsync(409, "payout.attempt_in_flight");

        // H2: a succeeded attempt means money moved.
        await SetAttemptStatusAsync(first.Id, PaymentAttemptStatus.Succeeded);
        await (await CancelAsync(finance, batchId)).ShouldFailAsync(409, "payout.instructions_exported");

        // H2: exporting payment instructions records a marker; after it the batch can never be cancelled.
        await SetAttemptStatusAsync(first.Id, PaymentAttemptStatus.RequiresManualAction);
        Assert.Equal(JsonValueKind.Null, (await finance.BatchAsync(batchId)).GetProperty("batch").GetProperty("instructionsExportedAt").ValueKind);
        (await finance.GetAsync($"{Batches}/{batchId}/payment-instructions.csv?confirm=true")).EnsureSuccessStatusCode();
        var exportedAt = (await finance.BatchAsync(batchId)).GetProperty("batch").GetProperty("instructionsExportedAt").GetDateTime();
        Assert.Equal(Api.Now(), exportedAt.ToUniversalTime(), TimeSpan.FromSeconds(1));
        var cancel = await CancelAsync(finance, batchId);
        await cancel.ShouldFailAsync(409, "payout.instructions_exported");
        Assert.Equal(PayoutBatchStatus.Finalized, (await Api.BatchesAsync()).Single().Status);

        // The way out is per item: mark it failed with a reason.
        var failed = await finance.PostJsonAsync($"{Batches}/{batchId}/items/{first.Id}/mark-failed", new { reason = "Transfer never left the bank" });
        Assert.Equal("Failed", failed.GetProperty("item").Str("status"));
    }

    // ---------------------------------------------------------------- M1

    [Fact]
    public async Task M1_payment_instructions_exclude_held_and_inactive_participants_and_record_payment_needs_an_override()
    {
        var (batchId, _, users, _) = await DraftAsync(20m, 30m, 40m);
        var (payable, held, inactive) = (users[0], users[1], users[2]);
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        await finance.FinalizeAsync(batchId);
        var hold = await finance.PostJsonAsync("/api/v1/finance/holds", new { userId = held.Id, reason = "Fraud review after finalize" });
        Assert.Single(hold.GetProperty("awaitingPaymentItemIds").EnumerateArray());
        await Api.SetUserStatusAsync(inactive.Id, UserStatus.Deactivated);

        var csv = await (await finance.GetAsync($"{Batches}/{batchId}/payment-instructions.csv?confirm=true")).Content.ReadAsStringAsync();
        var lines = csv.Trim('﻿').Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lines.Length);
        var payableLine = Assert.Single(lines, l => l.Contains(payable.Email));
        Assert.Contains("PK36SCBL0000001123456702", payableLine);
        Assert.DoesNotContain(PayoutBatchesController.ExcludedMarker, payableLine);
        foreach (var excluded in new[] { held, inactive })
        {
            var line = Assert.Single(lines, l => l.Contains(excluded.Email));
            Assert.Contains($",{PayoutBatchesController.ExcludedMarker},", line);
            Assert.Contains("EXCLUDED — do not pay", line);
            Assert.DoesNotContain("PK36SCBL0000001123456702", line);
        }
        Assert.Contains("Payout hold: Fraud review after finalize", Assert.Single(lines, l => l.Contains(held.Email)));
        var audit = await Api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking().SingleAsync(a => a.Action == "payout.payment_instructions_exported"));
        Assert.Equal(2, JsonDocument.Parse(audit.AfterJson!).RootElement.GetProperty("excluded").GetInt32());

        var heldItem = (await Api.ItemsAsync(batchId)).Single(i => i.UserId == held.Id);
        var url = $"{Batches}/{batchId}/items/{heldItem.Id}/record-payment";
        await (await finance.PostAsJsonAsync(url, new { paymentReference = "WIRE-HELD-1", paidAt = Api.Now() }))
            .ShouldFailAsync(409, "payout.user_on_hold");
        var paid = await finance.PostJsonAsync(url, new
        {
            paymentReference = "WIRE-HELD-1", paidAt = Api.Now(), overrideReason = "Transfer left the account before the hold was placed",
        });
        Assert.Equal("Paid", paid.GetProperty("item").Str("status"));
        var overrideAudit = await Api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking().SingleAsync(a => a.Action == "payout.payment_hold_overridden"));
        Assert.Equal(heldItem.Id.ToString(), overrideAudit.EntityId);
        Assert.Equal("Transfer left the account before the hold was placed", overrideAudit.Reason);
    }

    // ---------------------------------------------------------------- M2

    [Fact]
    public async Task M2_regenerating_a_draft_keeps_manual_item_holds()
    {
        var (batchId, _, users, _) = await DraftAsync(20m, 30m);
        var (heldUser, other) = (users[0], users[1]);
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        var detail = await finance.BatchAsync(batchId);
        await finance.PostJsonAsync($"{Batches}/{batchId}/items/{detail.ItemFor(heldUser.Id).Id("itemId")}/hold",
            new { reason = "Checking a dispute ticket" });

        var regenerated = await finance.PostJsonAsync($"{Batches}/{batchId}/regenerate", new { reason = "New earnings were approved" });
        Assert.Equal(1, regenerated.GetProperty("itemCount").GetInt32());
        Assert.Equal(30m, regenerated.Dec("totalAmount"));
        var items = await Api.ItemsAsync(batchId);
        var heldItem = items.Single(i => i.UserId == heldUser.Id);
        Assert.Equal(PayoutItemStatus.Held, heldItem.Status);
        Assert.Equal("Checking a dispute ticket", heldItem.HoldReason);
        Assert.Equal(PayoutItemStatus.Pending, items.Single(i => i.UserId == other.Id).Status);
    }

    // ---------------------------------------------------------------- M3

    [Fact]
    public async Task M3_a_held_item_keeps_a_finalized_and_completed_batch_balanced()
    {
        var (batchId, _, users, _) = await DraftAsync(20m, 30m);
        var (_, f2) = await Api.CreateClientAsync(Role.Finance);
        var detail = await f2.BatchAsync(batchId);
        await f2.PostJsonAsync($"{Batches}/{batchId}/items/{detail.ItemFor(users[0].Id).Id("itemId")}/hold", new { reason = "Checking a dispute" });
        var (_, f3) = await Api.CreateClientAsync(Role.Finance);
        await f3.FinalizeAsync(batchId);

        var report = await f3.GetJsonAsync($"{Batches}/{batchId}/reconciliation");
        Assert.True(report.GetProperty("isBalanced").GetBoolean(), report.ToString());
        Assert.Empty(report.GetProperty("discrepancies").EnumerateArray());
        Assert.Equal(30m, report.Dec("expected"));

        var payItem = (await Api.ItemsAsync(batchId)).Single(i => i.UserId == users[1].Id);
        await f3.PostJsonAsync($"{Batches}/{batchId}/items/{payItem.Id}/record-payment", new { paymentReference = "WIRE-BAL-1", paidAt = Api.Now() });
        var completed = await f3.GetJsonAsync($"{Batches}/{batchId}/reconciliation");
        Assert.Equal("Completed", completed.Str("status"));
        Assert.True(completed.GetProperty("isBalanced").GetBoolean(), completed.ToString());
    }

    [Fact]
    public async Task M3_a_double_payment_injected_into_the_database_is_detected()
    {
        var (batchId, period, users, earnings) = await DraftAsync(20m);
        var user = users[0];
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        await finance.FinalizeAsync(batchId);
        var item = Assert.Single(await Api.ItemsAsync(batchId));
        await finance.PostJsonAsync($"{Batches}/{batchId}/items/{item.Id}/record-payment", new { paymentReference = "WIRE-ORIG-1", paidAt = Api.Now() });
        Assert.True((await finance.GetJsonAsync($"{Batches}/{batchId}/reconciliation")).GetProperty("isBalanced").GetBoolean());
        var paidEarnings = await Api.WithDbAsync(db => db.Set<EarningEntry>().AsNoTracking().Where(e => e.PayoutItemId == item.Id).ToListAsync());
        Assert.Equal(earnings[0].Id, Assert.Single(paidEarnings).Id);

        // Inject a second, "paid" batch for a later period that paid the same earnings again (relinked to its item).
        var next = PayoutPeriodCalculator.PeriodContaining(await Api.ScheduleAsync(), period.CutoffUtc.AddDays(1));
        var injected = new PayoutBatch
        {
            Reference = "PB-INJECTED", IdempotencyKey = "injected:" + Guid.NewGuid().ToString("N"), PeriodKey = next.PeriodKey,
            PeriodStart = next.PeriodStartUtc, CutoffAt = next.CutoffUtc, ScheduledPaymentDate = next.PaymentDate, Currency = "USD",
            Status = PayoutBatchStatus.Completed, ItemCount = 1, TotalAmount = item.Amount,
        };
        var injectedItem = new PayoutItem
        {
            BatchId = injected.Id, UserId = user.Id, Amount = item.Amount, Currency = "USD", EarningCount = paidEarnings.Count,
            Status = PayoutItemStatus.Paid, PaymentReference = "WIRE-DUP-1", PaidAt = Api.Now(),
        };
        await Api.WithDbAsync(async db =>
        {
            db.Add(injected);
            db.Add(injectedItem);
            await db.SaveChangesAsync();
            var ids = paidEarnings.Select(e => e.Id).ToList();
            await db.Set<EarningEntry>().Where(e => ids.Contains(e.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.PayoutItemId, (Guid?)injectedItem.Id));
        });

        var dup = await finance.GetJsonAsync($"{Batches}/{injected.Id}/reconciliation");
        Assert.False(dup.GetProperty("isBalanced").GetBoolean(), dup.ToString());
        var dupTypes = dup.GetProperty("discrepancies").EnumerateArray()
            .Where(d => d.Str("severity") == "error").Select(d => d.Str("type")).ToList();
        Assert.Contains("duplicate_earning_payment", dupTypes);
        Assert.Contains("duplicate_payment_across_periods", dupTypes);

        var original = await finance.GetJsonAsync($"{Batches}/{batchId}/reconciliation");
        Assert.False(original.GetProperty("isBalanced").GetBoolean());
        var originalTypes = original.GetProperty("discrepancies").EnumerateArray().Select(d => d.Str("type")).ToList();
        Assert.Contains("duplicate_earning_payment", originalTypes);
        Assert.Contains("paid_earning_linked_elsewhere", originalTypes);
    }
}

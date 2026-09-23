using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Modules.Payouts;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Jobs;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Payouts;

public sealed class PreparationRetryTests : FreshDatabaseTest
{
    [Fact]
    public async Task Running_the_preparation_job_twice_creates_one_batch_prepared_by_the_system()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var user = await Api.ParticipantAsync();
        var earning = await Api.EarnAsync(user.Id, 15m);
        var financeWatcher = await Api.CreateUserAsync(new[] { Role.Finance });
        Api.SetNow(period.CutoffUtc.AddMinutes(1));

        var first = await Api.RunJobAsync<PayoutPreparationJob>();
        // The job-run log is keyed by the clock timestamp, so the retry runs a moment later (as the 15-minute loop would).
        Api.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await Api.RunJobAsync<PayoutPreparationJob>();
        Assert.Equal(JobRunStatus.Succeeded, first!.Status);
        Assert.Equal(JobRunStatus.Succeeded, second!.Status);
        Assert.Contains("already exists", second.Summary);

        var batch = Assert.Single(await Api.BatchesAsync());
        Assert.Null(batch.PreparedByUserId);
        Assert.Equal(period.PeriodKey, batch.PeriodKey);
        Assert.Equal(PayoutBatchService.IdempotencyKeyFor(period.PeriodKey, "USD"), batch.IdempotencyKey);
        Assert.Equal(Assert.Single(await Api.ItemsAsync(batch.Id)).Id, (await Api.EarningAsync(earning.Id)).PayoutItemId);

        // Audited as a system action and finance users were told.
        var audit = await Api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking().SingleAsync(a => a.Action == "payout.batch_prepared"));
        Assert.Equal("system", audit.ActorType);
        Assert.True(await Api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == financeWatcher.Id && n.Type == NotificationTypes.BatchPrepared)));

        // Calling prepare through the API for the same period is a no-op that returns the existing batch.
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        var response = await finance.PostAsJsonAsync("/api/v1/finance/payout-batches/prepare", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.ReadJsonAsync();
        Assert.False(body.GetProperty("created").GetBoolean());
        Assert.Equal(batch.Id, body.GetProperty("batch").Id());

        // A system-prepared batch can be finalized by any finance user.
        await finance.FinalizeAsync(batch.Id);
    }

    [Fact]
    public async Task The_job_does_nothing_when_auto_preparation_is_disabled()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        (await finance.PutAsJsonAsync("/api/v1/finance/payout-schedule", new
        {
            frequency = "Biweekly", anchorCutoffDate = "2026-01-04", cutoffLocalTime = "23:59:59", timeZone = "UTC",
            paymentDelayDays = 5, minimumPayoutAmount = 10, settlementCurrency = "USD", earningHoldDays = 3,
            autoPrepareBatches = false, effectiveFrom = Api.Now(), reason = "Finance prepares batches manually", confirm = true,
        })).EnsureSuccessStatusCode();
        var user = await Api.ParticipantAsync();
        await Api.EarnAsync(user.Id, 15m);
        Api.SetNow(period.CutoffUtc.AddMinutes(1));
        var run = await Api.RunJobAsync<PayoutPreparationJob>();
        Assert.Contains("disabled", run!.Summary);
        Assert.Empty(await Api.BatchesAsync());
    }

    [Fact]
    public async Task Concurrent_prepare_calls_create_exactly_one_batch_and_attach_each_earning_once()
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var users = new List<TestUser>();
        var earnings = new List<EarningEntry>();
        for (var i = 0; i < 4; i++)
        {
            var u = await Api.ParticipantAsync();
            users.Add(u);
            earnings.Add(await Api.EarnAsync(u.Id, 10m + i));
            earnings.Add(await Api.EarnAsync(u.Id, 5m));
        }
        Api.SetNow(period.CutoffUtc.AddMinutes(1));
        var clients = new List<HttpClient>();
        for (var i = 0; i < 3; i++) clients.Add((await Api.CreateClientAsync(Role.Finance)).Client);

        var responses = await Task.WhenAll(clients.Select(c => c.PostAsJsonAsync("/api/v1/finance/payout-batches/prepare", new { })));
        var bodies = await Task.WhenAll(responses.Select(r => r.ReadJsonAsync()));
        Assert.Single(bodies, b => b.GetProperty("created").GetBoolean());
        Assert.Equal(2, bodies.Count(b => !b.GetProperty("created").GetBoolean()));
        Assert.Single(bodies.Select(b => b.GetProperty("batch").Id()).Distinct());
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);

        var batch = Assert.Single(await Api.BatchesAsync());
        var items = await Api.ItemsAsync(batch.Id);
        Assert.Equal(4, items.Count);
        var stored = await Api.WithDbAsync(db => db.Set<EarningEntry>().AsNoTracking().ToListAsync());
        Assert.All(stored, e => Assert.Equal(EarningStatus.Scheduled, e.Status));
        Assert.Equal(8, stored.Count(e => e.PayoutItemId != null));
        foreach (var item in items)
            Assert.Equal(item.EarningCount, stored.Count(e => e.PayoutItemId == item.Id));
        Assert.Equal(items.Sum(i => i.Amount), batch.TotalAmount);
    }
}

public sealed class FinalizeAndPaymentTests : FreshDatabaseTest
{
    private async Task<(Guid BatchId, TestUser Preparer, HttpClient PreparerClient, List<TestUser> Users, List<EarningEntry> Earnings)> DraftAsync(int participants = 1)
    {
        var period = await Api.AlignToFreshPeriodAsync();
        var users = new List<TestUser>();
        var earnings = new List<EarningEntry>();
        for (var i = 0; i < participants; i++)
        {
            var u = await Api.ParticipantAsync();
            users.Add(u);
            earnings.Add(await Api.EarnAsync(u.Id, 20m));
            earnings.Add(await Api.EarnAsync(u.Id, 4.5m));
        }
        Api.SetNow(period.CutoffUtc.AddMinutes(1));
        var (preparer, client) = await Api.CreateClientAsync(Role.Finance);
        var batchId = (await client.PrepareAsync()).GetProperty("batch").Id();
        return (batchId, preparer, client, users, earnings);
    }

    [Fact]
    public async Task Finalize_requires_a_second_person_and_exactly_one_of_two_concurrent_finalizes_wins()
    {
        var (batchId, _, preparerClient, _, _) = await DraftAsync();
        var stamp = (await preparerClient.BatchAsync(batchId)).Str("concurrencyStamp");

        await (await preparerClient.PostAsJsonAsync($"/api/v1/finance/payout-batches/{batchId}/finalize",
            new { confirm = true, concurrencyStamp = stamp })).ShouldFailAsync(403, "payout.self_finalize");
        var (_, second) = await Api.CreateClientAsync(Role.Finance);
        var (_, third) = await Api.CreateClientAsync(Role.Finance);
        await (await second.PostAsJsonAsync($"/api/v1/finance/payout-batches/{batchId}/finalize",
            new { confirm = false, concurrencyStamp = stamp })).ShouldFailAsync(400, "request.confirm_required");
        var (_, participant) = await Api.CreateClientAsync(Role.Participant);
        await (await participant.PostAsJsonAsync($"/api/v1/finance/payout-batches/{batchId}/finalize",
            new { confirm = true, concurrencyStamp = stamp })).ShouldFailAsync(403);

        var results = await Task.WhenAll(
            second.PostAsJsonAsync($"/api/v1/finance/payout-batches/{batchId}/finalize", new { confirm = true, reason = "ok", concurrencyStamp = stamp }),
            third.PostAsJsonAsync($"/api/v1/finance/payout-batches/{batchId}/finalize", new { confirm = true, reason = "ok", concurrencyStamp = stamp }));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);

        var batch = Assert.Single(await Api.BatchesAsync());
        Assert.Equal(PayoutBatchStatus.Finalized, batch.Status);
        Assert.Equal(1, await Api.WithDbAsync(db => db.Set<AuditLog>().CountAsync(a => a.Action == "payout.batch_finalized")));
    }

    [Fact]
    public async Task Manual_provider_never_marks_paid_and_dispatch_is_idempotent()
    {
        var (batchId, _, _, users, earnings) = await DraftAsync(2);
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        var finalized = await finance.FinalizeAsync(batchId);
        Assert.Equal("Finalized", finalized.GetProperty("batch").Str("status"));
        var dispatch = finalized.GetProperty("dispatch").EnumerateArray().ToList();
        Assert.Equal(2, dispatch.Count);
        Assert.All(dispatch, d => Assert.Equal("RequiresManualAction", d.Str("status")));
        Assert.All(dispatch, d => Assert.Equal("Pay manually and record the payment reference", d.Str("message")));

        var items = await Api.ItemsAsync(batchId);
        Assert.All(items, i => Assert.Equal(PayoutItemStatus.AwaitingPayment, i.Status));
        Assert.All(items, i => Assert.Equal("manual", i.PaymentProvider));
        foreach (var e in earnings) Assert.Equal(EarningStatus.Scheduled, (await Api.EarningAsync(e.Id)).Status);

        var attempts = await Api.WithDbAsync(db => db.Set<PaymentAttempt>().AsNoTracking().ToListAsync());
        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, a => Assert.Equal(PaymentAttemptStatus.RequiresManualAction, a.Status));
        Assert.All(attempts, a => Assert.Equal($"payout-item:{a.PayoutItemId}:dispatch", a.IdempotencyKey));

        // Re-dispatching (retry) reuses the attempts: no duplicates, nothing paid.
        var again = await finance.PostJsonAsync($"/api/v1/finance/payout-batches/{batchId}/dispatch");
        Assert.All(again.EnumerateArray(), d => Assert.True(d.GetProperty("reused").GetBoolean()));
        Assert.Equal(2, await Api.WithDbAsync(db => db.Set<PaymentAttempt>().CountAsync()));
        Assert.All(await Api.ItemsAsync(batchId), i => Assert.Equal(PayoutItemStatus.AwaitingPayment, i.Status));

        // Participants were told the payout is scheduled (in-app + email outbox).
        foreach (var u in users)
        {
            var n = await Api.WithDbAsync(db => db.Set<Notification>().AsNoTracking().SingleAsync(x => x.UserId == u.Id && x.Type == NotificationTypes.PayoutScheduled));
            Assert.Contains("24.50 USD", n.Body);
            Assert.True(await Api.WithDbAsync(db => db.Set<NotificationDelivery>().AnyAsync(d => d.NotificationId == n.Id && d.Channel == NotificationChannel.Email)));
        }
    }

    [Fact]
    public async Task Concurrent_record_payment_succeeds_once_and_retries_are_rejected()
    {
        var (batchId, _, _, users, earnings) = await DraftAsync(2);
        var (_, f2) = await Api.CreateClientAsync(Role.Finance);
        var (_, f3) = await Api.CreateClientAsync(Role.Finance);
        await f2.FinalizeAsync(batchId);
        var items = await Api.ItemsAsync(batchId);
        var first = items.Single(i => i.UserId == users[0].Id);
        var url = $"/api/v1/finance/payout-batches/{batchId}/items/{first.Id}/record-payment";
        var paidAt = Api.Now().AddMinutes(-1);

        // Draft/unknown validations.
        await (await f2.PostAsJsonAsync(url, new { paymentReference = "X", paidAt })).ShouldFailAsync(400);
        await (await f2.PostAsJsonAsync(url, new { paymentReference = "BANK-REF-1", paidAt = Api.Now().AddDays(1) })).ShouldFailAsync(400, "payout.paid_at_in_future");

        var results = await Task.WhenAll(
            f2.PostAsJsonAsync(url, new { paymentReference = "BANK-REF-0001", paidAt }),
            f3.PostAsJsonAsync(url, new { paymentReference = "BANK-REF-0002", paidAt }));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        var loser = Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        await loser.ShouldFailAsync(409, "payout.already_recorded");
        var winner = await results.Single(r => r.StatusCode == HttpStatusCode.OK).ReadJsonAsync();
        Assert.Equal("Paid", winner.GetProperty("item").Str("status"));
        Assert.Equal("Finalized", winner.Str("batchStatus"));

        // A retried request is rejected too.
        await (await f2.PostAsJsonAsync(url, new { paymentReference = "BANK-REF-0001", paidAt })).ShouldFailAsync(409, "payout.already_recorded");

        var paidItem = (await Api.ItemsAsync(batchId)).Single(i => i.Id == first.Id);
        Assert.Equal(PayoutItemStatus.Paid, paidItem.Status);
        var userEarnings = earnings.Where(e => e.UserId == users[0].Id).ToList();
        foreach (var e in userEarnings)
        {
            var stored = await Api.EarningAsync(e.Id);
            Assert.Equal(EarningStatus.Paid, stored.Status);
            Assert.Equal(paidAt, stored.PaidAt!.Value, TimeSpan.FromMilliseconds(1));
        }
        Assert.Equal(1, await Api.WithDbAsync(db => db.Set<AuditLog>().CountAsync(a => a.Action == "payout.payment_recorded")));
        var attempt = await Api.WithDbAsync(db => db.Set<PaymentAttempt>().AsNoTracking().SingleAsync(a => a.PayoutItemId == first.Id));
        Assert.Equal(PaymentAttemptStatus.Succeeded, attempt.Status);
        Assert.Equal(paidItem.PaymentReference, attempt.ProviderReference);
        Assert.True(await Api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == users[0].Id && n.Type == NotificationTypes.PayoutPaid)));

        // Bulk entry: already-recorded, invalid and new lines are processed independently; batch completes.
        var secondItem = items.Single(i => i.UserId == users[1].Id);
        var bulk = await f3.PostJsonAsync($"/api/v1/finance/payout-batches/{batchId}/record-payments", new object[]
        {
            new { itemId = first.Id, paymentReference = "BANK-REF-0003", paidAt },
            new { itemId = Guid.NewGuid(), paymentReference = "BANK-REF-0004", paidAt },
            new { itemId = secondItem.Id, paymentReference = "BANK-REF-0005", paidAt },
        });
        var statuses = bulk.EnumerateArray().Select(r => r.Str("status")).ToList();
        Assert.Equal(new[] { "already_recorded", "invalid", "recorded" }, statuses);
        var batch = Assert.Single(await Api.BatchesAsync());
        Assert.Equal(PayoutBatchStatus.Completed, batch.Status);
        Assert.NotNull(batch.CompletedAt);
        var paidTotal = await Api.WithDbAsync(db => db.Set<EarningEntry>().Where(e => e.Status == EarningStatus.Paid).SumAsync(e => e.SettlementAmount));
        Assert.Equal(49m, paidTotal);
    }

    [Fact]
    public async Task Mark_failed_returns_earnings_to_the_next_batch()
    {
        var (batchId, _, _, users, earnings) = await DraftAsync();
        var (financeUser, finance) = await Api.CreateClientAsync(Role.Finance);
        await finance.FinalizeAsync(batchId);
        var item = Assert.Single(await Api.ItemsAsync(batchId));
        var failed = await finance.PostJsonAsync($"/api/v1/finance/payout-batches/{batchId}/items/{item.Id}/mark-failed",
            new { reason = "Bank rejected: account closed" });
        Assert.Equal("Failed", failed.GetProperty("item").Str("status"));
        Assert.Equal("Completed", failed.Str("batchStatus"));
        await (await finance.PostAsJsonAsync($"/api/v1/finance/payout-batches/{batchId}/items/{item.Id}/record-payment",
            new { paymentReference = "LATE-REF-1", paidAt = Api.Now() })).ShouldFailAsync(409);

        foreach (var e in earnings)
        {
            var stored = await Api.EarningAsync(e.Id);
            Assert.Equal(EarningStatus.Approved, stored.Status);
            Assert.Null(stored.PayoutItemId);
        }

        var next = PayoutPeriodCalculator.PeriodContaining(await Api.ScheduleAsync(), Api.Now());
        Api.SetNow(next.CutoffUtc.AddMinutes(1));
        finance = await Api.LoginAsync(financeUser);
        var second = await finance.PrepareAsync();
        var nextItem = Assert.Single(await Api.ItemsAsync(second.GetProperty("batch").Id()));
        Assert.Equal(users[0].Id, nextItem.UserId);
        Assert.Equal(24.5m, nextItem.Amount);
        Assert.Equal(2, nextItem.EarningCount);
    }

    [Fact]
    public async Task Reversing_a_paid_earning_creates_a_clawback_netted_in_the_next_batch_and_scheduled_earnings_cannot_be_reversed()
    {
        var (batchId, _, _, users, earnings) = await DraftAsync();
        var (financeUser, finance) = await Api.CreateClientAsync(Role.Finance);
        var user = users[0];

        // Scheduled (in a draft batch) → cannot be reversed.
        await (await finance.PostAsJsonAsync($"/api/v1/finance/earnings/{earnings[0].Id}/reverse",
            new { reason = "Post was deleted after approval", confirm = true })).ShouldFailAsync(409, "ledger.in_payout_batch");

        await finance.FinalizeAsync(batchId);
        var item = Assert.Single(await Api.ItemsAsync(batchId));
        await finance.PostJsonAsync($"/api/v1/finance/payout-batches/{batchId}/items/{item.Id}/record-payment",
            new { paymentReference = "WIRE-778899", paidAt = Api.Now() });

        await (await finance.PostAsJsonAsync($"/api/v1/finance/earnings/{earnings[0].Id}/reverse",
            new { reason = "short", confirm = true })).ShouldFailAsync(400);
        var reversal = await finance.PostJsonAsync($"/api/v1/finance/earnings/{earnings[0].Id}/reverse",
            new { reason = "Post was deleted after payment", confirm = true });
        var leg = reversal.GetProperty("reversal");
        Assert.Equal("Reversal", leg.Str("type"));
        Assert.Equal("Approved", leg.Str("status"));
        Assert.Equal(-20m, leg.Dec("settlementAmount"));
        Assert.Equal("Paid", reversal.GetProperty("original").Str("status"));
        await (await finance.PostAsJsonAsync($"/api/v1/finance/earnings/{earnings[0].Id}/reverse",
            new { reason = "Post was deleted after payment", confirm = true })).ShouldFailAsync(409, "ledger.already_reversed");

        // Next period: new 30 earning − 20 clawback = 10 (meets the 10 minimum).
        var fresh = await Api.EarnAsync(user.Id, 30m);
        var next = PayoutPeriodCalculator.PeriodContaining(await Api.ScheduleAsync(), Api.Now());
        Api.SetNow(next.CutoffUtc.AddMinutes(1));
        finance = await Api.LoginAsync(financeUser);
        var second = await finance.PrepareAsync();
        var nextItem = Assert.Single(await Api.ItemsAsync(second.GetProperty("batch").Id()));
        Assert.Equal(10m, nextItem.Amount);
        Assert.Equal(2, nextItem.EarningCount);
        Assert.Equal(nextItem.Id, (await Api.EarningAsync(leg.Id())).PayoutItemId);
        Assert.Equal(nextItem.Id, (await Api.EarningAsync(fresh.Id)).PayoutItemId);
    }

    [Fact]
    public async Task Cancelling_a_finalized_batch_without_payments_releases_everything_and_the_period_can_be_prepared_again()
    {
        var (batchId, _, preparerClient, _, earnings) = await DraftAsync(2);
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        await finance.FinalizeAsync(batchId);
        await (await finance.PostAsJsonAsync($"/api/v1/finance/payout-batches/{batchId}/cancel", new { reason = "Wrong period", confirm = false }))
            .ShouldFailAsync(400, "request.confirm_required");
        var cancelled = await finance.PostJsonAsync($"/api/v1/finance/payout-batches/{batchId}/cancel", new { reason = "Bank file rejected", confirm = true });
        Assert.Equal("Cancelled", cancelled.Str("status"));
        Assert.All(await Api.ItemsAsync(batchId), i => Assert.Equal(PayoutItemStatus.Cancelled, i.Status));
        foreach (var e in earnings)
        {
            var stored = await Api.EarningAsync(e.Id);
            Assert.Equal(EarningStatus.Approved, stored.Status);
            Assert.Null(stored.PayoutItemId);
        }
        Assert.All(await Api.WithDbAsync(db => db.Set<PaymentAttempt>().AsNoTracking().ToListAsync()),
            a => Assert.Equal(PaymentAttemptStatus.Failed, a.Status));

        // The automatic job does not resurrect a cancelled period; finance can prepare it again deliberately.
        var run = await Api.RunJobAsync<PayoutPreparationJob>();
        Assert.Contains("already exists", run!.Summary);
        var again = await preparerClient.PrepareAsync();
        Assert.True(again.GetProperty("created").GetBoolean());
        Assert.EndsWith("-R2", again.GetProperty("batch").Str("reference"));
        Assert.Equal(2, again.GetProperty("batch").GetProperty("itemCount").GetInt32());
    }

    [Fact]
    public async Task A_batch_with_recorded_payments_cannot_be_cancelled()
    {
        var (batchId, _, _, _, _) = await DraftAsync();
        var (_, finance) = await Api.CreateClientAsync(Role.Finance);
        await finance.FinalizeAsync(batchId);
        var item = Assert.Single(await Api.ItemsAsync(batchId));
        await finance.PostJsonAsync($"/api/v1/finance/payout-batches/{batchId}/items/{item.Id}/record-payment",
            new { paymentReference = "WIRE-1", paidAt = Api.Now() });
        await (await finance.PostAsJsonAsync($"/api/v1/finance/payout-batches/{batchId}/cancel", new { reason = "Too late now", confirm = true }))
            .ShouldFailAsync(409, "payout.cannot_cancel");
    }
}

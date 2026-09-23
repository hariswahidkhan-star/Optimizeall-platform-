using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Audit;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Payouts;

namespace OptimizeAll.IntegrationTests.Ledger;

/// <summary>Ledger tests share one database; every assertion is scoped to users created by the test.</summary>
public sealed class LedgerTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static object Adjustment(Guid requestId, Guid userId, decimal amount, string currency = "USD",
        string reason = "Goodwill credit after support dispute", bool confirm = true) =>
        new { requestId, userId, amount, currency, reason, confirm };

    [Fact]
    public async Task Adjustments_are_idempotent_by_request_id_and_validated()
    {
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        var user = await api.ParticipantAsync();
        var requestId = Guid.NewGuid();

        var created = await finance.PostAsJsonAsync("/api/v1/finance/adjustments", Adjustment(requestId, user.Id, 12.345m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var first = await created.ReadJsonAsync();
        Assert.True(first.GetProperty("created").GetBoolean());
        var earning = first.GetProperty("earning");
        Assert.Equal("Adjustment", earning.Str("type"));
        Assert.Equal("Approved", earning.Str("status"));
        Assert.Equal(12.35m, earning.Dec("originalAmount"));   // rounded to cents
        Assert.Equal("Goodwill credit after support dispute", earning.Str("reason"));

        // Double-click / retry with the same requestId returns the same adjustment.
        var replay = await finance.PostAsJsonAsync("/api/v1/finance/adjustments", Adjustment(requestId, user.Id, 12.345m));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayed = await replay.ReadJsonAsync();
        Assert.False(replayed.GetProperty("created").GetBoolean());
        Assert.Equal(earning.Id(), replayed.GetProperty("earning").Id());

        // Same requestId reused for a different adjustment → conflict.
        await (await finance.PostAsJsonAsync("/api/v1/finance/adjustments", Adjustment(requestId, user.Id, 99m)))
            .ShouldFailAsync(409, "ledger.request_id_reused");

        // Concurrent double submit of a new request → exactly one entry.
        var burstId = Guid.NewGuid();
        var burst = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            finance.PostAsJsonAsync("/api/v1/finance/adjustments", Adjustment(burstId, user.Id, -3m, reason: "Recover duplicated bonus payment"))));
        Assert.All(burst, r => Assert.True(r.IsSuccessStatusCode));
        Assert.Equal(1, await api.WithDbAsync(db => db.Set<EarningEntry>().CountAsync(e => e.IdempotencyKey == $"adjustment:{burstId:N}")));
        var debit = await api.WithDbAsync(db => db.Set<EarningEntry>().AsNoTracking().SingleAsync(e => e.IdempotencyKey == $"adjustment:{burstId:N}"));
        Assert.Equal(-3m, debit.SettlementAmount);
        Assert.Equal(debit.CreatedAt, debit.AvailableAt); // debits net against the very next payout

        // Validation.
        await (await finance.PostAsJsonAsync("/api/v1/finance/adjustments", Adjustment(Guid.NewGuid(), user.Id, 5m, reason: "too short")))
            .ShouldFailAsync(400);
        await (await finance.PostAsJsonAsync("/api/v1/finance/adjustments", Adjustment(Guid.NewGuid(), user.Id, 5m, confirm: false)))
            .ShouldFailAsync(400, "request.confirm_required");
        await (await finance.PostAsJsonAsync("/api/v1/finance/adjustments", Adjustment(Guid.NewGuid(), user.Id, 0.001m)))
            .ShouldFailAsync(400, "ledger.zero_amount");
        await (await finance.PostAsJsonAsync("/api/v1/finance/adjustments", Adjustment(Guid.NewGuid(), user.Id, 5m, currency: "XYZ")))
            .ShouldFailAsync(400, "ledger.currency_unsupported");
        await (await finance.PostAsJsonAsync("/api/v1/finance/adjustments", Adjustment(Guid.NewGuid(), Guid.NewGuid(), 5m)))
            .ShouldFailAsync(404);
        await (await finance.PostAsJsonAsync("/api/v1/finance/adjustments",
            new { userId = user.Id, amount = 5m, currency = "USD", reason = "Missing request identifier", confirm = true })).ShouldFailAsync(400);

        // Permission: reviewers and campaign managers cannot adjust.
        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        await (await reviewer.PostAsJsonAsync("/api/v1/finance/adjustments", Adjustment(Guid.NewGuid(), user.Id, 5m))).ShouldFailAsync(403);
        var (_, manager) = await api.CreateClientAsync(Role.CampaignManager);
        await (await manager.PostAsJsonAsync("/api/v1/finance/adjustments", Adjustment(Guid.NewGuid(), user.Id, 5m))).ShouldFailAsync(403);

        var audit = await api.WithDbAsync(db => db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Action == "ledger.adjustment_created" && a.EntityId == earning.Str("id")).ToListAsync());
        Assert.Single(audit);
        Assert.Equal("Goodwill credit after support dispute", audit[0].Reason);
    }

    [Fact]
    public async Task Pending_bonus_approval_enforces_four_eyes_concurrency_and_live_checks()
    {
        var (creatorUser, creator) = await api.CreateClientAsync(Role.Finance);
        var (_, approver) = await api.CreateClientAsync(Role.Finance);
        var participant = await api.ParticipantAsync();

        var bonus = await api.EarnAsync(participant.Id, 8m, type: EarningType.QualityBonus, requiresApproval: true, createdBy: creatorUser.Id);
        var list = await approver.GetJsonAsync("/api/v1/finance/pending-earnings?type=QualityBonus&pageSize=200");
        var row = list.GetProperty("items").EnumerateArray().Single(e => e.Id() == bonus.Id);
        Assert.False(row.GetProperty("awaitingLiveCheck").GetBoolean());
        var stamp = row.Str("concurrencyStamp");

        await (await creator.PostAsJsonAsync($"/api/v1/finance/pending-earnings/{bonus.Id}/approve", new { concurrencyStamp = stamp }))
            .ShouldFailAsync(403, "ledger.self_approval");
        await (await approver.PostAsJsonAsync($"/api/v1/finance/pending-earnings/{bonus.Id}/approve", new { concurrencyStamp = Guid.NewGuid() }))
            .ShouldFailAsync(409, "concurrency.conflict");
        var approved = await approver.PostJsonAsync($"/api/v1/finance/pending-earnings/{bonus.Id}/approve", new { concurrencyStamp = stamp });
        Assert.Equal("Approved", approved.Str("status"));
        Assert.NotNull(approved.GetProperty("availableAt").GetString());
        Assert.True(await api.WithDbAsync(db => db.Set<Notification>().AnyAsync(n => n.UserId == participant.Id && n.Type == NotificationTypes.EarningApproved)));
        await (await approver.PostAsJsonAsync($"/api/v1/finance/pending-earnings/{bonus.Id}/approve", new { concurrencyStamp = approved.Str("concurrencyStamp") }))
            .ShouldFailAsync(409, "ledger.not_pending");

        // Decline with a reason.
        var referral = await api.EarnAsync(participant.Id, 5m, type: EarningType.ReferralReward, requiresApproval: true);
        var declined = await approver.PostJsonAsync($"/api/v1/finance/pending-earnings/{referral.Id}/decline",
            new { reason = "Referred account is a duplicate", concurrencyStamp = referral.ConcurrencyStamp });
        Assert.Equal("Declined", declined.Str("status"));
        Assert.Equal("Referred account is a duplicate", declined.Str("reason"));

        // Post rewards awaiting their live check cannot be approved here.
        var submissionId = await api.SubmissionAsync(participant.Id, SubmissionStatus.Approved, 10m, liveCheck: LiveCheckStatus.Pending);
        var postReward = await api.EarnAsync(participant.Id, 10m, requiresApproval: true, submissionId: submissionId);
        var pending = await approver.GetJsonAsync("/api/v1/finance/pending-earnings?type=PostReward&pageSize=200");
        Assert.True(pending.GetProperty("items").EnumerateArray().Single(e => e.Id() == postReward.Id).GetProperty("awaitingLiveCheck").GetBoolean());
        await (await approver.PostAsJsonAsync($"/api/v1/finance/pending-earnings/{postReward.Id}/approve", new { concurrencyStamp = postReward.ConcurrencyStamp }))
            .ShouldFailAsync(409, "ledger.awaiting_live_check");

        var (_, reviewer) = await api.CreateClientAsync(Role.Reviewer);
        await (await reviewer.GetAsync("/api/v1/finance/pending-earnings")).ShouldFailAsync(403);
        // Campaign managers hold rewards.approve_bonus.
        var (_, manager) = await api.CreateClientAsync(Role.CampaignManager);
        (await manager.GetAsync("/api/v1/finance/pending-earnings")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Exchange_rates_apply_to_earnings_created_after_they_take_effect_and_never_change_existing_ones()
    {
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        var participant = await api.ParticipantAsync();
        // Use a pair no other test in this class touches.
        await (await finance.PostAsJsonAsync("/api/v1/finance/adjustments", Adjustment(Guid.NewGuid(), participant.Id, 10m, "AED")))
            .ShouldFailAsync(409, "fx.rate_missing");

        var rate = await finance.PostAsJsonAsync("/api/v1/finance/exchange-rates", new
        {
            baseCurrency = "aed", quoteCurrency = "USD", rate = 0.2725m, effectiveAt = api.Now().AddMinutes(-1),
            source = "Central bank", reason = "Monthly rate update from treasury", confirm = true,
        });
        Assert.Equal(HttpStatusCode.Created, rate.StatusCode);
        var rateBody = await rate.ReadJsonAsync();
        Assert.Equal("AED", rateBody.Str("baseCurrency"));

        var first = await api.EarnAsync(participant.Id, 100m, "AED");
        Assert.Equal(27.25m, first.SettlementAmount);
        Assert.Equal(0.2725m, first.ExchangeRate);
        Assert.Equal(rateBody.Id(), first.ExchangeRateId);

        api.Clock.Advance(TimeSpan.FromMinutes(1));
        await finance.PostJsonAsync("/api/v1/finance/exchange-rates", new
        {
            baseCurrency = "AED", quoteCurrency = "USD", rate = 0.3m, effectiveAt = api.Now(),
            source = "Central bank", reason = "Corrected rate from treasury", confirm = true,
        });
        var second = await api.EarnAsync(participant.Id, 100m, "AED");
        Assert.Equal(30m, second.SettlementAmount);
        var reread = await api.EarningAsync(first.Id);
        Assert.Equal(27.25m, reread.SettlementAmount);
        Assert.Equal(0.2725m, reread.ExchangeRate);

        // Inverse lookup works (USD→AED uses 1/rate) and validation rejects bad input.
        var list = await finance.GetJsonAsync("/api/v1/finance/exchange-rates?base=AED&quote=USD");
        Assert.Equal(2, list.GetProperty("total").GetInt32());
        foreach (var bad in new object[]
        {
            new { baseCurrency = "AED", quoteCurrency = "USD", rate = 0m, effectiveAt = api.Now(), source = "x1", reason = "Invalid zero rate here", confirm = true },
            new { baseCurrency = "AED", quoteCurrency = "USD", rate = 2_000_000m, effectiveAt = api.Now(), source = "x1", reason = "Invalid huge rate here", confirm = true },
            new { baseCurrency = "AED", quoteCurrency = "USD", rate = 0.3m, effectiveAt = api.Now().AddDays(-2), source = "x1", reason = "Backdated rate is refused", confirm = true },
            new { baseCurrency = "AED", quoteCurrency = "AED", rate = 1m, effectiveAt = api.Now(), source = "x1", reason = "Same currency is refused", confirm = true },
        })
        {
            await (await finance.PostAsJsonAsync("/api/v1/finance/exchange-rates", bad)).ShouldFailAsync(400);
        }
        var (_, participantClient) = await api.CreateClientAsync(Role.Participant);
        await (await participantClient.GetAsync("/api/v1/finance/exchange-rates")).ShouldFailAsync(403);
        Assert.True(await api.WithDbAsync(db => db.Set<AuditLog>().AnyAsync(a => a.Action == "fx.rate_created" && a.EntityId == rateBody.Str("id"))));
    }

    [Fact]
    public async Task Finance_ledger_lists_filters_and_exports()
    {
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        var participant = await api.ParticipantAsync();
        var a = await api.EarnAsync(participant.Id, 11m);
        await api.EarnAsync(participant.Id, 12m, type: EarningType.QualityBonus, requiresApproval: true);

        var ledger = await finance.GetJsonAsync($"/api/v1/finance/ledger?userId={participant.Id}&pageSize=50");
        Assert.Equal(2, ledger.GetProperty("total").GetInt32());
        var row = ledger.GetProperty("items").EnumerateArray().Single(r => r.Id() == a.Id);
        Assert.Equal(participant.Email, row.GetProperty("user").Str("email"));

        var filtered = await finance.GetJsonAsync($"/api/v1/finance/ledger?userId={participant.Id}&status=PendingApproval");
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        var searched = await finance.GetJsonAsync($"/api/v1/finance/ledger?search={Uri.EscapeDataString(participant.Email)}");
        Assert.Equal(2, searched.GetProperty("total").GetInt32());

        var csv = await finance.GetAsync($"/api/v1/finance/ledger/export.csv?userId={participant.Id}");
        csv.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        var text = await csv.Content.ReadAsStringAsync();
        var lines = text.Trim('﻿').Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Contains(a.Id.ToString(), text);

        var balance = await finance.GetJsonAsync($"/api/v1/finance/users/{participant.Id}/balance");
        Assert.Equal(11m, balance.Dec("approved"));
        Assert.Equal(12m, balance.Dec("pending"));
    }

    public static IEnumerable<object[]> FinanceEndpoints() => new[]
    {
        new object[] { "GET", "/api/v1/finance/ledger" },
        new object[] { "GET", "/api/v1/finance/ledger/export.csv" },
        new object[] { "GET", $"/api/v1/finance/users/{Guid.NewGuid()}/balance" },
        new object[] { "POST", "/api/v1/finance/adjustments" },
        new object[] { "POST", $"/api/v1/finance/earnings/{Guid.NewGuid()}/reverse" },
        new object[] { "GET", "/api/v1/finance/exchange-rates" },
        new object[] { "POST", "/api/v1/finance/exchange-rates" },
        new object[] { "GET", "/api/v1/finance/payout-schedule" },
        new object[] { "PUT", "/api/v1/finance/payout-schedule" },
        new object[] { "GET", "/api/v1/finance/holds" },
        new object[] { "POST", "/api/v1/finance/holds" },
        new object[] { "POST", "/api/v1/finance/payout-batches/prepare" },
        new object[] { "GET", "/api/v1/finance/payout-batches" },
        new object[] { "GET", $"/api/v1/finance/payout-batches/{Guid.NewGuid()}" },
        new object[] { "POST", $"/api/v1/finance/payout-batches/{Guid.NewGuid()}/finalize" },
        new object[] { "POST", $"/api/v1/finance/payout-batches/{Guid.NewGuid()}/items/{Guid.NewGuid()}/record-payment" },
        new object[] { "POST", $"/api/v1/finance/payout-batches/{Guid.NewGuid()}/record-payments" },
        new object[] { "GET", $"/api/v1/finance/payout-batches/{Guid.NewGuid()}/export.csv" },
        new object[] { "GET", $"/api/v1/finance/payout-batches/{Guid.NewGuid()}/payment-instructions.csv?confirm=true" },
        new object[] { "GET", $"/api/v1/finance/payout-batches/{Guid.NewGuid()}/reconciliation" },
    };

    [Theory]
    [MemberData(nameof(FinanceEndpoints))]
    public async Task Participants_and_reviewers_cannot_reach_finance_endpoints(string method, string url)
    {
        foreach (var role in new[] { Role.Participant, Role.Reviewer })
        {
            var (_, client) = await api.CreateClientAsync(role);
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (method != "GET") request.Content = JsonContent.Create(new { });
            var response = await client.SendAsync(request);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{role} {method} {url} → {(int)response.StatusCode}");
        }
        var anonymous = await api.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = method == "GET" ? null : JsonContent.Create(new { }),
        });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Participant_endpoints_are_scoped_to_the_caller()
    {
        var (me, client) = await api.CreateClientAsync(Role.Participant);
        var other = await api.ParticipantAsync();
        var mine = await api.EarnAsync(me.Id, 3m);
        await api.EarnAsync(other.Id, 4m);
        var list = await client.GetJsonAsync("/api/v1/me/earnings?pageSize=100");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal(mine.Id, list.GetProperty("items")[0].Id());
        Assert.Equal(0, (await client.GetJsonAsync("/api/v1/me/payouts")).GetProperty("total").GetInt32());
        await (await client.GetAsync($"/api/v1/me/payouts/{Guid.NewGuid()}")).ShouldFailAsync(404);
        var (_, finance) = await api.CreateClientAsync(Role.Finance);
        await (await finance.GetAsync("/api/v1/me/earnings/summary")).ShouldFailAsync(403);
    }
}

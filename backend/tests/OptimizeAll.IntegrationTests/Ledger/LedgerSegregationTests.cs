using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Payouts;

namespace OptimizeAll.IntegrationTests.Ledger;

/// <summary>Regression tests for C2 (credits need a second person), H1 (no self-approval / self-adjustment) and M4 (FX).</summary>
public sealed class LedgerSegregationTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static object Adjustment(Guid userId, decimal amount, string reason = "Goodwill credit after support dispute") =>
        new { requestId = Guid.NewGuid(), userId, amount, currency = "USD", reason, confirm = true };

    [Fact]
    public async Task C2_positive_adjustments_wait_for_approval_by_another_user_and_debits_apply_immediately()
    {
        var (_, author) = await api.CreateClientAsync(Role.Finance);
        var (_, approver) = await api.CreateClientAsync(Role.Finance);
        var participant = await api.ParticipantAsync();

        var created = await author.PostAsJsonAsync("/api/v1/finance/adjustments", Adjustment(participant.Id, 15m));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var credit = (await created.ReadJsonAsync()).GetProperty("earning");
        Assert.Equal("PendingApproval", credit.Str("status"));
        var stored = await api.EarningAsync(credit.Id());
        Assert.Null(stored.AvailableAt);
        Assert.Null(stored.ApprovedByUserId);
        var balance = await author.GetJsonAsync($"/api/v1/finance/users/{participant.Id}/balance");
        Assert.Equal(0m, balance.Dec("approved"));
        Assert.Equal(15m, balance.Dec("pending"));

        // It is in the pending-earnings queue; its author cannot approve it, a second finance user can.
        var queue = await approver.GetJsonAsync("/api/v1/finance/pending-earnings?type=Adjustment&pageSize=200");
        var row = queue.GetProperty("items").EnumerateArray().Single(e => e.Id() == credit.Id());
        await (await author.PostAsJsonAsync($"/api/v1/finance/pending-earnings/{credit.Id()}/approve",
            new { concurrencyStamp = row.Str("concurrencyStamp") })).ShouldFailAsync(403, "ledger.self_approval");
        var approved = await approver.PostJsonAsync($"/api/v1/finance/pending-earnings/{credit.Id()}/approve",
            new { concurrencyStamp = row.Str("concurrencyStamp") });
        Assert.Equal("Approved", approved.Str("status"));
        Assert.NotNull(approved.GetProperty("availableAt").GetString());

        // Debits (money owed back) are not a way to create money: immediate, available at once.
        var debit = (await author.PostJsonAsync("/api/v1/finance/adjustments",
            Adjustment(participant.Id, -4m, "Recover duplicated bonus payment"))).GetProperty("earning");
        Assert.Equal("Approved", debit.Str("status"));
        var storedDebit = await api.EarningAsync(debit.Id());
        Assert.Equal(storedDebit.CreatedAt, storedDebit.AvailableAt);
    }

    [Fact]
    public async Task H1_finance_cannot_approve_decline_or_adjust_their_own_earnings()
    {
        var (me, myClient) = await api.CreateClientAsync(Role.Finance);
        var colleague = await api.CreateUserAsync(new[] { Role.Finance });

        // Earnings credited to me, created by a colleague (so the creator rule alone does not stop me).
        var bonus = await api.EarnAsync(me.Id, 8m, type: EarningType.QualityBonus, requiresApproval: true, createdBy: colleague.Id);
        var referral = await api.EarnAsync(me.Id, 5m, type: EarningType.ReferralReward, requiresApproval: true, createdBy: colleague.Id);

        await (await myClient.PostAsJsonAsync($"/api/v1/finance/pending-earnings/{bonus.Id}/approve",
            new { concurrencyStamp = bonus.ConcurrencyStamp })).ShouldFailAsync(403, "ledger.self_approval");
        await (await myClient.PostAsJsonAsync($"/api/v1/finance/pending-earnings/{referral.Id}/decline",
            new { reason = "Declining my own reward", concurrencyStamp = referral.ConcurrencyStamp })).ShouldFailAsync(403, "ledger.self_approval");
        Assert.Equal(EarningStatus.PendingApproval, (await api.EarningAsync(bonus.Id)).Status);
        Assert.Equal(EarningStatus.PendingApproval, (await api.EarningAsync(referral.Id)).Status);

        await (await myClient.PostAsJsonAsync("/api/v1/finance/adjustments", Adjustment(me.Id, 50m)))
            .ShouldFailAsync(403, "ledger.self_adjustment");
        await (await myClient.PostAsJsonAsync("/api/v1/finance/adjustments", Adjustment(me.Id, -5m, "Debit on my own account")))
            .ShouldFailAsync(403, "ledger.self_adjustment");
        Assert.False(await api.WithDbAsync(db => db.Set<EarningEntry>().AnyAsync(e => e.UserId == me.Id && e.Type == EarningType.Adjustment)));

        // A different finance user can decide them.
        var (_, other) = await api.CreateClientAsync(Role.Finance);
        var ok = await other.PostJsonAsync($"/api/v1/finance/pending-earnings/{bonus.Id}/approve", new { concurrencyStamp = bonus.ConcurrencyStamp });
        Assert.Equal("Approved", ok.Str("status"));
    }

    [Fact]
    public async Task M4_the_later_of_the_latest_direct_and_latest_inverse_rate_is_used()
    {
        var now = api.Now();
        var ids = new Dictionary<string, Guid>();
        async Task AddRateAsync(string key, string from, string to, decimal rate, DateTime effectiveAt)
        {
            var row = new ExchangeRate
            {
                BaseCurrency = from, QuoteCurrency = to, Rate = rate, EffectiveAt = effectiveAt, Source = "test", CreatedAt = now,
            };
            ids[key] = row.Id;
            await api.WithDbAsync(async db => { db.Add(row); await db.SaveChangesAsync(); });
        }
        async Task<ResolvedRate> RateAsync(string from, string to, DateTime at)
        {
            using var scope = api.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IExchangeRateProvider>().GetRateAsync(from, to, at);
        }

        // Older direct quote, newer inverse quote: the inverse (1 / 4.0) wins.
        await AddRateAsync("oldDirect", "SAR", "USD", 0.27m, now.AddHours(-10));
        await AddRateAsync("newInverse", "USD", "SAR", 4.0m, now.AddHours(-1));
        var rate = await RateAsync("SAR", "USD", now);
        Assert.Equal(0.25m, rate.Rate);
        Assert.Equal(ids["newInverse"], rate.ExchangeRateId);
        // …and in the other direction the same newest row is used as a direct quote.
        Assert.Equal(4.0m, (await RateAsync("USD", "SAR", now)).Rate);

        // At a time before the inverse took effect, the direct quote is the latest.
        var earlier = await RateAsync("SAR", "USD", now.AddHours(-2));
        Assert.Equal(0.27m, earlier.Rate);
        Assert.Equal(ids["oldDirect"], earlier.ExchangeRateId);

        // A newer direct quote wins again; on an exact tie the direct quote is preferred.
        await AddRateAsync("newDirect", "SAR", "USD", 0.26m, now.AddMinutes(-30));
        Assert.Equal(0.26m, (await RateAsync("SAR", "USD", now)).Rate);
        await AddRateAsync("tieInverse", "USD", "SAR", 3.0m, now.AddMinutes(-30));
        Assert.Equal(ids["newDirect"], (await RateAsync("SAR", "USD", now)).ExchangeRateId);

        // Earnings use it: 100 SAR at the newest rate.
        var participant = await api.ParticipantAsync();
        await AddRateAsync("newestInverse", "USD", "SAR", 5.0m, now.AddMinutes(-1));
        var earning = await api.EarnAsync(participant.Id, 100m, "SAR");
        Assert.Equal(20m, earning.SettlementAmount);
        Assert.Equal(ids["newestInverse"], earning.ExchangeRateId);
    }
}

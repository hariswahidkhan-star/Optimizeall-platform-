using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Ledger;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Payouts;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.Domain.Social;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.Infrastructure.Persistence;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Payouts;

/// <summary>A fresh database per test: payout batches are global per period, so tests must not share state.</summary>
public abstract class FreshDatabaseTest : IAsyncLifetime
{
    protected ApiFactory Api { get; } = new();

    public Task InitializeAsync() => Api.InitializeAsync();

    public Task DisposeAsync() => Api.DisposeAsync();
}

/// <summary>Arrange/act helpers for ledger and payout tests.</summary>
public static class FinanceKit
{
    public const string DestinationPurpose = "OptimizeAll.PayoutProfile.Destination.v1";

    public static DateTime Now(this ApiFactory api) => api.Clock.GetUtcNow().UtcDateTime;

    public static void SetNow(this ApiFactory api, DateTime utc) => api.Clock.SetUtcNow(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)));

    public static async Task<PayoutSchedule> ScheduleAsync(this ApiFactory api)
    {
        using var scope = api.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPayoutScheduleProvider>().GetActiveAsync(api.Now());
    }

    /// <summary>Moves the clock to one minute after the current period's cutoff and returns the new (fresh) period.</summary>
    public static async Task<PayoutPeriod> AlignToFreshPeriodAsync(this ApiFactory api)
    {
        var schedule = await api.ScheduleAsync();
        var current = PayoutPeriodCalculator.PeriodContaining(schedule, api.Now());
        api.SetNow(current.CutoffUtc.AddMinutes(1));
        return PayoutPeriodCalculator.PeriodContaining(schedule, api.Now());
    }

    public static async Task<TestUser> ParticipantAsync(this ApiFactory api, bool withProfile = true, string destination = "PK36SCBL0000001123456702")
    {
        var user = await api.CreateUserAsync();
        if (withProfile) await api.AddPayoutProfileAsync(user.Id, destination);
        return user;
    }

    public static async Task AddPayoutProfileAsync(this ApiFactory api, Guid userId, string destination)
    {
        using var scope = api.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(DestinationPurpose);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Set<PayoutProfile>().Add(new PayoutProfile
        {
            UserId = userId,
            Method = PayoutMethod.BankTransfer,
            AccountHolderName = "Test Holder",
            MaskedDestination = "••••" + destination[^4..],
            EncryptedDestination = protector.Protect(destination),
            PreferredCurrency = "USD",
            CountryCode = "PK",
        });
        await db.SaveChangesAsync();
    }

    public static async Task<EarningEntry> EarnAsync(this ApiFactory api, Guid userId, decimal amount, string currency = "USD",
        EarningType type = EarningType.PostReward, bool requiresApproval = false, Guid? createdBy = null, Guid? submissionId = null,
        string? reason = null)
    {
        using var scope = api.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<ILedgerWriter>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await writer.RecordAsync(new NewEarning(userId, type, amount, currency, $"test:{Guid.NewGuid():N}",
            $"Test {type}", requiresApproval, SubmissionId: submissionId, Reason: reason, CreatedByUserId: createdBy));
        await db.SaveChangesAsync();
        return entry;
    }

    public static async Task<EarningEntry> ReverseAsync(this ApiFactory api, Guid earningId, string reason = "Post removed by participant")
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.Set<EarningEntry>().FirstAsync(e => e.Id == earningId);
        var reversal = await scope.ServiceProvider.GetRequiredService<ILedgerWriter>().ReverseAsync(entry, reason, null);
        await db.SaveChangesAsync();
        return reversal;
    }

    public static Task<EarningEntry> EarningAsync(this ApiFactory api, Guid id) =>
        api.WithDbAsync(db => db.Set<EarningEntry>().AsNoTracking().FirstAsync(e => e.Id == id));

    public static Task<List<PayoutBatch>> BatchesAsync(this ApiFactory api) =>
        api.WithDbAsync(db => db.Set<PayoutBatch>().AsNoTracking().OrderBy(b => b.CreatedAt).ToListAsync());

    public static Task<List<PayoutItem>> ItemsAsync(this ApiFactory api, Guid batchId) =>
        api.WithDbAsync(db => db.Set<PayoutItem>().AsNoTracking().Where(i => i.BatchId == batchId).ToListAsync());

    public static Task SetUserStatusAsync(this ApiFactory api, Guid userId, UserStatus status) =>
        api.WithDbAsync(db => db.Set<User>().Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, status)));

    /// <summary>Creates a submission (with the campaign, rule set and social account it needs).</summary>
    public static async Task<Guid> SubmissionAsync(this ApiFactory api, Guid userId, SubmissionStatus status, decimal estimated,
        string currency = "USD", LiveCheckStatus liveCheck = LiveCheckStatus.NotRequired, int riskScore = 0)
    {
        return await api.WithDbAsync(async db =>
        {
            var now = api.Now();
            var campaign = new Campaign
            {
                Slug = "c-" + Guid.NewGuid().ToString("N"), Title = "Test campaign", Summary = "s", Description = "d",
                PostingInstructions = "p", StartsAt = now.AddDays(-10), EndsAt = now.AddDays(10), SubmissionDeadline = now.AddDays(12),
                CreatedByUserId = userId,
            };
            var ruleSet = new RewardRuleSet { CampaignId = campaign.Id, Version = 1, Currency = currency, EffectiveFrom = now.AddDays(-10), CreatedAt = now, CreatedByUserId = userId };
            var handle = "h" + Guid.NewGuid().ToString("N")[..12];
            var account = new SocialAccount
            {
                UserId = userId, Platform = SocialPlatform.Instagram, Handle = handle, NormalizedHandle = handle,
                ProfileUrl = "https://instagram.com/" + handle, AccountCreatedAt = now.AddYears(-2), FollowerCount = 500,
            };
            var url = "https://instagram.com/p/" + Guid.NewGuid().ToString("N");
            var submission = new Submission
            {
                CampaignId = campaign.Id, UserId = userId, SocialAccountId = account.Id, Platform = SocialPlatform.Instagram,
                PostUrl = url, NormalizedPostUrl = url, PostedAt = now.AddHours(-2), Status = status, SubmittedAt = now.AddHours(-1),
                RewardRuleSetId = ruleSet.Id, RewardRuleSetVersion = 1, EstimatedRewardAmount = estimated, RewardCurrency = currency,
                LiveCheckStatus = liveCheck, RiskScore = riskScore,
            };
            db.Add(campaign);
            db.Add(ruleSet);
            db.Add(account);
            db.Add(submission);
            await db.SaveChangesAsync();
            return submission.Id;
        });
    }

    // ---------- HTTP ----------

    public static async Task<JsonElement> PostJsonAsync(this HttpClient client, string url, object? body = null) =>
        await (await client.PostAsJsonAsync(url, body ?? new { })).ReadJsonAsync();

    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string url) =>
        await (await client.GetAsync(url)).ReadJsonAsync();

    public static Guid Id(this JsonElement json, string property = "id") => json.GetProperty(property).GetGuid();

    public static decimal Dec(this JsonElement json, string property) => json.GetProperty(property).GetDecimal();

    public static string Str(this JsonElement json, string property) => json.GetProperty(property).GetString()!;

    /// <summary>Prepares the default (last completed) period's batch through the API.</summary>
    public static async Task<JsonElement> PrepareAsync(this HttpClient finance, string? periodKey = null)
    {
        var response = await finance.PostAsJsonAsync("/api/v1/finance/payout-batches/prepare", new { periodKey });
        return await response.ReadJsonAsync();
    }

    public static async Task<JsonElement> BatchAsync(this HttpClient finance, Guid batchId, string query = "?pageSize=200") =>
        await finance.GetJsonAsync($"/api/v1/finance/payout-batches/{batchId}{query}");

    public static async Task<JsonElement> FinalizeAsync(this HttpClient finance, Guid batchId, string? stamp = null)
    {
        stamp ??= (await finance.BatchAsync(batchId)).Str("concurrencyStamp");
        return await finance.PostJsonAsync($"/api/v1/finance/payout-batches/{batchId}/finalize",
            new { confirm = true, reason = "Reviewed and approved", concurrencyStamp = stamp });
    }

    public static JsonElement ItemFor(this JsonElement batchDetail, Guid userId) =>
        batchDetail.GetProperty("items").GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("user").Id() == userId);
}

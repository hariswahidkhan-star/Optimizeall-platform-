using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Rewards;
using OptimizeAll.IntegrationTests.Campaigns;
using OptimizeAll.IntegrationTests.Infrastructure;

namespace OptimizeAll.IntegrationTests.Rates;

/// <summary>Helpers for the person-level pricing tests (rate cards, groups, assignments).</summary>
public sealed class RatesKit(ApiFactory api)
{
    public CampaignTestKit Campaigns { get; } = new(api);

    public DateTime Now => api.Clock.GetUtcNow().UtcDateTime;

    /// <summary>Campaign managers hold rates.view, rates.manage and rates.assign.</summary>
    public Task<(TestUser User, HttpClient Client)> ManagerAsync() => api.CreateClientAsync(Role.CampaignManager);

    public static object Line(decimal amount, string? platform = null, string? format = null, string? country = null, string? label = null) =>
        new { amount, platform, format, countryCode = country, label };

    public static async Task<HttpResponseMessage> PostCardAsync(HttpClient client, string? name = null, string currency = "USD", object[]? lines = null,
        bool activate = true, bool stack = true, decimal? dailyCap = null) =>
        await client.PostAsJsonAsync("/api/v1/admin/rate-cards", new
        {
            name = name ?? "Card " + Guid.NewGuid().ToString("N")[..8], description = "Test card", currency,
            lines = lines ?? new[] { Line(10m) }, reason = "Test rate card", activate, stackCampaignBonuses = stack,
            dailyCapPerParticipant = dailyCap,
        });

    public static async Task<JsonElement> CardAsync(HttpClient client, string? name = null, string currency = "USD", object[]? lines = null,
        bool activate = true, bool stack = true, decimal? dailyCap = null) =>
        await (await PostCardAsync(client, name, currency, lines, activate, stack, dailyCap)).ReadJsonAsync();

    public static Task<HttpResponseMessage> PostVersionAsync(HttpClient client, Guid cardId, object[] lines, string currency = "USD", int? baseVersion = null,
        DateTime? effectiveFrom = null, bool confirm = true) =>
        client.PostAsJsonAsync($"/api/v1/admin/rate-cards/{cardId}/versions", new
        {
            currency, lines, reason = "Adjusting rates", baseVersion, effectiveFrom, confirm,
        });

    public static async Task<JsonElement> GroupAsync(HttpClient client, string? name = null, int priority = 0, string mode = "Manual",
        string[]? tiers = null, int? minFollowers = null, int? maxFollowers = null) =>
        await (await client.PostAsJsonAsync("/api/v1/admin/rate-groups", new
        {
            name = name ?? "Group " + Guid.NewGuid().ToString("N")[..8], priority, membershipMode = mode, autoTiers = tiers,
            autoMinFollowers = minFollowers, autoMaxFollowers = maxFollowers,
        })).ReadJsonAsync();

    public static async Task<JsonElement> AddMembersAsync(HttpClient client, Guid groupId, params Guid[] userIds) =>
        await (await client.PostAsJsonAsync($"/api/v1/admin/rate-groups/{groupId}/members", new { userIds, note = "test" })).ReadJsonAsync();

    public static Task<HttpResponseMessage> PostAssignmentAsync(HttpClient client, Guid cardId, Guid? userId = null, Guid? groupId = null,
        Guid? campaignId = null, DateTime? from = null, DateTime? to = null) =>
        client.PostAsJsonAsync("/api/v1/admin/rate-assignments", new
        {
            rateCardId = cardId, target = userId is null ? "Group" : "Person", userId, groupId, campaignId, validFrom = from, validTo = to,
            reason = "Negotiated in test",
        });

    public static async Task<JsonElement> AssignAsync(HttpClient client, Guid cardId, Guid? userId = null, Guid? groupId = null,
        Guid? campaignId = null, DateTime? from = null, DateTime? to = null) =>
        await (await PostAssignmentAsync(client, cardId, userId, groupId, campaignId, from, to)).ReadJsonAsync();

    public static async Task<JsonElement> CustomRateAsync(HttpClient client, Guid userId, object[] lines, Guid? campaignId = null,
        DateTime? validTo = null, string currency = "USD") =>
        await (await client.PostAsJsonAsync($"/api/v1/admin/users/{userId}/custom-rates", new
        {
            currency, lines, campaignId, validTo, reason = "Negotiated deal",
        })).ReadJsonAsync();

    public static Guid Id(JsonElement e) => e.GetProperty("id").GetGuid();

    public static Guid Stamp(JsonElement e) => e.GetProperty("concurrencyStamp").GetGuid();

    public Task<SubmissionRate?> SnapshotAsync(Guid submissionId) =>
        api.WithDbAsync(db => db.Set<SubmissionRate>().AsNoTracking().FirstOrDefaultAsync(r => r.SubmissionId == submissionId));

    public async Task<EarningEntry> PostRewardAsync(Guid submissionId) =>
        (await Campaigns.EarningsAsync(submissionId)).Single(e => e.Type == EarningType.PostReward);

    public async Task<decimal> EstimateAsync(HttpClient participant, Guid submissionId) =>
        (await (await participant.GetAsync($"/api/v1/me/submissions/{submissionId}")).ReadJsonAsync()).GetProperty("estimatedReward").GetDecimal();

    public async Task AddExchangeRateAsync(string from, string to, decimal rate)
    {
        await api.WithDbAsync(async db =>
        {
            db.Add(new ExchangeRate
            {
                BaseCurrency = from, QuoteCurrency = to, Rate = rate, EffectiveAt = Now.AddDays(-1), Source = "test", CreatedAt = Now,
            });
            await db.SaveChangesAsync();
        });
    }

    /// <summary>A published campaign (Instagram + TikTok) with the given base rate and extra rule-set fields.</summary>
    public async Task<CreatedCampaign> CampaignAsync(HttpClient manager, decimal baseAmount = 5m, string currency = "USD",
        Action<Dictionary<string, object?>>? customize = null, bool publish = true, params object[] extraRules)
    {
        var body = Campaigns.CampaignBody(baseAmount: baseAmount, extraRules: extraRules);
        ((Dictionary<string, object?>)body["rewardRules"]!)["currency"] = currency;
        customize?.Invoke(body);
        return await Campaigns.CreateCampaignAsync(manager, body, publish);
    }
}

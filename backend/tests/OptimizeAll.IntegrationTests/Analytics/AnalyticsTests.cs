using System.Text;
using System.Text.Json;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Ledger;
using OptimizeAll.Domain.Marketing;
using OptimizeAll.Domain.Submissions;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Marketing;

namespace OptimizeAll.IntegrationTests.Analytics;

/// <summary>
/// Small known dataset (this class owns its database):
///   participants P1, P2, P3 (verified) and P4 (unverified); P1 has an eligible Instagram and a too-new TikTok profile,
///   P2 an eligible Instagram profile. Six submissions (2 approved, 1 rejected, 1 needs correction, 1 pending, 1 reversed),
///   ledger rows including a declined bonus, a cancelled (reversed unpaid) bonus and a paid-then-clawed-back reward,
///   4 human clicks (3 unique) + 1 bot, 2 verified conversions (20 USD, 10 EUR) + 1 unverified.
/// </summary>
public sealed class AnalyticsFixture : IAsyncLifetime
{
    public ApiFactory Api { get; } = new();
    public Guid C1 { get; private set; }
    public Guid C2 { get; private set; }
    public HttpClient Client { get; private set; } = null!;
    public string Range { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await Api.InitializeAsync();
        var api = Api;
        var (managerUser, client) = await api.CreateClientAsync(Role.CampaignManager);
        Client = client;
        var now = api.Now();
        Range = $"from={Uri.EscapeDataString(now.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(now.AddHours(1).ToString("O"))}";

        var c1 = await api.CreateCampaignAsync(managerUser.Id, title: "Alpha");
        var c2 = await api.CreateCampaignAsync(managerUser.Id, title: "Beta");
        (C1, C2) = (c1.Id, c2.Id);

        var p1 = await api.CreateUserAsync();
        var p2 = await api.CreateUserAsync();
        await api.CreateUserAsync();
        await api.CreateUserAsync(emailVerified: false);

        var ig1 = await api.CreateSocialAccountAsync(p1.Id, SocialPlatform.Instagram, followers: 1000, ageDays: 400);
        var tt1 = await api.CreateSocialAccountAsync(p1.Id, SocialPlatform.TikTok, followers: 50, ageDays: 10);
        var ig2 = await api.CreateSocialAccountAsync(p2.Id, SocialPlatform.Instagram, followers: 2000, ageDays: 200);

        var s1 = await api.CreateSubmissionAsync(p1.Id, c1.Id, ig1, SubmissionStatus.Approved);
        var s2 = await api.CreateSubmissionAsync(p2.Id, c1.Id, ig2, SubmissionStatus.Approved);
        var s3 = await api.CreateSubmissionAsync(p1.Id, c1.Id, ig1, SubmissionStatus.Rejected);
        await api.CreateSubmissionAsync(p2.Id, c2.Id, ig2, SubmissionStatus.Pending);
        await api.CreateSubmissionAsync(p1.Id, c2.Id, tt1, SubmissionStatus.NeedsCorrection);
        var s6 = await api.CreateSubmissionAsync(p2.Id, c2.Id, ig2, SubmissionStatus.Reversed);

        await api.WithDbAsync(async db =>
        {
            var e = db.Set<EarningEntry>();
            e.Add(MarketingTestData.Earning(p1.Id, s1, EarningType.PostReward, 5m, EarningStatus.Approved, now));
            e.Add(MarketingTestData.Earning(p2.Id, s2, EarningType.PostReward, 5m, EarningStatus.Paid, now));
            e.Add(MarketingTestData.Earning(p2.Id, s2, EarningType.FirstPostBonus, 1.5m, EarningStatus.PendingApproval, now));
            var paid = MarketingTestData.Earning(p2.Id, s6, EarningType.PostReward, 5m, EarningStatus.Paid, now);
            e.Add(paid);
            e.Add(MarketingTestData.Earning(p2.Id, s6, EarningType.Reversal, -5m, EarningStatus.Approved, now, reverses: paid.Id)); // clawback
            e.Add(MarketingTestData.Earning(p1.Id, s3, EarningType.QualityBonus, 2m, EarningStatus.Declined, now));
            var cancelled = MarketingTestData.Earning(p1.Id, s1, EarningType.TimeLimitedBonus, 1m, EarningStatus.Reversed, now);
            e.Add(cancelled);
            e.Add(MarketingTestData.Earning(p1.Id, s1, EarningType.Reversal, -1m, EarningStatus.Reversed, now, reverses: cancelled.Id));

            var l1 = new TrackingLink { Code = "anl" + Guid.NewGuid().ToString("N")[..8], CampaignId = c1.Id, UserId = p1.Id, DestinationUrl = "https://x.example.com", UtmSource = "optimizeall", UtmCampaign = "a", CreatedAt = now };
            var l2 = new TrackingLink { Code = "anl" + Guid.NewGuid().ToString("N")[..8], CampaignId = c2.Id, UserId = p2.Id, DestinationUrl = "https://x.example.com", UtmSource = "optimizeall", UtmCampaign = "b", CreatedAt = now };
            db.AddRange(l1, l2);
            TrackingClick Click(TrackingLink l, bool unique, bool bot) => new() { TrackingLinkId = l.Id, ClickedAt = now.AddMinutes(-30), VisitorHash = new string('a', 64), IsUnique = unique, IsSuspectedBot = bot };
            db.AddRange(Click(l1, true, false), Click(l1, false, false), Click(l1, true, false), Click(l1, true, true), Click(l2, true, false));
            db.AddRange(
                new TrackingConversion { TrackingLinkId = l1.Id, ExternalReference = "o-1", Value = 20m, Currency = "USD", OccurredAt = now.AddMinutes(-20), ReceivedAt = now, VerifiedAt = now },
                new TrackingConversion { TrackingLinkId = l1.Id, ExternalReference = "o-2", Value = 99m, Currency = "USD", OccurredAt = now.AddMinutes(-20), ReceivedAt = now, VerifiedAt = null },
                new TrackingConversion { TrackingLinkId = l2.Id, ExternalReference = "o-3", Value = 10m, Currency = "EUR", OccurredAt = now.AddMinutes(-20), ReceivedAt = now, VerifiedAt = now });
            await db.SaveChangesAsync();
        });
    }

    public Task DisposeAsync() => Api.DisposeAsync();
}

public sealed class AnalyticsTests(AnalyticsFixture fixture) : IClassFixture<AnalyticsFixture>
{
    private readonly ApiFactory api = fixture.Api;
    private readonly Guid _c1 = fixture.C1;
    private readonly Guid _c2 = fixture.C2;
    private readonly HttpClient _client = fixture.Client;
    private readonly string _range = fixture.Range;

    private static JsonElement Metric(JsonElement section, string key, string? currency = null) =>
        section.GetProperty("metrics").EnumerateArray().Single(m => m.GetProperty("key").GetString() == key &&
            (currency is null || m.GetProperty("currency").GetString() == currency));

    private static decimal? Value(JsonElement section, string key, string? currency = null)
    {
        var v = Metric(section, key, currency).GetProperty("value");
        return v.ValueKind == JsonValueKind.Null ? null : v.GetDecimal();
    }

    [Fact]
    public async Task Overview_metrics_match_the_known_dataset_exactly()
    {
        var o = await (await _client.GetAsync($"/api/v1/analytics/overview?{_range}")).ReadJsonAsync();

        var funnel = o.GetProperty("funnel");
        Assert.Equal("counted", funnel.GetProperty("measurement").GetString());
        Assert.Equal(4m, Value(funnel, "registrations"));
        Assert.Equal(3m, Value(funnel, "emailVerified"));
        Assert.Equal(2m, Value(funnel, "participantsWithSocialAccount"));
        Assert.Equal(2m, Value(funnel, "eligibleAccounts"));
        Assert.Equal(2m, Value(funnel, "participantsWithSubmission"));
        Assert.Equal(50m, Value(funnel, "submissionRate"));
        Assert.Equal("percent", Metric(funnel, "submissionRate").GetProperty("unit").GetString());

        var posts = o.GetProperty("posts");
        Assert.Equal(6m, Value(posts, "postsSubmitted"));
        Assert.Equal(2m, Value(posts, "postsApproved"));
        Assert.Equal(1m, Value(posts, "postsRejected"));
        Assert.Equal(1m, Value(posts, "postsNeedingCorrection"));
        Assert.Equal(1m, Value(posts, "postsPending"));
        Assert.Equal(1m, Value(posts, "postsReversed"));
        Assert.Equal(50m, Value(posts, "approvalRate")); // 2 / (2 approved + 1 rejected + 1 reversed)

        var spend = o.GetProperty("spend");
        Assert.Equal(11.5m, Value(spend, "spend", "USD")); // 5 + 5 + 1.5 + (5 − 5 clawback); declined and cancelled excluded
        Assert.Equal(5.75m, Value(spend, "costPerApprovedPost", "USD"));
        Assert.Equal("money", Metric(spend, "spend", "USD").GetProperty("unit").GetString());
        Assert.Equal("counted", Metric(spend, "spend", "USD").GetProperty("measurement").GetString());
        var byCampaign = o.GetProperty("spendByCampaign").EnumerateArray().ToList();
        Assert.Equal(11.5m, byCampaign.Single(x => x.GetProperty("campaignId").GetGuid() == _c1).GetProperty("amount").GetDecimal());
        Assert.Equal(0m, byCampaign.Single(x => x.GetProperty("campaignId").GetGuid() == _c2).GetProperty("amount").GetDecimal());

        var reach = o.GetProperty("reach");
        Assert.Equal("estimated", reach.GetProperty("measurement").GetString());
        var reachMetric = Metric(reach, "estimatedReach");
        Assert.Equal(3000m, reachMetric.GetProperty("value").GetDecimal());
        Assert.Equal("estimated", reachMetric.GetProperty("measurement").GetString());
        Assert.Equal("Estimated reach (declared follower counts, not measured views)", reachMetric.GetProperty("label").GetString());

        var traffic = o.GetProperty("traffic");
        Assert.Equal("measured", traffic.GetProperty("measurement").GetString());
        Assert.Equal(4m, Value(traffic, "trackedClicks"));
        Assert.Equal(3m, Value(traffic, "uniqueClicks"));
        Assert.Equal(1m, Value(traffic, "botClicksExcluded"));
        Assert.All(traffic.GetProperty("metrics").EnumerateArray(), m => Assert.Equal("measured", m.GetProperty("measurement").GetString()));

        var conversions = o.GetProperty("conversions");
        Assert.Equal("measured", conversions.GetProperty("measurement").GetString());
        Assert.Equal(2m, Value(conversions, "verifiedConversions"));
        Assert.Equal(20m, Value(conversions, "conversionValue", "USD"));
        Assert.Equal(10m, Value(conversions, "conversionValue", "EUR"));

        var today = DateOnly.FromDateTime(api.Now()).ToString("yyyy-MM-dd");
        var point = o.GetProperty("timeseries").EnumerateArray().Single(p => p.GetProperty("date").GetString() == today);
        Assert.Equal(4, point.GetProperty("registrations").GetInt32());
        Assert.Equal(6, o.GetProperty("timeseries").EnumerateArray().Sum(p => p.GetProperty("submissions").GetInt32()));
        Assert.Equal(2, o.GetProperty("timeseries").EnumerateArray().Sum(p => p.GetProperty("approvals").GetInt32()));
        Assert.Equal(4, o.GetProperty("timeseries").EnumerateArray().Sum(p => p.GetProperty("clicks").GetInt32()));

        var rows = o.GetProperty("campaigns").EnumerateArray().ToList();
        var alpha = rows.Single(r => r.GetProperty("campaignId").GetGuid() == _c1);
        Assert.Equal(3, alpha.GetProperty("submitted").GetInt32());
        Assert.Equal(2, alpha.GetProperty("approved").GetInt32());
        Assert.Equal(66.67m, alpha.GetProperty("approvalRate").GetDecimal());
        Assert.Equal(11.5m, alpha.GetProperty("spend")[0].GetProperty("amount").GetDecimal());
        Assert.Equal(5.75m, alpha.GetProperty("costPerApproved")[0].GetProperty("amount").GetDecimal());
        Assert.Equal(3, alpha.GetProperty("clicks").GetInt32());
        Assert.Equal(2, alpha.GetProperty("uniqueClicks").GetInt32());
        Assert.Equal(1, alpha.GetProperty("verifiedConversions").GetInt32());
        Assert.Equal(3000, alpha.GetProperty("estimatedReach").GetInt64());
        var beta = rows.Single(r => r.GetProperty("campaignId").GetGuid() == _c2);
        Assert.Equal(3, beta.GetProperty("submitted").GetInt32());
        Assert.Equal(0, beta.GetProperty("approved").GetInt32());
        Assert.Equal(0m, beta.GetProperty("approvalRate").GetDecimal()); // 0 / 1 decided (reversed)
        Assert.Empty(beta.GetProperty("costPerApproved").EnumerateArray());
        Assert.Equal(1, beta.GetProperty("clicks").GetInt32());
        Assert.Equal(0, beta.GetProperty("estimatedReach").GetInt64());
    }

    [Fact]
    public async Task Filters_and_campaign_detail_with_platform_breakdown()
    {
        var instagram = await (await _client.GetAsync($"/api/v1/analytics/overview?{_range}&platform=Instagram")).ReadJsonAsync();
        Assert.Equal(5m, Value(instagram.GetProperty("posts"), "postsSubmitted")); // the TikTok submission is excluded
        Assert.Equal(2m, Value(instagram.GetProperty("funnel"), "participantsWithSocialAccount"));
        Assert.Equal(2m, Value(instagram.GetProperty("funnel"), "eligibleAccounts"));
        var tiktok = await (await _client.GetAsync($"/api/v1/analytics/overview?{_range}&platform=TikTok")).ReadJsonAsync();
        Assert.Equal(1m, Value(tiktok.GetProperty("posts"), "postsSubmitted"));
        Assert.Equal(1m, Value(tiktok.GetProperty("funnel"), "participantsWithSocialAccount"));
        Assert.Equal(0m, Value(tiktok.GetProperty("funnel"), "eligibleAccounts")); // the TikTok profile is too new
        Assert.Contains("Platform filter not applied", Metric(instagram.GetProperty("traffic"), "trackedClicks").GetProperty("note").GetString());

        var detail = await (await _client.GetAsync($"/api/v1/analytics/campaigns/{_c1}?{_range}")).ReadJsonAsync();
        Assert.Equal(_c1, detail.GetProperty("campaignId").GetGuid());
        Assert.Equal(3m, Value(detail.GetProperty("posts"), "postsSubmitted"));
        Assert.Equal(3m, Value(detail.GetProperty("traffic"), "trackedClicks"));
        Assert.Equal(1m, Value(detail.GetProperty("conversions"), "verifiedConversions"));
        var platform = Assert.Single(detail.GetProperty("platforms").EnumerateArray().ToList());
        Assert.Equal("Instagram", platform.GetProperty("platform").GetString());
        Assert.Equal(3, platform.GetProperty("submitted").GetInt32());
        Assert.Equal(2, platform.GetProperty("approved").GetInt32());
        Assert.Equal(11.5m, platform.GetProperty("spend")[0].GetProperty("amount").GetDecimal());
        Assert.Equal(3000, platform.GetProperty("estimatedReach").GetInt64());
        Assert.Equal(JsonValueKind.Null, (await (await _client.GetAsync($"/api/v1/analytics/overview?{_range}")).ReadJsonAsync()).GetProperty("platforms").ValueKind);

        await (await _client.GetAsync($"/api/v1/analytics/campaigns/{Guid.NewGuid()}")).ShouldFailAsync(404, "campaign.not_found");
    }

    [Fact]
    public async Task Range_validation_and_csv_export()
    {
        var now = api.Now();
        await (await _client.GetAsync($"/api/v1/analytics/overview?from={Uri.EscapeDataString(now.ToString("O"))}&to={Uri.EscapeDataString(now.AddDays(-1).ToString("O"))}"))
            .ShouldFailAsync(400, "range.invalid");
        await (await _client.GetAsync($"/api/v1/analytics/overview?from={Uri.EscapeDataString(now.AddDays(-400).ToString("O"))}&to={Uri.EscapeDataString(now.ToString("O"))}"))
            .ShouldFailAsync(400, "range.too_long");
        var defaults = await (await _client.GetAsync("/api/v1/analytics/overview")).ReadJsonAsync();
        Assert.Equal(30, (defaults.GetProperty("to").GetDateTime() - defaults.GetProperty("from").GetDateTime()).TotalDays, 3);

        var csv = await _client.GetAsync($"/api/v1/analytics/overview/export.csv?{_range}");
        csv.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync()).TrimStart('﻿');
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        Assert.Equal("section,campaign,key,label,value,unit,measurement,currency,note", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("posts,,postsSubmitted,Posts submitted,6,count,counted,"));
        Assert.Contains(lines, l => l.StartsWith("spend,,spend,Spend on posts,11.50,money,counted,USD,"));
        Assert.Contains(lines, l => l.StartsWith("reach,,estimatedReach,") && l.Contains(",3000,count,estimated,"));
        Assert.Contains(lines, l => l.StartsWith("campaigns,Alpha,clicks,Tracked clicks,3,count,measured,"));
    }
}

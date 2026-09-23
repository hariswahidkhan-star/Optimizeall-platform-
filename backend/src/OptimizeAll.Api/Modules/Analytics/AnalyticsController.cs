using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Marketing.Shared;
using OptimizeAll.Domain.Campaigns;
using OptimizeAll.Domain.Common;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Analytics;

public sealed class AnalyticsQuery
{
    /// <summary>UTC start (default: 30 days before <see cref="To"/>).</summary>
    public DateTime? From { get; set; }

    /// <summary>UTC end (default: now). Range at most 366 days.</summary>
    public DateTime? To { get; set; }

    public Guid? CampaignId { get; set; }
    public SocialPlatform? Platform { get; set; }
}

[ApiController]
[Route("api/v1/analytics")]
[HasPermission(Permissions.AnalyticsView)]
public sealed class AnalyticsController(AnalyticsService analytics, AppDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet("overview")]
    public Task<AnalyticsDto> Overview([FromQuery] AnalyticsQuery query, CancellationToken ct) =>
        analytics.BuildAsync(Filter(query, query.CampaignId), includePlatforms: false, ct);

    /// <summary>Same sections for one campaign plus a per-platform breakdown.</summary>
    [HttpGet("campaigns/{id:guid}")]
    public async Task<AnalyticsDto> Campaign(Guid id, [FromQuery] AnalyticsQuery query, CancellationToken ct)
    {
        if (!await db.Set<Campaign>().AnyAsync(c => c.Id == id, ct)) throw DomainException.NotFound("Campaign");
        return await analytics.BuildAsync(Filter(query, id), includePlatforms: true, ct);
    }

    /// <summary>CSV of every overview metric and the per-campaign table (long format).</summary>
    [HttpGet("overview/export.csv")]
    public async Task<FileContentResult> Export([FromQuery] AnalyticsQuery query, CancellationToken ct)
    {
        var dto = await analytics.BuildAsync(Filter(query, query.CampaignId), includePlatforms: false, ct);
        var rows = new List<object?[]>();
        foreach (var section in new[] { dto.Funnel, dto.Posts, dto.Spend, dto.Reach, dto.Traffic, dto.Conversions })
            foreach (var m in section.Metrics)
                rows.Add(new object?[] { section.Key, null, m.Key, m.Label, m.Value, m.Unit, m.Measurement, m.Currency, m.Note });
        foreach (var c in dto.Campaigns)
        {
            void Add(string key, string label, decimal? value, string unit, string measurement, string? currency = null) =>
                rows.Add(new object?[] { "campaigns", c.Title, key, label, value, unit, measurement, currency, null });
            Add("submitted", "Posts submitted", c.Submitted, MetricUnits.Count, Measurement.Counted);
            Add("approved", "Approved", c.Approved, MetricUnits.Count, Measurement.Counted);
            Add("approvalRate", "Approval rate", c.ApprovalRate, MetricUnits.Percent, Measurement.Counted);
            foreach (var s in c.Spend) Add("spend", "Spend on posts", s.Amount, MetricUnits.Money, Measurement.Counted, s.Currency);
            foreach (var s in c.CostPerApproved) Add("costPerApproved", "Cost per approved post", s.Amount, MetricUnits.Money, Measurement.Counted, s.Currency);
            Add("clicks", "Tracked clicks", c.Clicks, MetricUnits.Count, Measurement.Measured);
            Add("uniqueClicks", "Unique clicks", c.UniqueClicks, MetricUnits.Count, Measurement.Measured);
            Add("verifiedConversions", "Verified conversions", c.VerifiedConversions, MetricUnits.Count, Measurement.Measured);
            Add("estimatedReach", AnalyticsService.ReachLabel, c.EstimatedReach, MetricUnits.Count, Measurement.Estimated);
        }
        return Csv.File($"analytics-{dto.From:yyyyMMdd}-{dto.To:yyyyMMdd}.csv",
            new[] { "section", "campaign", "key", "label", "value", "unit", "measurement", "currency", "note" }, rows);
    }

    private AnalyticsFilter Filter(AnalyticsQuery query, Guid? campaignId) =>
        new(DateRange.Resolve(query.From, query.To, clock.GetUtcNow().UtcDateTime), campaignId, query.Platform);
}

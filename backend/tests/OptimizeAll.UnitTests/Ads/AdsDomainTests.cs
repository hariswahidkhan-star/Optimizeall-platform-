using System.Text.Json;
using OptimizeAll.Api.Modules.Ads;
using OptimizeAll.Domain.Ads;
using OptimizeAll.Domain.Common;

namespace OptimizeAll.UnitTests.Ads;

public sealed class AdKpiTests
{
    [Fact]
    public void Kpis_are_null_when_denominators_are_zero()
    {
        var k = AdKpis.From(AdTotals.Zero);
        Assert.Null(k.Ctr);
        Assert.Null(k.Cpc);
        Assert.Null(k.Cpm);
        Assert.Null(k.Cpa);
        Assert.Null(k.Roas);
        Assert.Null(k.ConversionRate);
        Assert.Null(k.Frequency);

        var spendOnly = AdKpis.From(new AdTotals(50m, 0, 0, 0, 0, 0, 0));
        Assert.Null(spendOnly.Cpa);
        Assert.Equal(0m, spendOnly.Roas);
    }

    [Fact]
    public void Kpi_formulas()
    {
        var k = AdKpis.From(new AdTotals(Spend: 500m, Impressions: 100_000, Clicks: 2_000, Conversions: 40m, ConversionValue: 2_000m, Reach: 40_000, VideoViews: 0));
        Assert.Equal(0.02m, k.Ctr);
        Assert.Equal(0.25m, k.Cpc);
        Assert.Equal(5m, k.Cpm);
        Assert.Equal(12.5m, k.Cpa);
        Assert.Equal(4m, k.Roas);
        Assert.Equal(0.02m, k.ConversionRate);
        Assert.Equal(2.5m, k.Frequency);
    }

    [Fact]
    public void Currency_conversion_rounds_money_to_the_target_minor_unit_and_keeps_counts()
    {
        var original = new AdTotals(1000m, 10, 5, 2m, 3333.33m, 0, 0);
        var converted = original.Convert(278.12345m, "PKR");
        Assert.Equal(278123.45m, converted.Spend);
        Assert.Equal(927077.24m, converted.ConversionValue);
        Assert.Equal(10, converted.Impressions);
        Assert.Equal(1000m, original.Spend); // original untouched
        Assert.Equal(1m, new AdTotals(3.6725m, 0, 0, 0, 0, 0, 0).Convert(0.27229m, "USD").Spend);
    }
}

public sealed class PacingTests
{
    private static readonly DateOnly Month = new(2026, 9, 1);

    private static Dictionary<DateOnly, decimal> Daily(int days, decimal perDay) =>
        Enumerable.Range(0, days).ToDictionary(i => Month.AddDays(i), _ => perDay);

    [Fact]
    public void Expected_to_date_actual_ratio_and_projection()
    {
        // 30-day month, 3000 budget: 10 days elapsed → expected 1000. Spent 120/day → 1200, ratio 1.2, projected 1200 + 120×20 = 3600.
        var p = Pacing.Compute(3000m, Month, Month.AddDays(9), Daily(10, 120m), 1.15m, 0.85m, "USD");
        Assert.Equal(10, p.DaysElapsed);
        Assert.Equal(30, p.DaysInMonth);
        Assert.Equal(1000m, p.ExpectedToDate);
        Assert.Equal(1200m, p.ActualToDate);
        Assert.Equal(1.2m, p.PacingRatio);
        Assert.Equal(3600m, p.ProjectedMonthEnd);
        Assert.Equal(1.2m, p.ProjectedVsBudget);
        Assert.Equal(PacingState.Over, p.State);
    }

    [Fact]
    public void Projection_uses_the_recent_run_rate_and_under_pacing_is_detected()
    {
        var spend = Daily(10, 100m);
        foreach (var d in Enumerable.Range(3, 7)) spend[Month.AddDays(d)] = 20m; // slowed down in the last 7 days
        var p = Pacing.Compute(3000m, Month, Month.AddDays(9), spend, 1.15m, 0.85m, "USD");
        Assert.Equal(440m, p.ActualToDate);
        Assert.Equal(20m, p.DailyRunRate);
        Assert.Equal(840m, p.ProjectedMonthEnd);
        Assert.Equal(PacingState.Under, p.State);
    }

    [Fact]
    public void Edge_cases_before_the_month_after_it_and_without_budget()
    {
        Assert.Equal(PacingState.NotStarted, Pacing.Compute(3000m, Month, Month.AddDays(-1), new Dictionary<DateOnly, decimal>(), 1.15m, 0.85m, "USD").State);
        Assert.Null(Pacing.Compute(3000m, Month, Month.AddDays(-1), new Dictionary<DateOnly, decimal>(), 1.15m, 0.85m, "USD").PacingRatio);
        var full = Pacing.Compute(3000m, Month, Month.AddDays(40), Daily(30, 100m), 1.15m, 0.85m, "USD");
        Assert.Equal(30, full.DaysElapsed);
        Assert.Equal(3000m, full.ProjectedMonthEnd);
        Assert.Equal(PacingState.OnTrack, full.State);
        Assert.Equal(PacingState.NoBudget, Pacing.Compute(0m, Month, Month.AddDays(5), Daily(5, 10m), 1.15m, 0.85m, "USD").State);
    }
}

public sealed class NamingAndUtmTests
{
    private const string Template = "{client}_{platform}_{objective}_{yyyymm}_{name}";
    private static readonly Dictionary<string, string?> Fixed = new() { ["client"] = "nimbus-fitness", ["platform"] = "google", ["yyyymm"] = "202609" };

    [Theory]
    [InlineData("nimbus-fitness_google_conversions_202609_brand-search", true)]
    [InlineData("nimbus-fitness_google_awareness_202609_youtube", true)]
    [InlineData("Nimbus Brand Search", false)]
    [InlineData("nimbus-fitness_meta_conversions_202609_brand", false)]
    [InlineData("nimbus-fitness_google_conversions_202610_brand", false)]
    [InlineData("nimbus-fitness_google__202609_brand", false)]
    [InlineData("nimbus-fitness_google_conversions_202609_", false)]
    public void Names_are_checked_against_the_template(string name, bool compliant)
    {
        var check = NamingConvention.Check(Template, name, Fixed);
        Assert.Equal(compliant, check.Compliant);
        if (!compliant)
        {
            Assert.NotEmpty(check.Problems);
            Assert.Equal("nimbus-fitness_google_conversions_202609_summer-sale", check.Expected);
        }
    }

    [Fact]
    public void Build_slugifies_free_values_and_template_problems_are_reported()
    {
        var values = new Dictionary<string, string?>(Fixed) { ["objective"] = "Lead Gen", ["name"] = "Winter Sale 2026!" };
        Assert.Equal("nimbus-fitness_google_lead-gen_202609_winter-sale-2026", NamingConvention.Build(Template, values));
        Assert.Contains(NamingConvention.TemplateProblems("{client}_{unknown}"), p => p.Contains("unknown"));
        Assert.NotEmpty(NamingConvention.TemplateProblems("no tokens"));
    }

    [Fact]
    public void Utm_builder_keeps_other_parameters_and_fragment_and_replaces_existing_utm()
    {
        var url = UtmBuilder.Build("https://shop.example.com/p/serum?color=red&utm_source=old#reviews",
            new UtmParameters("Facebook", "Paid Social", "Winter Sale", Content: "carousel 1"));
        Assert.Equal("https://shop.example.com/p/serum?color=red&utm_source=facebook&utm_medium=paid-social&utm_campaign=winter-sale&utm_content=carousel-1#reviews", url);
        var raw = UtmBuilder.Build("https://example.com", new UtmParameters("Google", "cpc", "Brand"), normalize: false);
        Assert.Equal("https://example.com/?utm_source=Google&utm_medium=cpc&utm_campaign=Brand", raw);
        Assert.Throws<DomainException>(() => UtmBuilder.Build("ftp://example.com", new UtmParameters("a", "b", "c")));
        Assert.Throws<DomainException>(() => UtmBuilder.Build("https://example.com", new UtmParameters("a", "", "c")));
    }

    [Fact]
    public void Copy_limits_per_platform()
    {
        var google = AdCopyLimits.Validate(AdPlatform.GoogleAds, new[] { "Short one", "Another", new string('h', 31) }, new[] { "d1", "d2" }, null);
        Assert.Contains(google, i => i.Field == "headline" && i.Index == 2 && i.Severity == "Error");
        Assert.Contains(AdCopyLimits.Validate(AdPlatform.GoogleAds, new[] { "One" }, new[] { "d" }, null), i => i.Message.Contains("at least 3"));
        var meta = AdCopyLimits.Validate(AdPlatform.MetaAds, new[] { new string('h', 41) }, Array.Empty<string>(), new string('p', 126));
        Assert.All(meta, i => Assert.Equal("Warning", i.Severity));
        Assert.Equal(2, meta.Count);
    }
}

public sealed class AdImportTests
{
    private const string GoogleExport =
        "﻿Campaign report\r\n" +
        "\"September 1, 2026 - September 3, 2026\"\r\n" +
        "Day,Campaign,Campaign ID,Currency code,Cost,Impr.,Clicks,Conversions,Conv. value\r\n" +
        "2026-09-01,Brand search,111,USD,\"1,204.50\",\"48,210\",\"1,930\",96.00,\"9,120.00\"\r\n" +
        "2026-09-01,Brand search,111,USD,10.00,100,10,1.00,50.00\r\n" +
        "2026-09-02,\"Generic, search\",222,USD,300.00,9000,300,--,0\r\n" +
        "Total: Account,,,,\"1,514.50\",\"57,310\",\"2,240\",97.00,\"9,170.00\"\r\n";

    [Fact]
    public void Csv_reader_handles_quotes_bom_and_delimiters()
    {
        var rows = CsvReader.Parse("a;b;c\n\"x;1\";\"he said \"\"hi\"\"\";3\n");
        Assert.Equal(new[] { "x;1", "he said \"hi\"", "3" }, rows[1]);
        Assert.Equal("Campaign report", CsvReader.Parse(GoogleExport)[0][0]);
    }

    [Fact]
    public void Google_export_maps_skips_title_and_total_rows_and_merges_segments()
    {
        var rows = CsvReader.Parse(GoogleExport);
        var mapping = AdImportTemplates.SuggestMapping(AdImportTemplates.GoogleAds, rows[2].ToList());
        Assert.Equal("Cost", mapping[AdImportFields.Spend]);
        Assert.Equal("Impr.", mapping[AdImportFields.Impressions]);
        var parsed = AdImportParser.Parse(rows, mapping, "USD");
        Assert.Empty(parsed.Errors);
        Assert.Equal(3, parsed.RowsTotal);
        Assert.Equal(2, parsed.Rows.Count);
        var brand = parsed.Rows.Single(r => r.CampaignKey == "111");
        Assert.Equal(1214.50m, brand.Spend);
        Assert.Equal(48_310, brand.Impressions);
        Assert.Equal(97m, brand.Conversions);
        var generic = parsed.Rows.Single(r => r.CampaignKey == "222");
        Assert.Equal("Generic, search", generic.CampaignName);
        Assert.Equal(0m, generic.Conversions);
        Assert.Equal(AdLevel.Campaign, generic.Level);
    }

    [Fact]
    public void Meta_export_reads_currency_from_the_spend_header_and_levels_from_mapping()
    {
        var csv = "Day,Campaign name,Ad set name,Amount spent (AED),Impressions,Reach,Link clicks,Purchases,Purchases conversion value\n" +
                  "2026-09-01,Aurora | Shopping,Women 25-44,120.50,10000,8000,300,6,1500\n";
        var rows = CsvReader.Parse(csv);
        var mapping = AdImportTemplates.SuggestMapping(AdImportTemplates.MetaAds, rows[0].ToList());
        Assert.Equal("Amount spent (AED)", mapping[AdImportFields.Spend]);
        var parsed = AdImportParser.Parse(rows, mapping, "USD");
        var row = Assert.Single(parsed.Rows);
        Assert.Equal("AED", row.Currency);
        Assert.Equal(AdLevel.AdGroup, row.Level);
        Assert.Equal("name:women-25-44", row.AdGroupKey);
        Assert.Equal(8000, row.Reach);
    }

    [Fact]
    public void Validation_reports_bad_rows_and_missing_mapping()
    {
        var csv = "date,campaign,spend,impressions,clicks\n2026-13-01,A,10,100,5\n2026-09-02,B,abc,100,5\n2026-09-03,C,-5,100,5\n2026-09-04,D,5,10,50\n2026-09-05,E,5,100,5\n";
        var rows = CsvReader.Parse(csv);
        var mapping = AdImportTemplates.SuggestMapping(AdImportTemplates.Generic, rows[0].ToList());
        var parsed = AdImportParser.Parse(rows, mapping, "USD");
        Assert.Equal(4, parsed.Errors.Count);
        Assert.Contains(parsed.Errors, e => e.StartsWith("Row 2:") && e.Contains("invalid date"));
        Assert.Contains(parsed.Errors, e => e.StartsWith("Row 3:") && e.Contains("not a number"));
        Assert.Contains(parsed.Errors, e => e.StartsWith("Row 4:") && e.Contains("negative"));
        Assert.Contains(parsed.Errors, e => e.StartsWith("Row 5:") && e.Contains("clicks exceed impressions"));
        Assert.Single(parsed.Rows);

        var missing = AdImportParser.Parse(rows, new Dictionary<string, string> { ["date"] = "date" }, "USD");
        Assert.Contains(missing.Errors, e => e.Contains("\"spend\""));
    }

    [Fact]
    public void Google_search_stream_and_meta_insights_responses_are_parsed()
    {
        var stream = """
        [{"results":[{"customer":{"currencyCode":"USD"},"campaign":{"resourceName":"customers/1/campaigns/55","status":"ENABLED","name":"Brand","id":"55"},
          "metrics":{"clicks":"120","conversionsValue":450.5,"conversions":9.5,"costMicros":"123450000","impressions":"4000","videoViews":"0"},
          "segments":{"date":"2026-09-01"}}],"fieldMask":"segments.date","requestId":"x"}]
        """;
        var rows = GoogleAdsReportingProvider.ParseSearchStream(stream, "EUR");
        var r = Assert.Single(rows);
        Assert.Equal(123.45m, r.Spend);
        Assert.Equal(9.5m, r.Conversions);
        Assert.Equal("USD", r.Currency);
        Assert.Equal(AdEntityStatus.Active, r.CampaignStatus);

        var insights = JsonDocument.Parse("""
        {"data":[{"campaign_id":"77","campaign_name":"Shop","account_currency":"AED","spend":"99.10","impressions":"5000","clicks":"80","reach":"3500",
          "actions":[{"action_type":"link_click","value":"80"},{"action_type":"purchase","value":"4"}],
          "action_values":[{"action_type":"purchase","value":"820.5"}],"date_start":"2026-09-02","date_stop":"2026-09-02"}],"paging":{}}
        """).RootElement;
        var m = Assert.Single(MetaAdsReportingProvider.ParseInsights(insights, "USD", new[] { "omni_purchase", "purchase" }));
        Assert.Equal(4m, m.Conversions);
        Assert.Equal(820.5m, m.ConversionValue);
        Assert.Equal("AED", m.Currency);
        Assert.Equal(3500, m.Reach);
    }
}

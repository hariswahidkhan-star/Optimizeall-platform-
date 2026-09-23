using OptimizeAll.Domain.Ads;

namespace OptimizeAll.UnitTests.Ads;

public sealed class AdImportCurrencyTests
{
    [Fact]
    public void Rows_of_one_entity_and_day_in_different_currencies_are_never_summed()
    {
        // Merging segmented rows must not add EUR to USD: the EUR row stays separate so the import's currency check rejects it.
        const string csv = "Day,Campaign,Campaign ID,Currency code,Cost,Impr.,Clicks,Conversions,Conv. value\n" +
                           "2026-09-01,Brand,901,USD,100.00,1000,10,1,50.00\n" +
                           "2026-09-01,Brand,901,EUR,40.00,400,4,0,0\n" +
                           "2026-09-01,Brand,901,usd,10.00,100,1,0,0\n";
        var rows = CsvReader.Parse(csv);
        var mapping = AdImportTemplates.SuggestMapping(AdImportTemplates.GoogleAds, rows[0].ToList());
        var parsed = AdImportParser.Parse(rows, mapping, "USD");

        Assert.Equal(2, parsed.Rows.Count);
        var usd = parsed.Rows.Single(r => string.Equals(r.Currency, "USD", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(110.00m, usd.Spend);
        Assert.Equal(1100, usd.Impressions);
        var eur = parsed.Rows.Single(r => r.Currency == "EUR");
        Assert.Equal(40.00m, eur.Spend);
    }
}

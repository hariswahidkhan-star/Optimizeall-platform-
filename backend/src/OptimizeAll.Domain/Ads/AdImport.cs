using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OptimizeAll.Domain.Ads;

/// <summary>Minimal RFC 4180 reader: quoted fields, escaped quotes, CRLF/LF, BOM; delimiter auto-detected (, ; or tab).</summary>
public static class CsvReader
{
    public const int MaxRows = 50_000;

    public static List<string[]> Parse(string text, char? delimiter = null)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var d = delimiter ?? Detect(text);
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }
            if (c == '"' && field.Length == 0) inQuotes = true;
            else if (c == d) { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row.ToArray());
                row.Clear();
                if (rows.Count > MaxRows) throw new Common.DomainException("import.too_many_rows", $"Files may have at most {MaxRows:N0} rows.");
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }
        return rows.Where(r => r.Any(v => !string.IsNullOrWhiteSpace(v))).ToList();
    }

    private static char Detect(string text)
    {
        var firstLines = string.Join('\n', text.Split('\n').Take(10));
        var candidates = new[] { ',', ';', '\t' };
        return candidates.OrderByDescending(c => firstLines.Count(ch => ch == c)).First();
    }
}

/// <summary>Target fields of an ad metrics import.</summary>
public static class AdImportFields
{
    public const string Date = "date";
    public const string Campaign = "campaign";
    public const string CampaignId = "campaignId";
    public const string AdGroup = "adGroup";
    public const string AdGroupId = "adGroupId";
    public const string Ad = "ad";
    public const string AdId = "adId";
    public const string Spend = "spend";
    public const string Currency = "currency";
    public const string Impressions = "impressions";
    public const string Clicks = "clicks";
    public const string Conversions = "conversions";
    public const string ConversionValue = "conversionValue";
    public const string Reach = "reach";
    public const string VideoViews = "videoViews";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Date, Campaign, CampaignId, AdGroup, AdGroupId, Ad, AdId, Spend, Currency, Impressions, Clicks, Conversions, ConversionValue, Reach, VideoViews,
    };

    public static readonly IReadOnlyList<string> Required = new[] { Date, Campaign, Spend, Impressions, Clicks };
}

public sealed record ImportTemplate(string Id, string Name, AdPlatform? Platform, IReadOnlyDictionary<string, string[]> HeaderAliases, string SampleCsv);

/// <summary>Import templates matching the platforms' standard report exports, plus a generic one.</summary>
public static class AdImportTemplates
{
    public static readonly ImportTemplate GoogleAds = new("google-ads", "Google Ads report export", AdPlatform.GoogleAds,
        new Dictionary<string, string[]>
        {
            [AdImportFields.Date] = new[] { "Day", "Date" },
            [AdImportFields.Campaign] = new[] { "Campaign" },
            [AdImportFields.CampaignId] = new[] { "Campaign ID" },
            [AdImportFields.AdGroup] = new[] { "Ad group" },
            [AdImportFields.AdGroupId] = new[] { "Ad group ID" },
            [AdImportFields.AdId] = new[] { "Ad ID" },
            [AdImportFields.Spend] = new[] { "Cost" },
            [AdImportFields.Currency] = new[] { "Currency code", "Currency" },
            [AdImportFields.Impressions] = new[] { "Impr.", "Impressions" },
            [AdImportFields.Clicks] = new[] { "Clicks" },
            [AdImportFields.Conversions] = new[] { "Conversions" },
            [AdImportFields.ConversionValue] = new[] { "Conv. value", "Conversion value" },
            [AdImportFields.VideoViews] = new[] { "Views", "Video views" },
        },
        "Campaign report\r\n\"September 1, 2026 - September 30, 2026\"\r\n" +
        "Day,Campaign,Campaign ID,Currency code,Cost,Impr.,Clicks,Conversions,Conv. value\r\n" +
        "2026-09-01,nimbus_google_search_202609_brand,12345678901,USD,\"1,204.50\",\"48,210\",\"1,930\",96.00,\"9,120.00\"\r\n");

    public static readonly ImportTemplate MetaAds = new("meta-ads", "Meta Ads Manager export", AdPlatform.MetaAds,
        new Dictionary<string, string[]>
        {
            [AdImportFields.Date] = new[] { "Day", "Reporting starts" },
            [AdImportFields.Campaign] = new[] { "Campaign name" },
            [AdImportFields.CampaignId] = new[] { "Campaign ID" },
            [AdImportFields.AdGroup] = new[] { "Ad set name" },
            [AdImportFields.AdGroupId] = new[] { "Ad set ID" },
            [AdImportFields.Ad] = new[] { "Ad name" },
            [AdImportFields.AdId] = new[] { "Ad ID" },
            [AdImportFields.Spend] = new[] { "Amount spent", "Amount spent (*)" },
            [AdImportFields.Currency] = new[] { "Currency" },
            [AdImportFields.Impressions] = new[] { "Impressions" },
            [AdImportFields.Clicks] = new[] { "Link clicks", "Clicks (all)" },
            [AdImportFields.Conversions] = new[] { "Purchases", "Results" },
            [AdImportFields.ConversionValue] = new[] { "Purchases conversion value", "Purchase conversion value" },
            [AdImportFields.Reach] = new[] { "Reach" },
            [AdImportFields.VideoViews] = new[] { "ThruPlays", "3-second video plays" },
        },
        "Reporting starts,Reporting ends,Day,Campaign name,Campaign ID,Amount spent (USD),Impressions,Reach,Link clicks,Purchases,Purchases conversion value\r\n" +
        "2026-09-01,2026-09-30,2026-09-01,nimbus_meta_conversions_202609_trial,120210000000001,310.25,42100,30950,655,21,1890.00\r\n");

    public static readonly ImportTemplate Generic = new("generic", "Generic daily CSV", null,
        AdImportFields.All.ToDictionary(f => f, f => new[] { f, Humanize(f) }),
        string.Join(',', AdImportFields.All) + "\r\n" +
        "2026-09-01,brand-search,,,,,,120.50,USD,10000,420,12,960.00,,\r\n");

    public static readonly IReadOnlyList<ImportTemplate> All = new[] { GoogleAds, MetaAds, Generic };

    public static ImportTemplate? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    private static string Humanize(string field) => Regex.Replace(field, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant();

    /// <summary>Suggests a mapping (field → header) for the given headers; "Amount spent (USD)" style headers match "(*)".</summary>
    public static Dictionary<string, string> SuggestMapping(ImportTemplate template, IReadOnlyList<string> headers)
    {
        var mapping = new Dictionary<string, string>();
        foreach (var (field, aliases) in template.HeaderAliases)
        {
            foreach (var alias in aliases)
            {
                var match = headers.FirstOrDefault(h => Matches(h, alias));
                if (match is null) continue;
                mapping[field] = match;
                break;
            }
        }
        return mapping;
    }

    public static bool Matches(string header, string alias)
    {
        var h = header.Trim();
        if (alias.EndsWith("(*)", StringComparison.Ordinal))
        {
            var prefix = alias[..^3].Trim();
            return h.StartsWith(prefix + " (", StringComparison.OrdinalIgnoreCase) && h.EndsWith(')');
        }
        return string.Equals(h, alias, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Currency embedded in a header such as "Amount spent (USD)".</summary>
    public static string? CurrencyFromHeader(string header)
    {
        var m = Regex.Match(header, @"\(([A-Z]{3})\)\s*$");
        return m.Success ? m.Groups[1].Value : null;
    }
}

public sealed record ParsedAdRow(
    int RowNumber, DateOnly Date, AdLevel Level, string CampaignKey, string CampaignName, string? AdGroupKey, string? AdGroupName,
    string? AdKey, string? AdName, string? Currency, decimal Spend, long Impressions, long Clicks, decimal Conversions,
    decimal ConversionValue, long? Reach, long? VideoViews)
{
    public string EntityKey => Level switch
    {
        AdLevel.Ad => AdKey!,
        AdLevel.AdGroup => AdGroupKey!,
        _ => CampaignKey,
    };

    public string EntityName => Level switch
    {
        AdLevel.Ad => AdName ?? AdKey!,
        AdLevel.AdGroup => AdGroupName ?? AdGroupKey!,
        _ => CampaignName,
    };
}

public sealed record ImportParseResult(
    IReadOnlyList<string> Headers, int HeaderRowIndex, IReadOnlyList<ParsedAdRow> Rows, IReadOnlyList<string> Errors, int RowsTotal);

/// <summary>
/// Turns CSV rows into validated daily metric rows using a field → header mapping. The header row is found by
/// looking for the row that contains the mapped date header (platform exports put a title and date range above it).
/// Totals rows ("Total: …"), blank dates and "--" values are handled; rows sharing the same (date, level, entity) are
/// summed (segmented exports), so a re-import of the same file produces the same totals.
/// </summary>
public static class AdImportParser
{
    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd", "yyyy/MM/dd", "MMM d, yyyy", "MMMM d, yyyy", "d MMM yyyy", "yyyyMMdd",
    };

    public static int FindHeaderRow(IReadOnlyList<string[]> rows, string dateHeader)
    {
        for (var i = 0; i < Math.Min(rows.Count, 20); i++)
            if (rows[i].Any(c => string.Equals(c.Trim(), dateHeader.Trim(), StringComparison.OrdinalIgnoreCase))) return i;
        return -1;
    }

    public static ImportParseResult Parse(IReadOnlyList<string[]> rows, IReadOnlyDictionary<string, string> mapping, string? fallbackCurrency)
    {
        var errors = new List<string>();
        foreach (var field in AdImportFields.Required)
            if (!mapping.ContainsKey(field) || string.IsNullOrWhiteSpace(mapping[field]))
                errors.Add($"Map a column to \"{field}\".");
        foreach (var field in mapping.Keys)
            if (!AdImportFields.All.Contains(field)) errors.Add($"Unknown target field \"{field}\".");
        if (errors.Count > 0) return new ImportParseResult(Array.Empty<string>(), -1, Array.Empty<ParsedAdRow>(), errors, 0);

        var headerIndex = FindHeaderRow(rows, mapping[AdImportFields.Date]);
        if (headerIndex < 0)
            return new ImportParseResult(Array.Empty<string>(), -1, Array.Empty<ParsedAdRow>(),
                new[] { $"No header row contains the date column \"{mapping[AdImportFields.Date]}\"." }, 0);

        var headers = rows[headerIndex].Select(h => h.Trim()).ToArray();
        var index = new Dictionary<string, int>();
        foreach (var (field, header) in mapping)
        {
            var i = Array.FindIndex(headers, h => string.Equals(h, header.Trim(), StringComparison.OrdinalIgnoreCase));
            if (i < 0) errors.Add($"Column \"{header}\" (mapped to {field}) is not in the file.");
            else index[field] = i;
        }
        if (errors.Count > 0) return new ImportParseResult(headers, headerIndex, Array.Empty<ParsedAdRow>(), errors, 0);

        var headerCurrency = AdImportTemplates.CurrencyFromHeader(mapping[AdImportFields.Spend]);
        var parsed = new List<ParsedAdRow>();
        var total = 0;
        for (var r = headerIndex + 1; r < rows.Count; r++)
        {
            var row = rows[r];
            var rowNumber = r + 1;
            string Cell(string field) => index.TryGetValue(field, out var i) && i < row.Length ? row[i].Trim() : string.Empty;

            var dateText = Cell(AdImportFields.Date);
            var first = row.Length > 0 ? row[0].Trim() : string.Empty;
            if (first.StartsWith("Total", StringComparison.OrdinalIgnoreCase) || (dateText.Length == 0 && Cell(AdImportFields.Campaign).Length == 0))
                continue; // summary rows
            total++;
            var rowErrors = new List<string>();

            if (!TryParseDate(dateText, out var date)) rowErrors.Add($"invalid date \"{dateText}\"");
            var campaignName = Cell(AdImportFields.Campaign);
            var campaignId = Cell(AdImportFields.CampaignId);
            if (campaignName.Length == 0 && campaignId.Length == 0) rowErrors.Add("campaign is empty");

            var spend = Number(Cell(AdImportFields.Spend), "spend", rowErrors);
            var impressions = Whole(Cell(AdImportFields.Impressions), "impressions", rowErrors);
            var clicks = Whole(Cell(AdImportFields.Clicks), "clicks", rowErrors);
            var conversions = Number(Cell(AdImportFields.Conversions), "conversions", rowErrors);
            var value = Number(Cell(AdImportFields.ConversionValue), "conversion value", rowErrors);
            long? reach = index.ContainsKey(AdImportFields.Reach) ? Whole(Cell(AdImportFields.Reach), "reach", rowErrors) : null;
            long? views = index.ContainsKey(AdImportFields.VideoViews) ? Whole(Cell(AdImportFields.VideoViews), "video views", rowErrors) : null;
            if (spend < 0 || impressions < 0 || clicks < 0 || conversions < 0 || value < 0) rowErrors.Add("negative values are not allowed");
            if (clicks > impressions && impressions > 0) rowErrors.Add("clicks exceed impressions");

            var currency = Cell(AdImportFields.Currency);
            if (currency.Length == 0) currency = headerCurrency ?? fallbackCurrency ?? string.Empty;
            currency = currency.ToUpperInvariant();
            if (currency.Length != 3) rowErrors.Add("currency is missing");

            if (rowErrors.Count > 0)
            {
                errors.Add($"Row {rowNumber}: {string.Join("; ", rowErrors)}.");
                continue;
            }

            var adGroupName = NullIfEmpty(Cell(AdImportFields.AdGroup));
            var adGroupId = NullIfEmpty(Cell(AdImportFields.AdGroupId));
            var adName = NullIfEmpty(Cell(AdImportFields.Ad));
            var adId = NullIfEmpty(Cell(AdImportFields.AdId));
            var level = adName is not null || adId is not null ? AdLevel.Ad
                : adGroupName is not null || adGroupId is not null ? AdLevel.AdGroup
                : AdLevel.Campaign;

            parsed.Add(new ParsedAdRow(rowNumber, date, level, Key(campaignId, campaignName), campaignName.Length > 0 ? campaignName : campaignId,
                level >= AdLevel.AdGroup ? Key(adGroupId, adGroupName) : null, adGroupName,
                level == AdLevel.Ad ? Key(adId, adName) : null, adName, currency, spend, impressions, clicks, conversions, value, reach, views));
        }

        // Sum rows of the same entity and day (segmented exports, e.g. by device or network). Amounts in different
        // currencies are never added up: such rows stay separate so the currency check reports them.
        var merged = parsed
            .GroupBy(p => (p.Date, p.Level, p.EntityKey, Currency: (p.Currency ?? string.Empty).ToUpperInvariant()))
            .Select(g => g.Skip(1).Aggregate(g.First(), (a, b) => a with
            {
                Spend = a.Spend + b.Spend,
                Impressions = a.Impressions + b.Impressions,
                Clicks = a.Clicks + b.Clicks,
                Conversions = a.Conversions + b.Conversions,
                ConversionValue = a.ConversionValue + b.ConversionValue,
                Reach = a.Reach is null && b.Reach is null ? null : (a.Reach ?? 0) + (b.Reach ?? 0),
                VideoViews = a.VideoViews is null && b.VideoViews is null ? null : (a.VideoViews ?? 0) + (b.VideoViews ?? 0),
            }))
            .OrderBy(p => p.Date).ThenBy(p => p.EntityKey)
            .ToList();

        return new ImportParseResult(headers, headerIndex, merged, errors, total);
    }

    public static string Key(string? id, string? name) =>
        !string.IsNullOrWhiteSpace(id) ? id.Trim() : "name:" + NamingConvention.Slug(name ?? string.Empty);

    public static bool TryParseDate(string text, out DateOnly date)
    {
        text = text.Trim();
        if (DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) return true;
        // "Mon, Sep 1, 2026" (Google Ads "Day" with weekday)
        var comma = text.IndexOf(", ", StringComparison.Ordinal);
        return comma > 0 && comma <= 4 &&
               DateOnly.TryParseExact(text[(comma + 2)..], DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static string? NullIfEmpty(string v) => v.Length == 0 || v == "--" ? null : v;

    public static decimal Number(string raw, string name, List<string> errors)
    {
        var v = raw.Replace(",", string.Empty).Replace("%", string.Empty).Trim();
        foreach (var symbol in new[] { "$", "£", "€", "AED", "PKR", "Rs" }) v = v.Replace(symbol, string.Empty);
        v = v.Trim();
        if (v.Length == 0 || v == "--") return 0m;
        if (decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return d;
        errors.Add($"{name} \"{raw}\" is not a number");
        return 0m;
    }

    public static long Whole(string raw, string name, List<string> errors)
    {
        var d = Number(raw, name, errors);
        if (d != Math.Truncate(d))
        {
            errors.Add($"{name} \"{raw}\" must be a whole number");
            return 0;
        }
        return (long)d;
    }
}

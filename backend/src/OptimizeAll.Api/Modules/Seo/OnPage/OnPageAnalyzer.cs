using System.Text.Json;
using System.Text.RegularExpressions;
using OptimizeAll.Api.Modules.Seo.Crawling;
using OptimizeAll.Domain.Seo;

namespace OptimizeAll.Api.Modules.Seo.OnPage;

public sealed record ChecklistResult(string Key, string Label, bool Passed, int Weight, string Detail);

public sealed record HeadingDto(int Level, string Text);

public sealed record OnPageResult(
    string Keyword, string? Url, int Score, int WordCount, int KeywordOccurrences, double KeywordDensity,
    double? FleschReadingEase, string? ReadabilityLabel, string ReadabilityNote, string? Title, string? MetaDescription,
    IReadOnlyList<string> H1, IReadOnlyList<HeadingDto> Headings, int InternalLinks, int ExternalLinks, int Images,
    int ImagesMissingAlt, IReadOnlyList<string> SchemaTypesFound, IReadOnlyList<string> SchemaSuggestions,
    IReadOnlyList<ChecklistResult> Checklist);

/// <summary>
/// Scores a page (HTML) or plain copy against a target keyword. The score is the weighted share of applicable checklist
/// items that pass (items that cannot be evaluated — e.g. the URL slug for pasted text — are excluded).
/// </summary>
public static partial class OnPageAnalyzer
{
    public const string ReadabilityNote =
        "Flesch reading ease is calibrated for English (60–70 = plain English). Treat scores for other languages as indicative only.";

    public static OnPageResult AnalyzeHtml(string html, string keyword, string? url)
    {
        var pageUrl = url is not null && Uri.TryCreate(url, UriKind.Absolute, out var u) ? u : new Uri("https://page.invalid/");
        var data = HtmlPageExtractor.Extract(html, pageUrl);
        return Analyze(data, keyword, url, isHtml: true, pageUrl);
    }

    public static OnPageResult AnalyzeText(string text, string keyword)
    {
        var data = new PageData { Text = text.Trim(), WordCount = SeoText.WordCount(text) };
        return Analyze(data, keyword, null, isHtml: false, new Uri("https://page.invalid/"));
    }

    private static OnPageResult Analyze(PageData d, string keyword, string? url, bool isHtml, Uri pageUrl)
    {
        keyword = SeoText.NormalizeKeyword(keyword);
        var checks = new List<ChecklistResult>();
        void Add(string key, string label, bool passed, int weight, string detail) => checks.Add(new ChecklistResult(key, label, passed, weight, detail));

        var words = SeoText.Words(d.Text);
        var occurrences = SeoText.CountPhrase(d.Text, keyword);
        var keywordWords = Math.Max(1, SeoText.Words(keyword).Count);
        var density = words.Count == 0 ? 0 : Math.Round(100.0 * occurrences * keywordWords / words.Count, 2);
        var first100 = string.Join(' ', words.Take(100));
        var flesch = SeoText.FleschReadingEase(d.Text);

        var internalLinks = 0;
        var externalLinks = 0;
        foreach (var link in d.Links)
        {
            if (Uri.TryCreate(link.Href, UriKind.Absolute, out var l) && RegistrableDomain.SameSite(l.Host, pageUrl.Host)) internalLinks++;
            else externalLinks++;
        }

        if (isHtml)
        {
            Add("keyword_in_title", "Keyword in the title tag", SeoText.ContainsPhrase(d.Title, keyword), 15,
                d.Title is null ? "The page has no title." : $"Title: “{d.Title}”");
            var titleLength = d.Title?.Length ?? 0;
            Add("title_length", "Title is 30–60 characters", titleLength is >= SeoAuditRules.TitleMinLength and <= SeoAuditRules.TitleMaxLength, 5,
                $"{titleLength} characters");
            Add("keyword_in_h1", "Keyword in the H1", d.H1.Any(h => SeoText.ContainsPhrase(h, keyword)), 10,
                d.H1.Count == 0 ? "No H1 found." : $"H1: “{d.H1[0]}”");
            Add("single_h1", "Exactly one H1", d.H1.Count == 1, 5, $"{d.H1.Count} H1 heading(s)");
            Add("keyword_in_meta", "Keyword in the meta description", SeoText.ContainsPhrase(d.MetaDescription, keyword), 10,
                d.MetaDescription is null ? "No meta description." : $"{d.MetaDescription.Length} characters");
            var metaLength = d.MetaDescription?.Length ?? 0;
            Add("meta_length", "Meta description is 70–160 characters", metaLength is >= SeoAuditRules.DescriptionMinLength and <= SeoAuditRules.DescriptionMaxLength, 5,
                $"{metaLength} characters");
        }
        if (url is not null)
        {
            var slug = Uri.UnescapeDataString(pageUrl.AbsolutePath).Replace('-', ' ').Replace('_', ' ').Replace('/', ' ');
            Add("keyword_in_url", "Keyword in the URL", SeoText.ContainsPhrase(slug, keyword), 5, pageUrl.AbsolutePath);
        }
        Add("keyword_in_intro", "Keyword in the first 100 words", SeoText.ContainsPhrase(first100, keyword), 10,
            SeoText.ContainsPhrase(first100, keyword) ? "Found early in the copy." : "Mention the keyword in the opening paragraph.");
        Add("keyword_density", "Keyword density 0.5–2.5%", density is >= 0.5 and <= 2.5, 10,
            $"{density:0.##}% ({occurrences} occurrence(s) in {words.Count} words)");
        Add("word_count", "At least 300 words", words.Count >= 300, 10, $"{words.Count} words");
        if (isHtml)
        {
            var levels = d.Headings.Select(h => h.Level).ToList();
            var skips = levels.Zip(levels.Skip(1)).Any(p => p.Second > p.First + 1);
            Add("heading_structure", "Logical heading structure with H2 sections", levels.Contains(2) && !skips, 5,
                skips ? "A heading level is skipped (e.g. H2 → H4)." : levels.Contains(2) ? $"{levels.Count(l => l == 2)} H2 section(s)" : "Add H2 subheadings to structure the page.");
            var missingAlt = d.Images.Count(i => i.Alt is null);
            Add("image_alt", "All images have alt text", missingAlt == 0, 5,
                d.Images.Count == 0 ? "No images." : $"{missingAlt} of {d.Images.Count} image(s) missing alt");
            Add("internal_links", "Links to other pages of the site", internalLinks > 0, 3, $"{internalLinks} internal link(s)");
            Add("external_links", "Cites at least one external source", externalLinks > 0, 2, $"{externalLinks} external link(s)");
            Add("structured_data", "Structured data (JSON-LD) present", d.JsonLd.Count > 0 || d.HasMicrodata, 5,
                d.JsonLd.Count > 0 ? $"{d.JsonLd.Count} JSON-LD block(s)" : "No structured data.");
        }
        Add("readability", "Readable copy (Flesch ≥ 50)", flesch is >= 50, 5,
            flesch is null ? "No text to measure." : $"{flesch:0.0} — {SeoText.FleschLabel(flesch.Value)}");

        var total = checks.Sum(c => c.Weight);
        var score = total == 0 ? 0 : (int)Math.Round(100.0 * checks.Where(c => c.Passed).Sum(c => c.Weight) / total, MidpointRounding.AwayFromZero);
        var found = SchemaTypes(d.JsonLd);
        return new OnPageResult(keyword, url, score, words.Count, occurrences, density, flesch,
            flesch is null ? null : SeoText.FleschLabel(flesch.Value), ReadabilityNote, d.Title, d.MetaDescription, d.H1,
            d.Headings.Select(h => new HeadingDto(h.Level, h.Text)).ToList(), internalLinks, externalLinks, d.Images.Count,
            d.Images.Count(i => i.Alt is null), found, SuggestSchema(d, pageUrl, url is not null, found), checks);
    }

    private static List<string> SchemaTypes(IEnumerable<string> jsonLd)
    {
        var types = new List<string>();
        foreach (var block in jsonLd)
        {
            try
            {
                using var doc = JsonDocument.Parse(block);
                Collect(doc.RootElement, types);
            }
            catch (JsonException)
            {
                // invalid blocks are reported by the audit
            }
        }
        return types.Distinct().ToList();

        static void Collect(JsonElement e, List<string> types)
        {
            if (e.ValueKind == JsonValueKind.Array) { foreach (var i in e.EnumerateArray()) Collect(i, types); return; }
            if (e.ValueKind != JsonValueKind.Object) return;
            if (e.TryGetProperty("@type", out var t))
            {
                if (t.ValueKind == JsonValueKind.String) types.Add(t.GetString()!);
                else if (t.ValueKind == JsonValueKind.Array) types.AddRange(t.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!));
            }
            if (e.TryGetProperty("@graph", out var graph)) Collect(graph, types);
        }
    }

    private static List<string> SuggestSchema(PageData d, Uri pageUrl, bool hasUrl, List<string> found)
    {
        var suggestions = new List<string>();
        void Suggest(string type, string why)
        {
            if (!found.Contains(type, StringComparer.OrdinalIgnoreCase)) suggestions.Add($"{type}: {why}");
        }
        var questions = d.Headings.Count(h => h.Text.TrimEnd().EndsWith('?'));
        if (questions >= 2) Suggest("FAQPage", $"{questions} question headings could appear as FAQ rich results.");
        if (PriceRegex().IsMatch(d.Text)) Suggest("Product", "Prices are mentioned; Product/Offer markup enables price and availability in results.");
        if (PhoneRegex().IsMatch(d.Text) || d.Text.Contains("opening hours", StringComparison.OrdinalIgnoreCase))
            Suggest("LocalBusiness", "Contact details found; LocalBusiness markup supports the local pack and knowledge panel.");
        if (hasUrl && pageUrl.AbsolutePath.Trim('/').Contains('/')) Suggest("BreadcrumbList", "Nested URL; breadcrumbs replace the raw URL in results.");
        if (hasUrl && pageUrl.AbsolutePath is "/" or "") Suggest("Organization", "Home page: Organization markup (logo, sameAs profiles) feeds the knowledge panel.");
        if (d.WordCount >= 600 && d.Headings.Count(h => h.Level == 2) >= 2) Suggest("Article", "Long-form content; Article markup adds headline, author and date signals.");
        if (suggestions.Count == 0 && found.Count == 0) suggestions.Add("WebPage: at minimum describe the page with WebPage markup (name, description, primaryImageOfPage).");
        return suggestions;
    }

    [GeneratedRegex(@"(?:[$€£]|\bUSD|\bGBP|\bAED|\bPKR|\bRs\.?)\s?\d", RegexOptions.IgnoreCase)]
    private static partial Regex PriceRegex();

    [GeneratedRegex(@"\+?\d[\d\s().-]{8,}\d")]
    private static partial Regex PhoneRegex();
}

using OptimizeAll.Api.Modules.Seo.OnPage;
using OptimizeAll.Domain.Seo;

namespace OptimizeAll.UnitTests.Seo;

public sealed class SeoTextTests
{
    [Fact]
    public void Flesch_reading_ease_matches_the_formula()
    {
        // 6 words, 1 sentence, 6 syllables: 206.835 − 1.015×6 − 84.6×1 = 116.145
        Assert.Equal(116.1, SeoText.FleschReadingEase("The cat sat on the mat."));
        // 2 sentences × 4 words, syllables: "Reading"2 "is"1 "very"2 "important"3 / "Children"2 "enjoy"2 "good"1 "stories"2 = 15
        // 206.835 − 1.015×4 − 84.6×(15/8) = 44.15 → 44.2 (one decimal)
        Assert.Equal(44.2, SeoText.FleschReadingEase("Reading is very important. Children enjoy good stories."));
        Assert.Null(SeoText.FleschReadingEase("   "));
    }

    [Theory]
    [InlineData("cat", 1)]
    [InlineData("table", 2)]
    [InlineData("reading", 2)]
    [InlineData("important", 3)]
    [InlineData("stories", 2)]
    [InlineData("jumped", 1)]
    [InlineData("wanted", 2)]
    [InlineData("beautiful", 3)]
    public void Syllable_estimates(string word, int expected) => Assert.Equal(expected, SeoText.Syllables(word));

    [Fact]
    public void Keyword_phrases_are_counted_on_word_boundaries()
    {
        const string text = "Best running shoes for trail running. Running shoes wear out; shoes-running is not a phrase.";
        Assert.Equal(2, SeoText.CountPhrase(text, "Running  Shoes"));
        Assert.Equal(0, SeoText.CountPhrase("runningshoes", "running shoes"));
        Assert.Equal("running shoes", SeoText.NormalizeKeyword("  Running   SHOES "));
    }

    [Fact]
    public void SimHash_is_stable_and_near_duplicates_are_close()
    {
        var a = string.Join(' ', Enumerable.Range(0, 200).Select(i => $"word{i % 50} sentence{i % 7}"));
        var b = a + " one extra tail";
        var c = string.Join(' ', Enumerable.Range(0, 200).Select(i => $"different{i} content{i * 3}"));
        Assert.Equal(SeoText.SimHash(a), SeoText.SimHash(a));
        Assert.True(SeoText.HammingDistance(SeoText.SimHash(a), SeoText.SimHash(b)) <= SeoAuditRules.DuplicateContentMaxHammingDistance);
        Assert.True(SeoText.HammingDistance(SeoText.SimHash(a), SeoText.SimHash(c)) > 10);
    }

    [Theory]
    [InlineData("www.example.com", "example.com")]
    [InlineData("shop.example.co.uk", "example.co.uk")]
    [InlineData("a.b.example.com.pk", "example.com.pk")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("localhost", "localhost")]
    public void Registrable_domain(string host, string expected) => Assert.Equal(expected, RegistrableDomain.Of(host));

    [Fact]
    public void Share_of_voice_weights_positions_by_ctr_and_volume()
    {
        var sov = RankMath.ShareOfVoice(new (string, int?, int?)[]
        {
            ("us.com", 1000, 1), ("them.com", 1000, 5), ("us.com", 100, null), ("them.com", 100, 2),
        });
        Assert.Equal("us.com", sov[0].Domain);
        Assert.Equal(1.0, sov.Sum(s => s.Share), 3);
        Assert.Equal(0, RankMath.ExpectedCtr(21));
        Assert.Equal(90, RankMath.Change(null, 11));
    }
}

public sealed class OnPageAnalyzerTests
{
    private static string Page(string title, string description, string h1, string body, string extraHead = "") => $$"""
        <html lang="en"><head><title>{{title}}</title><meta name="description" content="{{description}}">
        <meta name="viewport" content="width=device-width">{{extraHead}}</head>
        <body><h1>{{h1}}</h1><h2>Why it matters</h2><p>{{body}}</p><h2>How to choose?</h2><p>More copy here.</p>
        <a href="/pricing">Pricing</a><a href="https://research.example.org/study">Study</a><img src="/a.png" alt="Runner"></body></html>
        """;

    [Fact]
    public void Well_optimized_page_scores_high_and_checklist_explains_each_item()
    {
        var body = "Running shoes matter. " + string.Join(' ', Enumerable.Repeat("Choose light cushioned trainers for daily miles and easy recovery runs.", 40))
                   + " The best running shoes fit well.";
        var html = Page("Best Running Shoes for Beginners in 2026 | Stride", "Compare the best running shoes for beginners: cushioning, fit and price, tested over 500 miles by our coaches.",
            "The best running shoes for beginners", body,
            "<script type=\"application/ld+json\">{\"@context\":\"https://schema.org\",\"@type\":\"Article\"}</script>");
        var result = OnPageAnalyzer.AnalyzeHtml(html, "running shoes", "https://stride.example/guides/best-running-shoes");

        Assert.True(result.Score >= 85, $"score {result.Score}: {string.Join(", ", result.Checklist.Where(c => !c.Passed).Select(c => c.Key))}");
        Assert.All(new[] { "keyword_in_title", "keyword_in_h1", "keyword_in_meta", "keyword_in_url", "keyword_in_intro", "single_h1", "image_alt",
            "internal_links", "external_links", "structured_data" }, key => Assert.True(result.Checklist.Single(c => c.Key == key).Passed, key));
        Assert.Equal(1, result.InternalLinks);
        Assert.Equal(1, result.ExternalLinks);
        Assert.Contains("Article", result.SchemaTypesFound);
        Assert.Contains("English", result.ReadabilityNote);
    }

    [Fact]
    public void Missing_keyword_and_thin_copy_score_low()
    {
        var html = Page("Home", "", "Welcome", "Short text.");
        var result = OnPageAnalyzer.AnalyzeHtml(html, "running shoes", null);
        Assert.True(result.Score < 40, $"score {result.Score}");
        Assert.False(result.Checklist.Single(c => c.Key == "keyword_in_title").Passed);
        Assert.DoesNotContain(result.Checklist, c => c.Key == "keyword_in_url"); // not applicable without a URL
        Assert.Contains(result.SchemaSuggestions, s => s.StartsWith("FAQPage", StringComparison.Ordinal) || s.StartsWith("WebPage", StringComparison.Ordinal));
    }

    [Fact]
    public void Score_is_the_weighted_share_of_passed_items()
    {
        var result = OnPageAnalyzer.AnalyzeText("running shoes " + string.Join(' ', Enumerable.Repeat("easy words here now", 100)), "running shoes");
        var total = result.Checklist.Sum(c => c.Weight);
        var passed = result.Checklist.Where(c => c.Passed).Sum(c => c.Weight);
        Assert.Equal((int)Math.Round(100.0 * passed / total, MidpointRounding.AwayFromZero), result.Score);
        Assert.Equal(402, result.WordCount);
        Assert.Equal(1, result.KeywordOccurrences);
        Assert.Equal(0.5, result.KeywordDensity);
    }
}

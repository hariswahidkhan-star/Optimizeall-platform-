using OptimizeAll.Domain.Marketing;

namespace OptimizeAll.UnitTests.Marketing;

public sealed class TrackingUrlTests
{
    private static readonly IReadOnlyList<KeyValuePair<string, string?>> Utm =
        TrackingUrl.Utm("optimizeall", "social", "spring-sale", "ABC123");

    [Fact]
    public void Appends_utm_parameters_to_a_bare_url()
    {
        Assert.Equal("https://shop.example.com/landing?utm_source=optimizeall&utm_medium=social&utm_campaign=spring-sale&utm_content=ABC123",
            TrackingUrl.MergeQuery("https://shop.example.com/landing", Utm));
    }

    [Fact]
    public void Preserves_existing_parameters_and_fragment_and_overwrites_existing_utm()
    {
        var result = TrackingUrl.MergeQuery("https://shop.example.com/p?id=7&UTM_SOURCE=old&utm_medium=email&ref=x#reviews", Utm);
        Assert.Equal("https://shop.example.com/p?id=7&ref=x&utm_source=optimizeall&utm_medium=social&utm_campaign=spring-sale&utm_content=ABC123#reviews", result);
    }

    [Fact]
    public void Existing_utm_not_in_the_new_set_is_kept()
    {
        var result = TrackingUrl.MergeQuery("https://example.com/?utm_term=shoes", Utm);
        Assert.StartsWith("https://example.com/?utm_term=shoes&utm_source=optimizeall", result);
    }

    [Fact]
    public void Escapes_values_and_skips_empty_ones()
    {
        var result = TrackingUrl.MergeQuery("http://example.com", TrackingUrl.Utm("a b", "c&d", "e=f", null, ""));
        Assert.Equal("http://example.com/?utm_source=a%20b&utm_medium=c%26d&utm_campaign=e%3Df", result);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com/file")]
    [InlineData("")]
    public void Rejects_non_http_destinations(string destination)
    {
        Assert.False(TrackingUrl.IsValidDestination(destination));
        Assert.Throws<ArgumentException>(() => TrackingUrl.MergeQuery(destination, Utm));
    }

    [Theory]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", true)]
    [InlineData("facebookexternalhit/1.1", true)]
    [InlineData("Slackbot-LinkExpanding 1.0", true)]
    [InlineData("WhatsApp/2.23.20.0", true)]
    [InlineData("curl/8.4.0", true)]
    [InlineData("python-requests/2.31", true)]
    [InlineData("Mozilla/5.0 HeadlessChrome/120.0", true)]
    [InlineData("", true)]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148 Safari/604.1", false)]
    public void Bot_detection(string userAgent, bool bot) => Assert.Equal(bot, TrackingUrl.IsSuspectedBot(userAgent));
}

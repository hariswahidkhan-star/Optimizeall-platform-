using OptimizeAll.Domain.Seo;

namespace OptimizeAll.UnitTests.Seo;

public sealed class RobotsTxtTests
{
    private const string Bot = "OptimizeAllBot";

    [Fact]
    public void Missing_or_empty_file_allows_everything()
    {
        Assert.True(RobotsTxt.AllowAll.IsAllowed(Bot, "/anything"));
        Assert.True(RobotsTxt.Parse("").IsAllowed(Bot, "/admin"));
        Assert.True(RobotsTxt.Parse(null).IsAllowed(Bot, "/"));
    }

    [Fact]
    public void Star_group_applies_when_no_specific_group_matches()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /private/\nDisallow: /tmp");
        Assert.False(robots.IsAllowed(Bot, "/private/report"));
        Assert.False(robots.IsAllowed(Bot, "/tmp"));
        Assert.False(robots.IsAllowed(Bot, "/tmp-files/x"));
        Assert.True(robots.IsAllowed(Bot, "/public"));
        Assert.True(robots.IsAllowed(Bot, "/"));
    }

    [Fact]
    public void Most_specific_user_agent_group_wins_and_same_agent_groups_merge()
    {
        var robots = RobotsTxt.Parse("""
            User-agent: *
            Disallow: /

            User-agent: optimizeallbot
            Disallow: /drafts/

            User-agent: OptimizeAllBot
            Disallow: /internal/
            """);
        Assert.True(robots.IsAllowed(Bot, "/blog"));
        Assert.False(robots.IsAllowed(Bot, "/drafts/a"));
        Assert.False(robots.IsAllowed(Bot, "/internal/b"));
        Assert.False(robots.IsAllowed("SomeOtherBot", "/blog"));
    }

    [Fact]
    public void Consecutive_user_agent_lines_share_a_group()
    {
        var robots = RobotsTxt.Parse("User-agent: googlebot\nUser-agent: optimizeallbot\nDisallow: /search\n\nUser-agent: *\nAllow: /");
        Assert.False(robots.IsAllowed(Bot, "/search?q=x"));
        Assert.True(robots.IsAllowed("Otherbot", "/search"));
    }

    [Fact]
    public void Longest_match_wins_and_allow_wins_ties()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /shop\nAllow: /shop/public\nAllow: /page\nDisallow: /page");
        Assert.False(robots.IsAllowed(Bot, "/shop/cart"));
        Assert.True(robots.IsAllowed(Bot, "/shop/public/item"));
        Assert.True(robots.IsAllowed(Bot, "/page"));
    }

    [Fact]
    public void Wildcards_and_end_anchor()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /*.pdf$\nDisallow: /*?sessionid=\nDisallow: /a*b*c");
        Assert.False(robots.IsAllowed(Bot, "/files/report.pdf"));
        Assert.True(robots.IsAllowed(Bot, "/files/report.pdf?download=1"));
        Assert.False(robots.IsAllowed(Bot, "/cart?sessionid=123"));
        Assert.False(robots.IsAllowed(Bot, "/a-x-b-y-c"));
        Assert.True(robots.IsAllowed(Bot, "/a-x-y"));
    }

    [Fact]
    public void Pathological_wildcards_do_not_blow_up()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /*a*a*a*a*a*a*a*a*a*a*a*a*b");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(robots.IsAllowed(Bot, "/" + new string('a', 5000)));
        Assert.True(sw.ElapsedMilliseconds < 1000);
    }

    [Fact]
    public void Robots_txt_itself_is_always_allowed()
    {
        Assert.True(RobotsTxt.Parse("User-agent: *\nDisallow: /").IsAllowed(Bot, "/robots.txt"));
    }

    [Fact]
    public void Empty_disallow_means_allow_all_and_comments_are_ignored()
    {
        var robots = RobotsTxt.Parse("# comment\nUser-agent: * # everyone\nDisallow:   # nothing blocked\n");
        Assert.True(robots.IsAllowed(Bot, "/x"));
        Assert.Empty(robots.InvalidLines);
    }

    [Fact]
    public void Collects_sitemaps_crawl_delay_and_invalid_lines()
    {
        var robots = RobotsTxt.Parse("""
            Sitemap: https://example.com/sitemap.xml
            Disallow: /orphan-rule
            User-agent: *
            Crawl-delay: 2.5
            This line is broken
            Foo: bar
            Sitemap: not-a-url
            """);
        Assert.Equal(new[] { "https://example.com/sitemap.xml" }, robots.Sitemaps);
        Assert.Equal(2.5, robots.CrawlDelayFor(Bot));
        Assert.Equal(4, robots.InvalidLines.Count);
        Assert.Contains(robots.InvalidLines, l => l.Contains("outside a User-agent group"));
    }

    [Fact]
    public void Percent_encoded_unreserved_characters_compare_equal()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /~joe/");
        Assert.False(robots.IsAllowed(Bot, "/%7Ejoe/index.html"));
    }
}

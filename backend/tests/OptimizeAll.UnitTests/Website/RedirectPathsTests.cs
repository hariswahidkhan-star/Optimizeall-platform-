using OptimizeAll.Api.Modules.Website.Redirects;

namespace OptimizeAll.UnitTests.Website;

public sealed class RedirectPathsTests
{
    [Theory]
    [InlineData("/About-Us/", "/about-us", "")]
    [InlineData("/about-us?utm_source=x#team", "/about-us", "utm_source=x")]
    [InlineData("/blog/old%2Dpost", "/blog/old-post", "")]
    [InlineData("/services?category=Local-SEO&utm_medium=cpc", "/services?category=local-seo", "utm_medium=cpc")]
    [InlineData("/services?utm_medium=cpc&category=seo", "/services?category=seo", "utm_medium=cpc")]
    [InlineData("/services?utm_medium=cpc", "/services", "utm_medium=cpc")]
    [InlineData("/lp/nimbus/spring-offer", "/lp/nimbus/spring-offer", "")]
    public void Parses_and_normalizes_request_targets(string raw, string key, string query) =>
        Assert.Equal(new RedirectKey(key, query), RedirectPaths.Parse(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("about-us")]
    [InlineData("//evil.example/x")]
    [InlineData("https://evil.example/x")]
    [InlineData("/%2F%2Fevil.example")]
    [InlineData("/a/../admin")]
    [InlineData("/a\\b")]
    [InlineData("/a%0d%0aSet-Cookie:x")]
    public void Rejects_anything_but_a_safe_same_site_path(string? raw) => Assert.Null(RedirectPaths.Parse(raw));

    [Theory]
    [InlineData("/", true)]
    [InlineData("/services", true)]
    [InlineData("/blog", true)]
    [InlineData("/pricing", true)]
    [InlineData("/agency/website/pages", true)]
    [InlineData("/admin", true)]
    [InlineData("/api/v1/public/site", true)]
    [InlineData("/t/abc", true)]
    [InlineData("/services/seo", false)]
    [InlineData("/services?category=seo", false)]
    [InlineData("/about-us", false)]
    [InlineData("/lp/nimbus/offer", false)]
    public void Built_in_and_app_addresses_are_protected(string key, bool expected) => Assert.Equal(expected, RedirectPaths.IsProtected(key));

    [Fact]
    public void Location_carries_query_parameters_and_is_always_a_valid_header()
    {
        Assert.Equal("/new?utm_source=x", RedirectPaths.Location("/new", "utm_source=x"));
        Assert.Equal("/services?category=seo&utm_source=x", RedirectPaths.Location("/services?category=seo", "utm_source=x"));
        Assert.Equal("/caf%C3%A9", RedirectPaths.Location("/café", ""));
    }

    [Fact]
    public void Targets_must_be_same_site_paths()
    {
        Assert.Equal("/new-page?ref=a", RedirectPaths.NormalizeTarget(" /New-Page/?ref=a#top "));
        Assert.Null(RedirectPaths.NormalizeTarget("https://evil.example/"));
        Assert.Null(RedirectPaths.NormalizeTarget("//evil.example"));
        Assert.Null(RedirectPaths.NormalizeTarget("/\\evil.example"));
        Assert.Null(RedirectPaths.NormalizeTarget("javascript:alert(1)"));
    }
}

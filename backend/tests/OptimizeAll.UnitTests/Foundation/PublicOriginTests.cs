using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OptimizeAll.Api.Common.Hosting;

namespace OptimizeAll.UnitTests.Foundation;

/// <summary>Precedence and host validation of <see cref="PublicOrigin"/> (the database-backed parts are integration-tested).</summary>
public sealed class PublicOriginTests
{
    private static PublicOrigin Create(HttpContext? context, string? appBaseUrl = null, params string[] publicHosts)
    {
        var values = new Dictionary<string, string?> { ["Email:AppBaseUrl"] = appBaseUrl };
        for (var i = 0; i < publicHosts.Length; i++) values[$"Hosting:PublicHosts:{i}"] = publicHosts[i];
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        // No database: remembering the request origin fails quietly (logged at debug level).
        var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new PublicOrigin(configuration, new HttpContextAccessor { HttpContext = context }, scopes, TimeProvider.System,
            NullLogger<PublicOrigin>.Instance);
    }

    private static DefaultHttpContext Request(string scheme, string host, bool trusted)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = HostString.FromUriComponent(host);
        if (trusted) context.Items[PublicOrigin.TrustedProxyItem] = true;
        return context;
    }

    [Fact]
    public void Site_url_then_configured_app_base_url_then_the_trusted_request_then_nothing()
    {
        var trusted = Request("https", "optimizeall-web.onrender.com", trusted: true);
        Assert.Equal("https://www.example.com", Create(trusted, "https://app.example.com/").Resolve("https://www.example.com/"));
        Assert.Equal("https://app.example.com", Create(trusted, "https://app.example.com/").Resolve(null));
        Assert.Equal("https://optimizeall-web.onrender.com", Create(trusted).Resolve(null));
        Assert.Equal("https://optimizeall-web.onrender.com", Create(trusted, "   ").Resolve(""));
        Assert.Equal("http://localhost:4173", Create(Request("http", "LocalHost:4173", trusted: true)).Resolve(null));
        Assert.Equal("https://example.com", Create(Request("https", "example.com:443", trusted: true)).Resolve(null));
        // An invalid configured value is ignored rather than put into links.
        Assert.Equal("https://optimizeall-web.onrender.com", Create(trusted, "not a url").Resolve(null));

        // Untrusted connections never contribute their Host; without anything else, links stay root-relative.
        Assert.Equal(string.Empty, Create(Request("https", "evil.example", trusted: false)).Resolve(null));
        Assert.Equal(string.Empty, Create(null).Resolve(null));
    }

    [Theory]
    [InlineData("evil.example/path")]
    [InlineData("user@evil.example")]
    [InlineData("evil.example.")]
    [InlineData("-evil.example")]
    [InlineData("evil_example.com")]
    [InlineData("[::1]")]
    public void Hosts_that_are_not_plain_host_names_are_refused(string host)
    {
        Assert.False(PublicOrigin.IsValidHost(host));
        Assert.Equal(string.Empty, Create(Request("https", host, trusted: true)).Resolve(null));
    }

    [Fact]
    public void Overlong_hosts_and_other_schemes_are_refused()
    {
        Assert.False(PublicOrigin.IsValidHost(new string('a', 250) + ".com"));
        Assert.Equal(string.Empty, Create(Request("ftp", "example.com", trusted: true)).Resolve(null));
        Assert.Null(PublicOrigin.NormalizeBaseUrl("https://user:pw@example.com"));
        Assert.Null(PublicOrigin.NormalizeBaseUrl("javascript:alert(1)"));
        Assert.Equal("https://example.com/app", PublicOrigin.NormalizeBaseUrl(" https://example.com/app/ "));
    }

    [Fact]
    public void Public_hosts_limit_which_request_hosts_are_used()
    {
        Assert.Equal("https://www.example.com",
            Create(Request("https", "www.example.com", true), null, "www.example.com").Resolve(null));
        Assert.Equal("https://shop.example.com",
            Create(Request("https", "shop.example.com", true), null, "*.example.com").Resolve(null));
        Assert.Equal(string.Empty, Create(Request("https", "evil.example", true), null, "www.example.com", "*.example.com").Resolve(null));
        Assert.Equal(string.Empty, Create(Request("https", "example.com.evil", true), null, "*.example.com").Resolve(null));
    }

    [Fact]
    public void Trusted_proxies_are_the_configured_proxies_and_networks_only()
    {
        var options = new ForwardedHeadersOptions();
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
        options.KnownProxies.Add(IPAddress.Parse("192.0.2.1"));
        Assert.True(PublicOrigin.IsTrustedProxy(IPAddress.Parse("10.214.3.4"), options));
        Assert.True(PublicOrigin.IsTrustedProxy(IPAddress.Parse("::ffff:10.1.2.3"), options));
        Assert.True(PublicOrigin.IsTrustedProxy(IPAddress.Parse("192.0.2.1"), options));
        Assert.False(PublicOrigin.IsTrustedProxy(IPAddress.Parse("203.0.113.10"), options));
        Assert.False(PublicOrigin.IsTrustedProxy(null, options));
    }
}

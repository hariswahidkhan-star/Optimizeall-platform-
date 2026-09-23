using System.Net;
using System.Text;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Seo.Controllers;
using OptimizeAll.Api.Modules.Seo.Http;
using OptimizeAll.Api.Modules.Seo.Ranking;
using OptimizeAll.Domain.Integrations;
using OptimizeAll.Domain.Seo;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace OptimizeAll.UnitTests.Seo;

public sealed class IpPolicyTests
{
    [Theory]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")] // cloud metadata
    [InlineData("100.100.100.200")] // Alibaba metadata (CGNAT)
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fd00:ec2::254")] // AWS metadata over IPv6
    [InlineData("fc00::1")]
    [InlineData("::ffff:10.0.0.1")] // IPv4-mapped private
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("64:ff9b::a00:1")] // NAT64 to 10.0.0.1
    [InlineData("2002:0a00:0001::1")] // 6to4 embedding 10.0.0.1
    [InlineData("2001:db8::1")]
    [InlineData("ff02::1")]
    public void Private_link_local_and_metadata_ranges_are_blocked(string ip)
    {
        Assert.True(IpPolicy.IsBlocked(IPAddress.Parse(ip), allowLoopback: false));
        Assert.True(IpPolicy.IsBlocked(IPAddress.Parse(ip), allowLoopback: true));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.0.1")]
    [InlineData("::1")]
    [InlineData("::ffff:127.0.0.1")]
    public void Loopback_is_blocked_unless_explicitly_allowed(string ip)
    {
        Assert.True(IpPolicy.IsBlocked(IPAddress.Parse(ip), allowLoopback: false));
        Assert.False(IpPolicy.IsBlocked(IPAddress.Parse(ip), allowLoopback: true));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    [InlineData("172.32.0.1")]
    [InlineData("2606:4700:4700::1111")]
    public void Public_addresses_are_allowed(string ip) => Assert.False(IpPolicy.IsBlocked(IPAddress.Parse(ip), allowLoopback: false));

    [Theory]
    [InlineData("ftp://example.com/x")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:pass@example.com/")]
    [InlineData("gopher://example.com")]
    public void Only_plain_http_urls_are_fetchable(string url) => Assert.NotNull(SafeHttpFetcher.ValidateUrl(new Uri(url)));
}

public sealed class AuditDiffTests
{
    private static SeoAuditIssue Issue(string rule, SeoSeverity severity, params string[] urls) =>
        new() { RuleKey = rule, Severity = severity, AffectedCount = urls.Length, AffectedUrls = urls.ToList() };

    [Fact]
    public void Reports_new_and_fixed_rule_url_pairs()
    {
        var before = new[]
        {
            Issue(SeoAuditRules.TitleMissing, SeoSeverity.Error, "https://a.test/1", "https://a.test/2"),
            Issue(SeoAuditRules.ImageAltMissing, SeoSeverity.Warning, "https://a.test/3"),
        };
        var after = new[]
        {
            Issue(SeoAuditRules.TitleMissing, SeoSeverity.Error, "https://a.test/2", "https://a.test/4"),
            Issue(SeoAuditRules.ThinContent, SeoSeverity.Warning, "https://a.test/5"),
        };
        var (added, fixedIssues) = AuditDiff.Compare(before, after);

        Assert.Equal(new[] { SeoAuditRules.TitleMissing, SeoAuditRules.ThinContent }, added.Select(a => a.Rule));
        Assert.Equal(new[] { "https://a.test/4" }, added[0].Urls);
        Assert.Equal(new[] { SeoAuditRules.TitleMissing, SeoAuditRules.ImageAltMissing }, fixedIssues.Select(f => f.Rule));
        Assert.Equal(new[] { "https://a.test/1" }, fixedIssues[0].Urls);
    }

    [Fact]
    public void Identical_audits_have_no_diff()
    {
        var issues = new[] { Issue(SeoAuditRules.Http4xx, SeoSeverity.Error, "https://a.test/x") };
        var (added, fixedIssues) = AuditDiff.Compare(issues, issues);
        Assert.Empty(added);
        Assert.Empty(fixedIssues);
    }
}

/// <summary>DataForSEO adapter with a fake HTTP handler: not configured / configured / errors.</summary>
public sealed class DataForSeoProviderTests
{
    private sealed class FakeVault(IntegrationCredentials? credentials) : ICredentialVault
    {
        public List<(Guid Id, IntegrationStatus Status)> Marked { get; } = new();
        public Task<IntegrationCredentials?> GetAsync(string provider, Guid? clientAccountId, CancellationToken ct = default) =>
            Task.FromResult(provider == "dataforseo" ? credentials : null);
        public string Protect(IReadOnlyDictionary<string, string> secrets) => throw new NotSupportedException();
        public IReadOnlyDictionary<string, string> Unprotect(string encrypted) => throw new NotSupportedException();
        public Task MarkStatusAsync(Guid connectionId, IntegrationStatus status, string? message, CancellationToken ct = default)
        {
            Marked.Add((connectionId, status));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }

    private static readonly IntegrationCredentials Configured = new(Guid.NewGuid(), "dataforseo", null,
        new Dictionary<string, string>(), new Dictionary<string, string> { ["login"] = "api@agency.test", ["password"] = "s3cret" }, IntegrationStatus.Connected);

    private static RankCheckRequest Request(params string[] keywords) => new(Guid.NewGuid(), "www.nimbus.test", "GB", "en", keywords);

    private static DataForSeoRankProvider Provider(FakeHandler handler, FakeVault vault) =>
        new(new HttpClient(handler), vault, new ConfigurationBuilder().Build(), NullLogger<DataForSeoRankProvider>.Instance);

    private const string SerpJson = """
        {"status_code":20000,"status_message":"Ok.","tasks":[{"status_code":20000,"status_message":"Ok.","result":[{"keyword":"home workout app","items":[
          {"type":"featured_snippet","rank_group":1,"domain":"blog.other.test","url":"https://blog.other.test/a"},
          {"type":"organic","rank_group":1,"domain":"www.competitor.test","url":"https://www.competitor.test/x"},
          {"type":"people_also_ask","rank_group":1},
          {"type":"organic","rank_group":2,"domain":"nimbus.test","url":"https://nimbus.test/workouts"},
          {"type":"organic","rank_group":3,"domain":"www.nimbus.test","url":"https://www.nimbus.test/other"}
        ]}]}]}
        """;

    [Fact]
    public async Task Not_configured_without_credentials_and_makes_no_request()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("must not be called"));
        var result = await Provider(handler, new FakeVault(null)).CheckAsync(Request("x"), CancellationToken.None);
        Assert.Equal(ProviderOutcome.NotConfigured, result.Outcome);
        Assert.Empty(handler.Requests);
        Assert.Contains("not configured", result.Message);
    }

    [Fact]
    public async Task Configured_provider_parses_position_features_and_competitors()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SerpJson, Encoding.UTF8, "application/json") });
        var result = await Provider(handler, new FakeVault(Configured)).CheckAsync(Request("home workout app"), CancellationToken.None);

        Assert.Equal(ProviderOutcome.Ok, result.Outcome);
        var serp = Assert.Single(result.Results);
        Assert.Equal(2, serp.Position);
        Assert.Equal("https://nimbus.test/workouts", serp.Url);
        Assert.Equal(new[] { "featured_snippet", "people_also_ask" }, serp.Features);
        Assert.Contains(serp.Organic, o => o.Domain == "competitor.test" && o.Position == 1);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal("https://api.dataforseo.com/v3/serp/google/organic/live/advanced", request.RequestUri!.ToString());
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal("api@agency.test:s3cret", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
        Assert.Contains("\"location_code\":2826", body);
        Assert.Contains("\"depth\":100", body);
    }

    [Fact]
    public async Task Unauthorized_marks_the_connection_as_error()
    {
        var vault = new FakeVault(Configured);
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var result = await Provider(handler, vault).CheckAsync(Request("x"), CancellationToken.None);
        Assert.Equal(ProviderOutcome.Error, result.Outcome);
        Assert.Equal((Configured.ConnectionId, IntegrationStatus.Error), Assert.Single(vault.Marked));
    }

    [Fact]
    public async Task Api_level_errors_and_malformed_bodies_are_errors()
    {
        var apiError = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"status_code\":40200,\"status_message\":\"Payment Required.\"}") });
        var result = await Provider(apiError, new FakeVault(Configured)).CheckAsync(Request("x"), CancellationToken.None);
        Assert.Equal(ProviderOutcome.Error, result.Outcome);
        Assert.Contains("40200", result.Message);

        var garbage = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>oops</html>") });
        Assert.Equal(ProviderOutcome.Error, (await Provider(garbage, new FakeVault(Configured)).CheckAsync(Request("x"), CancellationToken.None)).Outcome);

        var down = new FakeHandler(_ => throw new HttpRequestException("connection refused"));
        Assert.Equal(ProviderOutcome.Error, (await Provider(down, new FakeVault(Configured)).CheckAsync(Request("x"), CancellationToken.None)).Outcome);
    }

    [Fact]
    public void Not_ranking_when_the_site_is_absent_from_results()
    {
        var serp = DataForSeoRankProvider.Parse(SerpJson, "k", "absent.test", out var error);
        Assert.Null(error);
        Assert.Null(serp!.Position);
    }
}

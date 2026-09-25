using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Common.Hosting;
using OptimizeAll.Api.Common.Jobs;
using OptimizeAll.Api.Modules.Notifications;
using OptimizeAll.Domain.Identity;
using OptimizeAll.Domain.Learning;
using OptimizeAll.Domain.Notifications;
using OptimizeAll.Domain.Settings;
using OptimizeAll.IntegrationTests.Learning;
using OptimizeAll.IntegrationTests.Notifications;
using OptimizeAll.IntegrationTests.Seo;

namespace OptimizeAll.IntegrationTests.Infrastructure;

/// <summary>
/// API hosts on one database with NO Email:AppBaseUrl (as on Render when Email__AppBaseUrl is left empty), nginx's private
/// network (10.0.0.0/8) as the trusted proxy network, and the test remote-IP middleware (X-Test-Ip).
/// </summary>
public sealed class PublicOriginFixture : IAsyncLifetime
{
    public const string TrustedProxyIp = "10.20.30.40";
    public const string WebHost = "optimizeall-web.onrender.com";
    public const string WebOrigin = "https://" + WebHost;

    public ApiFactory Api { get; } = new();

    /// <summary>No Email:AppBaseUrl.</summary>
    public WebApplicationFactory<Program> Host { get; private set; } = null!;

    public WebApplicationFactory<Program> Derive(string? appBaseUrl) =>
        Api.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:AppBaseUrl"] = appBaseUrl ?? string.Empty,
                ["Hosting:TrustedNetworks:0"] = "10.0.0.0/8",
            }));
            b.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, TestRemoteIpStartupFilter>());
        });

    public async Task InitializeAsync()
    {
        await Api.InitializeAsync();
        Host = Derive(null);
        await Host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        await Api.DisposeAsync();
    }

    /// <summary>A request as nginx forwards it: from the proxy's address, with the visitor's scheme and host.</summary>
    public static void AsProxied(HttpClient client, string proxyIp = TrustedProxyIp, string proto = "https", string host = WebHost)
    {
        client.DefaultRequestHeaders.Add(TestRemoteIpStartupFilter.Header, proxyIp);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.7");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", proto);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-Host", host);
    }

    public static HttpClient Proxied(WebApplicationFactory<Program> host, string proxyIp = TrustedProxyIp, string proto = "https", string hostName = WebHost)
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        AsProxied(client, proxyIp, proto, hostName);
        return client;
    }

    public static async Task<HttpClient> ProxiedLoginAsync(WebApplicationFactory<Program> host, TestUser user)
    {
        var client = await host.LoginAsync(user);
        AsProxied(client);
        return client;
    }
}

/// <summary>
/// Public links without a configured public URL (IPublicOrigin, docs/RENDER.md): the site URL setting, then
/// Email:AppBaseUrl, then the origin of a request through a trusted proxy, then the remembered origin for jobs. A client
/// that is not a trusted proxy can never choose the host that goes into links.
/// </summary>
public sealed class PublicOriginTests(PublicOriginFixture f) : IClassFixture<PublicOriginFixture>
{
    private const string Slug = LearningHelpers.Slug;
    private const string BadgePath = "/api/v1/public/learning/courses/" + Slug + "/badge.svg";
    private static readonly HtmlParser Parser = new();

    private static async Task<string> CanonicalAsync(HttpClient client, string path)
    {
        var html = await (await client.GetAsync("/_document" + path)).Content.ReadAsStringAsync();
        return Parser.ParseDocument(html).QuerySelector("link[rel=canonical]")?.GetAttribute("href") ?? string.Empty;
    }

    private async Task<(TestUser Learner, Guid CertificateId)> IssueCertificateAsync()
    {
        var admin = await f.Host.LoginAsync(await f.Api.CreateUserAsync(new[] { Role.Admin }));
        var learner = await f.Api.CreateUserAsync();
        var courseId = await f.Api.WithDbAsync(db => db.Set<Course>().Where(c => c.Slug == Slug).Select(c => c.Id).SingleAsync());
        var issued = await (await admin.PostAsJsonAsync("/api/v1/admin/learning/certificates",
            new { userId = learner.Id, courseId, reason = "Completed the workshop", confirm = true })).ReadJsonAsync();
        return (learner, Guid.Parse(issued.GetProperty("id").GetString()!));
    }

    private async Task SetSiteUrlAsync(string? siteUrl)
    {
        var admin = await f.Host.LoginAsync(await f.Api.CreateUserAsync(new[] { Role.Admin }));
        var current = await (await admin.GetAsync("/api/v1/agency/website/settings")).ReadJsonAsync();
        var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(current.GetProperty("settings").GetRawText())!;
        var seo = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(settings["seo"].GetRawText())!
            .ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        seo["siteUrl"] = siteUrl;
        var next = settings.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        next["seo"] = seo;
        (await admin.PutAsJsonAsync("/api/v1/agency/website/settings",
            new { settings = next, concurrencyStamp = current.GetProperty("concurrencyStamp").GetGuid() })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Through_a_trusted_proxy_every_public_link_uses_the_visitors_https_origin()
    {
        var anon = PublicOriginFixture.Proxied(f.Host);
        const string web = PublicOriginFixture.WebOrigin;

        // Canonical URL of a website page and of an academy page.
        Assert.Equal(web + "/services/seo", await CanonicalAsync(anon, "/services/seo"));
        Assert.Equal($"{web}/learn/{Slug}", await CanonicalAsync(anon, $"/learn/{Slug}"));

        // Sitemap index and a child sitemap; robots.txt links the index.
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var index = XDocument.Parse(await anon.GetStringAsync("/sitemap.xml"));
        var files = index.Root!.Elements(ns + "sitemap").Select(s => s.Element(ns + "loc")!.Value).ToList();
        Assert.NotEmpty(files);
        Assert.All(files, url => Assert.StartsWith(web + "/sitemaps/", url));
        var learn = XDocument.Parse(await anon.GetStringAsync("/sitemaps/learn.xml"));
        var locs = learn.Descendants(ns + "loc").Select(l => l.Value).ToList();
        Assert.Contains($"{web}/learn/{Slug}", locs);
        Assert.All(locs, url => Assert.StartsWith(web + "/", url));
        Assert.Contains($"Sitemap: {web}/sitemap.xml", await anon.GetStringAsync("/robots.txt"));

        // llms.txt.
        var llms = await anon.GetStringAsync("/llms.txt");
        Assert.Contains($"({web}/services/seo.md)", llms);
        Assert.DoesNotContain("localhost", llms);

        // Course JSON-LD is absolute (it leaves the site); the badge image our own pages show is root-relative.
        var course = await (await anon.GetAsync($"/api/v1/public/learning/courses/{Slug}")).ReadJsonAsync();
        var ld = course.GetProperty("jsonLd").EnumerateArray().First(j => j.GetProperty("@type").GetString() == "Course");
        Assert.Equal($"{web}/learn/{Slug}", ld.GetProperty("url").GetString());
        Assert.Equal(web + BadgePath, ld.GetProperty("image").GetString());
        Assert.Equal(BadgePath, course.GetProperty("card").GetProperty("badgeImageUrl").GetString());
        Assert.Equal(BadgePath, course.GetProperty("badge").GetProperty("imageUrl").GetString());
        var catalog = await (await anon.GetAsync("/api/v1/public/learning/courses")).ReadJsonAsync();
        Assert.All(catalog.GetProperty("items").EnumerateArray(),
            c => Assert.StartsWith("/api/v1/public/learning/courses/", c.GetProperty("badgeImageUrl").GetString()));

        // Certificate: verification link, Open Badges and LinkedIn absolute on the web origin; images and PDF root-relative.
        var (learner, certificateId) = await IssueCertificateAsync();
        var client = await PublicOriginFixture.ProxiedLoginAsync(f.Host, learner);
        var mine = await (await client.GetAsync($"/api/v1/me/learning/certificates/{certificateId}")).ReadJsonAsync();
        var links = mine.GetProperty("links");
        var verify = $"{web}/verify/certificates/{certificateId}";
        Assert.Equal(verify, links.GetProperty("verificationUrl").GetString());
        var addToProfile = new Uri(links.GetProperty("linkedInAddToProfileUrl").GetString()!);
        Assert.Equal(verify, System.Web.HttpUtility.ParseQueryString(addToProfile.Query)["certUrl"]);
        Assert.Equal("https://www.linkedin.com/sharing/share-offsite/?url=" + Uri.EscapeDataString(verify), links.GetProperty("linkedInShareUrl").GetString());
        Assert.Equal($"{web}/api/v1/public/learning/openbadges/assertions/{certificateId}", links.GetProperty("openBadgeAssertionUrl").GetString());
        Assert.Equal(BadgePath, links.GetProperty("badgeImageUrl").GetString());
        Assert.Equal($"/api/v1/public/learning/certificates/{certificateId}/certificate.svg", links.GetProperty("imageUrl").GetString());
        Assert.Equal($"/api/v1/public/learning/certificates/{certificateId}/certificate.pdf", links.GetProperty("pdfUrl").GetString());

        // /me/learning: every badge image is root-relative (always allowed by the web app's CSP img-src 'self').
        var dashboard = await (await client.GetAsync("/api/v1/me/learning")).ReadJsonAsync();
        var certificate = dashboard.GetProperty("certificates").EnumerateArray().Single();
        Assert.Equal(BadgePath, certificate.GetProperty("links").GetProperty("badgeImageUrl").GetString());
        Assert.All(dashboard.GetProperty("recommended").EnumerateArray(), c => Assert.StartsWith("/api/", c.GetProperty("badgeImageUrl").GetString()));

        // The Open Badges assertion (read by other sites) stays absolute; so does the verification page's canonical.
        var assertion = await (await anon.GetAsync($"/api/v1/public/learning/openbadges/assertions/{certificateId}")).ReadJsonAsync();
        Assert.Equal(web + BadgePath, assertion.GetProperty("image").GetString());
        Assert.Equal(verify, await CanonicalAsync(anon, $"/verify/certificates/{certificateId}"));
    }

    [Fact]
    public async Task A_forwarded_host_from_an_untrusted_address_or_an_invalid_host_is_never_used()
    {
        // Not from the trusted proxy network: its X-Forwarded-Host (and Host) are ignored.
        var spoofed = PublicOriginFixture.Proxied(f.Host, proxyIp: "203.0.113.10", hostName: "evil.example");
        var canonical = await CanonicalAsync(spoofed, "/services/seo");
        Assert.EndsWith("/services/seo", canonical);
        Assert.DoesNotContain("evil.example", canonical);
        Assert.DoesNotContain("localhost", canonical);
        Assert.DoesNotContain("evil.example", await spoofed.GetStringAsync("/sitemap.xml"));

        // From the trusted proxy, but not a plain host name: ignored as well.
        foreach (var bad in new[] { "user@evil.example", "evil.example/path", "evil.example:99999", new string('a', 300) + ".evil.example" })
        {
            var client = PublicOriginFixture.Proxied(f.Host, hostName: bad);
            Assert.DoesNotContain("evil.example", await CanonicalAsync(client, "/services/seo"));
        }

        Assert.False(PublicOrigin.IsValidHost("evil.example/x"));
        Assert.False(PublicOrigin.IsValidHost("user@evil.example"));
        Assert.False(PublicOrigin.IsValidHost("-evil.example"));
        Assert.False(PublicOrigin.IsValidHost("evil.example."));
        Assert.True(PublicOrigin.IsValidHost(PublicOriginFixture.WebHost));
    }

    [Fact]
    public async Task The_admin_site_url_wins_over_the_request_and_a_configured_app_base_url_wins_over_the_request()
    {
        var anon = PublicOriginFixture.Proxied(f.Host);
        await SetSiteUrlAsync("https://www.optimizeall.test");
        try
        {
            Assert.Equal("https://www.optimizeall.test/services/seo", await CanonicalAsync(anon, "/services/seo"));
            Assert.Contains("<loc>https://www.optimizeall.test/sitemaps/", await anon.GetStringAsync("/sitemap.xml"));
            var course = await (await anon.GetAsync($"/api/v1/public/learning/courses/{Slug}")).ReadJsonAsync();
            var ld = course.GetProperty("jsonLd").EnumerateArray().First(j => j.GetProperty("@type").GetString() == "Course");
            Assert.Equal($"https://www.optimizeall.test/learn/{Slug}", ld.GetProperty("url").GetString());
        }
        finally
        {
            await SetSiteUrlAsync(null);
        }
        Assert.Equal(PublicOriginFixture.WebOrigin + "/services/seo", await CanonicalAsync(anon, "/services/seo"));

        await using var configured = f.Derive("https://app.configured.test/");
        var client = PublicOriginFixture.Proxied(configured);
        Assert.Equal("https://app.configured.test/services/seo", await CanonicalAsync(client, "/services/seo"));
        Assert.Contains("(https://app.configured.test/services/seo.md)", await client.GetStringAsync("/llms.txt"));
    }

    [Fact]
    public async Task Jobs_without_a_request_use_the_remembered_public_origin()
    {
        // A request through the trusted proxy is remembered (at most one write a day) ...
        await PublicOriginFixture.Proxied(f.Host).GetStringAsync("/sitemap.xml");
        string? remembered = null;
        for (var i = 0; i < 100 && remembered is null; i++)
        {
            remembered = await f.Api.WithDbAsync(db => db.Set<SystemSetting>().AsNoTracking()
                .Where(s => s.Key == PublicOrigin.SettingKey).Select(s => s.ValueJson).FirstOrDefaultAsync());
            if (remembered is null) await Task.Delay(50);
        }
        Assert.Equal(JsonSerializer.Serialize(PublicOriginFixture.WebOrigin), remembered);

        // ... and a host that has not served any request (a restarted API running a job) builds its links from it.
        await using var restarted = f.Derive(null);
        await restarted.StartAsync();
        Assert.Equal(PublicOriginFixture.WebOrigin, await restarted.Services.GetRequiredService<IPublicOrigin>().GetAsync());

        var user = await f.Api.CreateUserAsync();
        await restarted.StageAsync(user.Id, NotificationTypes.SubmissionDecision, "Approved", "Your post was approved.", "/app/submissions/7",
            NotificationChannel.InApp, NotificationChannel.Email);
        f.Api.Clock.Advance(TimeSpan.FromMilliseconds(1));
        await restarted.Services.GetRequiredService<JobRunner>().RunAsync<NotificationDispatchJob>();
        var eml = Directory.GetFiles(f.Api.MailDirectory, "*.eml").Select(File.ReadAllText).Single(t => t.Contains(user.Email));
        Assert.Contains(PublicOriginFixture.WebOrigin + "/app/submissions/7", eml);
        Assert.DoesNotContain("localhost", eml);
    }
}

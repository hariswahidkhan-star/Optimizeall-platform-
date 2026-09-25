using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Website;

namespace OptimizeAll.IntegrationTests.Learning;

/// <summary>
/// The academy as crawlers see it (technical SEO, docs/SEO_CRO.md § 9 and docs/LEARNING.md): the sitemap index lists the
/// <c>learn</c> sitemap; every URL in it (/learn, each published course and each lesson of every course pack) is a
/// server-rendered 200 page with a title within 60 characters, a description within 155, a self canonical, one h1 and
/// valid JSON-LD; llms.txt lists the academy; unknown courses and lessons are 404.
/// </summary>
public sealed class AcademySeoTests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly HtmlParser Parser = new();

    [Fact]
    public async Task Every_academy_url_in_the_sitemap_is_a_complete_indexable_page_within_the_length_limits()
    {
        var anon = api.Anonymous();
        var index = await anon.GetStringAsync("/sitemap.xml");
        Assert.Contains("<loc>http://app.test/sitemaps/learn.xml</loc>", index);
        var urls = Regex.Matches(await anon.GetStringAsync("/sitemaps/learn.xml"), "<loc>([^<]+)</loc>")
            .Select(m => new Uri(m.Groups[1].Value).AbsolutePath).ToList();
        Assert.Contains("/learn", urls);
        Assert.Contains(urls, u => u.Count(c => c == '/') == 2); // a course
        Assert.Contains(urls, u => u.Count(c => c == '/') == 3); // a lesson

        var problems = new List<string>();
        foreach (var path in urls)
        {
            var response = await anon.GetAsync("/_document" + path);
            if (response.StatusCode != HttpStatusCode.OK) { problems.Add($"{path}: HTTP {(int)response.StatusCode}"); continue; }
            var doc = Parser.ParseDocument(await response.Content.ReadAsStringAsync());
            var title = doc.Title ?? string.Empty;
            var description = doc.QuerySelector("meta[name='description']")?.GetAttribute("content") ?? string.Empty;
            var canonical = doc.QuerySelector("link[rel='canonical']")?.GetAttribute("href");
            if (title.Length is 0 or > 60) problems.Add($"{path}: title has {title.Length} characters: {title}");
            if (description.Length is 0 or > 155) problems.Add($"{path}: description has {description.Length} characters");
            if (canonical != "http://app.test" + path) problems.Add($"{path}: canonical {canonical}");
            if (doc.QuerySelectorAll("#oa-ssr h1").Length != 1) problems.Add($"{path}: {doc.QuerySelectorAll("#oa-ssr h1").Length} h1 headings");
            if (response.Headers.TryGetValues("X-Robots-Tag", out var robots)) problems.Add($"{path}: X-Robots-Tag {string.Join(",", robots)}");
            foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
            {
                try { TechnicalSeoTests.AssertValidJsonLd(JsonDocument.Parse(script.TextContent).RootElement); }
                catch (Exception ex) { problems.Add($"{path}: JSON-LD {ex.Message.ReplaceLineEndings(" ")}"); }
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        // The partner blog posts (W1) link to these courses: each is a live academy page.
        foreach (var course in new[]
                 {
                     "project-controls-with-ai", "project-finance-and-financial-modelling", "project-management-leadership-with-ai",
                     "professional-certification-exam-success", "leadership-and-communication", "ai-for-data-analysis-and-decision-making",
                     "prompt-engineering-foundations", "advanced-prompt-engineering", "mastering-claude", "mastering-chatgpt",
                 })
            Assert.Contains("/learn/" + course, urls);
        var postLinks = OptimizeAll.Api.Modules.Website.Seed.PartnerPostLibrary.All
            .SelectMany(p => Regex.Matches(p.Body, @"\]\((/learn/[a-z0-9-]+)\)").Select(m => m.Groups[1].Value)).Distinct().ToList();
        Assert.NotEmpty(postLinks);
        Assert.All(postLinks, link => Assert.Contains(link, urls));

        var llms = await anon.GetStringAsync("/llms.txt");
        Assert.Contains("## Academy (free courses)", llms);
        Assert.Contains("/learn.md", llms);

        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/_document/learn/no-such-course")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/_document/learn/no-such-course/no-such-lesson")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/_document/verify/certificates/not-a-guid")).StatusCode);
    }
}

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DependencyInjection;
using OptimizeAll.Api.Modules.Learning;
using OptimizeAll.Domain.Learning;
using OptimizeAll.IntegrationTests.Auth;
using OptimizeAll.IntegrationTests.Infrastructure;
using OptimizeAll.IntegrationTests.Website;

namespace OptimizeAll.IntegrationTests.Learning;

/// <summary>
/// Course pack v2 end to end (lecture scripts, lastReviewed, tools) and learning paths: the API, the signed-in path
/// progress, the direct "enrol and start" flow (idempotent enrol → first unfinished lesson), the sitemap and the
/// server-rendered pages (crawlable lecture transcript, VideoObject only for a produced lecture, ItemList of Course).
/// </summary>
public sealed class LearningV2Tests(ApiFactory api) : IClassFixture<ApiFactory>
{
    private static readonly HtmlParser Parser = new();

    private static string Words(int n, string word) => string.Join(' ', Enumerable.Repeat(word, n));

    /// <summary>The sample pack as a v2 pack under a new slug; the first lesson's lecture is produced (has a video).</summary>
    private static CoursePack V2Pack(string slug)
    {
        var p = LearningHelpers.Pack.Clone();
        p.Slug = slug;
        p.LastReviewed = "2026-09";
        p.Tools = new() { "Claude", "n8n" };
        var first = true;
        foreach (var (_, lesson) in p.AllLessons)
        {
            var missing = 750 - CoursePackValidator.WordCount(lesson.Body);
            if (missing > 0) lesson.Body += "\n\n" + Words(missing, "detail");
            lesson.Lecture = new PackLecture
            {
                TargetMinutes = 7,
                Scenes = Enumerable.Range(1, 6).Select(i => new PackScene
                {
                    Narration = (i == 1 ? "Welcome to the lecture transcriptmarker. " : string.Empty) + Words(140, "spoken"),
                    OnScreen = $"Chapter {i} heading\n• First point\n• Second point",
                    Visual = "Animated diagram, then a screen recording.",
                    Seconds = 60,
                }).ToList(),
            };
            if (first)
            {
                // Lectures are hosted on YouTube (captions: an optional VTT we host).
                lesson.Lecture.Src = "https://youtu.be/dQw4w9WgXcQ";
                lesson.Lecture.Captions = "https://cdn.example.com/lecture.vtt";
                lesson.Lecture.PublishedAt = "2026-09-20";
                first = false;
            }
        }
        Assert.Empty(CoursePackValidator.Validate(p));
        return p;
    }

    private async Task UpsertAsync(CoursePack pack)
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OptimizeAll.Infrastructure.Persistence.AppDbContext>();
        var changes = await scope.ServiceProvider.GetRequiredService<LearningCatalogSeeder>()
            .UpsertAsync(db, new[] { new PackFile(pack.Slug + ".json", pack.ToJson(), pack, null) }, CancellationToken.None);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task A_v2_pack_serves_lectures_facts_and_crawlable_transcripts()
    {
        var pack = V2Pack("v2-lecture-course");
        await UpsertAsync(pack);
        var anon = api.Anonymous();

        var course = await (await anon.GetAsync("/api/v1/public/learning/courses/v2-lecture-course")).ReadJsonAsync();
        Assert.Equal("2026-09", course.GetProperty("lastReviewed").GetString());
        Assert.Equal(new[] { "Claude", "n8n" }, course.GetProperty("tools").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(7 * pack.LessonCount, course.GetProperty("lectureMinutes").GetInt32());
        Assert.Equal(pack.LessonCount, course.GetProperty("lectureCount").GetInt32());
        var firstSummary = course.GetProperty("modules")[0].GetProperty("lessons")[0];
        Assert.True(firstSummary.GetProperty("hasLecture").GetBoolean());
        Assert.True(firstSummary.GetProperty("hasVideo").GetBoolean());
        var ld = course.GetProperty("jsonLd")[0];
        Assert.Equal("2026-09-01", ld.GetProperty("dateModified").GetString() is { } dm && string.CompareOrdinal(dm, "2026-09-01") >= 0 ? "2026-09-01" : "bad");
        Assert.Contains("n8n", ld.GetProperty("teaches").EnumerateArray().Select(t => t.GetString()));

        var lessons = pack.AllLessons.Select(x => x.Lesson.Slug).ToList();
        var produced = await (await anon.GetAsync($"/api/v1/public/learning/courses/v2-lecture-course/lessons/{lessons[0]}")).ReadJsonAsync();
        var lecture = produced.GetProperty("lecture");
        Assert.True(lecture.GetProperty("produced").GetBoolean());
        Assert.Equal("dQw4w9WgXcQ", lecture.GetProperty("youTubeId").GetString());
        Assert.Equal("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", lecture.GetProperty("embedUrl").GetString());
        Assert.Equal("2026-09-20", lecture.GetProperty("publishedAt").GetString());
        var videoLd = produced.GetProperty("jsonLd").EnumerateArray().Single(x => x.GetProperty("@type").ValueKind == JsonValueKind.String && x.GetProperty("@type").GetString() == "VideoObject");
        Assert.Equal("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", videoLd.GetProperty("embedUrl").GetString());
        Assert.Equal(6, lecture.GetProperty("chapters").GetArrayLength());
        Assert.Equal("Chapter 2 heading", lecture.GetProperty("chapters")[1].GetProperty("title").GetString());
        Assert.Equal(60, lecture.GetProperty("chapters")[1].GetProperty("startSeconds").GetInt32());
        Assert.Contains(produced.GetProperty("jsonLd").EnumerateArray(), x => x.GetProperty("@type").ValueKind == JsonValueKind.String && x.GetProperty("@type").GetString() == "VideoObject");

        var soon = await (await anon.GetAsync($"/api/v1/public/learning/courses/v2-lecture-course/lessons/{lessons[1]}")).ReadJsonAsync();
        Assert.False(soon.GetProperty("lecture").GetProperty("produced").GetBoolean());
        Assert.DoesNotContain(soon.GetProperty("jsonLd").EnumerateArray(), x => x.GetProperty("@type").ValueKind == JsonValueKind.String && x.GetProperty("@type").GetString() == "VideoObject");

        // Server-rendered: the transcript is crawlable text, VideoObject only on the produced lecture's page, valid JSON-LD.
        foreach (var (lessonSlug, expectVideo) in new[] { (lessons[0], true), (lessons[1], false) })
        {
            var html = await anon.GetStringAsync($"/_document/learn/v2-lecture-course/{lessonSlug}");
            var doc = Parser.ParseDocument(html);
            var ssr = doc.QuerySelector("#oa-ssr")!.TextContent;
            Assert.Contains("transcriptmarker", ssr);
            Assert.Contains("Lecture transcript", ssr);
            Assert.Contains("Chapter 3 heading", ssr);
            var types = doc.QuerySelectorAll("script[type='application/ld+json']").Select(s => JsonDocument.Parse(s.TextContent).RootElement).ToList();
            foreach (var t in types) TechnicalSeoTests.AssertValidJsonLd(t);
            Assert.Equal(expectVideo, html.Contains("\"VideoObject\"", StringComparison.Ordinal));
            Assert.Contains("\"BreadcrumbList\"", html);
        }
        // The produced lecture's page embeds the privacy-enhanced player; the video sitemap lists it (player_loc).
        var producedHtml = await anon.GetStringAsync($"/_document/learn/v2-lecture-course/{lessons[0]}");
        Assert.Contains("<iframe src=\"https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ\"", producedHtml);
        var index = await anon.GetStringAsync("/sitemap.xml");
        var videoMaps = Regex.Matches(index, "<loc>http://app.test(/sitemaps/videos[^<]*)</loc>").Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(videoMaps);
        var videoXml = string.Concat(await Task.WhenAll(videoMaps.Select(m => anon.GetStringAsync(m))));
        Assert.Contains($"/learn/v2-lecture-course/{lessons[0]}</loc>", videoXml);
        Assert.Contains("<video:player_loc>https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ</video:player_loc>", videoXml);
        Assert.Contains("<video:thumbnail_loc>https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg</video:thumbnail_loc>", videoXml);
        Assert.DoesNotContain($"/learn/v2-lecture-course/{lessons[1]}</loc>", videoXml);

        var coursePage = await anon.GetStringAsync("/_document/learn/v2-lecture-course");
        Assert.Contains("Updated", coursePage);
        Assert.Contains("Sep 2026", coursePage);
        Assert.Contains("n8n", coursePage);
    }

    [Fact]
    public async Task Learning_paths_list_detail_and_seo()
    {
        var anon = api.Anonymous();
        var index = await (await anon.GetAsync("/api/v1/public/learning/paths")).ReadJsonAsync();
        var paths = index.GetProperty("paths").EnumerateArray().ToList();
        Assert.InRange(paths.Count, 6, 8);
        Assert.All(paths, p => Assert.True(p.GetProperty("courseCount").GetInt32() >= 3));

        var engineer = await (await anon.GetAsync("/api/v1/public/learning/paths/ai-engineer")).ReadJsonAsync();
        var slugs = engineer.GetProperty("courses").EnumerateArray().Select(c => c.GetProperty("course").GetProperty("slug").GetString()!).ToList();
        var defined = LearningPathLibrary.Find("ai-engineer")!.Courses!;
        // Only published courses, in path order (planned courses that have not landed yet are skipped).
        Assert.Equal(defined.Where(slugs.Contains), slugs);
        Assert.Contains("prompt-engineering-foundations", slugs);
        Assert.Equal(Enumerable.Range(1, slugs.Count), engineer.GetProperty("courses").EnumerateArray().Select(c => c.GetProperty("position").GetInt32()));
        var jsonLd = engineer.GetProperty("jsonLd");
        Assert.Equal("ItemList", jsonLd[0].GetProperty("@type").GetString());
        Assert.Equal(slugs.Count, jsonLd[0].GetProperty("itemListElement").GetArrayLength());
        Assert.Equal("Course", jsonLd[0].GetProperty("itemListElement")[0].GetProperty("item").GetProperty("@type").GetString());
        Assert.Contains(jsonLd.EnumerateArray(), x => x.GetProperty("@type").GetString() == "EducationalOccupationalCredential");

        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/api/v1/public/learning/paths/no-such-path")).StatusCode);

        // The small, cacheable academy summary for the marketing pages and the header.
        var summaryResponse = await anon.GetAsync("/api/v1/public/learning/summary");
        Assert.Equal("public, max-age=300", summaryResponse.Headers.CacheControl?.ToString());
        var summary = await summaryResponse.ReadJsonAsync();
        var catalog = await (await anon.GetAsync("/api/v1/public/learning/courses?pageSize=100")).ReadJsonAsync();
        Assert.Equal(catalog.GetProperty("total").GetInt32(), summary.GetProperty("courseCount").GetInt32());
        Assert.Equal(catalog.GetProperty("items").EnumerateArray().Sum(c => c.GetProperty("lessonCount").GetInt32()), summary.GetProperty("lessonCount").GetInt32());
        Assert.Equal(paths.Count, summary.GetProperty("pathCount").GetInt32());
        Assert.InRange(summary.GetProperty("highlights").GetArrayLength(), 1, 8);
        Assert.True(summary.GetProperty("categories").GetArrayLength() > 1);
        Assert.InRange(summary.GetProperty("skills").GetArrayLength(), 1, 36);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.GetAsync("/_document/learn/paths/no-such-path")).StatusCode);

        // Sitemap + server-rendered pages.
        var urls = Regex.Matches(await anon.GetStringAsync("/sitemaps/learn.xml"), "<loc>([^<]+)</loc>")
            .Select(m => new Uri(m.Groups[1].Value).AbsolutePath).ToList();
        Assert.Contains("/learn/paths", urls);
        Assert.Contains("/learn/paths/ai-engineer", urls);
        var html = await anon.GetStringAsync("/_document/learn/paths/ai-engineer");
        var doc = Parser.ParseDocument(html);
        Assert.Equal("http://app.test/learn/paths/ai-engineer", doc.QuerySelector("link[rel='canonical']")?.GetAttribute("href"));
        Assert.Contains("AI Engineer learning path", doc.QuerySelector("#oa-ssr h1")!.TextContent);
        Assert.Contains("\"ItemList\"", html);
        var indexHtml = await anon.GetStringAsync("/_document/learn/paths");
        Assert.Contains("Learning paths", Parser.ParseDocument(indexHtml).QuerySelector("#oa-ssr h1")!.TextContent);
        Assert.Contains("/learn/paths/seo-and-ai-search", indexHtml);
        Assert.Contains("/learn/paths/ai-engineer", await anon.GetStringAsync("/_document/learn"));
    }

    [Fact]
    public async Task Path_progress_and_direct_enrol_start_the_first_unfinished_lesson()
    {
        var (_, client) = await api.CreateClientAsync();
        const string course = "prompt-engineering-foundations";

        var before = await (await client.GetAsync("/api/v1/me/learning/paths/ai-engineer")).ReadJsonAsync();
        Assert.False(before.GetProperty("progress").GetProperty("started").GetBoolean());
        Assert.Equal(course, before.GetProperty("progress").GetProperty("nextCourseSlug").GetString());

        // "Enrol for free — start learning": enrol (201), again (200, idempotent); resume = the first lesson.
        var first = await client.PostAsync($"/api/v1/me/learning/courses/{course}/enrol", null);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var again = await client.PostAsync($"/api/v1/me/learning/courses/{course}/enrol", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var enrolled = await again.ReadJsonAsync();
        var lessons = enrolled.GetProperty("course").GetProperty("modules").EnumerateArray()
            .SelectMany(m => m.GetProperty("lessons").EnumerateArray().Select(l => l.GetProperty("slug").GetString()!)).ToList();
        Assert.Equal(lessons[0], enrolled.GetProperty("progress").GetProperty("resumeLessonSlug").GetString());

        // After completing lesson 1 the resume point is lesson 2 and the path shows progress.
        (await client.PostAsync($"/api/v1/me/learning/courses/{course}/lessons/{lessons[0]}/complete", null)).EnsureSuccessStatusCode();
        var mine = await (await client.GetAsync($"/api/v1/me/learning/courses/{course}")).ReadJsonAsync();
        Assert.Equal(lessons[1], mine.GetProperty("progress").GetProperty("resumeLessonSlug").GetString());

        var after = await (await client.GetAsync("/api/v1/me/learning/paths/ai-engineer")).ReadJsonAsync();
        var progress = after.GetProperty("progress");
        Assert.True(progress.GetProperty("started").GetBoolean());
        var row = progress.GetProperty("courses").EnumerateArray().Single(c => c.GetProperty("slug").GetString() == course);
        Assert.True(row.GetProperty("enrolled").GetBoolean());
        Assert.InRange(row.GetProperty("progressPercent").GetInt32(), 1, 99);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("certificateId").ValueKind);

        var list = await (await client.GetAsync("/api/v1/me/learning/paths")).ReadJsonAsync();
        Assert.Contains(list.EnumerateArray(), p => p.GetProperty("card").GetProperty("slug").GetString() == "ai-engineer" &&
                                                    p.GetProperty("progress").GetProperty("started").GetBoolean());
        // Signed-out callers cannot read progress.
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Anonymous().GetAsync("/api/v1/me/learning/paths")).StatusCode);
    }
}

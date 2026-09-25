using OptimizeAll.Api.Modules.Learning;
using OptimizeAll.Domain.Learning;

namespace OptimizeAll.UnitTests.Learning;

/// <summary>
/// Every course pack in Modules/Learning/Catalog (compiled into the API) is checked against the full contract in CI:
/// valid JSON with no unknown properties, every schema/count/word-range rule (<see cref="PackValidationMode.Strict"/>),
/// correct-index bounds, unique ids, pool ≥ 1.5 × questionCount covering every module — plus the catalog-wide rules
/// (file name = slug, unique slugs, prerequisites that exist).
/// </summary>
public sealed class CoursePackTests
{
    public static IEnumerable<object[]> Packs() => CoursePackLibrary.All.Select(f => new object[] { f.FileName });

    private static PackFile File(string name) => CoursePackLibrary.All.Single(f => f.FileName == name);

    [Fact]
    public void The_catalog_contains_the_sample_pack()
    {
        Assert.Contains(CoursePackLibrary.All, f => f.FileName == "platform-getting-started.json");
    }

    [Theory]
    [MemberData(nameof(Packs))]
    public void Every_pack_is_valid_against_the_contract(string fileName)
    {
        var file = File(fileName);
        Assert.True(file.Pack is not null, $"{fileName}: {file.ParseError}");
        var issues = CoursePackValidator.Validate(file.Pack!, PackValidationMode.Strict);
        Assert.True(issues.Count == 0, $"{fileName}:\n" + string.Join('\n', issues));
        Assert.Equal(file.Pack!.Slug + ".json", fileName);
    }

    [Fact]
    public void Slugs_are_unique_and_prerequisites_exist_in_the_catalog()
    {
        var packs = CoursePackLibrary.All.Where(f => f.Pack is not null).Select(f => f.Pack!).ToList();
        var duplicates = packs.GroupBy(p => p.Slug).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(duplicates.Count == 0, "Duplicate slugs: " + string.Join(", ", duplicates));
        var slugs = packs.Select(p => p.Slug).ToHashSet();
        var missing = packs.SelectMany(p => (p.Prerequisites ?? new()).Where(r => !slugs.Contains(r)).Select(r => $"{p.Slug} → {r}")).ToList();
        Assert.True(missing.Count == 0, "Unknown prerequisites: " + string.Join(", ", missing));
    }

    [Theory]
    [MemberData(nameof(Packs))]
    public void Every_pack_is_upserted_losslessly(string fileName)
    {
        // The stored document (normalized JSON) parses back to the same pack.
        var pack = File(fileName).Pack!;
        var roundTrip = CoursePack.Parse(pack.ToJson()).Pack!;
        Assert.Equal(pack.ToJson(), roundTrip.ToJson());
    }

    // ---------------------------------------------------------------- the validator itself

    private static CoursePack Sample() => File("platform-getting-started.json").Pack!.Clone();

    private static void AssertIssue(CoursePack pack, string path, PackValidationMode mode = PackValidationMode.Strict) =>
        Assert.Contains(CoursePackValidator.Validate(pack, mode), i => i.Path == path);

    [Fact]
    public void Unknown_properties_and_bad_json_are_rejected()
    {
        Assert.Null(CoursePack.Parse("{\"slug\":\"x\",\"tittle\":\"typo\"}").Pack);
        Assert.Null(CoursePack.Parse("{not json").Pack);
        Assert.NotNull(CoursePack.Parse("{\"slug\":\"x\",\"tittle\":\"typo\"}").Error);
    }

    [Fact]
    public void Structural_rules()
    {
        var p = Sample(); p.Slug = "Not A Slug"; AssertIssue(p, "slug");
        p = Sample(); p.Category = "cooking"; AssertIssue(p, "category");
        p = Sample(); p.Category = "seo"; Assert.Empty(CoursePackValidator.Validate(p));
        p = Sample(); p.Level = "expert"; AssertIssue(p, "level");
        p = Sample(); p.Title = new string('x', 81); AssertIssue(p, "title");
        p = Sample(); p.Outcomes = new() { "one" }; AssertIssue(p, "outcomes");
        p = Sample(); p.Skills = new() { "a", "b" }; AssertIssue(p, "skills");
        p = Sample(); p.Badge!.Name = new string('b', 61); AssertIssue(p, "badge.name");
        p = Sample(); p.PassingScore = 30; AssertIssue(p, "passingScore");
        p = Sample(); p.Prerequisites = new() { p.Slug }; AssertIssue(p, "prerequisites[0]");
        p = Sample(); p.Modules![1].Slug = p.Modules[0].Slug; AssertIssue(p, "modules[1].slug");
        p = Sample(); p.Modules![1].Lessons![0].Slug = p.Modules[0].Lessons![0].Slug; AssertIssue(p, "modules[1].lessons[0].slug");
    }

    [Fact]
    public void Lesson_rules()
    {
        var p = Sample(); p.Modules![0].Lessons![0].Body = "## Top heading\n\n" + p.Modules[0].Lessons![0].Body; AssertIssue(p, "modules[0].lessons[0].body");
        p = Sample(); p.Modules![0].Lessons![0].Body += "\n\n<div>html</div>"; AssertIssue(p, "modules[0].lessons[0].body");
        p = Sample(); p.Modules![0].Lessons![0].Body += "\n\n![x](https://example.com/a.png)"; AssertIssue(p, "modules[0].lessons[0].body");
        // HTML inside code is fine (technical SEO lessons show tags).
        p = Sample(); p.Modules![0].Lessons![0].Body += "\n\nUse `<link rel=\"canonical\">`.\n\n```html\n<title>Example</title>\n```";
        Assert.Empty(CoursePackValidator.Validate(p));
        // "#" lines inside fenced code are not headings (robots.txt and shell comments, Markdown samples).
        p = Sample(); p.Modules![0].Lessons![0].Body += "\n\n```text\n# robots.txt for https://www.example.com\nUser-agent: *\n```\n\n~~~markdown\n## Services\n~~~";
        Assert.Empty(CoursePackValidator.Validate(p));
        p = Sample(); p.Modules![0].Lessons![0].Body += "\n\n```text\ncode\n```\n\n# A real top heading after code";
        AssertIssue(p, "modules[0].lessons[0].body");
        p = Sample(); p.Modules![0].Lessons![0].Body = "Too short."; AssertIssue(p, "modules[0].lessons[0].body");
        Assert.DoesNotContain(CoursePackValidator.Validate(p, PackValidationMode.Authoring), i => i.Path == "modules[0].lessons[0].body");
        p = Sample(); p.Modules![0].Lessons![1].Video = null; AssertIssue(p, "modules[0].lessons[1].video");
        p = Sample(); p.Modules![0].Lessons![0].Video = new PackVideo { Script = "x" }; AssertIssue(p, "modules[0].lessons[0].video");
        p = Sample(); p.Modules![0].Lessons![1].Video!.Src = "http://insecure.example.com/v.mp4"; AssertIssue(p, "modules[0].lessons[1].video.src");
        p = Sample(); p.Modules![0].Lessons![1].Video!.Src = "/api/v1/files/0f8fad5b-d9cb-469f-a165-70867728950e"; Assert.Empty(CoursePackValidator.Validate(p));
        p = Sample(); p.Modules![0].Lessons![0].KeyTakeaways = new() { "one" }; AssertIssue(p, "modules[0].lessons[0].keyTakeaways");
        // Exact duplicate options are refused; options that differ only in capitalisation are different answers.
        p = Sample(); var dup = p.Modules![0].Lessons![0].KnowledgeCheck![0]; dup.Options![1] = dup.Options[0];
        AssertIssue(p, "modules[0].lessons[0].knowledgeCheck[0].options[1]");
        p = Sample(); var caps = p.Modules![0].Lessons![0].KnowledgeCheck![0];
        caps.Options = new() { "#smallbusinesstips", "#SMALLBUSINESSTIPS", "#SmallBusinessTips", "#s_m_a_l_l" }; caps.Correct = new() { 2 };
        Assert.DoesNotContain(CoursePackValidator.Validate(p), i => i.Path.StartsWith("modules[0].lessons[0].knowledgeCheck[0].options", StringComparison.Ordinal));
        p = Sample(); p.Modules![0].Lessons![0].KnowledgeCheck = new() { p.Modules[0].Lessons![0].KnowledgeCheck![0] };
        AssertIssue(p, "modules[0].lessons[0].knowledgeCheck");
        p = Sample(); p.Modules![0].Lessons![0].KnowledgeCheck![0].Correct = new() { 7 }; AssertIssue(p, "modules[0].lessons[0].knowledgeCheck[0].correct");
    }

    [Fact]
    public void Exam_rules_are_identical_for_packs_and_admin_authoring()
    {
        foreach (var mode in new[] { PackValidationMode.Strict, PackValidationMode.Authoring })
        {
            var p = Sample(); p.FinalExam!.Pool![0].Correct = new() { 0, 1 }; AssertIssue(p, "finalExam.pool[0].correct", mode);
            p = Sample(); p.FinalExam!.Pool![2].Correct = new() { 1 }; AssertIssue(p, "finalExam.pool[2].correct", mode); // "multiple" with one answer
            p = Sample(); p.FinalExam!.Pool![0].Correct = new() { 9 }; AssertIssue(p, "finalExam.pool[0].correct", mode);
            p = Sample(); p.FinalExam!.Pool![0].Correct = new() { -1 }; AssertIssue(p, "finalExam.pool[0].correct", mode);
            p = Sample(); p.FinalExam!.Pool![0].Options!.Add("None of the above"); AssertIssue(p, "finalExam.pool[0].options[4]", mode);
            p = Sample(); p.FinalExam!.Pool![0].Options = new() { "a", "b" }; AssertIssue(p, "finalExam.pool[0].options", mode);
            p = Sample(); p.FinalExam!.Pool![0].Options![1] = p.FinalExam.Pool[0].Options![0]; AssertIssue(p, "finalExam.pool[0].options[1]", mode);
            p = Sample(); p.FinalExam!.Pool![1].Id = p.FinalExam.Pool[0].Id; AssertIssue(p, "finalExam.pool[1].id", mode);
            p = Sample(); p.FinalExam!.Pool![0].Module = "nope"; AssertIssue(p, "finalExam.pool[0].module", mode);
            p = Sample(); p.FinalExam!.Pool![0].Difficulty = "trivial"; AssertIssue(p, "finalExam.pool[0].difficulty", mode);
            p = Sample(); p.FinalExam!.Pool![0].Explanation = ""; AssertIssue(p, "finalExam.pool[0].explanation", mode);
            // Pool ≥ 1.5 × questionCount.
            p = Sample(); p.FinalExam!.QuestionCount = 11; AssertIssue(p, "finalExam.pool", mode);
            // Every module covered.
            p = Sample(); foreach (var q in p.FinalExam!.Pool!) q.Module = p.Modules![0].Slug; AssertIssue(p, "finalExam.pool", mode);
        }
    }

    [Fact]
    public void Minimum_pool_and_word_count()
    {
        Assert.Equal(15, CoursePackValidator.MinimumPool(10));
        Assert.Equal(30, CoursePackValidator.MinimumPool(20));
        Assert.Equal(2, CoursePackValidator.MinimumPool(1));
        Assert.Equal(3, CoursePackValidator.WordCount(" three  words\nhere "));
        Assert.True(CoursePackValidator.IsMediaUrl("https://cdn.example.com/v.mp4"));
        Assert.False(CoursePackValidator.IsMediaUrl("javascript:alert(1)"));
        Assert.False(CoursePackValidator.IsMediaUrl("/api/v1/files/../../etc"));
    }

    // ---------------------------------------------------------------- pack v2 (lastReviewed, tools, lectures)

    private static string Words(int n, string word = "word") => string.Join(' ', Enumerable.Repeat(word, n));

    /// <summary>A lecture that satisfies every rule: 6 scenes × 150 words = 900 words ≈ 6.4 minutes.</summary>
    public static PackLecture ValidLecture() => new()
    {
        TargetMinutes = 7,
        Scenes = Enumerable.Range(1, 6).Select(i => new PackScene
        {
            Narration = Words(150, "spoken"),
            OnScreen = $"Scene {i} title\n• First point\n• Second point",
            Visual = "Animated diagram of the funnel, then a screen recording of the settings page.",
            Seconds = 65,
        }).ToList(),
        Pronunciations = new() { new PackPronunciation { Term = "GA4", Say = "G A four" } },
    };

    /// <summary>The sample pack upgraded to v2: lastReviewed, tools, 700+ word bodies and a lecture on every lesson.</summary>
    public static CoursePack V2Sample()
    {
        var p = Sample();
        p.LastReviewed = "2026-09";
        p.Tools = new() { "Claude", "n8n" };
        foreach (var (_, lesson) in p.AllLessons)
        {
            var missing = 750 - CoursePackValidator.WordCount(lesson.Body);
            if (missing > 0) lesson.Body += "\n\n" + Words(missing, "detail");
            lesson.Lecture = ValidLecture();
        }
        return p;
    }

    [Fact]
    public void A_v2_pack_with_lectures_is_valid_and_round_trips()
    {
        var p = V2Sample();
        Assert.True(p.IsV2);
        Assert.Empty(CoursePackValidator.Validate(p));
        var back = CoursePack.Parse(p.ToJson()).Pack!;
        Assert.Equal(p.ToJson(), back.ToJson());
        Assert.Equal("2026-09", back.LastReviewed);
        Assert.Equal(6, back.AllLessons.First().Lesson.Lecture!.Scenes!.Count);
        Assert.Equal(7 * p.LessonCount, back.LectureMinutes);
        Assert.Equal("Scene 1 title", back.AllLessons.First().Lesson.Lecture!.Scenes![0].ChapterTitle);
    }

    [Fact]
    public void V1_documents_serialize_without_the_v2_properties()
    {
        // Existing packs keep their exact stored JSON (and content hash): the new optional properties are omitted when null.
        var json = Sample().ToJson();
        Assert.DoesNotContain("lastReviewed", json);
        Assert.DoesNotContain("\"tools\"", json);
        Assert.DoesNotContain("lecture", json);
        Assert.False(Sample().IsV2);
    }

    [Fact]
    public void Unknown_lecture_and_scene_properties_are_rejected()
    {
        var json = V2Sample().ToJson();
        Assert.NotNull(CoursePack.Parse(json).Pack);
        Assert.Null(CoursePack.Parse(json.Replace("\"targetMinutes\"", "\"targetMinutez\"")).Pack);
        Assert.Null(CoursePack.Parse(json.Replace("\"onScreen\"", "\"onscreenText\"")).Pack);
        Assert.Null(CoursePack.Parse(json.Replace("\"say\"", "\"sayAs\"")).Pack);
        Assert.Null(CoursePack.Parse(json.Replace("\"lastReviewed\"", "\"lastReviewd\"")).Pack);
    }

    [Fact]
    public void V2_course_rules()
    {
        var p = V2Sample(); p.LastReviewed = "2026-9"; AssertIssue(p, "lastReviewed");
        p = V2Sample(); p.LastReviewed = "2026-13"; AssertIssue(p, "lastReviewed");
        p = V2Sample(); p.LastReviewed = "1999-01"; AssertIssue(p, "lastReviewed");
        p = V2Sample(); p.Tools = Enumerable.Range(1, 21).Select(i => $"Tool {i}").ToList(); AssertIssue(p, "tools");
        p = V2Sample(); p.Tools = new() { new string('t', 41) }; AssertIssue(p, "tools[0]");
        p = V2Sample(); p.Tools = new() { " " }; AssertIssue(p, "tools[0]");
        p = V2Sample(); p.Tools = new() { "n8n", "n8n" }; AssertIssue(p, "tools");
        p = V2Sample(); p.Tools = new(); Assert.Empty(CoursePackValidator.Validate(p));
        p = V2Sample(); p.Tools = null; Assert.Empty(CoursePackValidator.Validate(p));
    }

    [Fact]
    public void V2_body_range_is_700_to_1800_words_and_v1_keeps_500_to_1100()
    {
        const string path = "modules[0].lessons[0].body";
        var p = V2Sample(); p.Modules![0].Lessons![0].Body = Words(650); AssertIssue(p, path);
        p = V2Sample(); p.Modules![0].Lessons![0].Body = Words(1801); AssertIssue(p, path);
        p = V2Sample(); p.Modules![0].Lessons![0].Body = Words(1800); Assert.Empty(CoursePackValidator.Validate(p));
        // Editorial ranges are not applied to admin authoring.
        p = V2Sample(); p.Modules![0].Lessons![0].Body = Words(650);
        Assert.Empty(CoursePackValidator.Validate(p, PackValidationMode.Authoring));
        // v1: 1200 words is too long, 650 is fine.
        p = Sample(); p.Modules![0].Lessons![0].Body = Words(1200); AssertIssue(p, path);
        p = Sample(); p.Modules![0].Lessons![0].Body = Words(650); Assert.Empty(CoursePackValidator.Validate(p));
    }

    [Fact]
    public void V2_requires_a_lecture_on_every_lesson_and_v1_may_have_one()
    {
        var p = V2Sample(); p.Modules![1].Lessons![0].Lecture = null; AssertIssue(p, "modules[1].lessons[0].lecture");
        AssertIssue(p, "modules[1].lessons[0].lecture", PackValidationMode.Authoring);
        // A v1 pack may add lectures progressively; a present lecture is validated.
        p = Sample(); p.Modules![0].Lessons![0].Lecture = ValidLecture(); Assert.Empty(CoursePackValidator.Validate(p));
        p = Sample(); p.Modules![0].Lessons![0].Lecture = ValidLecture(); p.Modules[0].Lessons![0].Lecture!.TargetMinutes = 20;
        AssertIssue(p, "modules[0].lessons[0].lecture.targetMinutes");
    }

    [Fact]
    public void Lecture_rules()
    {
        const string lp = "modules[0].lessons[0].lecture";
        static PackLecture L(CoursePack pack) => pack.Modules![0].Lessons![0].Lecture!;

        var p = V2Sample(); L(p).Title = new string('t', 101); AssertIssue(p, $"{lp}.title");
        p = V2Sample(); L(p).Title = "A lecture title"; Assert.Empty(CoursePackValidator.Validate(p));
        p = V2Sample(); L(p).TargetMinutes = 3; AssertIssue(p, $"{lp}.targetMinutes");
        p = V2Sample(); L(p).TargetMinutes = 15; AssertIssue(p, $"{lp}.targetMinutes");
        // targetMinutes must be within ± 3 of narration words / 140 (900 words ≈ 6.4 → 10 is too far, 9 is fine).
        p = V2Sample(); L(p).TargetMinutes = 10; AssertIssue(p, $"{lp}.targetMinutes");
        p = V2Sample(); L(p).TargetMinutes = 9; Assert.Empty(CoursePackValidator.Validate(p));
        // 5–16 scenes.
        p = V2Sample(); L(p).Scenes = L(p).Scenes!.Take(4).ToList(); AssertIssue(p, $"{lp}.scenes");
        p = V2Sample(); L(p).Scenes = Enumerable.Range(0, 17).Select(_ => ValidLecture().Scenes![0]).ToList(); AssertIssue(p, $"{lp}.scenes");
        p = V2Sample(); L(p).Scenes = null; AssertIssue(p, $"{lp}.scenes");
        // Narration 40–260 words per scene, 600–1800 in total, spoken (no Markdown, no URLs).
        p = V2Sample(); L(p).Scenes![0].Narration = Words(39); AssertIssue(p, $"{lp}.scenes[0].narration");
        p = V2Sample(); L(p).Scenes![0].Narration = Words(261); AssertIssue(p, $"{lp}.scenes[0].narration");
        p = V2Sample(); L(p).Scenes![0].Narration = Words(100) + " see https://example.com"; AssertIssue(p, $"{lp}.scenes[0].narration");
        p = V2Sample(); L(p).Scenes![0].Narration = Words(100) + " **bold**"; AssertIssue(p, $"{lp}.scenes[0].narration");
        p = V2Sample(); L(p).Scenes![0].Narration = Words(100) + " [link]"; AssertIssue(p, $"{lp}.scenes[0].narration");
        p = V2Sample(); L(p).Scenes![0].Narration = ""; AssertIssue(p, $"{lp}.scenes[0].narration");
        p = V2Sample(); foreach (var s in L(p).Scenes!) s.Narration = Words(90); L(p).TargetMinutes = 4; AssertIssue(p, $"{lp}.scenes"); // 540 words
        p = V2Sample(); L(p).Scenes = Enumerable.Range(0, 8).Select(_ => new PackScene { Narration = Words(240), OnScreen = "T", Visual = "V", Seconds = 100 }).ToList();
        L(p).TargetMinutes = 14; AssertIssue(p, $"{lp}.scenes"); // 1920 words
        // Word ranges are editorial: not applied to admin authoring; structure still is.
        p = V2Sample(); L(p).Scenes![0].Narration = Words(10);
        Assert.Empty(CoursePackValidator.Validate(p, PackValidationMode.Authoring));
        // onScreen ≤ 320, visual ≤ 400, both required; seconds 15–150.
        p = V2Sample(); L(p).Scenes![1].OnScreen = new string('o', 321); AssertIssue(p, $"{lp}.scenes[1].onScreen");
        p = V2Sample(); L(p).Scenes![1].OnScreen = ""; AssertIssue(p, $"{lp}.scenes[1].onScreen");
        p = V2Sample(); L(p).Scenes![1].Visual = new string('v', 401); AssertIssue(p, $"{lp}.scenes[1].visual");
        p = V2Sample(); L(p).Scenes![1].Visual = " "; AssertIssue(p, $"{lp}.scenes[1].visual", PackValidationMode.Authoring);
        p = V2Sample(); L(p).Scenes![2].Seconds = 14; AssertIssue(p, $"{lp}.scenes[2].seconds");
        p = V2Sample(); L(p).Scenes![2].Seconds = 151; AssertIssue(p, $"{lp}.scenes[2].seconds");
        // Pronunciations: term and say required, at most 30.
        p = V2Sample(); L(p).Pronunciations![0].Say = ""; AssertIssue(p, $"{lp}.pronunciations[0].say");
        p = V2Sample(); L(p).Pronunciations![0].Term = ""; AssertIssue(p, $"{lp}.pronunciations[0].term");
        p = V2Sample(); L(p).Pronunciations = Enumerable.Range(0, 31).Select(i => new PackPronunciation { Term = $"T{i}", Say = "tee" }).ToList();
        AssertIssue(p, $"{lp}.pronunciations");
        p = V2Sample(); L(p).Pronunciations = null; Assert.Empty(CoursePackValidator.Validate(p));
    }

    [Fact]
    public void Lecture_media_follows_the_video_rules()
    {
        const string lp = "modules[0].lessons[0].lecture";
        var p = V2Sample(); var l = p.Modules![0].Lessons![0].Lecture!;
        l.Src = "https://cdn.example.com/lecture.mp4"; l.Poster = "/api/v1/files/0f8fad5b-d9cb-469f-a165-70867728950e";
        l.Captions = "https://cdn.example.com/lecture.vtt";
        Assert.Empty(CoursePackValidator.Validate(p));
        p = V2Sample(); p.Modules![0].Lessons![0].Lecture!.Src = "http://insecure.example.com/v.mp4"; AssertIssue(p, $"{lp}.src");
        p = V2Sample(); p.Modules![0].Lessons![0].Lecture!.Poster = "javascript:alert(1)"; AssertIssue(p, $"{lp}.poster");
        p = V2Sample(); p.Modules![0].Lessons![0].Lecture!.Captions = "https://cdn.example.com/c.vtt"; AssertIssue(p, $"{lp}.captions");
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ&t=30s", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?v=short", null)]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQx", null)]
    [InlineData("http://youtu.be/dQw4w9WgXcQ", null)]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", null)]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ/extra", null)]
    [InlineData("https://cdn.example.com/lecture.mp4", null)]
    public void YouTube_ids_are_read_from_the_accepted_url_forms(string url, string? id) => Assert.Equal(id, YouTube.IdFrom(url));

    [Fact]
    public void A_lecture_src_on_youtube_must_be_an_accepted_form_and_publishedAt_a_date()
    {
        const string lp = "modules[0].lessons[0].lecture";
        var p = V2Sample(); var l = p.Modules![0].Lessons![0].Lecture!;
        l.Src = "https://youtu.be/dQw4w9WgXcQ"; l.Captions = "/api/v1/files/0f8fad5b-d9cb-469f-a165-70867728950e"; l.PublishedAt = "2026-09-20";
        Assert.Empty(CoursePackValidator.Validate(p));
        Assert.Equal("dQw4w9WgXcQ", l.YouTubeId);
        p = V2Sample(); p.Modules![0].Lessons![0].Lecture!.Src = "https://www.youtube.com/watch?v=tooShort"; AssertIssue(p, $"{lp}.src");
        p = V2Sample(); p.Modules![0].Lessons![0].Lecture!.Src = "https://www.youtube.com/embed/dQw4w9WgXcQ"; AssertIssue(p, $"{lp}.src", PackValidationMode.Authoring);
        p = V2Sample(); p.Modules![0].Lessons![0].Lecture!.PublishedAt = "2026-02-30"; AssertIssue(p, $"{lp}.publishedAt");
        p = V2Sample(); p.Modules![0].Lessons![0].Lecture!.PublishedAt = "20-09-2026"; AssertIssue(p, $"{lp}.publishedAt");
        // Round trip keeps publishedAt.
        p = V2Sample(); p.Modules![0].Lessons![0].Lecture!.PublishedAt = "2026-09-20";
        Assert.Equal("2026-09-20", CoursePack.Parse(p.ToJson()).Pack!.Modules![0].Lessons![0].Lecture!.PublishedAt);
    }

    [Fact]
    public void Chapter_title_is_the_first_on_screen_line()
    {
        Assert.Equal("Why it matters", new PackScene { OnScreen = "Why it matters\n• One\n• Two" }.ChapterTitle);
        Assert.Equal("Bulleted first", new PackScene { OnScreen = "\n• Bulleted first\n• Two" }.ChapterTitle);
        Assert.Equal(string.Empty, new PackScene { OnScreen = "" }.ChapterTitle);
        var lecture = ValidLecture();
        Assert.Equal(900, lecture.NarrationWords);
        Assert.Equal(390, lecture.TotalSeconds);
        Assert.StartsWith("spoken spoken", lecture.Transcript);
    }
}

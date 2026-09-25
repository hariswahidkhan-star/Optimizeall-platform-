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
}

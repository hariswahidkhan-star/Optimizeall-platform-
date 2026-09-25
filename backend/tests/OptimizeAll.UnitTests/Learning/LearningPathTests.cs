using System.Text.Json;
using OptimizeAll.Api.Modules.Learning;
using OptimizeAll.Api.Modules.Learning.Certificates;
using OptimizeAll.Domain.Learning;

namespace OptimizeAll.UnitTests.Learning;

/// <summary>
/// Learning paths (Modules/Learning/Catalog/paths/*.json) against their contract, plus the catalog-wide rules. Paths may
/// name courses that are still being written: only slugs that exist in the catalog are checked for existence, and every
/// path must already have at least three real courses so its page is never near-empty.
/// </summary>
public sealed class LearningPathTests
{
    public static IEnumerable<object[]> Files() => LearningPathLibrary.All.Select(f => new object[] { f.FileName });

    [Fact]
    public void The_library_ships_six_to_eight_paths()
    {
        Assert.InRange(LearningPathLibrary.All.Count, 6, 8);
        Assert.Equal(LearningPathLibrary.All.Count, LearningPathLibrary.Paths.Count);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_path_is_valid(string fileName)
    {
        var file = LearningPathLibrary.All.Single(f => f.FileName == fileName);
        Assert.True(file.Path is not null, $"{fileName}: {file.ParseError}");
        Assert.True(file.Issues.Count == 0, $"{fileName}:\n" + string.Join('\n', file.Issues));
        Assert.Equal(file.Path!.Slug + ".json", fileName);
    }

    [Fact]
    public void Path_slugs_are_unique_and_known_courses_are_enough()
    {
        var paths = LearningPathLibrary.Paths;
        Assert.Equal(paths.Count, paths.Select(p => p.Slug).Distinct().Count());
        var courses = CoursePackLibrary.All.Where(f => f.Pack is not null).Select(f => f.Pack!.Slug).ToHashSet();
        Assert.DoesNotContain(paths, p => courses.Contains(p.Slug) && p.Slug == "paths");
        foreach (var p in paths)
        {
            var existing = p.Courses!.Count(courses.Contains);
            Assert.True(existing >= 3, $"{p.Slug}: only {existing} of its courses exist in the catalog");
        }
    }

    [Fact]
    public void Unknown_properties_are_rejected()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "OptimizeAll.Api",
            "Modules", "Learning", "Catalog", "paths", "ai-engineer.json"));
        Assert.NotNull(LearningPathDefinition.Parse(json).Path);
        Assert.Null(LearningPathDefinition.Parse(json.Replace("\"audience\"", "\"audiences\"")).Path);
    }

    private static LearningPathDefinition Sample() => LearningPathDefinition.Parse(JsonSerializer.Serialize(LearningPathLibrary.Find("ai-engineer"),
        new JsonSerializerOptions(JsonSerializerDefaults.Web))).Path!;

    [Fact]
    public void Validator_rules()
    {
        static void Issue(LearningPathDefinition p, string path) => Assert.Contains(LearningPathValidator.Validate(p), i => i.Path == path);
        Assert.Empty(LearningPathValidator.Validate(Sample()));
        var p = Sample(); p.Slug = "Bad Slug"; Issue(p, "slug");
        p = Sample(); p.Title = new string('t', 81); Issue(p, "title");
        p = Sample(); p.Subtitle = ""; Issue(p, "subtitle");
        p = Sample(); p.Description = "Too short."; Issue(p, "description");
        p = Sample(); p.Level = "expert"; Issue(p, "level");
        p = Sample(); p.Courses = new() { "a", "b" }; Issue(p, "courses");
        p = Sample(); p.Courses![1] = p.Courses[0]; Issue(p, "courses[1]");
        p = Sample(); p.Courses![0] = "Not A Slug"; Issue(p, "courses[0]");
        p = Sample(); p.Outcomes = new() { "one" }; Issue(p, "outcomes");
        p = Sample(); p.Audience = new() { new string('a', 141), "b" }; Issue(p, "audience[0]");
        p = Sample(); p.SortOrder = -1; Issue(p, "sortOrder");
    }

    [Fact]
    public void Missing_courses_are_skipped_in_path_order()
    {
        var path = new LearningPathDefinition { Slug = "p", Courses = new() { "b", "missing", "a" } };
        var courses = new Dictionary<string, Course>
        {
            ["a"] = new() { Slug = "a", EstimatedMinutes = 60, LessonCount = 3 },
            ["b"] = new() { Slug = "b", EstimatedMinutes = 90, LessonCount = 5 },
        };
        Assert.Equal(new[] { "b", "a" }, LearningPathService.Resolve(path, courses).Select(c => c.Slug));
        var card = LearningPathService.CardFor(path, LearningPathService.Resolve(path, courses));
        Assert.Equal(2, card.CourseCount);
        Assert.Equal(150, card.TotalMinutes);
        Assert.Equal(8, card.LessonCount);
    }

    [Fact]
    public void Path_progress_summary()
    {
        var courses = new List<Course> { new() { Slug = "a" }, new() { Slug = "b" }, new() { Slug = "c" } };
        var progress = new Dictionary<string, PathCourseProgressDto>
        {
            ["a"] = new("a", true, 100, true, Guid.NewGuid()),
            ["b"] = new("b", true, 50, false, null),
        };
        var s = MyLearningPathService.Summarize("p", courses, progress);
        Assert.Equal(1, s.CompletedCourses);
        Assert.Equal(3, s.CourseCount);
        Assert.Equal(50, s.ProgressPercent);
        Assert.Equal("b", s.NextCourseSlug);
        Assert.True(s.Started);
        var none = MyLearningPathService.Summarize("p", courses, new Dictionary<string, PathCourseProgressDto>());
        Assert.False(none.Started);
        Assert.Equal("a", none.NextCourseSlug);
        Assert.Equal(0, none.ProgressPercent);
    }

    // ---------------------------------------------------------------- JSON-LD for v2 packs

    private static readonly LearningIssuer Issuer = new("Optimize All", null, "https://www.example.com");
    private static readonly LearningLinks Links = new("https://www.example.com");

    [Fact]
    public void Course_json_ld_is_complete_for_course_rich_results()
    {
        var pack = CoursePackTests.V2Sample();
        var course = new Course { Slug = pack.Slug, UpdatedAt = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), PublishedAt = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc) };
        var ld = LearningJsonLd.Course(pack, Links, Issuer, course);
        Assert.Equal("Course", ld.GetProperty("@type").GetString());
        Assert.Equal("Organization", ld.GetProperty("provider").GetProperty("@type").GetString());
        Assert.Equal(0, ld.GetProperty("offers").GetProperty("price").GetInt32());
        Assert.Equal("Free", ld.GetProperty("offers").GetProperty("category").GetString());
        var instance = ld.GetProperty("hasCourseInstance");
        Assert.Equal("Online", instance.GetProperty("courseMode").GetString());
        Assert.StartsWith("PT", instance.GetProperty("courseWorkload").GetString());
        Assert.Equal("en", ld.GetProperty("inLanguage").GetString());
        Assert.False(string.IsNullOrEmpty(ld.GetProperty("educationalLevel").GetString()));
        var teaches = ld.GetProperty("teaches").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Contains("n8n", teaches);
        Assert.Contains(pack.Skills![0], teaches);
        Assert.Equal("2026-09-01", ld.GetProperty("dateModified").GetString());
        Assert.Equal("EducationalOccupationalCredential", ld.GetProperty("educationalCredentialAwarded").GetProperty("@type").GetString());
        Assert.StartsWith("https://", ld.GetProperty("image").GetString());
    }

    [Fact]
    public void Date_modified_uses_the_later_of_review_month_and_update()
    {
        var pack = CoursePackTests.V2Sample();
        Assert.Equal("2026-10-02", LearningJsonLd.DateModified(pack, new Course { UpdatedAt = new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc) }));
        pack.LastReviewed = null;
        Assert.Equal("2026-01-05", LearningJsonLd.DateModified(pack, new Course { UpdatedAt = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc) }));
    }

    [Fact]
    public void Video_object_only_for_a_produced_lecture()
    {
        var pack = CoursePackTests.V2Sample();
        var lesson = pack.AllLessons.First(x => x.Lesson.Type == "article").Lesson;
        var course = new Course { Slug = pack.Slug, UpdatedAt = DateTime.UtcNow, PublishedAt = DateTime.UtcNow };
        Assert.Null(LearningJsonLd.Video(pack, lesson, "excerpt", Links, course));
        lesson.Lecture!.Src = "https://cdn.example.com/l.mp4";
        var video = LearningJsonLd.Video(pack, lesson, "excerpt", Links, course)!.Value;
        Assert.Equal("VideoObject", video.GetProperty("@type").GetString());
        Assert.Equal("https://cdn.example.com/l.mp4", video.GetProperty("contentUrl").GetString());
        Assert.Equal("PT6M30S", video.GetProperty("duration").GetString());
        Assert.StartsWith("https://", video.GetProperty("thumbnailUrl").GetString());
        Assert.StartsWith("spoken", video.GetProperty("transcript").GetString());
        Assert.Equal(6, video.GetProperty("hasPart").GetArrayLength());
        Assert.Equal("PT1H2M5S", LearningJsonLd.DurationSeconds(3725));
    }

    [Fact]
    public void Lesson_lecture_dto_has_chapters_and_times()
    {
        var pack = CoursePackTests.V2Sample();
        var lesson = pack.AllLessons.First().Lesson;
        var dto = PublicLearningService.Lecture(lesson)!;
        Assert.False(dto.Produced);
        Assert.Equal(6, dto.Chapters.Count);
        Assert.Equal("Scene 1 title", dto.Chapters[0].Title);
        Assert.Equal(new[] { "First point", "Second point" }, dto.Chapters[0].Points);
        Assert.Equal(65, dto.Chapters[1].StartSeconds);
        Assert.Equal(390, dto.TotalSeconds);
        Assert.Equal(900, dto.TranscriptWords);
        lesson.Lecture = null;
        Assert.Null(PublicLearningService.Lecture(lesson));
    }
}

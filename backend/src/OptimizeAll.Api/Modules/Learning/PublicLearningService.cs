using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Modules.Learning.Certificates;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Learning;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Learning;

/// <summary>
/// The free, public academy read model (anonymous website pages and the participant portal share it): catalog, course
/// pages and lessons of published courses only. Lists read the listing columns of <see cref="Course"/>; a course or
/// lesson page reads one parsed version from <see cref="CourseContentCache"/>. Final-exam questions are never exposed here.
/// </summary>
public sealed partial class PublicLearningService(AppDbContext db, CourseContentCache cache, LearningIssuerProvider issuers, TimeProvider clock)
{
    /// <summary>How long a newly published course carries the "New" flag.</summary>
    public static readonly TimeSpan NewFor = TimeSpan.FromDays(30);

    public static readonly IReadOnlyDictionary<CourseCategory, string> CategoryLabels = new Dictionary<CourseCategory, string>
    {
        [CourseCategory.Sales] = "Sales",
        [CourseCategory.Marketing] = "Marketing",
        [CourseCategory.Seo] = "SEO",
        [CourseCategory.Ai] = "AI",
        [CourseCategory.Business] = "Business",
        [CourseCategory.Design] = "Design",
        [CourseCategory.Data] = "Data & analytics",
        [CourseCategory.Platform] = "Optimize All platform",
    };

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private LearningPathService? _paths;

    /// <summary>The learning paths read model (same scope, same data as the catalog).</summary>
    public LearningPathService Paths => _paths ??= new LearningPathService(db, this, cache, issuers, clock);

    public IQueryable<Course> Published() =>
        db.Set<Course>().AsNoTracking().Where(c => c.Status == CourseStatus.Published && c.PublishedVersionId != null);

    public static System.Linq.Expressions.Expression<Func<Course, CourseCardDto>> CardProjection(DateTime newSince) => c =>
        new CourseCardDto(c.Id, c.Slug, c.Title, c.Subtitle, c.Category, c.Level, c.EstimatedMinutes, c.ModuleCount, c.LessonCount,
            c.BadgeName, c.Skills, c.IsFeatured, c.PublishedAt != null && c.PublishedAt > newSince,
            "/api/v1/public/learning/courses/" + c.Slug + "/badge.svg", c.PublishedAt);

    public static CourseCardDto Card(Course c, DateTime now) =>
        new(c.Id, c.Slug, c.Title, c.Subtitle, c.Category, c.Level, c.EstimatedMinutes, c.ModuleCount, c.LessonCount,
            c.BadgeName, c.Skills, c.IsFeatured, c.PublishedAt != null && c.PublishedAt > now - NewFor,
            LearningLinks.BadgeImagePath(c.Slug), c.PublishedAt);

    public IQueryable<Course> Filter(IQueryable<Course> q, CatalogQuery query)
    {
        if (query.Category is { } category) q = q.Where(c => c.Category == category);
        if (query.Level is { } level) q = q.Where(c => c.Level == level);
        if (query.MaxMinutes is { } max) q = q.Where(c => c.EstimatedMinutes <= max);
        if (query.Featured == true) q = q.Where(c => c.IsFeatured);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var p = PagingExtensions.LikePattern(query.Search.Trim());
            q = q.Where(c => EF.Functions.Like(c.Title, p, "\\") || EF.Functions.Like(c.Subtitle, p, "\\") ||
                             EF.Functions.Like(c.BadgeName, p, "\\") || EF.Functions.Like(c.Slug, p, "\\"));
        }
        return q;
    }

    public static IQueryable<Course> Sort(IQueryable<Course> q, string? sort, bool desc) =>
        (sort?.ToLowerInvariant()) switch
        {
            "title" => (desc ? q.OrderByDescending(c => c.Title) : q.OrderBy(c => c.Title)).ThenBy(c => c.Id),
            "newest" => q.OrderByDescending(c => c.PublishedAt).ThenBy(c => c.Id),
            "duration" => (desc ? q.OrderByDescending(c => c.EstimatedMinutes) : q.OrderBy(c => c.EstimatedMinutes)).ThenBy(c => c.Title).ThenBy(c => c.Id),
            "level" => (desc ? q.OrderByDescending(c => c.Level) : q.OrderBy(c => c.Level)).ThenBy(c => c.Title).ThenBy(c => c.Id),
            // Default ("featured"): featured first, then the curated order, then A–Z.
            _ => q.OrderByDescending(c => c.IsFeatured).ThenBy(c => c.SortOrder).ThenBy(c => c.Title).ThenBy(c => c.Id),
        };

    public async Task<PagedResult<CourseCardDto>> CatalogAsync(CatalogQuery query, CancellationToken ct)
    {
        var sorted = Sort(Filter(Published(), query), query.Sort, query.Sort is null ? false : query.Desc);
        return await sorted.Select(CardProjection(Now - NewFor)).ToPagedAsync(query, ct);
    }

    public async Task<IReadOnlyList<CategorySummaryDto>> CategoriesAsync(CancellationToken ct)
    {
        var counts = await Published().GroupBy(c => c.Category).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return Enum.GetValues<CourseCategory>()
            .Select(c => new CategorySummaryDto(c, CategoryLabels[c], counts.FirstOrDefault(x => x.Key == c)?.Count ?? 0))
            .Where(c => c.CourseCount > 0)
            .ToList();
    }

    /// <summary>
    /// The academy in numbers for the marketing pages and the header (one small, cacheable response instead of the whole
    /// catalog): course, lesson, minute and path counts, subjects with counts, featured slugs, up to eight highlight cards
    /// (featured subject courses first, the platform course last, then the curated order) and up to 36 skills
    /// (round-robin across subject courses for variety).
    /// </summary>
    public async Task<LearningSummaryDto> SummaryAsync(CancellationToken ct)
    {
        var now = Now;
        var courses = await Sort(Published(), null, false).ToListAsync(ct);
        var categories = Enum.GetValues<CourseCategory>()
            .Select(c => new CategorySummaryDto(c, CategoryLabels[c], courses.Count(x => x.Category == c)))
            .Where(c => c.CourseCount > 0).ToList();
        var highlights = courses.OrderByDescending(c => c.IsFeatured).ThenBy(c => c.Category == CourseCategory.Platform)
            .ThenBy(c => c.SortOrder).ThenBy(c => c.Title, StringComparer.Ordinal).Take(8).Select(c => Card(c, now)).ToList();
        var subject = courses.Where(c => c.Category != CourseCategory.Platform).ToList();
        var depth = subject.Count == 0 ? 0 : subject.Max(c => c.Skills.Count);
        var skills = Enumerable.Range(0, depth).SelectMany(i => subject.Where(c => c.Skills.Count > i).Select(c => c.Skills[i]))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(36).ToList();
        var bySlug = courses.ToDictionary(c => c.Slug, StringComparer.Ordinal);
        var pathCount = LearningPathLibrary.Paths.Count(p => LearningPathService.Resolve(p, bySlug).Count > 0);
        return new LearningSummaryDto(courses.Count, courses.Sum(c => c.LessonCount), courses.Sum(c => c.EstimatedMinutes), pathCount,
            categories, courses.Where(c => c.IsFeatured).Select(c => c.Slug).ToList(), highlights, skills,
            courses.Count == 0 ? null : courses.Max(c => c.UpdatedAt));
    }

    /// <summary>A published course and its parsed published version, or 404.</summary>
    public async Task<(Course Course, CourseDocument Doc)> LoadPublishedAsync(string slug, CancellationToken ct)
    {
        if (!CoursePackValidator.IsSlug(slug)) throw DomainException.NotFound("Course");
        var course = await Published().FirstOrDefaultAsync(c => c.Slug == slug, ct) ?? throw DomainException.NotFound("Course");
        return (course, await cache.GetAsync(db, course.PublishedVersionId!.Value, ct));
    }

    public async Task<CourseDetailDto> CourseAsync(string slug, CancellationToken ct)
    {
        var (course, doc) = await LoadPublishedAsync(slug, ct);
        return await DetailAsync(course, doc, ct);
    }

    public async Task<CourseDetailDto> DetailAsync(Course course, CourseDocument doc, CancellationToken ct)
    {
        var pack = doc.Pack;
        var prerequisiteSlugs = pack.Prerequisites ?? new List<string>();
        var titles = prerequisiteSlugs.Count == 0
            ? new Dictionary<string, string>()
            : await Published().Where(c => prerequisiteSlugs.Contains(c.Slug)).ToDictionaryAsync(c => c.Slug, c => c.Title, ct);
        var issuer = await issuers.GetAsync(ct);
        var links = new LearningLinks(issuer.BaseUrl);
        var card = Card(course, Now);
        var exam = ExamInfo(pack);
        var modules = (pack.Modules ?? new()).Select(m => new ModuleDto(m.Slug, m.Title, m.Summary,
            (m.Lessons ?? new()).Select(l => new LessonSummaryDto(l.Slug, l.Title, l.TypeValue, l.DurationMinutes,
                l.Video?.Src is not null || l.Lecture?.Src is not null, l.Lecture is not null, l.Lecture?.TargetMinutes ?? 0)).ToList())).ToList();
        var seo = new LearningSeoDto(SeoTitle($"{pack.Title} — free course with certificate", $"{pack.Title} — free course", pack.Title),
            Truncate(pack.Subtitle, SeoDescriptionMax),
            LearningLinks.CoursePath(pack.Slug), links.BadgeImage(pack.Slug), false);
        var jsonLd = new List<JsonElement>
        {
            LearningJsonLd.Course(pack, links, issuer, course),
            LearningJsonLd.Breadcrumbs(links, ("Home", "/"), ("Academy", "/learn"), (pack.Title, LearningLinks.CoursePath(pack.Slug))),
        };
        return new CourseDetailDto(card, pack.Description, pack.Outcomes ?? new(),
            prerequisiteSlugs.Where(titles.ContainsKey).Select(s => new PrerequisiteDto(s, titles[s])).ToList(),
            new BadgeDto(pack.Badge!.Name, pack.Badge.Description, pack.Badge.Criteria, LearningLinks.BadgeImagePath(pack.Slug)),
            exam, modules, pack.Version, course.UpdatedAt, seo, jsonLd,
            pack.LastReviewed, pack.Tools ?? new(), pack.LectureMinutes, pack.AllLessons.Count(x => x.Lesson.Lecture is not null));
    }

    public static ExamInfoDto ExamInfo(CoursePack pack) => new(pack.FinalExam!.QuestionCount, pack.FinalExam.TimeLimitMinutes,
        pack.FinalExam.MaxAttemptsPerDay, pack.PassingScore);

    public async Task<LessonDto> LessonAsync(string slug, string lessonSlug, CancellationToken ct)
    {
        var (course, doc) = await LoadPublishedAsync(slug, ct);
        return await LessonAsync(course, doc, lessonSlug, ct);
    }

    public async Task<LessonDto> LessonAsync(Course course, CourseDocument doc, string lessonSlug, CancellationToken ct)
    {
        if (!doc.LessonsBySlug.TryGetValue(lessonSlug, out var r)) throw DomainException.NotFound("Lesson");
        var lesson = r.Lesson;
        var previous = r.Index > 0 ? doc.Lessons[r.Index - 1].Lesson : null;
        var next = r.Index < doc.Lessons.Count - 1 ? doc.Lessons[r.Index + 1].Lesson : null;
        var issuer = await issuers.GetAsync(ct);
        var links = new LearningLinks(issuer.BaseUrl);
        var excerpt = Truncate(PlainText(lesson.Body), SeoDescriptionMax);
        var path = LearningLinks.LessonPath(doc.Pack.Slug, lesson.Slug);
        var jsonLd = new List<JsonElement>
        {
            LearningJsonLd.Lesson(doc.Pack, lesson, excerpt, links, issuer, course),
            LearningJsonLd.Breadcrumbs(links, ("Home", "/"), ("Academy", "/learn"), (doc.Pack.Title, LearningLinks.CoursePath(doc.Pack.Slug)), (lesson.Title, path)),
        };
        if (LearningJsonLd.Video(doc.Pack, lesson, excerpt, links, course) is { } video) jsonLd.Add(video);
        return new LessonDto(doc.Pack.Slug, doc.Pack.Title, doc.Pack.CategoryValue, r.Module.Slug, r.Module.Title, lesson.Slug, lesson.Title,
            lesson.TypeValue, lesson.DurationMinutes, lesson.Body,
            lesson.TypeValue == LessonType.Video && lesson.Video is not null
                ? new LessonVideoDto(lesson.Video.Src, lesson.Video.Poster, lesson.Video.Captions, lesson.Video.Script)
                : null,
            lesson.KeyTakeaways ?? new(),
            (lesson.KnowledgeCheck ?? new()).Select((q, i) => new KnowledgeCheckDto(i, q.Question, q.Options ?? new(), q.Correct ?? new(),
                q.Explanation, (q.Correct?.Count ?? 0) > 1)).ToList(),
            lesson.Activity,
            previous is null ? null : new LessonNavDto(previous.Slug, previous.Title),
            next is null ? null : new LessonNavDto(next.Slug, next.Title),
            r.Index + 1, doc.Lessons.Count,
            new LearningSeoDto(SeoTitle($"{lesson.Title} — {doc.Pack.Title}", lesson.Title), excerpt, path,
                lesson.Lecture?.Poster is { } poster ? links.Absolute(poster)
                : lesson.Lecture?.YouTubeId is { } yt ? YouTube.Thumbnail(yt) : links.BadgeImage(doc.Pack.Slug), false),
            jsonLd, Lecture(lesson), doc.Pack.LastReviewed);
    }

    /// <summary>A published lesson's produced lecture as a site video (server-rendered embed/player), or null.</summary>
    public async Task<Website.SiteSeo.SeoVideo?> LectureVideoAsync(string slug, string lessonSlug, CancellationToken ct)
    {
        var (course, doc) = await LoadPublishedAsync(slug, ct);
        if (!doc.LessonsBySlug.TryGetValue(lessonSlug, out var r)) return null;
        var links = new LearningLinks((await issuers.GetAsync(ct)).BaseUrl);
        return LearningJsonLd.SeoVideo(doc.Pack, r.Lesson, Truncate(PlainText(r.Lesson.Body), SeoDescriptionMax), links, course);
    }

    /// <summary>The lesson's lecture for the player: chapters from scenes (title = first on-screen line), planned times.</summary>
    public static LessonLectureDto? Lecture(PackLesson lesson)
    {
        if (lesson.Lecture is not { } lecture) return null;
        var chapters = new List<LectureChapterDto>();
        var start = 0;
        var scenes = (lecture.Scenes ?? new()).Where(s => s is not null).ToList();
        for (var i = 0; i < scenes.Count; i++)
        {
            var scene = scenes[i];
            var lines = (scene.OnScreen ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var title = scene.ChapterTitle;
            if (title.Length == 0) title = $"Part {i + 1}";
            var points = lines.Skip(1).Select(l => l.TrimStart('•', '-', '*', ' ').Trim()).Where(l => l.Length > 0).ToList();
            chapters.Add(new LectureChapterDto(i, title, points, (scene.Narration ?? string.Empty).Trim(), start, scene.Seconds));
            start += scene.Seconds;
        }
        var youTube = lecture.YouTubeId;
        return new LessonLectureDto(lecture.Title ?? lesson.Title, lecture.TargetMinutes, start, lecture.Src is not null, lecture.Src,
            lecture.Poster, lecture.Captions, chapters, lecture.NarrationWords, youTube, youTube is null ? null : YouTube.EmbedUrl(youTube),
            lecture.PublishedAt);
    }

    // ---------------------------------------------------------------- text helpers

    [GeneratedRegex(@"(?ms)^\s*(```|~~~).*?^\s*\1[`~]*\s*$")]
    private static partial Regex FenceRegex();

    [GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"[#>*_`~|]+")]
    private static partial Regex MarkRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();

    /// <summary>Markdown → plain text for descriptions and excerpts.</summary>
    public static string PlainText(string markdown)
    {
        var text = FenceRegex().Replace(markdown ?? string.Empty, " ");
        text = LinkRegex().Replace(text, "$1");
        text = MarkRegex().Replace(text, " ");
        return SpaceRegex().Replace(text, " ").Trim();
    }

    /// <summary>Search-result limits of the site (docs/SEO_CRO.md § 9.3): titles ≤ 60 characters, descriptions ≤ 155.</summary>
    public const int SeoTitleMax = 60;
    public const int SeoDescriptionMax = 155;

    /// <summary>The first candidate that fits <see cref="SeoTitleMax"/>, else the last one shortened.</summary>
    public static string SeoTitle(params string[] candidates) =>
        candidates.FirstOrDefault(c => c.Length <= SeoTitleMax) ?? Truncate(candidates[^1], SeoTitleMax);

    public static string Truncate(string text, int max)
    {
        if (text.Length <= max) return text;
        var cut = text[..(max - 1)];
        var space = cut.LastIndexOf(' ');
        return (space > max / 2 ? cut[..space] : cut).TrimEnd(',', ';', ':', '.', ' ') + "…";
    }
}

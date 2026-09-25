using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Http;
using OptimizeAll.Api.Common.Security;
using OptimizeAll.Api.Modules.Learning.Certificates;
using OptimizeAll.Domain.Common;
using OptimizeAll.Domain.Learning;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Learning;

/// <summary>A learning path file shipped with the API (embedded resource from Modules/Learning/Catalog/paths).</summary>
public sealed record PathFile(string FileName, LearningPathDefinition? Path, string? ParseError, IReadOnlyList<PackIssue> Issues)
{
    public bool IsValid => Path is not null && Issues.Count == 0;
}

/// <summary>
/// The learning paths compiled into the API (<c>Modules/Learning/Catalog/paths/*.json</c>): a handful of small files,
/// parsed once and kept in memory. They are content, not data: no table, no migration — the courses they reference are
/// resolved against the published catalog on every request, and a slug that is not (yet) published is skipped.
/// </summary>
public static class LearningPathLibrary
{
    private const string Prefix = "OptimizeAll.Learning.Paths.";

    private static readonly Lazy<IReadOnlyList<PathFile>> Files = new(() =>
        typeof(LearningPathLibrary).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal)
            .Select(Read).ToList());

    /// <summary>Every path file, valid or not (for the unit test).</summary>
    public static IReadOnlyList<PathFile> All => Files.Value;

    /// <summary>The valid paths in page order (sortOrder, then title).</summary>
    public static IReadOnlyList<LearningPathDefinition> Paths =>
        Files.Value.Where(f => f.IsValid).Select(f => f.Path!).OrderBy(p => p.SortOrder).ThenBy(p => p.Title, StringComparer.Ordinal).ToList();

    public static LearningPathDefinition? Find(string slug) => Paths.FirstOrDefault(p => p.Slug == slug);

    private static PathFile Read(string name)
    {
        using var stream = typeof(LearningPathLibrary).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var (path, error) = LearningPathDefinition.Parse(reader.ReadToEnd());
        return new PathFile(name[Prefix.Length..], path, error, path is null ? Array.Empty<PackIssue>() : LearningPathValidator.Validate(path));
    }
}

// ---------------------------------------------------------------- DTOs

public sealed record PathBadgeDto(string CourseSlug, string CourseTitle, string BadgeName, string ImageUrl);

/// <summary>A path on the paths page. Totals cover the courses that are published now.</summary>
public sealed record PathCardDto(
    string Slug, string Title, string Subtitle, CourseLevel Level, int CourseCount, int LessonCount, int TotalMinutes,
    IReadOnlyList<CourseCategory> Categories, IReadOnlyList<PathBadgeDto> Badges);

public sealed record PathCourseDto(int Position, CourseCardDto Course);

public sealed record PathDetailDto(
    PathCardDto Card, string Description, IReadOnlyList<string> Outcomes, IReadOnlyList<string> Audience,
    IReadOnlyList<PathCourseDto> Courses, LearningSeoDto Seo, IReadOnlyList<JsonElement> JsonLd);

public sealed record PathsIndexDto(IReadOnlyList<PathCardDto> Paths, LearningSeoDto Seo, IReadOnlyList<JsonElement> JsonLd);

/// <summary>A signed-in learner's progress on one course of a path.</summary>
public sealed record PathCourseProgressDto(string Slug, bool Enrolled, int ProgressPercent, bool Passed, Guid? CertificateId);

/// <summary>
/// A learner's progress on a path: courses passed (badges earned), the overall percent (mean of the courses' progress,
/// a passed course counting 100) and the next course to take (the first one not passed, in path order).
/// </summary>
public sealed record MyPathProgressDto(
    string Slug, int CompletedCourses, int CourseCount, int ProgressPercent, string? NextCourseSlug, bool Started,
    IReadOnlyList<PathCourseProgressDto> Courses);

public sealed record MyPathDto(PathDetailDto Path, MyPathProgressDto Progress);

public sealed record MyPathCardDto(PathCardDto Card, MyPathProgressDto Progress);

// ---------------------------------------------------------------- service

/// <summary>
/// Public read model of the learning paths (the paths page and each path). Created through
/// <see cref="PublicLearningService.Paths"/> so the server-rendered pages use exactly the same data as the API.
/// </summary>
public sealed class LearningPathService(AppDbContext db, PublicLearningService catalog, CourseContentCache cache,
    LearningIssuerProvider issuers, TimeProvider clock)
{
    public const string IndexTitle = "Learning paths — free AI, marketing, SEO and sales tracks";
    public const string IndexDescription =
        "Follow a free, step-by-step learning path: AI engineering, AI-powered marketing, growth, SEO and AI search, sales, creators and project finance — with certificates.";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Published courses referenced by any of <paramref name="paths"/>, by slug.</summary>
    private async Task<Dictionary<string, Course>> CoursesAsync(IEnumerable<LearningPathDefinition> paths, CancellationToken ct)
    {
        var slugs = paths.SelectMany(p => p.Courses ?? new()).Distinct().ToList();
        return await catalog.Published().Where(c => slugs.Contains(c.Slug)).ToDictionaryAsync(c => c.Slug, StringComparer.Ordinal, ct);
    }

    /// <summary>The path's courses that are published now, in path order (missing or unpublished slugs are skipped).</summary>
    public static IReadOnlyList<Course> Resolve(LearningPathDefinition path, IReadOnlyDictionary<string, Course> courses) =>
        (path.Courses ?? new()).Where(courses.ContainsKey).Select(s => courses[s]).ToList();

    private static PathCardDto Card(LearningPathDefinition path, IReadOnlyList<Course> courses) => new(
        path.Slug, path.Title, path.Subtitle, path.LevelValue, courses.Count, courses.Sum(c => c.LessonCount), courses.Sum(c => c.EstimatedMinutes),
        courses.Select(c => c.Category).Distinct().ToList(),
        courses.Select(c => new PathBadgeDto(c.Slug, c.Title, c.BadgeName, LearningLinks.BadgeImagePath(c.Slug))).ToList());

    public async Task<PathsIndexDto> ListAsync(CancellationToken ct)
    {
        var paths = LearningPathLibrary.Paths;
        var courses = await CoursesAsync(paths, ct);
        var cards = paths.Select(p => Card(p, Resolve(p, courses))).Where(c => c.CourseCount > 0).ToList();
        var issuer = await issuers.GetAsync(ct);
        var links = new LearningLinks(issuer.BaseUrl);
        var jsonLd = new List<JsonElement>
        {
            LearningJsonLd.LinkList("Learning paths", LearningLinks.PathsPath, cards.Select(c => (c.Title, LearningLinks.PathPath(c.Slug))), links),
            LearningJsonLd.Breadcrumbs(links, ("Home", "/"), ("Academy", "/learn"), ("Learning paths", LearningLinks.PathsPath)),
        };
        return new PathsIndexDto(cards, new LearningSeoDto(IndexTitle, IndexDescription, LearningLinks.PathsPath, null, false), jsonLd);
    }

    public async Task<PathDetailDto> DetailAsync(string slug, CancellationToken ct)
    {
        var path = (CoursePackValidator.IsSlug(slug) ? LearningPathLibrary.Find(slug) : null) ?? throw DomainException.NotFound("Learning path");
        var resolved = Resolve(path, await CoursesAsync(new[] { path }, ct));
        if (resolved.Count == 0) throw DomainException.NotFound("Learning path");
        var now = Now;
        var card = Card(path, resolved);
        var issuer = await issuers.GetAsync(ct);
        var links = new LearningLinks(issuer.BaseUrl);
        var pagePath = LearningLinks.PathPath(path.Slug);
        var jsonLd = new List<JsonElement>
        {
            LearningJsonLd.PathItemList(path.Title, path.Subtitle, pagePath, resolved.Select(c => (c.Slug, c.Title, c.Subtitle)), links, issuer),
            LearningJsonLd.Breadcrumbs(links, ("Home", "/"), ("Academy", "/learn"), ("Learning paths", LearningLinks.PathsPath), (path.Title, pagePath)),
        };
        // The badges earned along the way, as credentials (read from each course's published version).
        foreach (var course in resolved)
        {
            var doc = await cache.GetAsync(db, course.PublishedVersionId!.Value, ct);
            jsonLd.Add(LearningJsonLd.Credential(doc.Pack, links, issuer));
        }
        var hours = card.TotalMinutes / 60.0;
        var seo = new LearningSeoDto(
            PublicLearningService.SeoTitle($"{path.Title} learning path — free courses & certificates", $"{path.Title} learning path — free", $"{path.Title} learning path"),
            PublicLearningService.Truncate($"{path.Subtitle}. {card.CourseCount} free courses, about {Math.Max(1, (int)Math.Round(hours))} hours, a certificate for each.", PublicLearningService.SeoDescriptionMax),
            pagePath, null, false);
        return new PathDetailDto(card, path.Description, path.Outcomes ?? new(), path.Audience ?? new(),
            resolved.Select((c, i) => new PathCourseDto(i + 1, PublicLearningService.Card(c, now))).ToList(), seo, jsonLd);
    }

    /// <summary>Published courses referenced by any path, by slug (for the signed-in progress).</summary>
    public Task<Dictionary<string, Course>> PathCoursesAsync(IEnumerable<LearningPathDefinition> paths, CancellationToken ct) => CoursesAsync(paths, ct);

    public static PathCardDto CardFor(LearningPathDefinition path, IReadOnlyList<Course> courses) => Card(path, courses);
}

/// <summary>A signed-in learner's progress on the learning paths.</summary>
public sealed class MyLearningPathService(AppDbContext db, PublicLearningService catalog, CertificateService certificates)
{
    private LearningPathService Paths => catalog.Paths;

    private async Task<Dictionary<string, PathCourseProgressDto>> ProgressAsync(Guid userId, IReadOnlyCollection<Course> courses, CancellationToken ct)
    {
        var ids = courses.Select(c => c.Id).ToList();
        var enrolments = await db.Set<Enrolment>().AsNoTracking().Where(e => e.UserId == userId && ids.Contains(e.CourseId)).ToListAsync(ct);
        var enrolmentIds = enrolments.Select(e => e.Id).ToList();
        var completed = await db.Set<LessonProgress>().AsNoTracking()
            .Where(p => enrolmentIds.Contains(p.EnrolmentId) && p.CompletedAt != null)
            .GroupBy(p => p.EnrolmentId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var certs = await certificates.ValidCertificatesByCourseAsync(userId, ids, ct);
        var result = new Dictionary<string, PathCourseProgressDto>(StringComparer.Ordinal);
        foreach (var c in courses)
        {
            var e = enrolments.FirstOrDefault(x => x.CourseId == c.Id);
            var passed = e?.PassedAt is not null;
            var percent = e is null ? 0 : passed ? 100 : c.LessonCount == 0 ? 0 : Math.Min(100, completed.GetValueOrDefault(e.Id) * 100 / c.LessonCount);
            result[c.Slug] = new PathCourseProgressDto(c.Slug, e is not null, percent, passed, certs.TryGetValue(c.Id, out var cid) ? cid : null);
        }
        return result;
    }

    public static MyPathProgressDto Summarize(string slug, IReadOnlyList<Course> courses, IReadOnlyDictionary<string, PathCourseProgressDto> progress)
    {
        var rows = courses.Select(c => progress.GetValueOrDefault(c.Slug) ?? new PathCourseProgressDto(c.Slug, false, 0, false, null)).ToList();
        var done = rows.Count(r => r.Passed);
        var percent = rows.Count == 0 ? 0 : (int)Math.Round(rows.Average(r => (double)r.ProgressPercent));
        return new MyPathProgressDto(slug, done, rows.Count, percent, rows.FirstOrDefault(r => !r.Passed)?.Slug, rows.Any(r => r.Enrolled), rows);
    }

    public async Task<IReadOnlyList<MyPathCardDto>> MyListAsync(Guid userId, CancellationToken ct)
    {
        var paths = LearningPathLibrary.Paths;
        var courses = await Paths.PathCoursesAsync(paths, ct);
        var progress = await ProgressAsync(userId, courses.Values, ct);
        return paths.Select(p => (Path: p, Courses: LearningPathService.Resolve(p, courses))).Where(x => x.Courses.Count > 0)
            .Select(x => new MyPathCardDto(LearningPathService.CardFor(x.Path, x.Courses), Summarize(x.Path.Slug, x.Courses, progress))).ToList();
    }

    public async Task<MyPathDto> MyDetailAsync(Guid userId, string slug, CancellationToken ct)
    {
        var detail = await Paths.DetailAsync(slug, ct);
        var courses = await catalog.Published().Where(c => detail.Courses.Select(x => x.Course.Slug).Contains(c.Slug)).ToListAsync(ct);
        var ordered = detail.Courses.Select(x => courses.First(c => c.Slug == x.Course.Slug)).ToList();
        var progress = await ProgressAsync(userId, ordered, ct);
        return new MyPathDto(detail, Summarize(slug, ordered, progress));
    }
}

// ---------------------------------------------------------------- controllers

/// <summary>Learning paths on the public academy (anonymous, rate-limited): the paths page and each path.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Public)]
[Route("api/v1/public/learning/paths")]
public sealed class PublicLearningPathsController(PublicLearningService learning) : ControllerBase
{
    /// <summary>Every learning path with at least one published course (sortOrder), with SEO and JSON-LD.</summary>
    [HttpGet]
    public Task<PathsIndexDto> List(CancellationToken ct) => learning.Paths.ListAsync(ct);

    /// <summary>A path: description, outcomes, audience, its published courses in order, SEO and JSON-LD (ItemList of Course).</summary>
    [HttpGet("{slug}")]
    public Task<PathDetailDto> Detail(string slug, CancellationToken ct) => learning.Paths.DetailAsync(slug, ct);
}

/// <summary>A participant's progress on the learning paths (courses passed = badges earned, next course).</summary>
[ApiController]
[HasPermission(Permissions.ParticipantPortal)]
[Route("api/v1/me/learning/paths")]
public sealed class MyLearningPathsController(MyLearningPathService paths, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<MyPathCardDto>> List(CancellationToken ct) => paths.MyListAsync(currentUser.Id, ct);

    [HttpGet("{slug}")]
    public Task<MyPathDto> Detail(string slug, CancellationToken ct) => paths.MyDetailAsync(currentUser.Id, slug, ct);
}

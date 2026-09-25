using Microsoft.EntityFrameworkCore;
using OptimizeAll.Api.Common.Persistence;
using OptimizeAll.Api.Modules.Learning.Admin;
using OptimizeAll.Api.Modules.Learning.Certificates;
using OptimizeAll.Api.Modules.Website.Public;
using OptimizeAll.Domain.Learning;
using OptimizeAll.Infrastructure.Persistence;

namespace OptimizeAll.Api.Modules.Learning;

public static class LearningModule
{
    /// <summary>
    /// Registers the Learning module (docs/LEARNING.md): the free academy (public catalog, courses, lessons), enrolments and
    /// progress, server-graded final exams, certificates (PDF/SVG, verification, Open Badges 2.0, LinkedIn), the Learning
    /// admin, the course-pack catalog upsert (Baseline seed) and the demo learners (Demo seed).
    /// </summary>
    public static IServiceCollection AddLearningModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<CourseContentCache>();
        services.AddScoped<LearningIssuerProvider>();
        services.AddScoped<PublicLearningService>();
        services.AddScoped<MyLearningService>();
        services.AddScoped<MyLearningPathService>();
        services.AddScoped<ExamService>();
        services.AddScoped<CertificateService>();
        services.AddScoped<LearningAdminService>();
        services.AddScoped<LearningMediaService>();
        services.AddScoped<ISitemapContributor, LearningSitemapContributor>();
        services.AddScoped<LearningCatalogSeeder>();
        services.AddScoped<ISeeder>(sp => sp.GetRequiredService<LearningCatalogSeeder>());
        services.AddScoped<LearningDemoSeeder>();
        services.AddScoped<ISeeder>(sp => sp.GetRequiredService<LearningDemoSeeder>());
        return services;
    }
}

/// <summary>
/// The <c>learn</c> child sitemap of the sitemap index (SiteSeo, <c>/sitemaps/learn.xml</c>): /learn, every published
/// course and each of its lessons, all server-rendered by <c>SeoPageResolver</c>.
/// </summary>
public sealed class LearningSitemapContributor(AppDbContext db, CourseContentCache cache, LearningIssuerProvider issuers) : ISitemapContributor
{
    public const string GroupName = "learn";

    public string Group => GroupName;

    public async Task<IReadOnlyList<SitemapContribution>> UrlsAsync(CancellationToken ct)
    {
        var courses = await db.Set<Course>().AsNoTracking()
            .Where(c => c.Status == CourseStatus.Published && c.PublishedVersionId != null)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Slug).ToListAsync(ct);
        var links = new LearningLinks((await issuers.GetAsync(ct)).BaseUrl);
        var lastUpdate = courses.Count == 0 ? (DateTime?)null : courses.Max(c => c.UpdatedAt);
        var result = new List<SitemapContribution> { new("/learn", lastUpdate, "Academy") };
        // Learning paths with at least one published course (/learn/paths and each path).
        var bySlug = courses.ToDictionary(c => c.Slug, StringComparer.Ordinal);
        var paths = LearningPathLibrary.Paths.Select(p => (Path: p, Courses: LearningPathService.Resolve(p, bySlug))).Where(x => x.Courses.Count > 0).ToList();
        if (paths.Count > 0)
        {
            result.Add(new SitemapContribution(LearningLinks.PathsPath, lastUpdate, "Learning paths"));
            result.AddRange(paths.Select(x => new SitemapContribution(LearningLinks.PathPath(x.Path.Slug), x.Courses.Max(c => c.UpdatedAt), x.Path.Title)));
        }
        foreach (var course in courses)
        {
            result.Add(new SitemapContribution(LearningLinks.CoursePath(course.Slug), course.UpdatedAt, course.Title));
            var doc = await cache.GetAsync(db, course.PublishedVersionId!.Value, ct);
            foreach (var l in doc.Lessons)
            {
                // Produced lectures go into the video sitemap (player_loc = the privacy-enhanced YouTube embed).
                var video = l.Lesson.Lecture?.Src is null ? null
                    : LearningJsonLd.SeoVideo(doc.Pack, l.Lesson, PublicLearningService.Truncate(PublicLearningService.PlainText(l.Lesson.Body), 300), links, course);
                result.Add(new SitemapContribution(LearningLinks.LessonPath(course.Slug, l.Lesson.Slug), course.UpdatedAt, l.Lesson.Title,
                    Videos: video is null ? null : new[] { video }));
            }
        }
        return result;
    }
}

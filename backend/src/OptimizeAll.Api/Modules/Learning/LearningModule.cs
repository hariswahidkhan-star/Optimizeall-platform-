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
public sealed class LearningSitemapContributor(AppDbContext db, CourseContentCache cache) : ISitemapContributor
{
    public const string GroupName = "learn";

    public string Group => GroupName;

    public async Task<IReadOnlyList<SitemapContribution>> UrlsAsync(CancellationToken ct)
    {
        var courses = await db.Set<Course>().AsNoTracking()
            .Where(c => c.Status == CourseStatus.Published && c.PublishedVersionId != null)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Slug).ToListAsync(ct);
        var result = new List<SitemapContribution> { new("/learn", courses.Count == 0 ? null : courses.Max(c => c.UpdatedAt), "Academy") };
        foreach (var course in courses)
        {
            result.Add(new SitemapContribution(LearningLinks.CoursePath(course.Slug), course.UpdatedAt, course.Title));
            var doc = await cache.GetAsync(db, course.PublishedVersionId!.Value, ct);
            result.AddRange(doc.Lessons.Select(l => new SitemapContribution(LearningLinks.LessonPath(course.Slug, l.Lesson.Slug), course.UpdatedAt, l.Lesson.Title)));
        }
        return result;
    }
}

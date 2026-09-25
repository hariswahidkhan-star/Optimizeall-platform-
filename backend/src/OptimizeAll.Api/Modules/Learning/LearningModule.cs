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
        services.AddScoped<IPublicSitemapContributor, LearningSitemapContributor>();
        services.AddScoped<LearningCatalogSeeder>();
        services.AddScoped<ISeeder>(sp => sp.GetRequiredService<LearningCatalogSeeder>());
        services.AddScoped<LearningDemoSeeder>();
        services.AddScoped<ISeeder>(sp => sp.GetRequiredService<LearningDemoSeeder>());
        return services;
    }
}

/// <summary>The academy's public URLs for the website sitemap: /learn, every published course and each of its lessons.</summary>
public sealed class LearningSitemapContributor(AppDbContext db, CourseContentCache cache) : IPublicSitemapContributor
{
    public async Task<IReadOnlyList<(string Path, DateTime? Modified)>> SitemapEntriesAsync(CancellationToken ct)
    {
        var courses = await db.Set<Course>().AsNoTracking()
            .Where(c => c.Status == CourseStatus.Published && c.PublishedVersionId != null)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Slug).ToListAsync(ct);
        var result = new List<(string, DateTime?)> { ("/learn", courses.Count == 0 ? null : courses.Max(c => c.UpdatedAt)) };
        foreach (var course in courses)
        {
            result.Add((LearningLinks.CoursePath(course.Slug), course.UpdatedAt));
            var doc = await cache.GetAsync(db, course.PublishedVersionId!.Value, ct);
            result.AddRange(doc.Lessons.Select(l => (LearningLinks.LessonPath(course.Slug, l.Lesson.Slug), (DateTime?)course.UpdatedAt)));
        }
        return result;
    }
}
